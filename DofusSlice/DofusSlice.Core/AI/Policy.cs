using DofusSlice.Core.Combat;
using DofusSlice.Core.Grid;
using DofusSlice.Core.Spells;

namespace DofusSlice.Core.AI;

/// <summary>
/// The autobattler brain for TITHE's watched combat. One entry point, <see cref="TakeTurn"/>,
/// dispatches on a unit's <see cref="AiPolicy"/>. The policies are deliberately legible so a
/// spectator can narrate the fight (Bible M1 exit criterion):
/// <list type="bullet">
///   <item><b>Bruiser</b> — march at the nearest enemy and hit it; hold the front.</item>
///   <item><b>Flanker</b> — dive the softest reachable enemy.</item>
///   <item><b>Skirmisher</b> — keep to a far range band, kite away when crowded, shoot the softest target.</item>
///   <item><b>Artillery</b> — hold a safe mid sightline and nuke the softest target; back off if meleed.</item>
/// </list>
/// </summary>
public static class Policy
{
    public static void TakeTurn(CombatEngine engine, Fighter self)
    {
        if (self.Policy == AiPolicy.Support) { SupportTurn(engine, self); return; }

        TrySelfBuff(engine, self);    // Ironhide etc. before wading in
        TrySelfEconomy(engine, self); // Blood Pact: trade blood for AP while healthy

        for (int guard = 0; guard < 24; guard++)
        {
            if (!self.IsAlive || engine.Outcome != FightOutcome.Ongoing) return;
            bool acted = self.Policy switch
            {
                AiPolicy.Skirmisher or AiPolicy.Artillery => Kite(engine, self),
                AiPolicy.Flanker => Charge(engine, self, preferSoftest: true),
                _ => Charge(engine, self, preferSoftest: false),
            };
            if (!acted) { StepOffHazard(engine, self); return; }
        }
    }

    /// <summary>The floor is lava awareness (owner report: a mob stood in an ember grave doing
    /// nothing). A turn that ends with MP to spare and fire underfoot steps to safe ground —
    /// preferring a cell that can still shoot, then the policy's preferred distance.</summary>
    private static void StepOffHazard(CombatEngine engine, Fighter self)
    {
        if (self.CurrentMp <= 0 || self.HazardImmune || engine.DangerAt(self.Pos) <= 0) return;
        var reachable = engine.MovementRange(self);
        bool ranged = self.Policy is AiPolicy.Skirmisher or AiPolicy.Artillery or AiPolicy.Support;
        var safe = reachable.Keys.Where(c => engine.DangerAt(c) <= 0)
            .OrderByDescending(c => CanHitAnyEnemyFrom(engine, self, c))
            .ThenBy(c => ranged ? -DistToNearestEnemy(engine, self, c) : DistToNearestEnemy(engine, self, c))
            .ThenBy(c => reachable[c])
            .Cast<CellCoord?>().FirstOrDefault();
        if (safe is { } cell) engine.TryMove(self, cell);
    }

    /// <summary>What stopping on this cell costs in blood — 0 for the hazard-immune.</summary>
    private static int Danger(CombatEngine engine, Fighter self, CellCoord c) =>
        self.HazardImmune ? 0 : engine.DangerAt(c);

    /// <summary>Cast a self-shield (Ironhide) once when an enemy is closing and we're unshielded.</summary>
    private static void TrySelfBuff(CombatEngine engine, Fighter self)
    {
        if (self.ShieldAmount > 0) return;
        if (DistToNearestEnemy(engine, self, self.Pos) > 3) return;
        var buff = self.Spells.FirstOrDefault(s => s.MaxRange == 0 &&
            s.Effects.Any(e => e.Kind == EffectKind.ApplyStatus && e.Status == StatusKind.Shield));
        if (buff != null && engine.CanCast(self, buff, self.Pos, out _))
            engine.TryCast(self, buff, self.Pos);
    }

    /// <summary>Blood Pact: a self-targeted AP grant paid in HP. Blood is only spent when it
    /// BUYS something (Pass 3, owner report: the cannon bled itself for nothing): the bonus AP
    /// must unlock at least one more attack this turn, and the enemies still standing must be
    /// able to absorb what we could already afford — no paying HP to overkill a won fight.</summary>
    private static void TrySelfEconomy(CombatEngine engine, Fighter self)
    {
        if (self.Hp * 5 <= self.MaxHp * 2) return;
        if (DistToNearestEnemy(engine, self, self.Pos) > 10) return;
        var pact = self.Spells.FirstOrDefault(s => s.MaxRange == 0 &&
            s.Effects.Any(e => e.Kind == EffectKind.GrantAp));
        if (pact == null || !engine.CanCast(self, pact, self.Pos, out _)) return;

        var attack = DamageSpells(self).OrderBy(s => s.ApCost).FirstOrDefault();
        if (attack == null) return;
        int bonus = pact.Effects.Where(e => e.Kind == EffectKind.GrantAp).Sum(e => e.Min);
        int castsNow = self.CurrentAp / attack.ApCost;
        int castsWith = (self.CurrentAp - pact.ApCost + bonus) / attack.ApCost;
        if (castsWith <= castsNow) return;   // the pact buys no extra swing — keep the blood

        // Overkill guard: measure the affordable swings (stat-scaled, vs the nearest target)
        // against every living enemy's HP. If that already finishes the fight, don't bleed.
        var nearest = engine.Fighters.Where(f => f.IsAlive && f.Team != self.Team)
            .OrderBy(f => f.Pos.DistanceTo(self.Pos)).FirstOrDefault();
        if (nearest == null) return;
        int avg = engine.EstimateDamage(self, attack, nearest.Pos) is { } est
            ? Math.Max(1, (est.min + est.max) / 2)
            : Math.Max(1, attack.Effects.Where(e => e.Kind is EffectKind.Damage or EffectKind.Lifesteal)
                .Sum(e => (e.Min + e.Max) / 2));
        int enemyHp = engine.Fighters.Where(f => f.IsAlive && f.Team != self.Team).Sum(f => f.Hp);
        if (castsNow * avg >= enemyHp) return;

        engine.TryCast(self, pact, self.Pos);
    }

    /// <summary>Blink: a ground-targeted self-teleport used as an escape — when meleed, jump to
    /// the freest cell in range and keep fighting from there. Returns true if it blinked.</summary>
    private static bool TryBlinkAway(CombatEngine engine, Fighter self)
    {
        if (DistToNearestEnemy(engine, self, self.Pos) > 1) return false;
        var blink = self.Spells.FirstOrDefault(s =>
            s.Effects.Any(e => e.Kind == EffectKind.Teleport) && s.ApCost <= self.CurrentAp);
        if (blink == null) return false;

        CellCoord? best = null; int bestDist = 1; // must beat staying in melee
        for (int dx = -blink.MaxRange; dx <= blink.MaxRange; dx++)
            for (int dy = -blink.MaxRange; dy <= blink.MaxRange; dy++)
            {
                var c = self.Pos.Offset(dx, dy);
                if (!engine.CanCast(self, blink, c, out _)) continue;
                int d = DistToNearestEnemy(engine, self, c);
                if (d > bestDist) { best = c; bestDist = d; }
            }
        return best is { } cell && engine.TryCast(self, blink, cell);
    }

    // ----- Support policy: never lead, feed the frontline AP, keep out of reach ------

    private static void SupportTurn(CombatEngine engine, Fighter self)
    {
        var gift = self.Spells.FirstOrDefault(s => s.Effects.Any(e => e.Kind == EffectKind.GrantAp));
        if (gift != null)
            for (int guard = 0; guard < 4 && self.CurrentAp >= gift.ApCost; guard++)
            {
                // Buff the ally with a real attack that stands closest to the enemy (the frontline).
                var ally = engine.Fighters
                    .Where(a => a.IsAlive && a.Team == self.Team && a != self && DamageSpells(a).Any())
                    .Where(a => engine.CanCast(self, gift, a.Pos, out _))
                    .OrderBy(a => DistToNearestEnemy(engine, self, a.Pos))
                    .FirstOrDefault();
                if (ally == null || !engine.TryCast(self, gift, ally.Pos)) break;
            }

        // Keep a safe distance: retreat if anything is closing, else hold near the court.
        if (self.CurrentMp <= 0) return;
        int band = self.PreferredRangeMin > 0 ? self.PreferredRangeMin : 3;
        int here = DistToNearestEnemy(engine, self, self.Pos);
        if (here > band) return;
        var reachable = engine.MovementRange(self);
        if (reachable.Count == 0) return;
        var safest = reachable.Keys
            .OrderByDescending(c => DistToNearestEnemy(engine, self, c)).ThenBy(c => reachable[c]).First();
        if (DistToNearestEnemy(engine, self, safest) > here) engine.TryMove(self, safest);
    }

    // ----- Melee policies: close the distance and hit ------------------------------

    private static bool Charge(CombatEngine engine, Fighter self, bool preferSoftest)
    {
        if (TryShootBest(engine, self)) return true;

        var target = preferSoftest ? Softest(engine, self) : Nearest(engine, self);
        if (target == null) return false;
        // March at an ATTACK cell, not the target's own cell: a geodesic walk to the enemy
        // itself happily spends its last MP on a DIAGONAL neighbor — adjacent to the eye,
        // yet outside every orthogonal melee range, so the turn's blow is wasted (QA runs:
        // hounds, husks and the Sexton all parked diagonally for whole turns).
        // A hot attack cell is worth a short detour, not a long one: danger rides the
        // distance as a soft cost, so a spike-free flank wins when it's near.
        var goal = engine.Field.Orthogonal(target.Pos)
            .Where(c => engine.Field.IsWalkable(c) && (c == self.Pos || !engine.IsOccupied(c)))
            .OrderBy(c => c.DistanceTo(self.Pos) + Danger(engine, self, c))
            .Cast<CellCoord?>()
            .FirstOrDefault() ?? target.Pos;
        return StepToward(engine, self, goal);
    }

    // ----- Ranged policies: hold range, shoot the softest --------------------------

    private static bool Kite(CombatEngine engine, Fighter self)
    {
        // Meleed with a Blink learned: jump clear first, then fight from the new ground.
        if (TryBlinkAway(engine, self)) return true;

        // Fire from where we stand if anything is already in the sights.
        if (TryShootBest(engine, self)) return true;
        if (self.CurrentMp <= 0) return false;

        var reachable = engine.MovementRange(self);
        if (reachable.Count == 0) return false;

        // Best: step onto a cell that can hit an enemy, preferring the safest such cell
        // (furthest from the nearest enemy) so the kiter keeps its distance while still firing.
        CellCoord? firing = reachable.Keys
            .Where(cell => CanHitAnyEnemyFrom(engine, self, cell))
            .OrderBy(cell => Danger(engine, self, cell))   // never snipe from inside a grave
            .ThenByDescending(cell => DistToNearestEnemy(engine, self, cell))
            .ThenBy(cell => reachable[cell])
            .Cast<CellCoord?>()
            .FirstOrDefault();
        if (firing is CellCoord fc) return engine.TryMove(self, fc);

        // No firing cell in reach. If the enemy is beyond our range band, close in to get a shot
        // next turn; if it is inside our band (crowding us), kite back to the safest cell.
        var target = Nearest(engine, self);
        if (target == null) return false;
        int here = DistToNearestEnemy(engine, self, self.Pos);
        int band = self.PreferredRangeMax > 0 ? self.PreferredRangeMax : 6;

        if (here > band) return StepToward(engine, self, target.Pos);

        var safest = reachable.Keys
            .OrderByDescending(cell => DistToNearestEnemy(engine, self, cell) * 3 - Danger(engine, self, cell))
            .ThenBy(cell => reachable[cell])
            .First();
        if (DistToNearestEnemy(engine, self, safest) > here) return engine.TryMove(self, safest);
        return false;
    }

    // ----- Shared helpers ----------------------------------------------------------

    private static IEnumerable<Fighter> Enemies(CombatEngine engine, Fighter self) =>
        engine.Fighters.Where(f => f.IsAlive && f.Team != self.Team);

    private static Fighter? Nearest(CombatEngine engine, Fighter self) =>
        Enemies(engine, self).OrderBy(f => f.Pos.DistanceTo(self.Pos)).FirstOrDefault();

    private static Fighter? Softest(CombatEngine engine, Fighter self) =>
        Enemies(engine, self).OrderBy(f => f.Hp).ThenBy(f => f.Pos.DistanceTo(self.Pos)).FirstOrDefault();

    private static IEnumerable<SpellDef> DamageSpells(Fighter self) =>
        self.Spells.Where(s => s.Effects.Any(e => e.Kind is EffectKind.Damage or EffectKind.Lifesteal));

    /// <summary>
    /// Shoot the highest-value reachable target (Bible: "highest-value target in range"). Prefer a
    /// kill we can secure this cast — and among those the <i>toughest</i>, so heavy burst isn't
    /// wasted overkilling a near-dead soft target while a real threat survives. Failing a kill,
    /// chip the softest. Each attacker re-evaluates per cast, so focus-fire stays adaptive.
    /// </summary>
    private static bool TryShootBest(CombatEngine engine, Fighter self)
    {
        var spells = DamageSpells(self).OrderByDescending(s => s.ApCost).ToList();
        Fighter? killE = null; SpellDef? killS = null; int killHp = -1;
        Fighter? chipE = null; SpellDef? chipS = null; int chipHp = int.MaxValue;

        foreach (var enemy in Enemies(engine, self))
            foreach (var spell in spells)
            {
                if (!engine.CanCast(self, spell, enemy.Pos, out _)) continue;
                if (enemy.Hp < chipHp) { chipE = enemy; chipS = spell; chipHp = enemy.Hp; }
                var est = engine.EstimateDamage(self, spell, enemy.Pos);
                if (est.HasValue && est.Value.max >= enemy.Hp && enemy.Hp > killHp)
                    { killE = enemy; killS = spell; killHp = enemy.Hp; }
                break; // strongest castable spell for this enemy
            }

        if (killE != null) return engine.TryCast(self, killS!, killE.Pos);
        if (chipE != null) return engine.TryCast(self, chipS!, chipE.Pos);
        return false;
    }

    private static int DistToNearestEnemy(CombatEngine engine, Fighter self, CellCoord from) =>
        Enemies(engine, self).Select(e => from.DistanceTo(e.Pos)).DefaultIfEmpty(99).Min();

    private static bool CanHitAnyEnemyFrom(CombatEngine engine, Fighter self, CellCoord from)
    {
        var affordable = DamageSpells(self).Where(s => s.ApCost <= self.CurrentAp).ToList();
        return Enemies(engine, self).Any(e => affordable.Any(s => CanHitFrom(engine, from, self.Pos, e.Pos, s)));
    }

    /// <summary>Move one hop toward <paramref name="goal"/>, routing around obstacles geodesically.</summary>
    private static bool StepToward(CombatEngine engine, Fighter self, CellCoord goal)
    {
        if (self.CurrentMp <= 0) return false;
        var reachable = engine.MovementRange(self);
        if (reachable.Count == 0) return false;

        var field = Pathfinding.DistanceField(engine.Field, goal);
        int Geo(CellCoord c) => field.TryGetValue(c, out int d) ? d : int.MaxValue;

        // Advance first, but among equally-advancing cells take the one that isn't on fire.
        var next = reachable.Keys.OrderBy(Geo).ThenBy(c => Danger(engine, self, c))
            .ThenBy(c => reachable[c]).First();
        if (Geo(next) < Geo(self.Pos)) return engine.TryMove(self, next);
        return false;
    }

    /// <summary>Would <paramref name="spell"/> land on <paramref name="target"/> from <paramref name="from"/>?</summary>
    private static bool CanHitFrom(CombatEngine engine, CellCoord from, CellCoord oldPos, CellCoord target, SpellDef spell)
    {
        int dist = from.DistanceTo(target);
        if (dist < spell.MinRange || dist > spell.MaxRange) return false;
        if (spell.LineOnly && !from.IsAlignedWith(target)) return false;
        if (spell.RequiresLineOfSight &&
            !LineOfSight.HasLineOfSight(engine.Field, from, target,
                c => c != target && c != from && c != oldPos && engine.IsOccupied(c)))
            return false;
        return true;
    }
}

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
            // Risk/reward first (owner: "is it worth pushing them into a bad tile?"): a shove that
            // tips an enemy into the void or across spikes/embers outscores an ordinary swing.
            bool acted = TryShove(engine, self) || self.Policy switch
            {
                AiPolicy.Skirmisher or AiPolicy.Artillery => Kite(engine, self),
                AiPolicy.Flanker => Charge(engine, self, preferSoftest: true),
                _ => Charge(engine, self, preferSoftest: false),
            };
            if (!acted) break;
        }
        SettleTurn(engine, self);   // step off fire; a wounded flanker gives ground rather than die
    }

    /// <summary>End of turn housekeeping (owner: "retreat or protect if getting hit buys nothing").
    /// Anyone still standing in fire steps off it — but KEEPS fighting: it prefers a safe cell it can
    /// still hit an enemy from, then the policy's preferred distance (a bruiser holds the line and
    /// stays close; a kiter drifts back), then the shortest walk. That firing-cell preference is the
    /// old StepOffHazard, folded in here so a bruiser on an ember-adjacent melee cell no longer bolts
    /// to the farthest safe cell and thrashes (charge in / hit / flee / charge back). A WOUNDED,
    /// cornered flanker is the one exception: it gives ground to the safest reachable cell rather than
    /// trade into a death — hit-and-run, not hit-and-die.</summary>
    private static void SettleTurn(CombatEngine engine, Fighter self)
    {
        if (!self.IsAlive || self.CurrentMp <= 0) return;

        bool wounded = self.Hp * 5 < self.MaxHp * 2;               // below 40%
        bool cornered = DistToNearestEnemy(engine, self, self.Pos) <= 1;
        bool flee = self.Policy == AiPolicy.Flanker && wounded && cornered;
        bool onFire = Danger(engine, self, self.Pos) > 0;
        if (!onFire && !flee) return;                              // safe ground and no reason to run

        var reachable = engine.MovementRange(self);
        if (reachable.Count == 0) return;

        if (flee)
        {
            // Survival trumps position: the safest cell that puts the most ground between us and them.
            var run = reachable.Keys
                .OrderBy(c => Danger(engine, self, c))
                .ThenByDescending(c => DistToNearestEnemy(engine, self, c))
                .ThenBy(c => reachable[c])
                .First();
            bool better = Danger(engine, self, run) < Danger(engine, self, self.Pos)
                          || DistToNearestEnemy(engine, self, run) > DistToNearestEnemy(engine, self, self.Pos);
            if (better) engine.TryMove(self, run);
            return;
        }

        // Standing in fire with plays spent: keep the ability to FIGHT first (a bruiser tanks a hot
        // melee cell rather than bolt to a safe cell it can't hit from — the old thrash), then the
        // coolest such cell, then the policy's distance (hold close if melee, drift back if ranged).
        bool ranged = self.Policy is AiPolicy.Skirmisher or AiPolicy.Artillery or AiPolicy.Support;
        var best = reachable.Keys
            .OrderByDescending(c => CanHitAnyEnemyFrom(engine, self, c))
            .ThenBy(c => Danger(engine, self, c))
            .ThenBy(c => ranged ? -DistToNearestEnemy(engine, self, c) : DistToNearestEnemy(engine, self, c))
            .ThenBy(c => reachable[c])
            .First();
        // Move only to shed some fire, and never trade away a firing/melee cell we still hold: this
        // steps a unit off an ember it needn't stand on (even when no cell is FULLY safe) without ever
        // bolting a bruiser out of the fight — both were regressions in the plain fully-safe filter.
        bool keepsFight = CanHitAnyEnemyFrom(engine, self, best) || !CanHitAnyEnemyFrom(engine, self, self.Pos);
        if (keepsFight && Danger(engine, self, best) < Danger(engine, self, self.Pos))
            engine.TryMove(self, best);
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
        var attackCells = engine.Field.Orthogonal(target.Pos)
            .Where(c => engine.Field.IsWalkable(c) && (c == self.Pos || !engine.IsOccupied(c)))
            .ToList();
        if (attackCells.Count == 0) return StepToward(engine, self, target.Pos);

        // A pusher lines the victim up with the drop: prefer the attack cell whose shove tips them
        // into the void or across spikes/embers (owner: set up the bad tile). But it's a SETUP, not a
        // goal — only take it if it is at most a one-cell detour off the nearest clean attack cell, so
        // the pusher never wanders far or onto fire chasing a shove it can line up next turn anyway.
        var shove = Shovers(self).FirstOrDefault(t => !t.pull);
        var plain = attackCells.OrderBy(c => c.DistanceTo(self.Pos) + Danger(engine, self, c)).First();
        int plainCost = plain.DistanceTo(self.Pos) + Danger(engine, self, plain);
        CellCoord goal;
        if (shove.spell != null &&
            attackCells.Select(c => (c, s: PushSetupScore(engine, c, target, shove.cells)))
                .Where(x => x.s > 0 && x.c.DistanceTo(self.Pos) + Danger(engine, self, x.c) <= plainCost + 1)
                .OrderByDescending(x => x.s)
                .ThenBy(x => x.c.DistanceTo(self.Pos) + Danger(engine, self, x.c))
                .Select(x => (CellCoord?)x.c).FirstOrDefault() is { } setup)
            goal = setup;
        else
            goal = plain;
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

    /// <summary>The caster's affordable movement spells, unpacked as (spell, pull?, cells).</summary>
    private static IEnumerable<(SpellDef spell, bool pull, int cells)> Shovers(Fighter self) =>
        self.Spells.Where(s => s.ApCost <= self.CurrentAp)
            .SelectMany(s => s.Effects.Where(e => e.Kind is EffectKind.Push or EffectKind.Pull)
                .Select(e => (s, e.Kind == EffectKind.Pull, e.Min)));

    /// <summary>The marquee menace (owner: risk/reward — a shove that "pushes you into a bad tile").
    /// A shove or drag that tips an enemy into the VOID, finishes them by collision/hazard, or simply
    /// out-damages our best ordinary attack is worth pre-empting the swing for — void first. But the
    /// shove spell is ALSO a normal attack (it hits and pushes at once), so casting it FOR the shove
    /// only earns its place when it beats letting damage-targeting choose the target: we never spend
    /// the AP on a mere chip-shove, and never knock a currently-killable enemy out of reach for one.
    /// A kiter never drags prey toward itself.</summary>
    private static bool TryShove(CombatEngine engine, Fighter self)
    {
        bool ranged = self.Policy is AiPolicy.Skirmisher or AiPolicy.Artillery;
        int plainBest = BestPlainDamage(engine, self);                         // the bar a chip-shove must clear
        bool anyPlainKill = Enemies(engine, self).Any(e => PlainCanKill(engine, self, e));

        SpellDef? bestS = null; Fighter? bestE = null; int bestValue = 0; bool bestKills = false;
        foreach (var enemy in Enemies(engine, self))
        {
            bool killablePlain = PlainCanKill(engine, self, enemy);
            foreach (var (spell, pull, cells) in Shovers(self))
            {
                if (pull && ranged) continue;                                 // a kiter never drags prey toward itself
                if (!engine.CanCast(self, spell, enemy.Pos, out _)) continue;
                int direct = engine.EstimateDamage(self, spell, enemy.Pos) is { } est ? (est.min + est.max) / 2 : 0;
                var fc = engine.ForecastShift(self, enemy, cells, pull, direct);
                if (!fc.Valid) continue;

                if (fc.IntoVoid)                                              // a certain kill off the map's edge
                {
                    if (100000 > bestValue) { bestValue = 100000; bestS = spell; bestE = enemy; bestKills = true; }
                    continue;
                }
                // The plain attack finishes a plain-killable enemy where it stands — never shove one:
                // a shove kill is only an AVERAGE prediction, so an unlucky roll would whiff and knock a
                // guaranteed kill out of reach instead.
                if (killablePlain) continue;
                if (fc.Kills)                                                 // a kill the plain path would MISS
                {
                    int killVal = 10000 + direct + fc.Damage;
                    if (killVal > bestValue) { bestValue = killVal; bestS = spell; bestE = enemy; bestKills = true; }
                    continue;
                }
                // A chip-shove earns the AP only when no plain kill is on the table (that kill's AP comes
                // first) and it out-damages our best plain swing; a kiter never chip-shoves at all.
                if (anyPlainKill || ranged) continue;
                int value = direct + fc.Damage;
                if (value > bestValue) { bestValue = value; bestS = spell; bestE = enemy; bestKills = false; }
            }
        }
        // Fire when the shove kills, or (chip) its damage strictly beats our best plain attack this turn.
        if (bestE != null && (bestKills || bestValue > plainBest))
            return engine.TryCast(self, bestS!, bestE.Pos);
        return false;
    }

    /// <summary>The most HP a plain swing/shot could strip from any one enemy this turn — the bar a
    /// shove must clear to be worth spending the AP on instead. Capped at the target's HP so overkill
    /// never inflates it.</summary>
    private static int BestPlainDamage(CombatEngine engine, Fighter self)
    {
        int best = 0;
        foreach (var enemy in Enemies(engine, self))
            foreach (var spell in DamageSpells(self))
            {
                if (!engine.CanCast(self, spell, enemy.Pos, out _)) continue;
                if (engine.EstimateDamage(self, spell, enemy.Pos) is { } est)
                    best = Math.Max(best, Math.Min(enemy.Hp, (est.min + est.max) / 2));
            }
        return best;
    }

    /// <summary>Could a plain damage spell finish <paramref name="enemy"/> outright this turn?
    /// (Mirrors <see cref="TryShootBest"/>'s kill test: max roll ≥ its HP.) A chip-shove must never
    /// displace such an enemy.</summary>
    private static bool PlainCanKill(CombatEngine engine, Fighter self, Fighter enemy) =>
        DamageSpells(self).Any(s => engine.CanCast(self, s, enemy.Pos, out _)
            && engine.EstimateDamage(self, s, enemy.Pos) is { } est && est.max >= enemy.Hp);

    /// <summary>How much a shove FROM <paramref name="from"/> would cost <paramref name="target"/>:
    /// a huge number if it tips them off a void rim, else the spikes/embers crossed plus any wall
    /// slam. Used to pick an approach cell that lines the victim up with the drop (owner: set up
    /// the bad tile, don't just wander into melee). Only clean orthogonal setups score.</summary>
    private static int PushSetupScore(CombatEngine engine, CellCoord from, Fighter target, int cells)
    {
        int dx = Math.Sign(target.Pos.X - from.X), dy = Math.Sign(target.Pos.Y - from.Y);
        if (dx != 0 && dy != 0) return 0;
        var pos = target.Pos; int hazard = 0, moved = 0;
        for (int i = 0; i < cells; i++)
        {
            var next = pos.Offset(dx, dy);
            if (engine.LethalVoid && engine.Field.TileAt(next) == TileKind.Void && !target.VoidAnchored)
                return 100000;
            if (!engine.Field.IsWalkable(next) || engine.IsOccupied(next))
                return hazard + (cells - moved) * 5;   // slam into wall/body
            pos = next; moved++;
            hazard += target.HazardImmune ? 0 : engine.DangerAt(pos);
        }
        return hazard;
    }

    /// <summary>
    /// Shoot the highest-value reachable target (Bible: "highest-value target in range"). Prefer a
    /// kill we can secure this cast — and among those the <i>toughest</i>, so heavy burst isn't
    /// wasted overkilling a near-dead soft target while a real threat survives. Failing a kill,
    /// chip the softest. Each attacker re-evaluates per cast, so focus-fire stays adaptive.
    /// </summary>
    // What one point of a stolen/denied resource is worth in damage-equivalent points. These are
    // the knobs that decide whether a debuff rider beats a bigger swing.
    private const int ApTempo = 6, MpTempo = 3, RootValue = 8, RangeTempo = 2;

    /// <summary>
    /// The worth of one status landing on this victim, in damage-equivalent points. Anything that
    /// would HELP them scores negative so the AI can never gift an enemy a buff.
    /// </summary>
    private static int StatusValueOn(Fighter victim, SpellEffect e, int directDamage)
    {
        int turns = Math.Max(1, e.Max);
        int v = e.Status switch
        {
            StatusKind.Poison => e.Min * turns,
            StatusKind.MpDrain => e.Min * turns * MpTempo,
            // Pinning matters most against something that wants to close or reposition.
            StatusKind.Rooted => victim.CurrentMp > 0 ? RootValue : RootValue / 2,
            StatusKind.DamageDebuff => e.Min * turns / 4,
            // Breaking defense is worth a share of what will actually be aimed at them next.
            StatusKind.Vulnerable => e.Min * turns * Math.Max(directDamage, 10) / 100,
            StatusKind.RangeDebuff => victim.PreferredRangeMax > 1 ? e.Min * turns * RangeTempo : 0,
            StatusKind.Shield or StatusKind.Regen or StatusKind.DamageBuff
                or StatusKind.DefenseBuff or StatusKind.RangeBuff => -(e.Min * turns),
            _ => 0,
        };
        // Statuses REFRESH rather than stack (CombatEngine.ApplyStatusEffect), so re-applying one
        // the victim already carries buys far less — this is what stops debuff spam.
        if (v > 0 && victim.Statuses.Any(s => s.Kind == e.Status)) v /= 4;
        return v;
    }

    /// <summary>What casting this spell at this enemy is worth, damage plus payload.</summary>
    private static int SpellScoreAgainst(CombatEngine engine, Fighter self, Fighter victim, SpellDef spell)
    {
        var est = engine.EstimateDamage(self, spell, victim.Pos);
        int direct = est.HasValue ? (est.Value.min + est.Value.max) / 2 : 0;
        int score = direct;
        foreach (var e in spell.Effects)
            score += e.Kind switch
            {
                EffectKind.StealAp => e.Min * ApTempo,
                EffectKind.StealMp => e.Min * MpTempo,
                EffectKind.StealRange => e.Min * RangeTempo * (victim.PreferredRangeMax > 1 ? 2 : 1),
                EffectKind.ApplyStatus => StatusValueOn(victim, e, direct),
                // Aimed at an enemy these are pure charity (the engine no-ops most of them, but
                // scoring them negative keeps the AI from ever wasting the AP).
                EffectKind.Heal => -(e.Min + e.Max) / 2,
                EffectKind.GrantAp => -e.Min * ApTempo,
                _ => 0,
            };
        return score;
    }

    /// <summary>
    /// Pick the single best (spell, target) this unit can cast right now. The caller's turn loop
    /// runs this repeatedly, so a whole AP budget gets spent as a sequence.
    /// Previously this only looked at damage spells, ordered them by AP cost and took the FIRST
    /// castable one — so a cheap spell only ever fired when the expensive ones were on cooldown,
    /// and every rider (poison, root, vulnerable, resource theft) was scored as zero and never
    /// chosen on purpose.
    /// </summary>
    private static bool TryShootBest(CombatEngine engine, Fighter self)
    {
        Fighter? bestE = null; SpellDef? bestS = null;
        // Ranked lexicographically: land a kill first; among kills drop the BIGGEST thing you can
        // (and spend the least AP doing it); otherwise take the highest-value cast.
        (int kill, int primary, int secondary) best = (-1, int.MinValue, int.MinValue);

        foreach (var enemy in Enemies(engine, self))
            foreach (var spell in self.Spells)
            {
                if (!engine.CanCast(self, spell, enemy.Pos, out _)) continue;
                var est = engine.EstimateDamage(self, spell, enemy.Pos);
                bool kills = est.HasValue && est.Value.max >= enemy.Hp;
                int score = SpellScoreAgainst(engine, self, enemy, spell);
                if (!kills && score <= 0) continue;   // never gift a buff or burn AP for nothing

                var rank = kills ? (1, enemy.Hp, -spell.ApCost) : (0, score, 0);
                if (rank.CompareTo(best) > 0) { best = rank; bestE = enemy; bestS = spell; }
            }

        return bestE != null && engine.TryCast(self, bestS!, bestE.Pos);
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

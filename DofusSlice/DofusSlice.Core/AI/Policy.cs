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
        for (int guard = 0; guard < 24; guard++)
        {
            if (!self.IsAlive || engine.Outcome != FightOutcome.Ongoing) return;
            bool acted = self.Policy switch
            {
                AiPolicy.Skirmisher or AiPolicy.Artillery => Kite(engine, self),
                AiPolicy.Flanker => Charge(engine, self, preferSoftest: true),
                _ => Charge(engine, self, preferSoftest: false),
            };
            if (!acted) return;
        }
    }

    // ----- Melee policies: close the distance and hit ------------------------------

    private static bool Charge(CombatEngine engine, Fighter self, bool preferSoftest)
    {
        if (TryShootBest(engine, self)) return true;

        var target = preferSoftest ? Softest(engine, self) : Nearest(engine, self);
        if (target == null) return false;
        return StepToward(engine, self, target.Pos);
    }

    // ----- Ranged policies: hold range, shoot the softest --------------------------

    private static bool Kite(CombatEngine engine, Fighter self)
    {
        // Fire from where we stand if anything is already in the sights.
        if (TryShootBest(engine, self)) return true;
        if (self.CurrentMp <= 0) return false;

        var reachable = engine.MovementRange(self);
        if (reachable.Count == 0) return false;

        // Best: step onto a cell that can hit an enemy, preferring the safest such cell
        // (furthest from the nearest enemy) so the kiter keeps its distance while still firing.
        CellCoord? firing = reachable.Keys
            .Where(cell => CanHitAnyEnemyFrom(engine, self, cell))
            .OrderByDescending(cell => DistToNearestEnemy(engine, self, cell))
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
            .OrderByDescending(cell => DistToNearestEnemy(engine, self, cell))
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

    /// <summary>Cast at the softest enemy we can currently hit, with the strongest affordable spell.</summary>
    private static bool TryShootBest(CombatEngine engine, Fighter self)
    {
        foreach (var enemy in Enemies(engine, self).OrderBy(f => f.Hp))
            foreach (var spell in DamageSpells(self).OrderByDescending(s => s.ApCost))
                if (engine.CanCast(self, spell, enemy.Pos, out _))
                    return engine.TryCast(self, spell, enemy.Pos);
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

        var next = reachable.Keys.OrderBy(Geo).ThenBy(c => reachable[c]).First();
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

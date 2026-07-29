using DofusSlice.Core.Combat;
using DofusSlice.Core.Grid;
using DofusSlice.Core.Spells;

namespace DofusSlice.Core.AI;

/// <summary>
/// A deliberately simple greedy brain for Incarnam mobs: pick the nearest hero, cast the
/// hardest-hitting affordable spell that can already reach them, otherwise walk into range
/// and try again — repeating until out of points. Good enough to make the slice a real
/// fight without pretending to be a full tactical AI.
/// </summary>
public static class MobBrain
{
    public static void TakeTurn(CombatEngine engine, Fighter self)
    {
        var target = NearestEnemy(engine, self);
        if (target == null) return;

        // Keep acting until a full pass makes no progress.
        for (int guard = 0; guard < 16; guard++)
        {
            if (!self.IsAlive || engine.Outcome != FightOutcome.Ongoing) return;

            if (TryAttack(engine, self, target)) continue;
            if (TryApproach(engine, self, target)) continue;
            break;
        }
    }

    private static Fighter? NearestEnemy(CombatEngine engine, Fighter self) =>
        engine.Fighters
            .Where(f => f.IsAlive && f.Team != self.Team)
            .OrderBy(f => f.Pos.DistanceTo(self.Pos))
            .FirstOrDefault();

    private static IEnumerable<SpellDef> DamageSpells(Fighter self) =>
        self.Spells.Where(s => s.Effects.Any(e => e.Kind is EffectKind.Damage or EffectKind.Lifesteal));

    private static bool TryAttack(CombatEngine engine, Fighter self, Fighter target)
    {
        // Prefer the most expensive (usually strongest) spell that lands right now.
        foreach (var spell in DamageSpells(self).OrderByDescending(s => s.ApCost))
        {
            if (engine.CanCast(self, spell, target.Pos, out _))
                return engine.TryCast(self, spell, target.Pos);
        }
        return false;
    }

    private static bool TryApproach(CombatEngine engine, Fighter self, Fighter target)
    {
        if (self.CurrentMp <= 0) return false;

        var affordable = DamageSpells(self)
            .Where(s => s.ApCost <= self.CurrentAp)
            .OrderBy(s => s.MaxRange)
            .ToList();

        var reachable = engine.MovementRange(self);
        if (reachable.Count == 0) return false;

        // Best case: step onto a cell from which some affordable spell can hit the target.
        CellCoord? firingCell = reachable.Keys
            .Where(cell => affordable.Any(s => CanHitFrom(engine, cell, self.Pos, target.Pos, s)))
            .OrderBy(cell => reachable[cell])
            .Cast<CellCoord?>()
            .FirstOrDefault();

        if (firingCell is CellCoord fc)
            return engine.TryMove(self, fc);

        // Otherwise close the distance, routing around walls via a geodesic field.
        var field = Pathfinding.DistanceField(engine.Field, target.Pos);
        int Geodesic(CellCoord c) => field.TryGetValue(c, out int d) ? d : int.MaxValue;

        var closer = reachable.Keys
            .OrderBy(Geodesic)
            .ThenBy(c => reachable[c])
            .First();

        if (Geodesic(closer) < Geodesic(self.Pos))
            return engine.TryMove(self, closer);

        return false;
    }

    /// <summary>Would <paramref name="spell"/> be castable at the target if the caster stood on <paramref name="from"/>?</summary>
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

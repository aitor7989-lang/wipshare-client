using DofusSlice.Core.Combat;
using DofusSlice.Core.Content;
using DofusSlice.Core.Grid;
using DofusSlice.Core.Spells;

namespace DofusSlice.Sim;

/// <summary>
/// A scripted stand-in for the human player, so the headless sim can play a whole fight.
/// It exercises the full Iop kit: Jump to close distance, Iop's Wrath in melee, Pressure at
/// range, Intimidation to shove. The real game hands these decisions to the player instead.
/// </summary>
public static class HeroAuto
{
    public static void TakeTurn(CombatEngine engine, Fighter self)
    {
        for (int guard = 0; guard < 20; guard++)
        {
            if (!self.IsAlive || engine.Outcome != FightOutcome.Ongoing) return;

            var target = engine.Fighters
                .Where(f => f.IsAlive && f.Team != self.Team)
                .OrderBy(f => f.Pos.DistanceTo(self.Pos))
                .FirstOrDefault();
            if (target == null) return;

            bool adjacent = self.Pos.IsAdjacentTo(target.Pos);

            if (adjacent && engine.CanCast(self, SpellLibrary.IopsWrath, target.Pos, out _))
            { engine.TryCast(self, SpellLibrary.IopsWrath, target.Pos); continue; }

            if (engine.CanCast(self, SpellLibrary.Pressure, target.Pos, out _))
            { engine.TryCast(self, SpellLibrary.Pressure, target.Pos); continue; }

            if (adjacent && engine.CanCast(self, SpellLibrary.Intimidation, target.Pos, out _))
            { engine.TryCast(self, SpellLibrary.Intimidation, target.Pos); continue; }

            if (Approach(engine, self, target)) continue;
            if (JumpToward(engine, self, target)) continue;
            break;
        }
    }

    private static bool Approach(CombatEngine engine, Fighter self, Fighter target)
    {
        if (self.CurrentMp <= 0) return false;
        var reachable = engine.MovementRange(self);
        if (reachable.Count == 0) return false;

        var spells = new[] { SpellLibrary.IopsWrath, SpellLibrary.Pressure, SpellLibrary.Intimidation }
            .Where(s => s.ApCost <= self.CurrentAp).ToList();

        CellCoord? firing = reachable.Keys
            .Where(cell => spells.Any(s => CanHitFrom(engine, cell, self.Pos, target.Pos, s)))
            .OrderBy(cell => reachable[cell])
            .Cast<CellCoord?>()
            .FirstOrDefault();
        if (firing is CellCoord fc) return engine.TryMove(self, fc);

        var field = Pathfinding.DistanceField(engine.Field, target.Pos);
        int Geodesic(CellCoord c) => field.TryGetValue(c, out int d) ? d : int.MaxValue;
        var closer = reachable.Keys.OrderBy(Geodesic).ThenBy(c => reachable[c]).First();
        if (Geodesic(closer) < Geodesic(self.Pos)) return engine.TryMove(self, closer);
        return false;
    }

    private static bool JumpToward(CombatEngine engine, Fighter self, Fighter target)
    {
        // Jump to a free cell next to the target so we can melee next iteration.
        var landing = engine.Field.Orthogonal(target.Pos)
            .Where(c => engine.CanCast(self, SpellLibrary.Jump, c, out _))
            .OrderBy(c => c.DistanceTo(self.Pos))
            .Cast<CellCoord?>()
            .FirstOrDefault();
        if (landing is CellCoord lc) return engine.TryCast(self, SpellLibrary.Jump, lc);
        return false;
    }

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

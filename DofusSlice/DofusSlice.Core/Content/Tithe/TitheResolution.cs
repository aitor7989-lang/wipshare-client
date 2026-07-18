using DofusSlice.Core.Combat;

namespace DofusSlice.Core.Content.Tithe;

/// <summary>
/// Resolves the meta consequences of a finished TITHE fight (Bible §3.1.4, §6.3): split the kill
/// XP across the crew, and decide each downed unit's fate — a player-managed unit is dragged out
/// <b>Wounded</b> (-1 PA / -1 PM), a hired mercenary <b>dies permanently</b>. Drops are a later
/// pass (§9 mining). Pure logic so the game and the sim share one source of truth.
/// </summary>
public static class TitheResolution
{
    public sealed record UnitResult(string Id, string Name, int XpGained, bool Wounded, bool Died);
    public sealed record Result(FightOutcome Outcome, int XpPool, IReadOnlyList<UnitResult> Units);

    public static Result Resolve(CombatEngine engine)
    {
        var crew = engine.Fighters.Where(f => f.Team == Team.Player).ToList();

        // XP pool = sum of the defeated skeletons' table XP.
        int pool = engine.Fighters
            .Where(f => f.Team == Team.Enemy && !f.IsAlive)
            .Sum(f => TitheContent.MobXp(f.Archetype));

        // Only crew that survived or were merely downed (not permanently dead mercs) share XP.
        // Dofus splits level-weighted; with everyone at level 1 that is an even split for now.
        var sharers = crew.Where(f => f.IsAlive || !f.IsMercenary).ToList();
        int totalWeight = sharers.Sum(f => f.Level);
        bool won = engine.Outcome == FightOutcome.Victory;

        var results = new List<UnitResult>();
        foreach (var f in crew)
        {
            bool down = !f.IsAlive;
            bool died = down && f.IsMercenary;                 // mercenaries are gone for good
            bool wounded = down && !f.IsMercenary && won;      // player-managed, side won → Wounded
            int xp = (won && (f.IsAlive || wounded) && totalWeight > 0)
                ? pool * f.Level / totalWeight : 0;

            if (won && (f.IsAlive || wounded))
            {
                f.Xp += xp;
                if (wounded) f.Hp = 1; // dragged out alive; the Wounded status is applied by the caller
            }
            results.Add(new UnitResult(f.Id, f.Name, xp, wounded, died));
        }

        return new Result(engine.Outcome, pool, results);
    }
}

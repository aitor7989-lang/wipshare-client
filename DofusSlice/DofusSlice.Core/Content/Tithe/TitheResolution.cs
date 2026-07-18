using DofusSlice.Core.Combat;

namespace DofusSlice.Core.Content.Tithe;

/// <summary>
/// Resolves the meta consequences of a finished TITHE fight (Bible §3.1.4, §5, §6.3): split the
/// kill XP across the crew, roll each defeated mob's essence drop, and decide each downed unit's
/// fate — a player-managed unit is dragged out <b>Wounded</b> (-1 PA / -1 PM), a hired mercenary
/// <b>dies permanently</b>. Pure logic so the game and the sim share one source of truth.
/// </summary>
public static class TitheResolution
{
    public sealed record UnitResult(string Id, string Name, bool Mercenary, int XpGained, bool Wounded, bool Died);
    public sealed record Result(FightOutcome Outcome, int XpPool, IReadOnlyList<UnitResult> Units,
                                IReadOnlyList<string> Drops, IReadOnlyList<string> GearDrops);

    public static Result Resolve(CombatEngine engine)
    {
        var crew = engine.Fighters.Where(f => f.Team == Team.Player).ToList();
        bool won = engine.Outcome == FightOutcome.Victory;

        var defeatedMobs = engine.Fighters.Where(f => f.Team == Team.Enemy && !f.IsAlive).ToList();

        // XP pool = sum of the defeated mobs' table XP.
        int pool = defeatedMobs.Sum(f => TitheContent.MobXp(f.Archetype));

        // Loot rolls (only a won fight collects): each defeated mob rolls its essence and, separately,
        // a chance at an Adventurer-set piece (Bible §5 drop-table peaks). A gear roll yields a set
        // token here; the concrete unowned piece is chosen when it's folded into the campaign.
        var drops = new List<string>();
        var gear = new List<string>();
        if (won)
            foreach (var f in defeatedMobs)
            {
                var (essence, rate) = TitheContent.MobDrop(f.Archetype);
                if (essence != null && rate > 0 && engine.Rng.Roll(1, 100) <= rate)
                    drops.Add(essence);

                int gearRate = TitheContent.MobGear(f.Archetype);
                if (gearRate > 0 && engine.Rng.Roll(1, 100) <= gearRate)
                    gear.Add(TitheContent.GraveyardSet);
            }

        // Only crew that survived or were merely downed (not permanently dead mercs) share XP.
        // Dofus splits level-weighted; with everyone at level 1 that is an even split for now.
        var sharers = crew.Where(f => f.IsAlive || !f.IsMercenary).ToList();
        int totalWeight = sharers.Sum(f => f.Level);

        var results = new List<UnitResult>();
        foreach (var f in crew)
        {
            bool down = !f.IsAlive;
            // A downed unit is dragged out Wounded only if it is player-managed AND the side won.
            // Otherwise a down means gone for good: any mercenary, or anyone in a lost fight
            // (all crew at 0 → campaign over, nobody is dragged back).
            bool wounded = down && !f.IsMercenary && won;
            bool died = down && !wounded;
            int xp = (won && (f.IsAlive || wounded) && totalWeight > 0)
                ? pool * f.Level / totalWeight : 0;
            xp = xp * (100 + f.Wisdom) / 100; // 1.29 rule: each point of Wisdom is +1% XP

            if (won && (f.IsAlive || wounded))
            {
                f.Xp += xp;
                if (wounded) f.Hp = 1; // dragged out alive; the Wounded status is applied by the caller
            }
            results.Add(new UnitResult(f.Id, f.Name, f.IsMercenary, xp, wounded, died));
        }

        return new Result(engine.Outcome, pool, results, drops, gear);
    }
}

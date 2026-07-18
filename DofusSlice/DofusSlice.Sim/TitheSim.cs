using DofusSlice.Core.AI;
using DofusSlice.Core.Combat;
using DofusSlice.Core.Content.Tithe;

namespace DofusSlice.Sim;

/// <summary>
/// Headless harness for TITHE's watched combat: build the crew-vs-skeletons fight from the data
/// tables and let every unit act by its <see cref="AiPolicy"/>. The printed play-by-play is the
/// Bible M1 fun test — a fight a stranger can narrate — and <see cref="Balance"/> samples the
/// win rate so the numbers can be tuned before art.
/// </summary>
public static class TitheSim
{
    public static int PlayOne(int seed, bool verbose, bool boss = false)
    {
        var engine = TitheContent.BuildFight(TitheContent.DefaultCrew, new SystemRng(seed), boss);
        if (verbose)
        {
            engine.Logged += Console.WriteLine;
            Console.WriteLine($"TITHE — {(boss ? "The Sexton's Court" : "The Graveyard")} (watched combat, seed {seed})");
            Console.WriteLine($"Crew vs {engine.Fighters.Count(f => f.Team == Team.Enemy)} enemies. All units act by AI policy.\n");
            foreach (var f in engine.Fighters.Where(f => f.Team == Team.Player))
                Console.WriteLine($"  {f.Name,-8} — {TitheContent.Blurb(f.Archetype)}");
            Console.WriteLine();
        }

        RunToEnd(engine);

        if (verbose)
        {
            Console.WriteLine($"\nOutcome after round {engine.Round}: {engine.Outcome}");
            foreach (var f in engine.Fighters)
                Console.WriteLine($"  [{(f.Team == Team.Player ? "crew" : "mob ")}] {f.Name,-14} " +
                                  $"{(f.IsAlive ? $"{f.Hp,3}/{f.MaxHp} HP" : "DOWN")}");
            PrintResolution(engine);
        }
        return engine.Outcome == FightOutcome.Ongoing ? 1 : 0;
    }

    public static int Balance(int trials, bool boss = false)
    {
        int wins = 0, unresolved = 0, defeats = 0, cleanWins = 0, costlyWins = 0, totalDowned = 0;
        var downHist = new int[4]; // crew members downed: 0,1,2,3
        for (int i = 1; i <= trials; i++)
        {
            var engine = TitheContent.BuildFight(TitheContent.DefaultCrew, new SystemRng(i * 101 + 3), boss);
            RunToEnd(engine);
            int downed = engine.Fighters.Count(f => f.Team == Team.Player && !f.IsAlive);
            totalDowned += downed;
            downHist[Math.Clamp(downed, 0, 3)]++;
            switch (engine.Outcome)
            {
                case FightOutcome.Victory when downed == 0: wins++; cleanWins++; break;
                case FightOutcome.Victory: wins++; costlyWins++; break;
                case FightOutcome.Defeat: defeats++; break;
                default: unresolved++; break;
            }
        }
        Console.WriteLine($"TITHE balance — {(boss ? "The Sexton's Court" : "The Graveyard")} — over {trials} seeds:");
        Console.WriteLine($"  wins {wins} ({100.0 * wins / trials:0}%)  —  clean {cleanWins}, costly {costlyWins}");
        Console.WriteLine($"  DEFEATS (campaign-over) {defeats} ({100.0 * defeats / trials:0}%),  unresolved {unresolved}");
        Console.WriteLine($"  crew downed histogram  0:{downHist[0]}  1:{downHist[1]}  2:{downHist[2]}  3:{downHist[3]}");
        Console.WriteLine($"  avg crew members downed per fight: {(double)totalDowned / trials:0.00}");
        return unresolved == 0 ? 0 : 1;
    }

    private static void RunToEnd(CombatEngine engine, int maxRounds = 40)
    {
        engine.Start();
        while (engine.Outcome == FightOutcome.Ongoing && engine.Round <= maxRounds)
        {
            Policy.TakeTurn(engine, engine.Current);
            engine.EndTurn();
        }
    }

    /// <summary>Fight-end meta per Bible §3.1.4/§6.3: XP split, Wounded vs merc death.</summary>
    private static void PrintResolution(CombatEngine engine)
    {
        var res = TitheResolution.Resolve(engine);
        Console.WriteLine($"\n-- aftermath --");
        Console.WriteLine($"XP pool from kills: {res.XpPool}");
        foreach (var u in res.Units)
            Console.WriteLine($"  {u.Name,-8} +{u.XpGained} XP" +
                              (u.Wounded ? "  (WOUNDED — dragged out, -1 PA/-1 PM)" : "") +
                              (u.Died ? (u.Mercenary ? "  (DEAD — mercenary lost)" : "  (DEAD — avatar fell)") : ""));
        Console.WriteLine(res.Drops.Count > 0
            ? $"Essences dropped: {string.Join(", ", res.Drops)}"
            : "Essences dropped: none");
    }
}

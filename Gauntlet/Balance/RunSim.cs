using System.Diagnostics;
using DofusSlice.Core.AI;
using DofusSlice.Core.Combat;
using DofusSlice.Core.Content.Tithe;

namespace Gauntlet.Balance;

/// <summary>
/// The balance harness (g10, owner's ask: "a hundred runs in a minute — I want to learn
/// from it"). Plays FULL runs headless — same boards, same waves, same covenant, same
/// mechanics as the game, because both sides call the very same RunRules — with the
/// autoplay Policy holding the leader's hand. Prints who wins, who dies, and where.
///
///   dotnet run -- --sim              300 runs, all three classes
///   dotnet run -- --sim 500 bulwark  500 runs of one class
///   dotnet run -- --sim 300 all 99   a different seed for the whole batch
/// </summary>
public static class RunSim
{
    private sealed class Tally
    {
        public int Runs, Wins, TollOuts;
        public readonly Dictionary<string, int> DeathsAt = new();
        public long Kills, Falls, Stones, Levels, FightsCleared;
        public double BossEntryHp; public int BossEntries;
    }

    public static int Main(string[] args)
    {
        int runs = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 300;
        string only = args.Length > 1 ? args[1] : "all";
        int seed0 = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 20260720;

        var classes = only is "all" or "" ? new[] { "cannon", "archer", "bulwark" } : new[] { only };
        var sw = Stopwatch.StartNew();
        var tallies = new Dictionary<string, Tally>();

        foreach (var cls in classes)
        {
            var t = new Tally();
            tallies[cls] = t;
            for (int i = 0; i < runs; i++) Simulate(cls, seed0 + i * 7, t);
        }

        Console.WriteLine($"THE GAUNTLET — balance ledger · {runs} runs/class · seed {seed0} · {sw.ElapsedMilliseconds} ms");
        Console.WriteLine(new string('-', 100));
        Console.WriteLine($"{"CLASS",-9}{"WIN%",6}{"  F1",5}{"  F2",5}{"  F3",5}{" SEXTON",8}"
            + $"{"  TOLL-OUT",10}{"  AVG LVL",9}{"  KILLS",8}{"  FALLS",8}{"  STONES",9}{"  BOSS HP%",10}");
        foreach (var (cls, t) in tallies)
        {
            double W(string k) => t.DeathsAt.GetValueOrDefault(k);
            Console.WriteLine($"{cls,-9}{100.0 * t.Wins / Math.Max(1, t.Runs),5:0.0}%"
                + $"{W("FIGHT 1 OF 3"),5:0}{W("FIGHT 2 OF 3"),5:0}{W("FIGHT 3 OF 3"),5:0}{W("THE SEXTON"),8:0}"
                + $"{t.TollOuts,10}{(double)t.Levels / Math.Max(1, t.Runs),9:0.0}"
                + $"{(double)t.Kills / Math.Max(1, t.Runs),8:0.0}{(double)t.Falls / Math.Max(1, t.Runs),8:0.0}"
                + $"{(double)t.Stones / Math.Max(1, t.Runs),9:0.0}"
                + $"{(t.BossEntries == 0 ? 0 : 100.0 * t.BossEntryHp / t.BossEntries),9:0.0}%");
        }
        Console.WriteLine(new string('-', 100));
        Console.WriteLine("F1/F2/F3/SEXTON = deaths at that stage · TOLL-OUT = the bell rang before the third pack fell");
        Console.WriteLine("BOSS HP% = average health walking into the Sexton's court");
        return 0;
    }

    private static void Simulate(string classId, int seed, Tally t)
    {
        t.Runs++;
        var you = new CampaignUnit { Id = "avatar", ClassId = classId, Name = "You", IsAvatar = true };
        var st = new RunState();
        var picks = new Random(seed * 31 + 5);
        int seedVar = seed;
        bool won = false;

        for (int fight = 0; fight < 8; fight++)
        {
            string stage = RunRules.FightLabel(st);
            if (stage == "THE SEXTON")
            {
                t.BossEntries++;
                t.BossEntryHp += (you.CurrentHp ?? TitheContent.UnitMaxHp(you))
                                 / (double)TitheContent.UnitMaxHp(you);
            }

            var (engine, avatar) = RunRules.CreateFight(st, you, null, ref seedVar);
            engine.Emitted += e => RunRules.HandleMechanics(engine, e, st, avatar, null);
            engine.Start();

            for (int guard = 0; guard < 600 && engine.Outcome == FightOutcome.Ongoing; guard++)
            {
                Policy.TakeTurn(engine, engine.Current);
                if (engine.Outcome == FightOutcome.Ongoing) engine.EndTurn();
            }

            if (engine.Outcome != FightOutcome.Victory)
            {
                t.DeathsAt[stage] = t.DeathsAt.GetValueOrDefault(stage) + 1;
                if (st.SextonNow && st.FightIndex < 3) t.TollOuts++;
                break;
            }

            you.CurrentHp = Math.Max(1, avatar.Hp);
            int before = you.Level;
            you.GainXp(RunRules.XpForFight(st.FightIndex));
            st.PendingLevels += you.Level - before;

            if (st.SextonNow && st.FightIndex < 3 || st.FightIndex >= 3)
            {
                if (st.FightIndex >= 3) { won = true; break; }
                st.FightIndex = 3;
                ApplyPick(RunRules.RollSpoils(st, you, new Random(++seedVar), null), st, you, picks);
            }
            else
            {
                st.FightIndex++;
                if (st.FightIndex == 2 && st.Road == "screaming") st.ExtraPick = true;
                ApplyPick(RunRules.RollSpoils(st, you, new Random(++seedVar), null), st, you, picks);
            }

            // The between-fight chain, exactly as the game's Advance() walks it.
            while (st.PendingLevels > 0)
            {
                st.PendingLevels--;
                ApplyLevelPick(RunRules.RollLevelCards(st, you, new Random(++seedVar)), picks);
            }
            if (st.ExtraPick)
            {
                st.ExtraPick = false;
                ApplyPick(RunRules.RollSpoils(st, you, new Random(++seedVar), null), st, you, picks);
            }
            if (st.FightIndex == 1 && st.Road == "")
                RunRules.RollRoadCards(st, null)[picks.Next(2)].Apply();
            if (st.FightIndex == 2 && !st.EventDone)
            {
                var (_, _, cards) = RunRules.RollEventCards(st, you, new Random(++seedVar));
                cards[picks.Next(cards.Count)].Apply();
            }
        }

        if (won) t.Wins++;
        t.Kills += st.Kills; t.Falls += st.Falls; t.Stones += st.RunStones;
        t.Levels += you.Level; t.FightsCleared += Math.Min(st.FightIndex, 4);
    }

    /// <summary>A decent hand, not a perfect one: mend when bleeding, chase set pieces,
    /// then essences — the way a competent player skims a spoils screen.</summary>
    private static void ApplyPick(List<PickCard> cards, RunState st, CampaignUnit you, Random rnd)
    {
        int maxHp = TitheContent.UnitMaxHp(you);
        int hp = you.CurrentHp ?? maxHp;
        PickCard? choice = null;
        // Any sane hand mends before walking into HIS court; mid-run, only when bleeding.
        int mendBar = st.FightIndex >= 3 ? maxHp * 4 / 5 : maxHp / 2;
        if (hp < mendBar) choice = cards.FirstOrDefault(c => c.Title == "MEND");
        choice ??= cards.Where(c => c.Kind == "GEAR")
            .OrderByDescending(c => RunRules.GearPool.Any(g => g.Name == c.Title
                && (st.FamilyCount(g.Family) > 0 || !st.Gear.ContainsKey(g.Slot))) ? 1 : 0)
            .FirstOrDefault();
        choice ??= cards.FirstOrDefault(c => c.Kind == "ESSENCE");
        choice ??= cards[rnd.Next(cards.Count)];
        choice.Apply();
    }

    /// <summary>Levels: real spells first (LEARN/DEEPEN lead the hand), else a stat word.</summary>
    private static void ApplyLevelPick(List<PickCard> cards, Random rnd)
    {
        var choice = cards.FirstOrDefault(c => c.Title.StartsWith("LEARN") || c.Title.StartsWith("DEEPEN"))
                     ?? cards[rnd.Next(cards.Count)];
        choice.Apply();
    }
}

using System.Diagnostics;
using DofusSlice.Core.AI;
using DofusSlice.Core.Combat;
using DofusSlice.Core.Content.Tithe;

namespace Gauntlet.Balance;

/// <summary>
/// The balance harness (g10/g11, owner's ask: "a hundred runs in a minute — I want to
/// learn from it"). Plays FULL runs headless — the same dealt road, waves, covenant,
/// shops and mysteries as the game, because both sides call the very same RunRules —
/// with the autoplay Policy holding the leader's hand and a sane bot working the
/// rooms. Prints who wins, who dies, and where.
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
        public long Kills, Falls, Stones, Levels, Spent;
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
        Console.WriteLine(new string('-', 108));
        Console.WriteLine($"{"CLASS",-9}{"WIN%",6}{"  EARLY",7}{"  MID",6}{"  LATE",7}{" SEXTON",8}"
            + $"{"  TOLL-OUT",10}{"  AVG LVL",9}{"  KILLS",8}{"  STONES",9}{"  SPENT",8}{"  BOSS HP%",10}");
        foreach (var (cls, t) in tallies)
        {
            double W(string k) => t.DeathsAt.GetValueOrDefault(k);
            Console.WriteLine($"{cls,-9}{100.0 * t.Wins / Math.Max(1, t.Runs),5:0.0}%"
                + $"{W("EARLY"),7:0}{W("MID"),6:0}{W("LATE"),7:0}{W("SEXTON"),8:0}"
                + $"{t.TollOuts,10}{(double)t.Levels / Math.Max(1, t.Runs),9:0.0}"
                + $"{(double)t.Kills / Math.Max(1, t.Runs),8:0.0}"
                + $"{(double)t.Stones / Math.Max(1, t.Runs),9:0.0}{(double)t.Spent / Math.Max(1, t.Runs),8:0.0}"
                + $"{(t.BossEntries == 0 ? 0 : 100.0 * t.BossEntryHp / t.BossEntries),9:0.0}%");
        }
        Console.WriteLine(new string('-', 108));
        Console.WriteLine("EARLY = died in fights 1-2 · MID = fights 3-4 · LATE = fights 5-6 · SEXTON = at his court");
        Console.WriteLine("SPENT = stones left at the trader · BOSS HP% = health walking into the court");
        return 0;
    }

    private static void Simulate(string classId, int seed, Tally t)
    {
        t.Runs++;
        var you = new CampaignUnit { Id = "avatar", ClassId = classId, Name = "You", IsAvatar = true };
        var st = new RunState { Road = RunRules.BuildRoad(new Random(seed * 13 + 1)) };
        var picks = new Random(seed * 31 + 5);
        int seedVar = seed;
        bool won = false, dead = false;

        for (int guardRooms = 0; guardRooms < 24 && !dead && !won; guardRooms++)
        {
            var kind = st.SextonNow ? RoomKind.Boss : st.RoomNow;
            switch (kind)
            {
                case RoomKind.Shop:
                    WorkTheStall(st, you, picks, ref seedVar, t);
                    break;
                case RoomKind.Event:
                {
                    var (_, _, cards) = RunRules.RollEventCards(st, you, new Random(++seedVar));
                    cards[picks.Next(cards.Count)].Apply();
                    break;
                }
                case RoomKind.Mystery:
                {
                    var (_, _, cards) = RunRules.RollMysteryCards(st, you, new Random(++seedVar), null);
                    // Gamble while healthy, take the sure coin while bleeding — a human's line.
                    int hp = you.CurrentHp ?? TitheContent.UnitMaxHp(you);
                    bool brave = hp * 10 >= TitheContent.UnitMaxHp(you) * 7;
                    cards[brave ? 0 : 1].Apply();
                    break;
                }
                default:
                {
                    if (kind == RoomKind.Boss)
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
                        string stage = kind == RoomKind.Boss || st.SextonNow ? "SEXTON"
                            : st.FightsWon < 2 ? "EARLY" : st.FightsWon < 4 ? "MID" : "LATE";
                        t.DeathsAt[stage] = t.DeathsAt.GetValueOrDefault(stage) + 1;
                        if (st.SextonNow && kind != RoomKind.Boss) t.TollOuts++;
                        dead = true;
                        break;
                    }
                    if (kind == RoomKind.Boss) { won = true; break; }

                    you.CurrentHp = Math.Max(1, avatar.Hp);
                    int before = you.Level;
                    you.GainXp(RunRules.XpForFight(st.FightsWon));
                    st.PendingLevels += you.Level - before;
                    st.FightsWon++;
                    ApplySpoils(RunRules.RollSpoils(st, you, new Random(++seedVar), null), st, you, picks);
                    while (st.PendingLevels > 0)
                    {
                        st.PendingLevels--;
                        ApplyLevelPick(RunRules.RollLevelCards(st, you, new Random(++seedVar)), picks);
                    }
                    break;
                }
            }
            if (!dead && !won && !st.SextonNow) st.Room++;
        }

        if (won) t.Wins++;
        t.Kills += st.Kills; t.Falls += st.Falls; t.Stones += st.RunStones;
        t.Levels += you.Level;
    }

    /// <summary>The trader bot: mend when hurt, wind the bell when it's late, buy the
    /// blade that agrees with your element, chase the set — and always keep bus fare.</summary>
    private static void WorkTheStall(RunState st, CampaignUnit you, Random picks, ref int seedVar, Tally t)
    {
        var cards = RunRules.RollShop(st, you, new Random(++seedVar), null);
        int maxHp = TitheContent.UnitMaxHp(you);
        for (int guard = 0; guard < 6; guard++)
        {
            int hp = you.CurrentHp ?? maxHp;
            PickCard? buy = null;
            if (hp * 10 < maxHp * 7) buy = cards.FirstOrDefault(c => c.Title == "MEND" && c.Price <= st.RunStones);
            if (buy == null && st.Tolls < 12)
                buy = cards.FirstOrDefault(c => c.Title.StartsWith("OIL") && c.Price <= st.RunStones);
            buy ??= cards
                .Where(c => c.Price <= st.RunStones - 10)
                .OrderByDescending(c => RunRules.GearPool.Any(g => g.Name == c.Title
                    && (g.Elem == RunRules.ClassElement(you.ClassId)
                        || st.FamilyCount(g.Family) > 0 && g.Elem == null)) ? 1 : 0)
                .FirstOrDefault(c => c.Title != "MEND" && !c.Title.StartsWith("OIL"));
            if (buy == null) break;
            st.RunStones -= buy.Price;
            t.Spent += buy.Price;
            buy.Apply();
            cards.Remove(buy);
        }
    }

    /// <summary>A decent hand, not a perfect one: mend when bleeding (always before HIM),
    /// take gear that agrees with your element, then essences.</summary>
    private static void ApplySpoils(List<PickCard> cards, RunState st, CampaignUnit you, Random rnd)
    {
        int maxHp = TitheContent.UnitMaxHp(you);
        int hp = you.CurrentHp ?? maxHp;
        PickCard? choice = null;
        int mendBar = st.BossIsNext ? maxHp * 4 / 5 : maxHp / 2;
        if (hp < mendBar) choice = cards.FirstOrDefault(c => c.Title == "MEND");
        choice ??= cards.Where(c => c.Kind == "GEAR")
            .OrderByDescending(c => RunRules.GearPool.Any(g => g.Name == c.Title
                && (g.Elem == RunRules.ClassElement(you.ClassId)
                    || g.Elem == null && (st.FamilyCount(g.Family) > 0 || !st.Gear.ContainsKey(g.Slot)))) ? 1 : 0)
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

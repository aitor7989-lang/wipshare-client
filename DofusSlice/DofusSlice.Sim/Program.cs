using DofusSlice.Core.Combat;
using DofusSlice.Core.Content;
using DofusSlice.Core.Content.Tithe;
using DofusSlice.Sim;

if (args.Length > 0 && args[0] == "effects")
{
    Console.WriteLine("Combat effects self-test:\n");
    return EffectsTest.Run();
}

if (args.Length > 0 && args[0] == "warren")
{
    // The WANDERER path generator, headless. dump = look at it, stats = tune it, verify = trust it.
    int wseed = args.Select(a => int.TryParse(a, out int v) ? v : (int?)null).FirstOrDefault(v => v != null) ?? 1;
    if (args.Contains("stats")) return WarrenSim.Stats(wseed, 4000);
    if (args.Contains("verify")) return WarrenSim.Verify(200, 400);
    return WarrenSim.Dump(wseed, 70);
}

if (args.Length > 0 && args[0] == "campaign")
{
    // The whole M2 loop, headless: city prep -> dive -> eject -> city, repeating.
    if (args.Contains("survey"))
        return CampaignSim.Survey(args.Select(a => int.TryParse(a, out int v) ? v : (int?)null)
            .FirstOrDefault(v => v != null) ?? 40);
    if (args.Contains("crypt"))
        return CampaignSim.Crypt(args.Select(a => int.TryParse(a, out int v) ? v : (int?)null).FirstOrDefault(v => v != null) ?? 7);
    if (args.Contains("survivors"))
        return CampaignSim.Survivors(args.Select(a => int.TryParse(a, out int v) ? v : (int?)null).FirstOrDefault(v => v != null) ?? 60);
    if (args.Contains("progression") || args.Contains("gear") || args.Contains("stats"))
        return CampaignSim.Progression(args.Select(a => int.TryParse(a, out int v) ? v : (int?)null).FirstOrDefault(v => v != null) ?? 7);
    int cseed = args.Select(a => int.TryParse(a, out int v) ? v : (int?)null).FirstOrDefault(v => v != null) ?? 7;
    return CampaignSim.RunLoop(cseed, maxDives: 12, verbose: true);
}

if (args.Length > 0 && args[0] == "tithe")
{
    // Watched TITHE fight: the crew and the enemies all act by AI policy.
    bool boss = args.Contains("boss");
    if (args.Contains("balance"))
        return TitheSim.Balance(args.Select(a => int.TryParse(a, out int v) ? v : (int?)null)
            .FirstOrDefault(v => v != null) ?? 40, boss);
    int tseed = args.Select(a => int.TryParse(a, out int v) ? v : (int?)null).FirstOrDefault(v => v != null) ?? 7;
    return TitheSim.PlayOne(tseed, verbose: true, boss);
}

int seed = args.Length > 0 && int.TryParse(args[0], out int s) ? s : 12345;
int maxRounds = 40;

var engine = Encounter.CreateIncarnamSandbox(new SystemRng(seed));
engine.Logged += Console.WriteLine;

Console.WriteLine($"Incarnam combat sandbox — headless sim (seed {seed})");
Console.WriteLine($"Map {engine.Field.Width}x{engine.Field.Height}, {engine.Fighters.Count} fighters.\n");

engine.Start();

while (engine.Outcome == FightOutcome.Ongoing && engine.Round <= maxRounds)
{
    var current = engine.Current;
    if (current.Team == Team.Enemy)
        DofusSlice.Core.AI.MobBrain.TakeTurn(engine, current);
    else
        HeroAuto.TakeTurn(engine, current);

    engine.EndTurn();
}

Console.WriteLine();
Console.WriteLine($"Outcome after round {engine.Round}: {engine.Outcome}");
foreach (var f in engine.Fighters)
    Console.WriteLine($"  {f.Name,-8} {(f.IsAlive ? $"{f.Hp}/{f.MaxHp} HP" : "DEAD")}");

// Non-zero exit if the fight never resolved — a useful smoke-test signal.
return engine.Outcome == FightOutcome.Ongoing ? 1 : 0;

using DofusSlice.Core.Combat;
using DofusSlice.Core.Content;
using DofusSlice.Core.Content.Tithe;
using DofusSlice.Sim;

if (args.Length > 0 && args[0] == "effects")
{
    Console.WriteLine("Combat effects self-test:\n");
    return EffectsTest.Run();
}

if (args.Length > 0 && args[0] == "tithe")
{
    // Watched TITHE fight: the crew and the skeleton pack all act by AI policy.
    if (args.Length > 1 && args[1] == "balance")
        return TitheSim.Balance(int.TryParse(args.ElementAtOrDefault(2), out int n) ? n : 40);
    int tseed = int.TryParse(args.ElementAtOrDefault(1), out int ts) ? ts : 7;
    return TitheSim.PlayOne(tseed, verbose: true);
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

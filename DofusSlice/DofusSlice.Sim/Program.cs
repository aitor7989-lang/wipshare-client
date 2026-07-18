using DofusSlice.Core.Combat;
using DofusSlice.Core.Content;
using DofusSlice.Sim;

if (args.Length > 0 && args[0] == "effects")
{
    Console.WriteLine("Combat effects self-test:\n");
    return EffectsTest.Run();
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

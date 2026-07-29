// THE GAUNTLET OF THE BELL. `Gauntlet --sim [runs] [class] [seed]` runs the headless
// balance harness (hundreds of full runs a minute) instead of opening the window.
if (args.Length > 0 && args[0] == "--sim")
    return Gauntlet.Balance.RunSim.Main(args.Skip(1).ToArray());

using var game = new Gauntlet.GauntletGame();
game.Run();
return 0;

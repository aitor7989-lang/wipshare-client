using System;
using System.IO;

try
{
    // TITHE watched-combat prototype by default; pass "dofus" for the original piloted slice.
    // An integer arg sets the starting RNG seed (handy for reproducing a specific fight).
    bool tithe = !args.Any(a => a.Equals("dofus", StringComparison.OrdinalIgnoreCase));
    int seed = args.Select(a => int.TryParse(a, out int s) ? s : (int?)null).FirstOrDefault(s => s != null) ?? 1;
    using var game = new DofusSlice.Game.SliceGame(tithe, seed);
    game.Run();
}
catch (Exception ex)
{
    // Don't vanish silently on an unhandled error — leave a crash log next to the exe.
    try
    {
        var path = Path.Combine(AppContext.BaseDirectory, "crash.log");
        File.WriteAllText(path, $"{DateTime.Now:u}\n{ex}\n");
        Console.Error.WriteLine($"DofusSlice crashed. Details written to {path}");
    }
    catch { /* nothing more we can do */ }
    Console.Error.WriteLine(ex);
    return 1;
}
return 0;

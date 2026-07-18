using System;
using System.IO;

try
{
    using var game = new DofusSlice.Game.SliceGame();
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

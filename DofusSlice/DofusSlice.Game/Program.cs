using System;
using System.IO;

try
{
    // Audition the synthesized SFX: write every sound as a .wav and exit.
    if (args.Any(a => a.Equals("--emit-wavs", StringComparison.OrdinalIgnoreCase)))
    {
        new DofusSlice.Game.Audio.SoundBank().EmitWavs(Path.Combine(AppContext.BaseDirectory, "wavs"));
        return 0;
    }

    // Regenerate the starter Gum UI project (ui/TitheHud.gumx) for the visual editor and exit.
    if (args.Any(a => a.Equals("--emit-gum", StringComparison.OrdinalIgnoreCase)))
    {
        DofusSlice.Game.Ui.GumProjectEmitter.Emit(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "ui"), 1280, 760, 600);
        return 0;
    }

    // Default: the TITHE campaign loop (City → Graveyard → fights). "pack"/"boss" drop straight
    // into a one-off watched fight; "dofus" is the original piloted slice. An integer arg sets the
    // starting RNG seed.
    bool dofus = args.Any(a => a.Equals("dofus", StringComparison.OrdinalIgnoreCase));
    bool boss = args.Any(a => a.Equals("boss", StringComparison.OrdinalIgnoreCase));
    bool directFight = boss || args.Any(a => a.Equals("pack", StringComparison.OrdinalIgnoreCase));
    bool tithe = !dofus;
    bool loop = tithe && !directFight;
    int seed = args.Select(a => int.TryParse(a, out int s) ? s : (int?)null).FirstOrDefault(s => s != null) ?? 1;
    using var game = new DofusSlice.Game.SliceGame(tithe, seed, boss, loop);
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

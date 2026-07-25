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
    bool uiDemo = args.Any(a => a.Equals("--uidemo", StringComparison.OrdinalIgnoreCase));
    bool tithe = !dofus;
    bool loop = tithe && !directFight;
    int seed = args.Select(a => int.TryParse(a, out int s) ? s : (int?)null).FirstOrDefault(s => s != null) ?? 1;

    // --crt=off|soft|full picks the starting tube level (F8 cycles it in-game).
    var crtArg = args.FirstOrDefault(a => a.StartsWith("--crt=", StringComparison.OrdinalIgnoreCase));
    var crt = crtArg?.Split('=')[1].ToLowerInvariant() switch
    {
        "off" => DofusSlice.Game.Rendering.CrtLevel.Off,
        "full" => DofusSlice.Game.Rendering.CrtLevel.Full,
        "soft" => DofusSlice.Game.Rendering.CrtLevel.Soft,
        _ => DofusSlice.Game.Rendering.CrtLevel.Soft,
    };
    // --pixels=off|soft|hard picks how far the fat-pixel grid reaches (F7 cycles it in-game).
    var pixArg = args.FirstOrDefault(a => a.StartsWith("--pixels=", StringComparison.OrdinalIgnoreCase));
    var pixels = pixArg?.Split('=')[1].ToLowerInvariant() switch
    {
        "off" => DofusSlice.Game.Rendering.PixelMode.Off,
        "hard" => DofusSlice.Game.Rendering.PixelMode.Hard,
        _ => DofusSlice.Game.Rendering.PixelMode.Soft,
    };
    // --ascii[=mono] starts in the terminal renderer (F6 toggles it, F5 swaps colour mode).
    var asciiArg = args.FirstOrDefault(a => a.StartsWith("--ascii", StringComparison.OrdinalIgnoreCase));
    bool ascii = asciiArg != null;
    bool asciiChromatic = !(asciiArg?.EndsWith("=mono", StringComparison.OrdinalIgnoreCase) ?? false);

    // --curve=0.10 sets how far the tube face bows; 0 is a flat panel.
    var curveArg = args.FirstOrDefault(a => a.StartsWith("--curve=", StringComparison.OrdinalIgnoreCase));
    float curve = curveArg != null && float.TryParse(curveArg.Split('=')[1],
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
        out float cv) ? Math.Clamp(cv, 0f, 0.5f) : 0.07f;

    using var game = new DofusSlice.Game.SliceGame(tithe, seed, boss, loop, uiDemo)
    {
        Crt = crt,
        CurveAmount = curve,
        Pixels = pixels,
        Ascii = ascii,
        AsciiChromatic = asciiChromatic,
    };
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

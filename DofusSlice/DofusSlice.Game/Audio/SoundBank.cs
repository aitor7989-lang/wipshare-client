using Microsoft.Xna.Framework.Audio;
using static DofusSlice.Game.Audio.Synth;

namespace DofusSlice.Game.Audio;

/// <summary>
/// Every sound in the game, synthesized once at startup (see <see cref="Synth"/>) — element-
/// flavored hits, the TITHE bell, stings, steps, ambient loops. Playback goes through a small
/// per-sound cooldown so 4× playback never machine-guns, and the whole bank disables itself
/// gracefully when no audio hardware exists (headless CI, containers), so it can never crash
/// the game. M toggles mute at runtime.
/// </summary>
public sealed class SoundBank
{
    private readonly Dictionary<string, SoundEffect> _sounds = new();
    private readonly Dictionary<string, double> _lastPlay = new();
    private readonly Dictionary<string, float[]> _pcm = new();     // kept for --emit-wavs
    private SoundEffectInstance? _ambient;
    private string _ambientName = "";
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Random _rng = new();

    public bool Enabled { get; private set; }
    public bool Muted { get; set; }
    public float Master { get; set; } = 0.55f;

    public SoundBank()
    {
        foreach (var (name, buf) in Recipes())
            _pcm[name] = buf;                    // pure math, always available (--emit-wavs)
        try
        {
            foreach (var (name, buf) in _pcm)
                _sounds[name] = new SoundEffect(ToPcm16(buf), Rate, AudioChannels.Mono);
            Enabled = true;
        }
        catch
        {
            Enabled = false; // no audio device -> silent game, never a crash
        }
    }

    private static IEnumerable<(string name, float[] pcm)> Recipes()
    {
        // -- combat foley ------------------------------------------------------------
        yield return ("step", Gain(Env(Mix(
            Noise(0.05), Tone(0.04, Glide(160, 90, 0.04), Sin)), 0.002, 0.014), 0.5));
        yield return ("cast", Gain(Env(Mix(
            Gain(Noise(0.22), 0.5), Tone(0.22, Glide(280, 980, 0.2), Sin)), 0.01, 0.07), 0.6));
        yield return ("hit_neutral", Env(Mix(
            Gain(Noise(0.14), 0.8), Tone(0.12, Glide(230, 140, 0.1), Square)), 0.002, 0.035));
        yield return ("hit_fire", Env(Mix(
            Gain(Noise(0.18), 0.9), Tone(0.16, Glide(330, 85, 0.14), Saw)), 0.002, 0.05));
        yield return ("hit_water", Env(Mix(
            Gain(Noise(0.08), 0.35), Tone(0.15, Glide(520, 170, 0.12), Sin),
            At(Tone(0.05, Glide(700, 900, 0.05), Sin), 0.08)), 0.003, 0.045));
        yield return ("hit_air", Env(Mix(
            Gain(Noise(0.09), 0.5), Tone(0.1, Glide(950, 480, 0.08), Tri)), 0.002, 0.03));
        yield return ("hit_earth", Env(Mix(
            Gain(Noise(0.12), 1.1), Tone(0.18, Glide(95, 52, 0.16), Sin)), 0.002, 0.06));
        yield return ("crit", Mix(
            Env(Mix(Gain(Noise(0.12), 0.9), Tone(0.12, Glide(240, 130, 0.1), Square)), 0.002, 0.04),
            At(Gain(Env(Tone(0.06, _ => 660, Square), 0.002, 0.05), 0.35), 0.05),
            At(Gain(Env(Tone(0.06, _ => 880, Square), 0.002, 0.05), 0.35), 0.11),
            At(Gain(Env(Tone(0.09, _ => 1320, Square), 0.002, 0.07), 0.35), 0.17)));
        yield return ("heal", Gain(Mix(
            Env(Tone(0.12, _ => 660, Tri), 0.01, 0.09),
            At(Env(Tone(0.2, _ => 990, Tri), 0.01, 0.12), 0.1)), 0.5));
        yield return ("death", Env(Mix(
            Gain(Noise(0.45), 0.55), Tone(0.5, Glide(220, 42, 0.45), Saw)), 0.004, 0.16));
        yield return ("zip", Gain(Env(Tone(0.13, Glide(420, 1500, 0.12), Sin), 0.004, 0.05), 0.5));
        yield return ("summon", Gain(Env(Mix(
            Tone(0.32, _ => 880, Tri), Tone(0.32, _ => 1108, Tri)), 0.02, 0.12), 0.35));

        // -- economy + stings --------------------------------------------------------
        yield return ("coin", Gain(Mix(
            Env(Tone(0.05, _ => 1568, Sin), 0.002, 0.04),
            At(Env(Tone(0.12, _ => 2093, Sin), 0.002, 0.09), 0.05)), 0.6));
        yield return ("click", Gain(Env(Tone(0.035, _ => 880, Square), 0.002, 0.02), 0.4));
        yield return ("victory", Mix(
            At(Gain(Env(Tone(0.1, _ => 523, Square), 0.004, 0.09), 0.4), 0.00),
            At(Gain(Env(Tone(0.1, _ => 659, Square), 0.004, 0.09), 0.4), 0.09),
            At(Gain(Env(Tone(0.1, _ => 784, Square), 0.004, 0.09), 0.4), 0.18),
            At(Gain(Env(Tone(0.30, _ => 1046, Square), 0.004, 0.2), 0.45), 0.27),
            At(Gain(Env(Tone(0.42, _ => 261, Tri), 0.01, 0.3), 0.5), 0.00)));
        yield return ("levelup", Mix(
            At(Gain(Env(Tone(0.09, _ => 587, Tri), 0.004, 0.08), 0.45), 0.00),
            At(Gain(Env(Tone(0.09, _ => 784, Tri), 0.004, 0.08), 0.45), 0.09),
            At(Gain(Env(Tone(0.09, _ => 988, Tri), 0.004, 0.08), 0.45), 0.18),
            At(Gain(Env(Tone(0.34, _ => 1175, Tri), 0.004, 0.24), 0.5), 0.27)));
        yield return ("defeat", Mix(
            At(Gain(Env(Tone(0.2, _ => 220, Saw), 0.01, 0.16), 0.4), 0.00),
            At(Gain(Env(Tone(0.2, _ => 174, Saw), 0.01, 0.16), 0.4), 0.18),
            At(Gain(Env(Tone(0.5, _ => 146, Saw), 0.01, 0.34), 0.45), 0.36)));

        // -- the bell (inharmonic partials, the sound of the whole game) --------------
        yield return ("bell", Mix(
            Env(Tone(1.6, _ => 220, Sin), 0.004, 0.5),
            Gain(Env(Tone(1.6, _ => 220 * 2.76, Sin), 0.004, 0.28), 0.5),
            Gain(Env(Tone(1.6, _ => 220 * 5.40, Sin), 0.004, 0.16), 0.25),
            Gain(Env(Tone(1.6, _ => 220 * 1.02, Sin), 0.004, 0.55), 0.4)));

        // -- ambient loops (frequencies chosen to loop seamlessly) --------------------
        yield return ("wind", Gain(Tremolo(LowPass(Noise(4.0), 28), 0.5, 0.55), 2.2));
        yield return ("drone", Gain(Mix(
            Tone(4.0, _ => 55, Sin), Gain(Tone(4.0, _ => 82.5, Sin), 0.5),
            Gain(Tremolo(LowPass(Noise(4.0), 40), 0.25, 0.5), 1.2)), 0.7));
    }

    /// <summary>Fire-and-forget with a per-sound cooldown and a touch of pitch jitter.</summary>
    public void Play(string name, float volume = 1f, bool jitter = true)
    {
        if (!Enabled || Muted || !_sounds.TryGetValue(name, out var fx)) return;
        double now = _clock.Elapsed.TotalSeconds;
        if (_lastPlay.TryGetValue(name, out double last) && now - last < 0.045) return;
        _lastPlay[name] = now;
        float pitch = jitter ? (float)(_rng.NextDouble() * 0.12 - 0.06) : 0f;
        fx.Play(Math.Clamp(volume * Master, 0, 1), pitch, 0f);
    }

    /// <summary>Switch the looping ambient bed ("" or null stops it).</summary>
    public void SetAmbient(string? name, float volume = 0.16f)
    {
        if (!Enabled) return;
        name ??= "";
        if (name == _ambientName)
        {
            if (_ambient != null) _ambient.Volume = Muted ? 0f : Math.Clamp(volume * Master, 0, 1);
            return;
        }
        _ambient?.Stop();
        _ambient?.Dispose();
        _ambient = null;
        _ambientName = name;
        if (name.Length == 0 || !_sounds.TryGetValue(name, out var fx)) return;
        _ambient = fx.CreateInstance();
        _ambient.IsLooped = true;
        _ambient.Volume = Muted ? 0f : Math.Clamp(volume * Master, 0, 1);
        _ambient.Play();
    }

    /// <summary>Write every synthesized sound as a .wav for auditioning/tuning.</summary>
    public void EmitWavs(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var (name, buf) in _pcm)
        {
            var pcm = ToPcm16(buf);
            using var fs = File.Create(Path.Combine(dir, name + ".wav"));
            using var w = new BinaryWriter(fs);
            w.Write("RIFF"u8); w.Write(36 + pcm.Length); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(pcm.Length); w.Write(pcm);
        }
        Console.WriteLine($"{_pcm.Count} wavs written to {dir}");
    }
}

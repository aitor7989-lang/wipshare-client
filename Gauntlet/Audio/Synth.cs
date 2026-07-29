namespace Gauntlet.Audio;

/// <summary>
/// A tiny chiptune synthesizer: oscillators with frequency glides, noise, exponential
/// envelopes and mixing, rendering float buffers that <see cref="SoundBank"/> turns into
/// SoundEffects. Everything the game plays is synthesized here at startup from these few
/// primitives — the repo ships no audio files at all.
/// </summary>
internal static class Synth
{
    public const int Rate = 22050;

    private static readonly Random Rng = new(929);

    public static float[] Silence(double seconds) => new float[(int)(seconds * Rate)];

    /// <summary>An oscillator with a per-sample frequency curve (t in seconds → Hz) and a
    /// waveform (phase 0..1 → -1..1).</summary>
    public static float[] Tone(double seconds, Func<double, double> freq, Func<double, double> wave)
    {
        var buf = new float[(int)(seconds * Rate)];
        double phase = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            double t = (double)i / Rate;
            phase += freq(t) / Rate;
            buf[i] = (float)wave(phase - Math.Floor(phase));
        }
        return buf;
    }

    /// <summary>A linear glide from <paramref name="f0"/> to <paramref name="f1"/> Hz.</summary>
    public static Func<double, double> Glide(double f0, double f1, double seconds) =>
        t => f0 + (f1 - f0) * Math.Clamp(t / seconds, 0, 1);

    public static double Sin(double p) => Math.Sin(p * Math.Tau);
    public static double Square(double p) => p < 0.5 ? 1 : -1;
    public static double Tri(double p) => 4 * Math.Abs(p - 0.5) - 1;
    public static double Saw(double p) => 2 * p - 1;

    public static float[] Noise(double seconds)
    {
        var buf = new float[(int)(seconds * Rate)];
        for (int i = 0; i < buf.Length; i++) buf[i] = (float)(Rng.NextDouble() * 2 - 1);
        return buf;
    }

    /// <summary>Crude low-pass (moving average) — softens noise into wind/rumble.</summary>
    public static float[] LowPass(float[] buf, int window)
    {
        var outBuf = new float[buf.Length];
        float acc = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            acc += buf[i];
            if (i >= window) acc -= buf[i - window];
            outBuf[i] = acc / window;
        }
        return outBuf;
    }

    /// <summary>Attack then exponential decay, in place. Returns the buffer for chaining.</summary>
    public static float[] Env(float[] buf, double attack = 0.004, double halfLife = 0.06)
    {
        int a = Math.Max(1, (int)(attack * Rate));
        double k = Math.Log(2) / (halfLife * Rate);
        for (int i = 0; i < buf.Length; i++)
        {
            double env = i < a ? (double)i / a : Math.Exp(-k * (i - a));
            buf[i] = (float)(buf[i] * env);
        }
        return buf;
    }

    /// <summary>A slow sine tremolo, in place (depth 0..1) — breathing for ambient loops.</summary>
    public static float[] Tremolo(float[] buf, double hz, double depth)
    {
        for (int i = 0; i < buf.Length; i++)
        {
            double t = (double)i / Rate;
            buf[i] = (float)(buf[i] * (1 - depth + depth * (0.5 + 0.5 * Math.Sin(t * hz * Math.Tau))));
        }
        return buf;
    }

    public static float[] Gain(float[] buf, double gain)
    {
        for (int i = 0; i < buf.Length; i++) buf[i] = (float)(buf[i] * gain);
        return buf;
    }

    /// <summary>Mix sources sample-wise into a buffer as long as the longest source.</summary>
    public static float[] Mix(params float[][] sources)
    {
        var buf = new float[sources.Max(s => s.Length)];
        foreach (var s in sources)
            for (int i = 0; i < s.Length; i++)
                buf[i] += s[i];
        return buf;
    }

    /// <summary>Add a source into the target starting at an offset (seconds), growing if needed.</summary>
    public static float[] At(float[] src, double offsetSeconds)
    {
        int off = (int)(offsetSeconds * Rate);
        var buf = new float[off + src.Length];
        Array.Copy(src, 0, buf, off, src.Length);
        return buf;
    }

    /// <summary>Soft-clip and convert to 16-bit little-endian mono PCM.</summary>
    public static byte[] ToPcm16(float[] buf, double gain = 1)
    {
        var bytes = new byte[buf.Length * 2];
        for (int i = 0; i < buf.Length; i++)
        {
            double v = Math.Tanh(buf[i] * gain);           // gentle limiter, never harsh clipping
            short s = (short)(v * short.MaxValue * 0.92);
            bytes[i * 2] = (byte)(s & 0xff);
            bytes[i * 2 + 1] = (byte)(s >> 8 & 0xff);
        }
        return bytes;
    }
}

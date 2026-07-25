using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DofusSlice.Game.Rendering;

/// <summary>How hard the tube is driven. OFF is byte-for-byte the pre-CRT image.</summary>
public enum CrtLevel { Off = 0, Soft = 1, Full = 2 }

/// <summary>
/// A shader-free CRT/VHS post pass.
///
/// This project has no content pipeline — no .mgcb, no .fx, nothing to compile — and it runs on
/// DesktopGL, so every effect here is built out of SpriteBatch blits and procedurally generated
/// textures. That constraint is not a compromise: the whole point of the ONE-BIT look is white
/// ink on near-black, and additive re-blits of a downsampled copy give a phosphor bloom that only
/// lights up where there IS ink, which is exactly the physics we want.
///
/// The restraint is deliberate. Mono.cs asks for "sharp 1px frames, no gradients, no gloss" —
/// a heavy blur would eat the hairlines that make the board readable. So: one dark row per two
/// (which lands exactly on the half-res world's fat pixels, so it lines up instead of moiring),
/// a bloom that adds ~2/255 to the background and ~70/255 to ink, and a vignette that never
/// touches the middle of the screen where the fight is.
/// </summary>
public sealed class CrtPass : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly Texture2D _scan;    // 4x4 POT, 1 dark row in 2 — tiled with PointWrap
    private readonly Texture2D _vig;     // 64x64 radial darkening, stretched with LinearClamp
    private readonly Texture2D _band;    // 1x64 soft vertical gradient — the drifting VHS band

    private RenderTarget2D? _frame;      // the whole composed frame, full res
    private RenderTarget2D? _half;       // frame / BloomDiv, for the bloom tap
    private int _w, _h;

    /// <summary>Bloom source divisor. 4 gives a soft glow without smearing the 1px frames.</summary>
    private const int BloomDiv = 4;

    public CrtLevel Level { get; set; } = CrtLevel.Soft;

    public CrtPass(GraphicsDevice gd)
    {
        _gd = gd;

        // Scanlines. Power-of-two so PointWrap tiling is legal on every profile. Row 0 dark,
        // row 1 clear, repeated: a 2px pitch, which is exactly WorldPx — each fat world pixel
        // gets one dark row and one lit row, so the grille aligns instead of beating against it.
        _scan = new Texture2D(gd, 4, 4);
        var scan = new Color[16];
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                scan[y * 4 + x] = (y % 2 == 0) ? new Color(0, 0, 0, 255) : Color.Transparent;
        _scan.SetData(scan);

        // Vignette. Flat across the middle 60% and only then rolling off, so the board never
        // dims — it is the corners and the HUD gutters that get the tube curvature.
        const int V = 64;
        _vig = new Texture2D(gd, V, V);
        var vig = new Color[V * V];
        for (int y = 0; y < V; y++)
            for (int x = 0; x < V; x++)
            {
                float nx = (x + 0.5f) / V * 2f - 1f, ny = (y + 0.5f) / V * 2f - 1f;
                float d = MathF.Sqrt(nx * nx * 0.78f + ny * ny);   // wider than tall: 16:9 tube
                float t = Math.Clamp((d - 0.62f) / 0.72f, 0f, 1f);
                byte a = (byte)(t * t * 255f);
                vig[y * V + x] = new Color((byte)0, (byte)0, (byte)0, a);
            }
        _vig.SetData(vig);

        // The drifting tracking band: a soft lobe, brightest in the middle, additive.
        const int B = 64;
        _band = new Texture2D(gd, 1, B);
        var band = new Color[B];
        for (int y = 0; y < B; y++)
        {
            float t = y / (float)(B - 1);
            float lobe = MathF.Sin(t * MathF.PI);
            byte v = (byte)(lobe * lobe * 255f);
            band[y] = new Color(v, v, v, v);
        }
        _band.SetData(band);
    }

    /// <summary>Cycles OFF → SOFT → FULL → OFF. Returns the new level so callers can log it.</summary>
    public CrtLevel Cycle() => Level = (CrtLevel)(((int)Level + 1) % 3);

    public string LevelName => Level switch
    {
        CrtLevel.Soft => "SOFT", CrtLevel.Full => "FULL", _ => "OFF",
    };

    /// <summary>
    /// The target the frame must be composed into, or null when the pass is off (in which case
    /// everything draws straight to the back buffer and nothing changes).
    /// </summary>
    public RenderTarget2D? FrameTarget => Level == CrtLevel.Off ? null : _frame;

    /// <summary>Point every draw at the offscreen frame. Call once at the top of Draw.</summary>
    public void Begin(int screenW, int screenH, Color clear)
    {
        if (Level == CrtLevel.Off) { _gd.SetRenderTarget(null); return; }
        Ensure(screenW, screenH);
        _gd.SetRenderTarget(_frame);
        _gd.Clear(clear);
    }

    /// <summary>
    /// Resolve the frame to the back buffer through the tube. <paramref name="time"/> is total
    /// elapsed seconds and drives the one moving part (the tracking band).
    /// </summary>
    public void End(SpriteBatch sb, float time)
    {
        if (Level == CrtLevel.Off || _frame is null) return;
        bool full = Level == CrtLevel.Full;
        var screen = new Rectangle(0, 0, _w, _h);

        // 1. Downsample for the bloom tap. Linear filtering across a 4x reduction IS the blur;
        //    no separable-Gaussian pass and no shader needed.
        _gd.SetRenderTarget(_half);
        _gd.Clear(Color.Transparent);
        sb.Begin(samplerState: SamplerState.LinearClamp);
        sb.Draw(_frame, new Rectangle(0, 0, _half!.Width, _half.Height), Color.White);
        sb.End();

        _gd.SetRenderTarget(null);

        // 2. The image itself, untouched and point-sampled — the pixels stay pixels.
        sb.Begin(samplerState: SamplerState.PointClamp);
        sb.Draw(_frame, screen, Color.White);
        sb.End();

        // 3. Phosphor bloom + (on FULL) the chromatic fringe. Additive, so this only lifts what
        //    is already lit: the near-black ground gains ~2/255, ink gains ~70/255.
        sb.Begin(blendState: BlendState.Additive, samplerState: SamplerState.LinearClamp);
        float glow = full ? 0.34f : 0.22f;
        sb.Draw(_half, Grow(screen, 2), Color.White * glow);           // tight halo
        sb.Draw(_half, Grow(screen, 10), Color.White * (glow * 0.55f)); // wide, soft spill
        if (full)
        {
            // VHS colour-under: the luma is fine but the chroma lands a shade left and right of
            // it. Exactly WorldPx of offset, so the fringe sits on the fat-pixel grid instead of
            // smearing across it — at 3px the 7px-tall HUD font started to double.
            sb.Draw(_half, Offset(screen, -2, 0), new Color(0.11f, 0.02f, 0.02f));
            sb.Draw(_half, Offset(screen, 2, 0), new Color(0.00f, 0.06f, 0.11f));
        }
        sb.End();

        // 4. The grille, the tube edge, and the drifting tracking band.
        sb.Begin(samplerState: SamplerState.PointWrap);
        sb.Draw(_scan, screen, screen, Color.White * (full ? 0.16f : 0.10f));
        sb.End();

        sb.Begin(samplerState: SamplerState.LinearClamp);
        sb.Draw(_vig, screen, Color.White * (full ? 0.55f : 0.35f));
        sb.End();

        if (full)
        {
            const int BandH = 140;
            float span = _h + BandH;
            int y = (int)((time * 26f) % span) - BandH;   // ~13s to crawl the screen
            sb.Begin(blendState: BlendState.Additive, samplerState: SamplerState.LinearClamp);
            sb.Draw(_band, new Rectangle(0, y, _w, BandH), Color.White * 0.045f);
            sb.End();
        }
    }

    private static Rectangle Grow(Rectangle r, int by) =>
        new(r.X - by, r.Y - by, r.Width + by * 2, r.Height + by * 2);

    private static Rectangle Offset(Rectangle r, int dx, int dy) =>
        new(r.X + dx, r.Y + dy, r.Width, r.Height);

    private void Ensure(int w, int h)
    {
        if (_frame is not null && _w == w && _h == h) return;
        _frame?.Dispose(); _half?.Dispose();
        _w = w; _h = h;
        _frame = new RenderTarget2D(_gd, w, h, false, SurfaceFormat.Color, DepthFormat.None);
        _half = new RenderTarget2D(_gd, Math.Max(1, w / BloomDiv), Math.Max(1, h / BloomDiv),
            false, SurfaceFormat.Color, DepthFormat.None);
    }

    public void Dispose()
    {
        _frame?.Dispose(); _half?.Dispose();
        _scan.Dispose(); _vig.Dispose(); _band.Dispose();
    }
}

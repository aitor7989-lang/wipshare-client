using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DofusSlice.Game.Rendering;

/// <summary>How hard the tube is driven. OFF is byte-for-byte the pre-CRT image.</summary>
public enum CrtLevel { Off = 0, Soft = 1, Full = 2 }

/// <summary>
/// How far the fat-pixel grid reaches. The world is always on it (it renders at 1/WorldPx and is
/// point-upscaled); the question is what happens to the HUD, which is drawn at full res on top.
/// <list type="bullet">
/// <item>OFF — HUD stays at native resolution, as it has always been.</item>
/// <item>SOFT — HUD chrome (icons, borders, bars, slots) wears the grid, but the quantized image
/// is cross-faded with the original so the 5x7 font keeps a readable core.</item>
/// <item>HARD — the whole screen is quantized, no exceptions. Maximally cohesive; the 1px font
/// cannot survive it, because seven glyph rows on a 2px grid is 14 screen pixels and the panels
/// are laid out for 7.</item>
/// </list>
/// </summary>
public enum PixelMode { Off = 0, Soft = 1, Hard = 2 }

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
    private RenderTarget2D? _pix;        // frame / PixelSize — the fat-pixel quantization
    private RenderTarget2D? _half;       // _pix / BloomDiv, for the bloom tap
    private int _w, _h;

    /// <summary>Bloom source divisor, relative to full res.</summary>
    private const int BloomDiv = 8;

    /// <summary>
    /// The fat-pixel grid, in screen pixels. Must equal SliceGame.WorldPx: the world already
    /// renders at 1/WorldPx and is blown back up point-sampled, so quantizing the composed frame
    /// by the same factor is a lossless round-trip for the board (each 2x2 block IS one world
    /// pixel) and a real quantization for everything drawn after it — which is the HUD.
    /// </summary>
    public int PixelSize { get; init; } = 2;

    public CrtLevel Level { get; set; } = CrtLevel.Soft;

    /// <summary>How far the fat-pixel grid reaches. Cycled with F7.</summary>
    public PixelMode Pixels { get; set; } = PixelMode.Soft;

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

    /// <summary>Cycles the fat-pixel reach OFF → SOFT → HARD → OFF.</summary>
    public PixelMode CyclePixels() => Pixels = (PixelMode)(((int)Pixels + 1) % 3);

    public string PixelName => Pixels switch
    {
        PixelMode.Soft => "SOFT", PixelMode.Hard => "HARD", _ => "OFF",
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

        // 1. Quantize the WHOLE frame — HUD included — onto the fat-pixel grid. A linear
        //    downsample by exactly PixelSize lands each destination texel centre on the corner
        //    between four source texels, so the hardware returns an exact 2x2 box average and
        //    no dedicated filter is needed. Because the world was already point-upscaled from
        //    that same grid it survives this untouched; the HUD, drawn at full res afterwards,
        //    is what actually gets chunked.
        bool quantize = Pixels != PixelMode.Off;
        if (quantize)
        {
            _gd.SetRenderTarget(_pix);
            sb.Begin(samplerState: SamplerState.LinearClamp);
            sb.Draw(_frame, new Rectangle(0, 0, _pix!.Width, _pix.Height), Color.White);
            sb.End();
        }

        // 2. Downsample for the bloom tap. Linear filtering across the reduction IS the blur;
        //    no separable-Gaussian pass and no shader needed. Tap the quantized image so the
        //    glow follows the fat pixels rather than fighting them.
        var lit = quantize ? _pix! : _frame;
        var halfRect = new Rectangle(0, 0, _half!.Width, _half.Height);
        _gd.SetRenderTarget(_half);
        _gd.Clear(Color.Transparent);
        sb.Begin(samplerState: SamplerState.LinearClamp);
        sb.Draw(lit, halfRect, Color.White);
        sb.End();

        // 2b. Bright pass, still without a shader: draw the same source over the tap with a
        //     multiply blend, so the tap becomes blur(lit)^2. A real bloom thresholds the
        //     highlights; squaring is the cheap monotonic stand-in — ink at 236 keeps 218,
        //     the cast-range overlay's mid-grey 150 falls to 88, the near-black ground falls
        //     to nothing. Without this, doubling the bloom saturated every large lit field
        //     (the range overlay went solid white) instead of making the ink glow.
        sb.Begin(blendState: Multiply, samplerState: SamplerState.LinearClamp);
        sb.Draw(lit, halfRect, Color.White);
        sb.End();

        _gd.SetRenderTarget(null);

        // 3. The quantized image, point-sampled back up — the fat pixels stay fat pixels.
        sb.Begin(samplerState: SamplerState.PointClamp);
        sb.Draw(lit, screen, Color.White);
        sb.End();

        // 3b. SOFT: cross-fade the un-quantized frame back in at half weight. The world is
        //     bit-identical in both images (it was already on the grid), so it does not move
        //     at all; only the HUD differs, and there the blend leaves a chunky 2px halo with
        //     a legible 1px core instead of the unreadable mush a pure box average gives a
        //     5x7 font.
        if (Pixels == PixelMode.Soft)
        {
            sb.Begin(samplerState: SamplerState.PointClamp);
            sb.Draw(_frame, screen, Color.White * 0.5f);
            sb.End();
        }

        // 4. Phosphor bloom + (on FULL) the chromatic fringe. Additive, so this only lifts what
        //    is already lit: the near-black ground gains a couple of levels, ink gains a lot.
        //    It doubles as the contrast the box-average quantization costs thin HUD strokes.
        sb.Begin(blendState: BlendState.Additive, samplerState: SamplerState.LinearClamp);
        float glow = full ? 0.68f : 0.44f;
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

    /// <summary>dst = dst * src. Not one of the stock BlendStates, but legal everywhere.</summary>
    private static readonly BlendState Multiply = new()
    {
        ColorSourceBlend = Blend.Zero,
        ColorDestinationBlend = Blend.SourceColor,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.SourceAlpha,
    };

    private static Rectangle Grow(Rectangle r, int by) =>
        new(r.X - by, r.Y - by, r.Width + by * 2, r.Height + by * 2);

    private static Rectangle Offset(Rectangle r, int dx, int dy) =>
        new(r.X + dx, r.Y + dy, r.Width, r.Height);

    private void Ensure(int w, int h)
    {
        if (_frame is not null && _w == w && _h == h) return;
        _frame?.Dispose(); _pix?.Dispose(); _half?.Dispose();
        _w = w; _h = h;
        _frame = Target(w, h);
        _pix = Target(w / PixelSize, h / PixelSize);
        _half = Target(w / BloomDiv, h / BloomDiv);
    }

    private RenderTarget2D Target(int w, int h) =>
        new(_gd, Math.Max(1, w), Math.Max(1, h), false, SurfaceFormat.Color, DepthFormat.None);

    public void Dispose()
    {
        _frame?.Dispose(); _pix?.Dispose(); _half?.Dispose();
        _scan.Dispose(); _vig.Dispose(); _band.Dispose();
    }
}

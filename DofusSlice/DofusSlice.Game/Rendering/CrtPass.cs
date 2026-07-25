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
    private RenderTarget2D? _flat;       // the finished flat picture, when it has to be curved
    private int _w, _h;

    // ---- Tube curvature --------------------------------------------------------------
    // No shader pipeline in this project, so the warp is geometry: a grid of quads whose
    // vertices are pulled toward the centre by an amount that grows with distance, drawn
    // through the stock BasicEffect. Corners move twice as far as edge midpoints, which is
    // what gives the convex sides and tucked corners of a real tube face.

    private BasicEffect? _warpFx;
    private VertexPositionTexture[]? _warpVerts;
    private short[]? _warpIdx;
    private float _warpBuiltFor = -1f;
    private const int WarpN = 24;        // grid cells per axis

    /// <summary>Barrel amount, 0 = a flat panel. At <c>c</c> the edge midpoints come in by
    /// c/2 and the corners by c, so the whole picture insets slightly rather than losing its
    /// corners off-screen — nothing is clipped, it just sits in a little black bezel.</summary>
    public float Curve { get; set; } = 0.07f;

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

        // Everything from here composes the finished picture. When the tube is curved that
        // has to land offscreen first, because the warp needs the whole thing as one texture.
        bool curved = Curve > 0.001f;
        _gd.SetRenderTarget(curved ? _flat : null);
        if (curved) _gd.Clear(Color.Black);

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

        if (curved) ResolveCurved();
    }

    /// <summary>
    /// Map a back-buffer point back into pre-warp screen space, so hit-testing lands where the
    /// player sees the thing rather than where it would have been on a flat panel. Without this
    /// the curve silently breaks input: at Curve 0.10 the corners move about 10% of a half-width,
    /// which on a 1280-wide screen is ~64px of pointing error.
    /// </summary>
    public Point Unwarp(Point p)
    {
        if (Curve <= 0.001f || _w == 0 || _h == 0) return p;
        float halfW = _w / 2f, halfH = _h / 2f;
        float ud = (p.X - halfW) / halfW, vd = (p.Y - halfH) / halfH;
        float rd = MathF.Sqrt(ud * ud + vd * vd);
        if (rd < 1e-5f) return p;

        // The forward map scales a point by s(r) = 1 - Curve*r^2/2, so rd = r - Curve*r^3/2.
        // Invert that cubic with a few Newton steps — it is smooth and monotonic over the
        // range we use, so it converges in two or three.
        float r = rd;
        for (int i = 0; i < 6; i++)
        {
            float f = r - Curve * r * r * r * 0.5f - rd;
            float df = 1f - 1.5f * Curve * r * r;
            if (MathF.Abs(df) < 1e-6f) break;
            r -= f / df;
        }
        float k = r / rd;
        return new Point(
            (int)MathF.Round(halfW + ud * k * halfW),
            (int)MathF.Round(halfH + vd * k * halfH));
    }

    /// <summary>Blit the finished flat picture onto the back buffer through the warped grid.</summary>
    private void ResolveCurved()
    {
        BuildWarp();
        _gd.SetRenderTarget(null);
        _gd.Clear(Color.Black);

        _warpFx!.Projection = Matrix.CreateOrthographicOffCenter(0, _w, _h, 0, 0f, 1f);
        _warpFx.Texture = _flat;

        // Linear, not point: the fat pixels are already baked into _flat, and resampling a
        // warped grid with point sampling drops whole rows of them where the curve is steep.
        _gd.SamplerStates[0] = SamplerState.LinearClamp;
        _gd.BlendState = BlendState.Opaque;
        _gd.DepthStencilState = DepthStencilState.None;
        _gd.RasterizerState = RasterizerState.CullNone;

        foreach (var pass in _warpFx.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList,
                _warpVerts!, 0, _warpVerts!.Length,
                _warpIdx!, 0, _warpIdx!.Length / 3);
        }
    }

    private void BuildWarp()
    {
        _warpFx ??= new BasicEffect(_gd)
        {
            TextureEnabled = true,
            VertexColorEnabled = false,
            LightingEnabled = false,
            World = Matrix.Identity,
            View = Matrix.Identity,
        };
        // The mesh only depends on the curve amount and the screen size; rebuild on change.
        float key = Curve * 100003f + _w * 31f + _h;
        if (_warpVerts != null && Math.Abs(key - _warpBuiltFor) < 0.0001f) return;
        _warpBuiltFor = key;

        int n = WarpN;
        _warpVerts = new VertexPositionTexture[(n + 1) * (n + 1)];
        float halfW = _w / 2f, halfH = _h / 2f;
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n * 2f - 1f;      // -1..1 across the screen
                float v = j / (float)n * 2f - 1f;
                float r2 = u * u + v * v;              // 0 centre, 1 edge midpoint, 2 corner
                float s = 1f - Curve * r2 * 0.5f;      // corners pull in twice as far as edges
                _warpVerts[j * (n + 1) + i] = new VertexPositionTexture(
                    new Vector3(halfW + u * s * halfW, halfH + v * s * halfH, 0f),
                    new Vector2(i / (float)n, j / (float)n));
            }

        _warpIdx = new short[n * n * 6];
        int k = 0;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                short a = (short)(j * (n + 1) + i), b = (short)(a + 1);
                short c = (short)(a + n + 1), d = (short)(c + 1);
                _warpIdx[k++] = a; _warpIdx[k++] = b; _warpIdx[k++] = c;
                _warpIdx[k++] = b; _warpIdx[k++] = d; _warpIdx[k++] = c;
            }
    }

    private static Rectangle Grow(Rectangle r, int by) =>
        new(r.X - by, r.Y - by, r.Width + by * 2, r.Height + by * 2);

    private static Rectangle Offset(Rectangle r, int dx, int dy) =>
        new(r.X + dx, r.Y + dy, r.Width, r.Height);

    private void Ensure(int w, int h)
    {
        if (_frame is not null && _w == w && _h == h) return;
        _frame?.Dispose(); _pix?.Dispose(); _half?.Dispose(); _flat?.Dispose();
        _w = w; _h = h;
        _frame = Target(w, h);
        _pix = Target(w / PixelSize, h / PixelSize);
        _half = Target(w / BloomDiv, h / BloomDiv);
        _flat = Target(w, h);
    }

    private RenderTarget2D Target(int w, int h) =>
        new(_gd, Math.Max(1, w), Math.Max(1, h), false, SurfaceFormat.Color, DepthFormat.None);

    public void Dispose()
    {
        _frame?.Dispose(); _pix?.Dispose(); _half?.Dispose(); _flat?.Dispose();
        _scan.Dispose(); _vig.Dispose(); _band.Dispose();
        _warpFx?.Dispose();
    }
}

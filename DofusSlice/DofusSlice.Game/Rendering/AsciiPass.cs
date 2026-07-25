using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DofusSlice.Game.Rendering;

/// <summary>
/// Re-renders the composed frame as a grid of glyphs — the terminal/REXPaint look, applied to
/// the whole game at once rather than by restyling every draw call.
///
/// The trick that makes it cheap: the GPU does the averaging. Drawing the frame into a tiny
/// render target (one texel per character cell) with linear filtering IS the per-cell box
/// average, so the only CPU work is one small GetData and one quad per cell. Glyphs are
/// rasterised once into an atlas, because at ~20k cells a per-pixel glyph draw would be
/// ~700k quads a frame — the atlas keeps it at one quad per cell.
///
/// Nothing here is an asset: the ramp is authored as ASCII art in source, the same idiom
/// <see cref="PixelFont"/> already uses, so this ships in the zip like everything else.
/// </summary>
public sealed class AsciiPass : IDisposable
{
    /// <summary>Cell pitch on screen. The glyph is 5x7; the extra column and row are the
    /// gutter that keeps characters from touching, which is what makes a grid read as text.</summary>
    public const int CellW = 6, CellH = 8;
    private const int GlyphW = 5, GlyphH = 7;

    public bool Enabled { get; set; }

    /// <summary>Tint each cell with the colour it sampled (keeps enemies red, allies blue)
    /// instead of forcing one phosphor. False gives the monochrome reference look.</summary>
    public bool Chromatic { get; set; } = true;

    /// <summary>The single phosphor used when <see cref="Chromatic"/> is off.</summary>
    public Color Phosphor { get; set; } = new(198, 186, 214);

    private readonly GraphicsDevice _gd;
    private readonly Texture2D _atlas;
    private RenderTarget2D? _cells;
    private Color[] _buf = Array.Empty<Color>();
    private int _cols, _rows;

    // The density ramp, sparse to dense. Ink coverage per glyph is what actually matters;
    // the shapes are chosen from the reference's vocabulary so the output reads as deliberate
    // ASCII rather than as a dither pattern.
    private static readonly string[][] Ramp =
    {
        new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." }, //  (0)
        new[] { ".....", ".....", ".....", ".....", "..#..", ".....", "....." }, // . (1)
        new[] { ".....", ".....", "..#..", ".....", "..#..", ".....", "....." }, // : (2)
        new[] { ".....", ".....", ".....", ".###.", ".....", ".....", "....." }, // - (3)
        new[] { ".....", ".....", ".###.", ".#.#.", ".###.", ".....", "....." }, // o (8)
        new[] { ".....", "..#..", "..#..", "#####", "..#..", "..#..", "....." }, // + (9)
        new[] { ".###.", ".#.#.", ".#.#.", ".#.#.", ".#.#.", ".#.#.", ".###." }, // 0 (16)
        new[] { ".....", "#.#.#", ".###.", "#####", ".###.", "#.#.#", "....." }, // * (17)
        new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" }, // W (19)
        new[] { ".###.", "#####", "#####", "#####", "#####", "#####", ".###." }, // @ (31)
        new[] { "#####", "#####", "#####", "#####", "#####", "#####", "#####" }, // # (35)
    };

    public AsciiPass(GraphicsDevice gd)
    {
        _gd = gd;
        _atlas = new Texture2D(gd, Ramp.Length * GlyphW, GlyphH);
        var data = new Color[Ramp.Length * GlyphW * GlyphH];
        for (int i = 0; i < Ramp.Length; i++)
            for (int y = 0; y < GlyphH; y++)
                for (int x = 0; x < GlyphW; x++)
                    if (Ramp[i][y][x] == '#')
                        data[y * Ramp.Length * GlyphW + i * GlyphW + x] = Color.White;
        _atlas.SetData(data);
    }

    private void Ensure(int screenW, int screenH)
    {
        int cols = Math.Max(1, screenW / CellW), rows = Math.Max(1, screenH / CellH);
        if (_cells != null && cols == _cols && rows == _rows) return;
        _cells?.Dispose();
        _cols = cols; _rows = rows;
        _cells = new RenderTarget2D(_gd, cols, rows);
        _buf = new Color[cols * rows];
    }

    /// <summary>Resolve <paramref name="source"/> to the back buffer as glyphs.</summary>
    public void Apply(SpriteBatch sb, Texture2D source, int screenW, int screenH)
    {
        Ensure(screenW, screenH);

        // One texel per cell, linear-filtered: the hardware returns each cell's average.
        _gd.SetRenderTarget(_cells);
        _gd.Clear(Color.Black);
        sb.Begin(samplerState: SamplerState.LinearClamp);
        sb.Draw(source, new Rectangle(0, 0, _cols, _rows), Color.White);
        sb.End();
        _cells!.GetData(_buf);

        _gd.SetRenderTarget(null);
        _gd.Clear(Color.Black);
        sb.Begin(samplerState: SamplerState.PointClamp);
        for (int row = 0; row < _rows; row++)
            for (int col = 0; col < _cols; col++)
            {
                var c = _buf[row * _cols + col];
                // Rec. 601 luma: the ramp should follow perceived brightness, so a saturated
                // red enemy picks a denser glyph than a dim grey tile of the same raw sum.
                float lum = (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;
                int idx = (int)MathF.Round(MathF.Min(1f, lum * 1.35f) * (Ramp.Length - 1));
                if (idx <= 0) continue;
                sb.Draw(_atlas,
                    new Rectangle(col * CellW, row * CellH, GlyphW, GlyphH),
                    new Rectangle(idx * GlyphW, 0, GlyphW, GlyphH),
                    Chromatic ? Boost(c) : Phosphor);
            }
        sb.End();
    }

    /// <summary>Cell averages are dim by construction — a lit glyph covers only part of its
    /// cell, so the average of a bright character is already well below the ink it came from.
    /// Push the tint back up or every cell renders as mud.</summary>
    private static Color Boost(Color c)
    {
        float max = Math.Max(c.R, Math.Max(c.G, c.B)) / 255f;
        if (max <= 0.001f) return c;
        float k = Math.Min(1f / max, 3.2f);
        return new Color((int)Math.Min(255, c.R * k), (int)Math.Min(255, c.G * k),
            (int)Math.Min(255, c.B * k));
    }

    public void Dispose()
    {
        _atlas.Dispose();
        _cells?.Dispose();
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DofusSlice.Game.Rendering;

/// <summary>
/// Procedurally generated textures (a white pixel, an iso diamond, a filled disc) plus a
/// few draw helpers. Everything is built at load time, so the slice ships without art assets.
/// </summary>
public sealed class Primitives
{
    public Texture2D Pixel { get; }
    public Texture2D Diamond { get; }
    public Texture2D Disc { get; }
    public Texture2D FaceL { get; }
    public Texture2D FaceR { get; }
    public Texture2D Halo { get; }

    public int TileW { get; }
    public int TileH { get; }
    public int DiscSize { get; }

    /// <summary>Extrusion height of a raised obstacle block (1.29 tactical-mode style).</summary>
    public const int BlockH = 14;

    public Primitives(GraphicsDevice gd, int tileW, int tileH, int discSize)
    {
        _gd = gd;
        TileW = tileW;
        TileH = tileH;
        DiscSize = discSize;

        Pixel = new Texture2D(gd, 1, 1);
        Pixel.SetData(new[] { Color.White });

        Diamond = BuildDiamond(gd, tileW, tileH);
        Disc = BuildDisc(gd, discSize);
        FaceL = BuildFace(gd, tileW / 2, tileH / 2, BlockH, left: true);
        FaceR = BuildFace(gd, tileW / 2, tileH / 2, BlockH, left: false);
        Halo = BuildHalo(gd, 46, 23, 3);
    }

    /// <summary>One extruded side of a raised tile: the diamond's lower edge swept down by
    /// <paramref name="h"/> px — a parallelogram strip (left = SW face, mirrored for SE).</summary>
    private static Texture2D BuildFace(GraphicsDevice gd, int w, int hh, int h, bool left)
    {
        var tex = new Texture2D(gd, w, hh + h);
        var data = new Color[w * (hh + h)];
        for (int x = 0; x < w; x++)
        {
            float t = left ? (x + 0.5f) / w : 1f - (x + 0.5f) / w;
            int yEdge = (int)(t * hh);
            for (int y = yEdge; y < yEdge + h && y < hh + h; y++)
                data[y * w + x] = Color.White;
        }
        tex.SetData(data);
        return tex;
    }

    /// <summary>The 1.29 team halo: an ellipse ring inscribed in the cell.</summary>
    private static Texture2D BuildHalo(GraphicsDevice gd, int w, int h, int thick)
    {
        var tex = new Texture2D(gd, w, h);
        var data = new Color[w * h];
        float rx = w / 2f, ry = h / 2f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x + 0.5f - rx) / rx, dy = (y + 0.5f - ry) / ry;
                float d = dx * dx + dy * dy;
                float inner = (rx - thick) / rx;
                if (d <= 1f && d >= inner * inner)
                    data[y * w + x] = Color.White;
            }
        tex.SetData(data);
        return tex;
    }

    /// <summary>A raised obstacle tile: top diamond lifted by <see cref="BlockH"/> with two
    /// darker extruded faces below — how 1.29's tactical mode shows LoS blockers.</summary>
    public void BlockAt(SpriteBatch sb, Vector2 center, Color top, Color faceL, Color faceR)
    {
        sb.Draw(FaceL, new Vector2(center.X - TileW / 2f, center.Y - BlockH), faceL);
        sb.Draw(FaceR, new Vector2(center.X, center.Y - BlockH), faceR);
        DiamondAt(sb, new Vector2(center.X, center.Y - BlockH), top);
    }

    /// <summary>The team halo ring at a unit's feet, centred on its cell.</summary>
    public void HaloAt(SpriteBatch sb, Vector2 center, Color color)
    {
        sb.Draw(Halo, new Vector2(center.X - Halo.Width / 2f, center.Y - Halo.Height / 2f), color);
    }

    private static Texture2D BuildDiamond(GraphicsDevice gd, int w, int h)
    {
        var tex = new Texture2D(gd, w, h);
        var data = new Color[w * h];
        float hw = w / 2f, hh = h / 2f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float nx = Math.Abs(x + 0.5f - hw) / hw;
                float ny = Math.Abs(y + 0.5f - hh) / hh;
                data[y * w + x] = nx + ny <= 1f ? Color.White : Color.Transparent;
            }
        tex.SetData(data);
        return tex;
    }

    private static Texture2D BuildDisc(GraphicsDevice gd, int d)
    {
        var tex = new Texture2D(gd, d, d);
        var data = new Color[d * d];
        float r = d / 2f;
        for (int y = 0; y < d; y++)
            for (int x = 0; x < d; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                data[y * d + x] = dx * dx + dy * dy <= r * r ? Color.White : Color.Transparent;
            }
        tex.SetData(data);
        return tex;
    }

    public void FillRect(SpriteBatch sb, Rectangle rect, Color color) => sb.Draw(Pixel, rect, color);

    // ---- Rounded corners -------------------------------------------------------------
    // Not an antialiased radius: a staircase cut in steps of CrtPass.WorldPx, so the corner
    // survives the quantize pass as whole fat pixels instead of being smeared into grey.
    // Two textures (a fill mask and a border mask) get flipped into the four corners, the
    // same 9-slice trick the diamond/disc builders above already use.

    /// <summary>Corner step size in screen px — matched to the pixel grid so notches land on it.</summary>
    public const int CornerStep = SliceGame.WorldPx;

    private readonly Dictionary<int, Texture2D> _cornerFill = new();
    private readonly Dictionary<(int, int), Texture2D> _cornerEdge = new();
    private readonly GraphicsDevice _gd;

    /// <summary>How far in the staircase has eaten at band <paramref name="band"/> (0 = outermost).
    /// A 45° chamfer, which at these sizes reads as round and stays honest about the grid.</summary>
    private static int Inset(int radius, int band) => radius - band * CornerStep;

    private Texture2D CornerFill(int radius)
    {
        if (_cornerFill.TryGetValue(radius, out var cached)) return cached;
        var tex = new Texture2D(_gd, radius, radius);
        var data = new Color[radius * radius];
        for (int y = 0; y < radius; y++)
        {
            int inset = Math.Max(0, Inset(radius, y / CornerStep));
            for (int x = inset; x < radius; x++) data[y * radius + x] = Color.White;
        }
        tex.SetData(data);
        return _cornerFill[radius] = tex;
    }

    private Texture2D CornerEdge(int radius, int thickness)
    {
        if (_cornerEdge.TryGetValue((radius, thickness), out var cached)) return cached;
        var tex = new Texture2D(_gd, radius, radius);
        var data = new Color[radius * radius];
        void Put(int x, int y)
        {
            if (x >= 0 && y >= 0 && x < radius && y < radius) data[y * radius + x] = Color.White;
        }
        for (int y = 0; y < radius; y++)
        {
            int inset = Math.Max(0, Inset(radius, y / CornerStep));
            int prev = Math.Max(0, Inset(radius, y / CornerStep - 1));
            for (int t = 0; t < thickness; t++)
            {
                Put(inset + t, y);                                   // the riser
                if (y % CornerStep == 0)                             // the tread, at each step down
                    for (int x = inset; x < prev; x++) Put(x, y + t);
            }
        }
        tex.SetData(data);
        return _cornerEdge[(radius, thickness)] = tex;
    }

    // ---- Generated glyphs ------------------------------------------------------------

    private Texture2D? _slotGlyph;

    /// <summary>The empty-cell mark: a quatrefoil (four overlapping lobes) with a checker
    /// dither on one diagonal pair. Built at 16x16 art pixels and point-scaled by whole
    /// factors at draw time, so the dither stays a crisp checker instead of turning to mush.
    /// Generated rather than drawn — the whole point is that new slots cost no art.</summary>
    public Texture2D SlotGlyph()
    {
        if (_slotGlyph != null) return _slotGlyph;
        // 12x12 is the size that works. At 16 the art pixels land ~2 screen px and the
        // dither dissolves to grey under the bloom; at 8 the four lobes overlap so hard the
        // mark is just a dithered square. At 12 the lobes still leave concave notches at the
        // edge midpoints, which is what makes it read as a quatrefoil and not a blob.
        const int n = 12;
        var data = new Color[n * n];
        (float cx, float cy, bool dither)[] lobes =
        {
            (3.5f, 3.5f, true), (8.5f, 3.5f, false),
            (3.5f, 8.5f, false), (8.5f, 8.5f, true),
        };
        const float r = 3.4f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                foreach (var (cx, cy, dither) in lobes)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    if (dx * dx + dy * dy > r * r) continue;
                    // 2x2 blocks, not a 1px checker: a single-pixel checker averages to flat
                    // grey the moment the bloom touches it.
                    if (dither && (x / 2 + y / 2) % 2 != 0) continue;
                    data[y * n + x] = Color.White;
                    break;
                }
        _slotGlyph = new Texture2D(_gd, n, n);
        _slotGlyph.SetData(data);
        return _slotGlyph;
    }

    /// <summary>Draw a 16x16 generated glyph centred in <paramref name="r"/> at a whole scale.</summary>
    public void GlyphIn(SpriteBatch sb, Texture2D glyph, Rectangle r, Color color, int pad = 4)
    {
        int k = Math.Max(1, (Math.Min(r.Width, r.Height) - pad * 2) / glyph.Width);
        int s = glyph.Width * k;
        sb.Draw(glyph, new Rectangle(r.Center.X - s / 2, r.Center.Y - s / 2, s, s), color);
    }

    // ---- Bracketed frames ------------------------------------------------------------
    // The dungeon-UI signature: a hairline frame that thickens into an ornate L at each
    // corner, with a square notch punched out of the elbow. Same corner-mask + flip path
    // as the rounded corners, just a different mask — all of it on the 2px grid.

    private readonly Dictionary<(int, int), Texture2D> _cornerBracket = new();

    private Texture2D CornerBracket(int arm, int cell)
    {
        if (_cornerBracket.TryGetValue((arm, cell), out var cached)) return cached;
        int n = arm * cell;
        var tex = new Texture2D(_gd, n, n);
        var data = new Color[n * n];
        void Rect(int cx, int cy, int cw, int ch, bool on)
        {
            for (int y = cy * cell; y < (cy + ch) * cell && y < n; y++)
                for (int x = cx * cell; x < (cx + cw) * cell && x < n; x++)
                    data[y * n + x] = on ? Color.White : Color.Transparent;
        }
        Rect(0, 0, arm, 1, true);        // the outer arms, one cell thick
        Rect(0, 0, 1, arm, true);
        Rect(2, 2, arm - 2, 1, true);    // the inner arms, set in by a cell of black
        Rect(2, 2, 1, arm - 2, true);
        Rect(2, 2, 1, 1, false);         // and the notch punched from the elbow
        tex.SetData(data);
        return _cornerBracket[(arm, cell)] = tex;
    }

    /// <summary>A hairline frame with an ornate bracket at each corner. <paramref name="arm"/>
    /// is the bracket length in grid cells; it shrinks on small rects so a short panel keeps a
    /// frame rather than four brackets meeting in the middle.</summary>
    public void BracketRect(SpriteBatch sb, Rectangle r, Color color, int arm = 0, int thickness = 1)
    {
        int cell = CornerStep;
        // Scale the bracket with the panel: a fixed arm reads as a hairline tick on a wide
        // HUD and swallows a small tooltip. Roughly a sixth of the shorter side, as in the
        // reference art, then clamped so two brackets can never meet.
        if (arm <= 0) arm = Math.Clamp(Math.Min(r.Width, r.Height) / (cell * 6), 4, 14);
        arm = Math.Min(arm, Math.Min(r.Width, r.Height) / (2 * cell) - 1);
        if (arm < 4) { StrokeRect(sb, r, thickness, color); return; }
        // The hairline runs the full perimeter at half weight; the brackets sit over its
        // corners at full weight. That hierarchy is what makes the corners read as ornament
        // instead of the frame just being thick.
        StrokeRect(sb, r, thickness, color * 0.55f);
        Corners(sb, r, CornerBracket(arm, cell), arm * cell, color);
    }

    /// <summary>Clamp a requested radius so it can never eat more than a third of the smaller
    /// side — a 10px-tall bar keeps its shape instead of becoming a lozenge.</summary>
    public static int FitRadius(Rectangle r, int radius)
    {
        int max = Math.Min(r.Width, r.Height) / 3;
        int fit = Math.Min(radius, max);
        return fit - fit % CornerStep;   // stay on the grid
    }

    private void Corners(SpriteBatch sb, Rectangle r, Texture2D tex, int radius, Color color)
    {
        int x1 = r.Right - radius, y1 = r.Bottom - radius;
        sb.Draw(tex, new Vector2(r.X, r.Y), null, color, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
        sb.Draw(tex, new Vector2(x1, r.Y), null, color, 0f, Vector2.Zero, 1f, SpriteEffects.FlipHorizontally, 0f);
        sb.Draw(tex, new Vector2(r.X, y1), null, color, 0f, Vector2.Zero, 1f, SpriteEffects.FlipVertically, 0f);
        sb.Draw(tex, new Vector2(x1, y1), null, color, 0f, Vector2.Zero, 1f,
            SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically, 0f);
    }

    /// <summary>A filled rect with stepped corners. Falls back to a plain fill below 1 step.</summary>
    public void FillRoundRect(SpriteBatch sb, Rectangle r, int radius, Color color)
    {
        radius = FitRadius(r, radius);
        if (radius < CornerStep) { FillRect(sb, r, color); return; }
        FillRect(sb, new Rectangle(r.X, r.Y + radius, r.Width, r.Height - 2 * radius), color);
        FillRect(sb, new Rectangle(r.X + radius, r.Y, r.Width - 2 * radius, radius), color);
        FillRect(sb, new Rectangle(r.X + radius, r.Bottom - radius, r.Width - 2 * radius, radius), color);
        Corners(sb, r, CornerFill(radius), radius, color);
    }

    /// <summary>The border of a stepped-corner rect.</summary>
    public void StrokeRoundRect(SpriteBatch sb, Rectangle r, int radius, int thickness, Color color)
    {
        radius = FitRadius(r, radius);
        if (radius < CornerStep) { StrokeRect(sb, r, thickness, color); return; }
        FillRect(sb, new Rectangle(r.X + radius, r.Y, r.Width - 2 * radius, thickness), color);
        FillRect(sb, new Rectangle(r.X + radius, r.Bottom - thickness, r.Width - 2 * radius, thickness), color);
        FillRect(sb, new Rectangle(r.X, r.Y + radius, thickness, r.Height - 2 * radius), color);
        FillRect(sb, new Rectangle(r.Right - thickness, r.Y + radius, thickness, r.Height - 2 * radius), color);
        Corners(sb, r, CornerEdge(radius, thickness), radius, color);
    }

    public void StrokeRect(SpriteBatch sb, Rectangle r, int thickness, Color color)
    {
        FillRect(sb, new Rectangle(r.X, r.Y, r.Width, thickness), color);
        FillRect(sb, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), color);
        FillRect(sb, new Rectangle(r.X, r.Y, thickness, r.Height), color);
        FillRect(sb, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), color);
    }

    public void Line(SpriteBatch sb, Vector2 a, Vector2 b, float thickness, Color color)
    {
        Vector2 delta = b - a;
        float len = delta.Length();
        float angle = (float)Math.Atan2(delta.Y, delta.X);
        sb.Draw(Pixel, a, null, color, angle, new Vector2(0, 0.5f),
            new Vector2(len, thickness), SpriteEffects.None, 0f);
    }

    /// <summary>Draw the iso diamond so its centre sits at <paramref name="center"/>.</summary>
    /// <summary>Square-grid pixel mode: cell highlights render as squares instead of iso diamonds.
    /// One switch here re-shapes every hover/range/placement highlight in the game.</summary>
    public bool SquareMode { get; set; }
    public int SquareSize { get; set; } = 40;

    public void DiamondAt(SpriteBatch sb, Vector2 center, Color color)
    {
        if (SquareMode)
        {
            int s = SquareSize - 2; // a 1px inset so adjacent highlights read as separate cells
            FillRect(sb, new Rectangle((int)(center.X - s / 2f), (int)(center.Y - s / 2f), s, s), color);
            return;
        }
        sb.Draw(Diamond, new Vector2(center.X - TileW / 2f, center.Y - TileH / 2f), color);
    }

    public void DiscAt(SpriteBatch sb, Vector2 center, float radius, Color color)
    {
        float scale = radius * 2f / DiscSize;
        sb.Draw(Disc, center, null, color, 0f, new Vector2(DiscSize / 2f), scale, SpriteEffects.None, 0f);
    }
}

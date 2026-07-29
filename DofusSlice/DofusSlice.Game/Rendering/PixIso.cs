using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DofusSlice.Game.Rendering;

/// <summary>
/// Bakes Dofus-style iso diamond floor tiles out of the Chamber8x8 floor sheet: the 8×8 paving
/// art is tiled axis-aligned across a 32×16 native diamond (exactly how 1.29 grounds clip a
/// flat texture to the cell), then point-doubled to the 64×32 screen diamond so every texel is
/// a crisp 2×2. Plain variants floor the room; worn variants (the pack's detail tiles) cluster
/// toward the map edge per TILESET-RULES §7.
/// </summary>
public sealed class IsoFloorBank
{
    public const int W = 64, H = 32;          // screen diamond
    private const int NW = 32, NH = 16;       // native diamond (2× point scale)

    private readonly Texture2D?[] _plain = new Texture2D?[3];
    private readonly Texture2D?[] _worn = new Texture2D?[3];
    private readonly Texture2D? _pit;
    public bool Loaded { get; }

    public IsoFloorBank(GraphicsDevice gd, Texture2D? floorSheet)
    {
        if (floorSheet == null) return;
        var data = new Color[floorSheet.Width * floorSheet.Height];
        floorSheet.GetData(data);
        int cols = floorSheet.Width / 8;

        Color Sample(int tile, int px, int py) =>
            data[(tile / cols * 8 + py) * floorSheet.Width + tile % cols * 8 + px];

        // A variant is a 4×2 arrangement of floor tiles sampled under the diamond mask.
        Texture2D Bake(int[] tiles)
        {
            var native = new Color[NW * NH];
            for (int y = 0; y < NH; y++)
                for (int x = 0; x < NW; x++)
                {
                    float nx = MathF.Abs(x + 0.5f - NW / 2f) / (NW / 2f);
                    float ny = MathF.Abs(y + 0.5f - NH / 2f) / (NH / 2f);
                    if (nx + ny > 1f) continue;
                    int t = tiles[y / 8 * 4 + x / 8];
                    native[y * NW + x] = Sample(t, x % 8, y % 8);
                }
            var tex = new Texture2D(gd, W, H);
            var scaled = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    scaled[y * W + x] = native[y / 2 * NW + x / 2];
            tex.SetData(scaled);
            return tex;
        }

        int P = Ch.FPlain;
        _plain[0] = Bake(new[] { P, P, P, P, P, P, P, P });
        _plain[1] = Bake(new[] { P, Ch.FDetail[0], P, P, P, P, Ch.FDetail[1], P });
        _plain[2] = Bake(new[] { P, P, Ch.FDetail[2], P, Ch.FDetail[3], P, P, P });
        // Worn = denser detail tiles only; the sheet's edge tiles carry heavy brown blocks
        // that tile into streaks, so they stay out of the diamond bakes.
        _worn[0] = Bake(new[] { Ch.FDetail[0], P, Ch.FDetail[3], P, P, Ch.FDetail[2], P, Ch.FDetail[1] });
        _worn[1] = Bake(new[] { P, Ch.FDetail[1], Ch.FDetail[2], P, Ch.FDetail[0], P, Ch.FDetail[3], P });
        _worn[2] = Bake(new[] { Ch.FDetail[2], Ch.FDetail[0], P, Ch.FDetail[1], P, Ch.FDetail[3], Ch.FDetail[0], P });
        _pit = Bake(new[] { Ch.FVoid, Ch.FVoid, Ch.FVoid, Ch.FVoid, Ch.FVoid, Ch.FVoid, Ch.FVoid, Ch.FVoid });
        Loaded = true;
    }

    /// <summary>Draw one floor diamond centred on the cell centre.</summary>
    public void Draw(SpriteBatch sb, Vector2 center, int variant, bool worn, Color tint)
    {
        var set = worn ? _worn : _plain;
        var tex = set[variant % set.Length] ?? _plain[0];
        if (tex == null) return;
        sb.Draw(tex, center - new Vector2(W / 2f, H / 2f), tint);
    }

    public void DrawPit(SpriteBatch sb, Vector2 center, Color tint)
    {
        if (_pit != null) sb.Draw(_pit, center - new Vector2(W / 2f, H / 2f), tint);
    }
}

/// <summary>A 9-slice texture: corners stay crisp, edges and centre stretch.</summary>
public sealed class NineSlice
{
    private readonly Texture2D _tex;
    private readonly int _b;

    public NineSlice(Texture2D tex, int border) { _tex = tex; _b = border; }

    public void Draw(SpriteBatch sb, Rectangle r, Color tint, int scale = 2)
    {
        int b = _b * scale;
        int tw = _tex.Width, th = _tex.Height;
        int sb_ = _b;
        // rows: top, middle, bottom × cols: left, centre, right
        (int sy, int sh, int dy, int dh)[] rows =
        {
            (0, sb_, r.Y, b),
            (sb_, th - 2 * sb_, r.Y + b, r.Height - 2 * b),
            (th - sb_, sb_, r.Bottom - b, b),
        };
        (int sx, int sw, int dx, int dw)[] cols =
        {
            (0, sb_, r.X, b),
            (sb_, tw - 2 * sb_, r.X + b, r.Width - 2 * b),
            (tw - sb_, sb_, r.Right - b, b),
        };
        foreach (var (sy, sh, dy, dh) in rows)
            foreach (var (sx, sw, dx, dw) in cols)
                sb.Draw(_tex, new Rectangle(dx, dy, dw, dh), new Rectangle(sx, sy, sw, sh), tint);
    }
}

/// <summary>
/// The pixel UI skin baked from the Free Basic Pixel Art UI pack (local-only art): cream
/// panels, titled panels, cards and the green button, all 9-sliced at 2×. Absent files mean
/// <see cref="Loaded"/> is false and the game keeps its flat procedural panels.
/// </summary>
public sealed class UiSkin
{
    public NineSlice? Panel, PanelTitled, Card, Button, ButtonDown;
    public bool Loaded { get; }

    public UiSkin(SpriteBank bank)
    {
        if (bank.Get("ui_panel") is { } p) Panel = new NineSlice(p, 6);
        if (bank.Get("ui_panel_titled") is { } t) PanelTitled = new NineSlice(t, 7);
        if (bank.Get("ui_card") is { } c) Card = new NineSlice(c, 7);
        if (bank.Get("ui_button") is { } b) Button = new NineSlice(b, 5);
        if (bank.Get("ui_button_down") is { } d) ButtonDown = new NineSlice(d, 5);
        Loaded = Panel != null && Button != null;
    }
}

/// <summary>
/// Bitmap font baked from a local TTF by tools/bake_assets.py (atlas + JSON metrics, both
/// gitignored). Integer scales only — the dungeon font stays pixel-perfect at 1× and 2×.
/// </summary>
public sealed class UiFont
{
    private readonly Texture2D? _tex;
    private readonly Dictionary<char, int[]> _glyphs = new();
    private readonly int _lineHeight = 16;
    public bool Loaded => _tex != null;
    public int LineHeight(int scale) => _lineHeight * scale;

    public UiFont(SpriteBank bank, string name = "ui_font")
    {
        _tex = bank.Get(name);
        if (_tex == null) return;
        try
        {
            var metaPath = new[] { "assets", "assets-default" }
                .Select(d => Path.Combine(AppContext.BaseDirectory, d, name + ".json"))
                .FirstOrDefault(File.Exists);
            if (metaPath == null) { _tex = null; return; }
            using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
            _lineHeight = doc.RootElement.GetProperty("lineHeight").GetInt32();
            foreach (var g in doc.RootElement.GetProperty("glyphs").EnumerateObject())
                _glyphs[g.Name[0]] = g.Value.EnumerateArray().Select(v => v.GetInt32()).ToArray();
        }
        catch
        {
            _tex = null; // malformed metrics -> PixelFont fallback
        }
    }

    public int Measure(string text, int scale)
    {
        int w = 0;
        foreach (var ch in text)
            w += (_glyphs.TryGetValue(ch, out var g) ? g[6] : _lineHeight / 2) * scale;
        return w;
    }

    public void Draw(SpriteBatch sb, string text, int x, int y, int scale, Color color)
    {
        if (_tex == null) return;
        int cx = x;
        foreach (var ch in text)
        {
            if (!_glyphs.TryGetValue(ch, out var g)) { cx += _lineHeight / 2 * scale; continue; }
            sb.Draw(_tex, new Rectangle(cx + g[4] * scale, y + g[5] * scale, g[2] * scale, g[3] * scale),
                new Rectangle(g[0], g[1], g[2], g[3]), color);
            cx += g[6] * scale;
        }
    }

    public void DrawCentered(SpriteBatch sb, string text, int cx, int y, int scale, Color color) =>
        Draw(sb, text, cx - Measure(text, scale) / 2, y, scale, color);
}

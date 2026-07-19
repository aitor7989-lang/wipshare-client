using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DofusSlice.Game.Rendering;

/// <summary>
/// A sheet of 8×8 tiles drawn point-scaled to <see cref="Cell"/>-pixel cells (index = row·cols + col;
/// the column count comes from the sheet width). Loaded from local-only PNGs that are never
/// committed (third-party art); when absent the game keeps its procedural iso look, so the repo
/// runs without them. Pure-magenta (#FF00FF) pixels are keyed out for sheets that use it.
/// </summary>
public sealed class TileSet
{
    public const int Src = 8;    // tile size in the sheet
    public const int Cell = 32;  // on-screen cell size (a crisp 4× point scale)

    private const int Pad = Src + 2; // padded stride: 1px extruded gutter kills atlas bleeding

    private readonly Texture2D? _tex;
    private readonly int _cols;
    public bool Loaded => _tex != null;

    public TileSet(Texture2D? sheet)
    {
        if (sheet == null) return;
        _cols = sheet.Width / Src;

        var src = new Color[sheet.Width * sheet.Height];
        sheet.GetData(src);
        for (int i = 0; i < src.Length; i++)
            if (src[i].R > 240 && src[i].B > 240 && src[i].G < 40)
                src[i] = Color.Transparent;

        // Repack into a padded atlas: each 8×8 tile gets a 1px gutter of its own extruded edge
        // pixels, so sub-pixel camera offsets can never sample a neighbouring tile's edge.
        int cols = _cols, rows = sheet.Height / Src;
        var tex = new Texture2D(sheet.GraphicsDevice, cols * Pad, rows * Pad);
        var dst = new Color[cols * Pad * rows * Pad];
        for (int ty = 0; ty < rows; ty++)
            for (int tx = 0; tx < cols; tx++)
                for (int py = -1; py <= Src; py++)
                    for (int px = -1; px <= Src; px++)
                    {
                        int sx = tx * Src + Math.Clamp(px, 0, Src - 1);
                        int sy = ty * Src + Math.Clamp(py, 0, Src - 1);
                        dst[(ty * Pad + 1 + py) * cols * Pad + tx * Pad + 1 + px] = src[sy * sheet.Width + sx];
                    }
        tex.SetData(dst);
        _tex = tex;
    }

    private Rectangle Rect(int idx) => new(idx % _cols * Pad + 1, idx / _cols * Pad + 1, Src, Src);

    /// <summary>Draw a tile centred on a cell centre (floors, walls, flat props).</summary>
    public void Draw(SpriteBatch sb, int idx, Vector2 center, Color? tint = null, float scale = 1f,
        bool flip = false)
    {
        if (_tex == null) return;
        float s = Cell / (float)Src * scale;
        sb.Draw(_tex, center, Rect(idx), tint ?? Color.White, 0f,
            new Vector2(Src / 2f, Src / 2f), s, flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
    }

    /// <summary>Draw anchored at the feet (bottom edge sits on the cell's bottom edge) — standing
    /// sprites and props grow upward out of their tile, so a scaled-up boss looms over the board.</summary>
    public void DrawFeet(SpriteBatch sb, int idx, Vector2 cellCenter, Color? tint = null, float scale = 1f)
    {
        if (_tex == null) return;
        float s = Cell / (float)Src * scale;
        sb.Draw(_tex, cellCenter + new Vector2(0, Cell / 2f), Rect(idx), tint ?? Color.White, 0f,
            new Vector2(Src / 2f, Src), s, SpriteEffects.None, 0f);
    }
}

/// <summary>
/// The Chamber8x8 skin: wall + floor autotile atlases, named props, and the 4-frame knight.
/// All art is loaded from the gitignored assets folder (chamber_*.png) and never committed;
/// <see cref="Loaded"/> is false without it and the game keeps its procedural look.
/// See docs/TILESET-RULES.md for the composition grammar these pieces obey.
/// </summary>
public sealed class ChamberSet
{
    public TileSet Wall { get; }
    public TileSet Floor { get; }
    public bool Loaded { get; }

    private readonly Dictionary<string, Texture2D> _props = new();
    private readonly Dictionary<string, Texture2D> _anim = new();

    public ChamberSet(SpriteBank bank)
    {
        Wall = new TileSet(bank.Get("chamber_wall"));
        Floor = new TileSet(bank.Get("chamber_floor"));
        Loaded = Wall.Loaded && Floor.Loaded;
        if (!Loaded) return;

        foreach (var name in new[]
        {
            "chest", "chest_open", "chest_b", "gold1", "gold2", "gold3", "door", "door_side",
            "pillar", "web", "webb", "bones", "bonesb", "barrel", "crown", "key", "metal",
            "wood", "woodr", "fx_smear",
        })
            if (bank.Get("chamber_" + name) is { } t) _props[name] = t;
        foreach (var state in new[] { "idle", "walk", "use" })
            if (bank.Get("chamber_knight_" + state) is { } t) _anim[state] = t;
    }

    /// <summary>Feet-anchored prop draw at an integer point scale (4× per unit of scale).</summary>
    public void Prop(SpriteBatch sb, string name, Vector2 feet, Color? tint = null, float scale = 1f)
    {
        if (!_props.TryGetValue(name, out var t)) return;
        float s = TileSet.Cell / (float)TileSet.Src * scale;
        sb.Draw(t, feet, null, tint ?? Color.White, 0f,
            new Vector2(t.Width / 2f, t.Height), s, SpriteEffects.None, 0f);
    }

    /// <summary>One knight frame, feet-anchored; <paramref name="flip"/> mirrors it to face west.</summary>
    public void Knight(SpriteBatch sb, string state, int frame, Vector2 feet, Color tint,
        bool flip = false, float scale = 1f)
    {
        if (!_anim.TryGetValue(state, out var t) && !_anim.TryGetValue("idle", out t)) return;
        int frames = Math.Max(1, t.Width / TileSet.Src);
        var rect = new Rectangle(frame % frames * TileSet.Src, 0, TileSet.Src, t.Height);
        float s = TileSet.Cell / (float)TileSet.Src * scale;
        sb.Draw(t, feet, rect, tint, 0f, new Vector2(TileSet.Src / 2f, t.Height), s,
            flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
    }
}

/// <summary>
/// Named tile indices for the Chamber8x8 sheets, decoded from the pack's example screenshots
/// (docs/TILESET-RULES.md cites the evidence per tile). Data-ish by design: retile the game by
/// editing this table, not the draw code.
/// </summary>
public static class Ch
{
    // Wall sheet (4 columns): the ring autotile. Index = row·4 + col.
    public const int WallNW = 0, WallN = 1, WallNE = 2;
    public const int WallW = 4, WallE = 6;
    public const int WallSW = 8, WallS = 9, WallSE = 10;
    public const int WallInnerW = 12, WallInnerE = 13;   // concave corners for stepped outlines
    public const int WallFill = 14;                       // plain cap: 2-thick walls, junctions

    // Floor sheet (16 columns): the edge autotile. Dark side of each edge tile faces the wall.
    public const int FVoid = 26, FPlain = 41;
    public const int FNW = 8, FNE = 11, FSW = 56, FSE = 59;
    public static readonly int[] FN = { 9, 10 };
    public static readonly int[] FW = { 24, 40 };
    public static readonly int[] FE = { 27, 43 };
    public static readonly int[] FS = { 57, 58 };
    public const int FNubNW = 21, FNubNE = 22, FNubSW = 37, FNubSE = 38; // diagonal-only contact
    public static readonly int[] FDetail = { 29, 30, 45, 46 };           // sparse interior detail

    /// <summary>The dark between-rooms colour — the screen clear when the skin is active.</summary>
    public static readonly Color VoidInk = new(11, 16, 22);

    // Family tints: one stone family ships, so scenes differ by tint (TILESET-RULES.md §5.3).
    public static readonly Color CityTint = Color.White;
    public static readonly Color YardTint = new(202, 212, 192);
    public static readonly Color CryptTint = new(188, 198, 220);
}

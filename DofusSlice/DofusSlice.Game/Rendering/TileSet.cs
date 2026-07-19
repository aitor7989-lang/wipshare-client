using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DofusSlice.Game.Rendering;

/// <summary>
/// The 8-bit tileset renderer: an 8-column sheet of 8×8 tiles (index = row·8 + col), drawn
/// point-scaled to <see cref="Cell"/>-pixel cells for the top-down pixel look. Loaded from
/// <c>assets/tileset.png</c> — a local-only file that is never committed (third-party art);
/// when it is absent the game keeps its procedural iso look, so the repo runs without it.
/// The pack marks sprite transparency with pure magenta (#FF00FF), keyed out on load.
/// </summary>
public sealed class TileSet
{
    public const int Src = 8;    // tile size in the sheet
    public const int Cell = 32;  // on-screen cell size (a crisp 4× point scale)

    private const int Pad = Src + 2; // padded stride: 1px extruded gutter kills atlas bleeding

    private readonly Texture2D? _tex;
    public bool Loaded => _tex != null;

    public TileSet(Texture2D? sheet)
    {
        if (sheet == null) return;

        var src = new Color[sheet.Width * sheet.Height];
        sheet.GetData(src);
        for (int i = 0; i < src.Length; i++)
            if (src[i].R > 240 && src[i].B > 240 && src[i].G < 40)
                src[i] = Color.Transparent;

        // Repack into a padded atlas: each 8×8 tile gets a 1px gutter of its own extruded edge
        // pixels, so sub-pixel camera offsets can never sample a neighbouring tile's edge.
        int cols = sheet.Width / Src, rows = sheet.Height / Src;
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

    private static Rectangle Rect(int idx) => new(idx % 8 * Pad + 1, idx / 8 * Pad + 1, Src, Src);

    /// <summary>Draw a tile centred on a cell centre (floors, rugs, flat props).</summary>
    public void Draw(SpriteBatch sb, int idx, Vector2 center, Color? tint = null, float scale = 1f)
    {
        if (_tex == null) return;
        float s = Cell / (float)Src * scale;
        sb.Draw(_tex, center, Rect(idx), tint ?? Color.White, 0f,
            new Vector2(Src / 2f, Src / 2f), s, SpriteEffects.None, 0f);
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
/// Named tile indices for the Scut 7DRL sheet, catalogued from the pack's own example map.
/// Data-ish by design: retile the game by editing this table, not the draw code.
/// </summary>
public static class Tid
{
    // Purple interior (the City). Wall sandwich per TILESET-RULES.md §1.
    public const int CityFloor = 15;
    public static readonly int[] FloorShades = { 16, 17, 26 }; // west-wall floor shading, varied
    public const int CityBand = 2, CityCornerL = 1, CityCornerR = 3, CitySide = 3;
    public static readonly int[] CityFace = { 5, 13, 14 };  // [edge, base, sparse]
    public const int CityDecor = 12, CitySkirt = 46;
    public const int RugGold = 20, RugCheck = 21, RugWeave = 47, RugBlue = 52;
    public const int Shelf = 6, Console = 10, Brazier = 50, Statue = 12;

    // Dark mossy outdoors (the Graveyard): dark ground, mossy growth in clumps.
    public static readonly int[] YardBase = { 82, 83 };  // mixed per-cell, reference-style
    public static readonly int[] YardMoss = { 88, 89, 97 };
    public const int YardBramble = 90;                   // rare accent, never common
    public const int DunBand = 105, DunCornerL = 84, DunCornerR = 85, DunSide = 94;
    public static readonly int[] DunFace = { 111, 112, 113 };  // [edge, base, sparse]
    public const int DunDecor = 114, DunSkirt = 106, DunSkirtAlt = 107;
    public const int YardVoid = 94, CityVoid = 7, DunVoid = 91;

    // Blue dungeon (the Crypt's fight arenas).
    public const int CryptFloor = 25;
    public static readonly int[] CryptClumps = { 82, 83, 86, 93 };

    public const int Void = 94, Water = 92, WaterGlint = 98;
    public const int Tombstone = 110, Tree = 45, RockBlob = 44, Pillar = 33;

    // Sprites (magenta-keyed). Pairs are idle animation frames (base, base+1).
    public const int FigA = 74, FigB = 76, FigC = 78, FigD = 80; // little black people
    public const int Ghost = 65, Bird = 63, Spider = 101, Bat = 99, Crab = 100, Mite = 67;
    public const int Bang = 68, Question = 69, GoldPile = 70, Chest = 71, Arch = 110, Torch = 114;
}

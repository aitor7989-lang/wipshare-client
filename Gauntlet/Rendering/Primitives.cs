using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Gauntlet.Rendering;

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
        TileW = tileW;
        TileH = tileH;
        DiscSize = discSize;

        Pixel = new Texture2D(gd, 1, 1);
        Pixel.SetData(new[] { Color.White });

        Diamond = BuildDiamond(gd, tileW, tileH);
        Disc = BuildDisc(gd, discSize);
        FaceL = BuildFace(gd, tileW / 2, tileH / 2, BlockH, left: true);
        FaceR = BuildFace(gd, tileW / 2, tileH / 2, BlockH, left: false);
        Halo = BuildHalo(gd, 54, 27, 3);   // wide enough that 44px bodies stand inside it
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

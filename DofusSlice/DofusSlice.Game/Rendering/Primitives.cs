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

    public int TileW { get; }
    public int TileH { get; }
    public int DiscSize { get; }

    public Primitives(GraphicsDevice gd, int tileW, int tileH, int discSize)
    {
        TileW = tileW;
        TileH = tileH;
        DiscSize = discSize;

        Pixel = new Texture2D(gd, 1, 1);
        Pixel.SetData(new[] { Color.White });

        Diamond = BuildDiamond(gd, tileW, tileH);
        Disc = BuildDisc(gd, discSize);
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

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DofusSlice.Game.Rendering;

/// <summary>
/// The Emberwick combat palette — tokens imported from the user's Claude Design project
/// ("Game UI system design" → Emberwick Combat UI). Combat runs the slate surface set with
/// the dark-amber accent; resource gems keep the Vim/Spark/Stride colors.
/// </summary>
public static class Ew
{
    // Under the ONE-BIT reset every Emberwick token collapses to the Mono inks, so the whole
    // combat HUD re-reads as ink-on-black without touching a single call site.

    // Slate surfaces (the combat variant of the moss-glass system).
    public static readonly Color SurfaceDeep = Mono.On ? Mono.Bg : new Color(0x2b, 0x2d, 0x35);
    public static readonly Color Surface = Mono.On ? Mono.Panel : new Color(0x32, 0x34, 0x3d);
    public static readonly Color SurfaceRaised = Mono.On ? new Color(20, 20, 20) : new Color(0x46, 0x4a, 0x56);
    public static readonly Color SurfaceSunken = Mono.On ? new Color(10, 10, 10) : new Color(0x24, 0x26, 0x2d);
    public static readonly Color Outline = Mono.On ? Mono.Faint : new Color(0x1c, 0x1e, 0x24);

    // Panel gradient stops (--grad-panel) and header stops (--grad-header).
    public static readonly Color PanelTop = Mono.On ? Mono.Panel : new Color(0x37, 0x3a, 0x45);
    public static readonly Color PanelBottom = Mono.On ? Mono.Panel : new Color(0x2d, 0x2f, 0x38);
    public static readonly Color HeaderTop = Mono.On ? new Color(22, 22, 21) : new Color(0x57, 0x5c, 0x6a);
    public static readonly Color HeaderBottom = Mono.On ? new Color(22, 22, 21) : new Color(0x3a, 0x3d, 0x47);

    // Ink.
    public static readonly Color Ink = Mono.On ? Mono.Ink : new Color(0xf2, 0xef, 0xdc);
    public static readonly Color InkSoft = Mono.On ? Mono.Dim : new Color(0xbd, 0xbc, 0xa6);
    public static readonly Color InkMuted = Mono.On ? new Color(84, 84, 82) : new Color(0x83, 0x85, 0x6c);
    public static readonly Color InkInverse = Mono.On ? Mono.Panel : new Color(0x23, 0x26, 0x1a);

    // Dark-amber accent (combat) + gold + danger. Mono keeps ONE accent: Danger red.
    public static readonly Color Accent = Mono.On ? Mono.Ink : new Color(0xc0, 0x81, 0x45);
    public static readonly Color AccentBright = Mono.On ? Mono.Ink : new Color(0xdf, 0xa5, 0x62);
    public static readonly Color AccentTop = Mono.On ? new Color(20, 20, 20) : new Color(0xdd, 0xa4, 0x5c);
    public static readonly Color AccentBottom = Mono.On ? new Color(20, 20, 20) : new Color(0x9d, 0x66, 0x30);
    public static readonly Color Gold = Mono.On ? Mono.Ink : new Color(0xff, 0xd0, 0x42);
    public static readonly Color GoldTop = Mono.On ? new Color(20, 20, 20) : new Color(0xf0, 0xc7, 0x5e);
    public static readonly Color GoldBottom = Mono.On ? new Color(20, 20, 20) : new Color(0xb7, 0x84, 0x28);
    public static readonly Color Danger = Mono.On ? Mono.Danger : new Color(0xe9, 0x4b, 0x58);

    // Resource gems: Vim / Spark / Stride (mono: white shapes, dim depths).
    public static readonly Color Hp = Mono.On ? Mono.Ink : new Color(0xe9, 0x4b, 0x58);
    public static readonly Color HpDeep = Mono.On ? Mono.Faint : new Color(0x8e, 0x27, 0x33);
    public static readonly Color Ap = Mono.On ? Mono.Ink : new Color(0x4f, 0x8f, 0xf0);
    public static readonly Color ApDeep = Mono.On ? Mono.Faint : new Color(0x2a, 0x55, 0xa8);
    public static readonly Color Mp = Mono.On ? Mono.Ink : new Color(0x59, 0xc1, 0x4e);
    public static readonly Color MpDeep = Mono.On ? Mono.Faint : new Color(0x2e, 0x7a, 0x2a);

    // Elements: ember / brook / loam / gale / moon (mono: all ink — the log stays legible).
    public static readonly Color Ember = Mono.On ? Mono.Ink : new Color(0xf0, 0x81, 0x2e);
    public static readonly Color Brook = Mono.On ? Mono.Ink : new Color(0x52, 0xa7, 0xe8);
    public static readonly Color Loam = Mono.On ? Mono.Ink : new Color(0xbd, 0x8a, 0x3c);
    public static readonly Color Gale = Mono.On ? Mono.Ink : new Color(0x86, 0xce, 0x4b);
    public static readonly Color Moon = Mono.On ? Mono.Ink : new Color(0xa9, 0x7f, 0xd6);

    public static readonly Color Xp = Mono.On ? Mono.Ink : new Color(0x8f, 0xd1, 0x30);
}

/// <summary>
/// Emberwick chrome, rendered from nothing: rounded slate panels with the 2px ink outline and
/// vertical gradient, sunken slot wells, amber/gold pill buttons, and the glossy gem badges
/// (heart / star / diamond) for Vim / Spark / Stride. Everything is rasterized at load or on
/// first use — the design ships in the repo with zero image assets.
/// </summary>
public sealed class EwChrome
{
    private readonly GraphicsDevice _gd;
    private readonly Texture2D _pixel;
    private readonly Dictionary<string, Texture2D> _cache = new();

    /// <summary>When the Dofus oldUI theme is loaded, every Emberwick surface re-dresses in it —
    /// one hook reskins the combat band, log, timeline cards, tooltips and pills at once.</summary>
    public DofusUi? Theme { get; set; }
    private DofusUi? T => Theme is { Loaded: true } t ? t : null;

    public EwChrome(GraphicsDevice gd, Texture2D pixel)
    {
        _gd = gd;
        _pixel = pixel;
    }

    // ----- rounded panels -------------------------------------------------------------

    /// <summary>A rounded slate panel with outline, gradient fill and top bevel highlight.</summary>
    public void Panel(SpriteBatch sb, Rectangle r, bool sunken = false, int radius = 12)
    {
        if (Mono.On)
        {
            sb.Draw(_pixel, r, Mono.Panel * 0.94f);
            Stroke(sb, r, sunken ? Mono.Faint : Mono.Dim);
            return;
        }
        if (T is { } th) { th.Panel(sb, r, light: !sunken); return; }
        var tex = RoundedTex(Math.Max(8, Math.Min(r.Width, 64)), Math.Max(8, Math.Min(r.Height, 64)),
            radius, sunken);
        NineSlice(sb, tex, r, radius + 3);
    }

    /// <summary>A slot well: sunken, tighter radius (the design's item slots).</summary>
    public void Well(SpriteBatch sb, Rectangle r)
    {
        if (Mono.On)
        {
            sb.Draw(_pixel, r, Mono.Panel);
            Stroke(sb, r, Mono.Faint);
            return;
        }
        if (T is { } th) { th.Slot(sb, r); return; }
        Panel(sb, r, sunken: true, radius: 8);
    }

    /// <summary>The header band gradient inside a panel's top (drawn as a plain strip).</summary>
    public void HeaderStrip(SpriteBatch sb, Rectangle r)
    {
        if (Mono.On)
        {
            sb.Draw(_pixel, r, Mono.Panel);
            sb.Draw(_pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), Mono.Ink);
            return;
        }
        if (T != null)
        {
            // Themed: a flat near-black band with a gold hairline — the ornament strip art
            // distorts when squashed below its native height, so it is never scaled here.
            GradientV(sb, r, new Color(30, 28, 26), new Color(16, 15, 14));
            sb.Draw(_pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), new Color(146, 116, 58));
            return;
        }
        GradientV(sb, r, Ew.HeaderTop, Ew.HeaderBottom);
        sb.Draw(_pixel, new Rectangle(r.X, r.Y, r.Width, 1), Color.White * 0.12f);
        sb.Draw(_pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), Color.Black * 0.38f);
    }

    /// <summary>An amber (or gold) pill button, design's "Pick challenges" CTA.</summary>
    public void Pill(SpriteBatch sb, Rectangle r, bool gold = false, bool pressed = false)
    {
        if (Mono.On)
        {
            // 1-bit CTA: pressed/hover INVERTS. Label ink comes from Mono.ButtonInk at call sites.
            sb.Draw(_pixel, r, pressed ? Mono.Ink : Mono.Panel);
            Stroke(sb, r, Mono.Ink);
            return;
        }
        if (T is { } th) { th.Button(sb, r, pressed: pressed); return; }
        var top = gold ? Ew.GoldTop : Ew.AccentTop;
        var bottom = gold ? Ew.GoldBottom : Ew.AccentBottom;
        if (pressed) { top = bottom; }
        var tex = PillTex(Math.Min(r.Height, 64), top, bottom);
        // stretch horizontally between two rounded caps
        int cap = tex.Height / 2;
        sb.Draw(tex, new Rectangle(r.X, r.Y, cap, r.Height), new Rectangle(0, 0, cap, tex.Height), Color.White);
        sb.Draw(tex, new Rectangle(r.X + cap, r.Y, r.Width - cap * 2, r.Height),
            new Rectangle(cap, 0, tex.Width - cap * 2, tex.Height), Color.White);
        sb.Draw(tex, new Rectangle(r.Right - cap, r.Y, cap, r.Height),
            new Rectangle(tex.Width - cap, 0, cap, tex.Height), Color.White);
    }

    private void Stroke(SpriteBatch sb, Rectangle r, Color c)
    {
        sb.Draw(_pixel, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(_pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(_pixel, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(_pixel, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }

    public void GradientV(SpriteBatch sb, Rectangle r, Color top, Color bottom)
    {
        if (Mono.On) { sb.Draw(_pixel, r, top); return; }   // 1-bit: gradients collapse to flat fills
        var tex = Cached($"gradv|{top.PackedValue}|{bottom.PackedValue}", () =>
        {
            var t = new Texture2D(_gd, 1, 32);
            var d = new Color[32];
            for (int i = 0; i < 32; i++) d[i] = Color.Lerp(top, bottom, i / 31f);
            t.SetData(d);
            return t;
        });
        sb.Draw(tex, r, Color.White);
    }

    // ----- gem badges ------------------------------------------------------------------

    public enum Gem { Heart, Star, Diamond }

    /// <summary>
    /// A glossy resource badge centred at <paramref name="center"/>: dark ink outline, deep
    /// base, bright fill (partial for the heart's HP fraction), glossy top highlight.
    /// </summary>
    public void Badge(SpriteBatch sb, Gem gem, Vector2 center, int size, Color color, Color deep,
        float fillPct = 1f)
    {
        int pctKey = (int)(Math.Clamp(fillPct, 0f, 1f) * 20); // 5% steps keep the cache tiny
        var tex = Cached($"gem|{gem}|{size}|{color.PackedValue}|{pctKey}", () =>
            BuildGem(gem, size, color, deep, pctKey / 20f));
        sb.Draw(tex, center - new Vector2(size / 2f), Color.White);
    }

    private Texture2D BuildGem(Gem gem, int size, Color color, Color deep, float fillPct)
    {
        var data = new Color[size * size];
        bool[] inside = new bool[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // normalized coords, y up
                float nx = (x + 0.5f) / size * 2f - 1f;
                float ny = 1f - (y + 0.5f) / size * 2f;
                inside[y * size + x] = gem switch
                {
                    Gem.Heart => InsideHeart(nx * 1.25f, ny * 1.25f + 0.12f),
                    Gem.Star => InsideStar(nx, ny),
                    _ => Math.Abs(nx) * 1.12f + Math.Abs(ny) * 0.98f <= 0.95f,
                };
            }

        int border = Math.Max(2, size / 26);
        float fillLine = 1f - fillPct; // fraction of height that stays deep (drains top-down)
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                if (!inside[i]) continue;
                bool edge = false;
                for (int dy = -border; dy <= border && !edge; dy++)
                    for (int dx = -border; dx <= border && !edge; dx++)
                    {
                        int px = x + dx, py = y + dy;
                        if (px < 0 || py < 0 || px >= size || py >= size || !inside[py * size + px])
                            edge = true;
                    }
                if (edge) { data[i] = Ew.Outline; continue; }

                bool filled = (float)y / size >= fillLine;
                var baseCol = filled ? color : deep;
                // subtle vertical shading + glossy top highlight (design's inset gloss)
                float fy = (float)y / size;
                var shaded = Color.Lerp(baseCol, Color.Black, fy * 0.22f);
                if (fy < 0.30f)
                    shaded = Color.Lerp(shaded, Color.White, (0.30f - fy) / 0.30f * 0.35f);
                data[i] = shaded;
            }

        var tex = new Texture2D(_gd, size, size);
        tex.SetData(data);
        return tex;
    }

    private static bool InsideHeart(float x, float y)
    {
        // classic implicit heart: (x^2 + y^2 - 1)^3 - x^2 y^3 <= 0
        float a = x * x + y * y - 1f;
        return a * a * a - x * x * y * y * y <= 0f;
    }

    private static bool InsideStar(float x, float y)
    {
        // 5-point star via angular radius modulation
        float r = MathF.Sqrt(x * x + y * y);
        if (r < 0.001f) return true;
        float ang = MathF.Atan2(y, x) - MathF.PI / 2f;
        float k = MathF.Cos(MathF.PI / 5f) / MathF.Cos((MathF.Abs(ang * 5f % (2f * MathF.PI)
            - MathF.PI) / 5f) - MathF.PI / 5f);
        // simpler robust approach: interpolate between outer/inner radius by angle
        float seg = MathF.Abs(((ang / (MathF.PI * 2f) * 5f % 1f) + 1f) % 1f - 0.5f) * 2f; // 0 tip..1 valley
        float rMax = 0.95f - seg * 0.52f;
        _ = k;
        return r <= rMax;
    }

    // ----- internals --------------------------------------------------------------------

    private Texture2D RoundedTex(int w, int h, int radius, bool sunken)
    {
        return Cached($"panel|{w}|{h}|{radius}|{sunken}", () =>
        {
            var data = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float d = RoundedDist(x + 0.5f, y + 0.5f, w, h, radius);
                    int i = y * w + x;
                    if (d > 0) { data[i] = Color.Transparent; continue; }
                    if (d > -2f) { data[i] = Ew.Outline; continue; }
                    float fy = (float)y / h;
                    var col = sunken
                        ? Color.Lerp(Ew.SurfaceSunken, Color.Black, (1f - fy) * 0.18f)
                        : Color.Lerp(Ew.PanelTop, Ew.PanelBottom, fy);
                    // bevel: top highlight / bottom shade (inverted when sunken)
                    if (!sunken && d > -4f && y < h / 2) col = Color.Lerp(col, Color.White, 0.10f);
                    if (!sunken && d > -4f && y > h / 2) col = Color.Lerp(col, Color.Black, 0.20f);
                    if (sunken && d > -4f && y < h / 2) col = Color.Lerp(col, Color.Black, 0.30f);
                    data[i] = col;
                }
            var tex = new Texture2D(_gd, w, h);
            tex.SetData(data);
            return tex;
        });
    }

    private static float RoundedDist(float x, float y, int w, int h, int r)
    {
        float qx = Math.Abs(x - w / 2f) - (w / 2f - r);
        float qy = Math.Abs(y - h / 2f) - (h / 2f - r);
        float ax = Math.Max(qx, 0), ay = Math.Max(qy, 0);
        return MathF.Sqrt(ax * ax + ay * ay) + Math.Min(Math.Max(qx, qy), 0f) - r;
    }

    private Texture2D PillTex(int h, Color top, Color bottom)
    {
        return Cached($"pill|{h}|{top.PackedValue}|{bottom.PackedValue}", () =>
        {
            int w = h * 2;
            var data = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float d = RoundedDist(x + 0.5f, y + 0.5f, w, h, h / 2 - 1);
                    int i = y * w + x;
                    if (d > 0) { data[i] = Color.Transparent; continue; }
                    if (d > -2f) { data[i] = Ew.Outline; continue; }
                    float fy = (float)y / h;
                    var col = Color.Lerp(top, bottom, fy);
                    if (fy < 0.35f) col = Color.Lerp(col, Color.White, (0.35f - fy) * 0.9f);
                    data[i] = col;
                }
            var tex = new Texture2D(_gd, w, h);
            tex.SetData(data);
            return tex;
        });
    }

    private void NineSlice(SpriteBatch sb, Texture2D tex, Rectangle r, int b)
    {
        int tw = tex.Width, th = tex.Height;
        b = Math.Min(b, Math.Min(tw, th) / 2 - 1);
        (int sy, int sh, int dy, int dh)[] rows =
        {
            (0, b, r.Y, b), (b, th - 2 * b, r.Y + b, r.Height - 2 * b), (th - b, b, r.Bottom - b, b),
        };
        (int sx, int sw, int dx, int dw)[] cols =
        {
            (0, b, r.X, b), (b, tw - 2 * b, r.X + b, r.Width - 2 * b), (tw - b, b, r.Right - b, b),
        };
        foreach (var (sy, sh, dy, dh) in rows)
            foreach (var (sx, sw, dx, dw) in cols)
                sb.Draw(tex, new Rectangle(dx, dy, dw, dh), new Rectangle(sx, sy, sw, sh), Color.White);
    }

    private Texture2D Cached(string key, Func<Texture2D> build)
    {
        if (!_cache.TryGetValue(key, out var tex)) _cache[key] = tex = build();
        return tex;
    }

    /// <summary>Element → element color (floats, log lines, spell chips). The 1-bit reset
    /// collapses the elemental rainbow to plain ink — damage is damage.</summary>
    public static Color ElementColor(DofusSlice.Core.Combat.Element e) => Mono.On ? Mono.Ink : e switch
    {
        DofusSlice.Core.Combat.Element.Fire => Ew.Ember,
        DofusSlice.Core.Combat.Element.Water => Ew.Brook,
        DofusSlice.Core.Combat.Element.Air => Ew.Gale,
        DofusSlice.Core.Combat.Element.Earth => Ew.Loam,
        _ => Ew.InkSoft,
    };
}

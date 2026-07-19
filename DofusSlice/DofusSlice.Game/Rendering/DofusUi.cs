using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DofusSlice.Game.Rendering;

/// <summary>
/// The Dofus "oldUI Remake" fan-theme chrome (local-only art baked by tools/bake_dofus_ui.py
/// into assets/dofusui/ — never committed). Loads a manifest of nine-slice pieces with the
/// theme's own scale9 margins and draws the classic 1.29 interface: parchment windows with
/// grey frames, glossy orange pill buttons, segmented candy gauges, dark rounded slots.
/// Absent folder → <see cref="Loaded"/> is false and the game keeps its procedural chrome.
/// </summary>
public sealed class DofusUi
{
    private sealed record Piece(Texture2D Tex, int[]? M, bool Tile);

    private readonly Dictionary<string, Piece> _pieces = new();
    public bool Loaded { get; }

    /// <summary>Ink colors for text sitting on the parchment window body.</summary>
    public static readonly Color Ink = new(62, 48, 34);
    public static readonly Color InkDim = new(122, 104, 82);
    public static readonly Color InkGold = new(146, 96, 22);

    public DofusUi(GraphicsDevice gd)
    {
        try
        {
            string dir = new[] { "assets", "assets-default" }
                .Select(d => Path.Combine(AppContext.BaseDirectory, d, "dofusui"))
                .FirstOrDefault(Directory.Exists) ?? "";
            if (dir.Length == 0) return;
            string manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                string png = Path.Combine(dir, p.Name + ".png");
                if (!File.Exists(png)) continue;
                using var fs = File.OpenRead(png);
                var tex = Texture2D.FromStream(gd, fs);
                Premultiply(tex);
                int[]? m = p.Value.TryGetProperty("m", out var mEl)
                    ? mEl.EnumerateArray().Select(v => v.GetInt32()).ToArray() : null;
                bool tile = p.Value.TryGetProperty("tile", out var tEl) && tEl.GetBoolean();
                _pieces[p.Name] = new Piece(tex, m, tile);
            }
            Loaded = _pieces.ContainsKey("panel_fill") && _pieces.ContainsKey("btn");
        }
        catch
        {
            Loaded = false; // malformed theme -> procedural chrome, never a crash
        }
    }

    private static void Premultiply(Texture2D tex)
    {
        var data = new Color[tex.Width * tex.Height];
        tex.GetData(data);
        for (int i = 0; i < data.Length; i++)
        {
            var c = data[i];
            data[i] = new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A);
        }
        tex.SetData(data);
    }

    private Piece? Get(string name) => _pieces.TryGetValue(name, out var p) ? p : null;

    // ----- Nine-slice / tiling primitives ---------------------------------------------

    /// <summary>Draw a piece stretched into r, honoring its per-side scale9 margins.</summary>
    public void Slice(SpriteBatch sb, string name, Rectangle r, Color? tint = null)
    {
        var p = Get(name);
        if (p == null) return;
        var c = tint ?? Color.White;
        if (p.Tile) { TileInto(sb, p.Tex, r, c); return; }
        if (p.M == null) { sb.Draw(p.Tex, r, c); return; }

        int l = p.M[0], t = p.M[1], rt = p.M[2], b = p.M[3];
        int tw = p.Tex.Width, th = p.Tex.Height;
        // Clamp margins so tiny destination rects never invert.
        int dl = Math.Min(l, r.Width / 2), dr = Math.Min(rt, r.Width / 2);
        int dt = Math.Min(t, r.Height / 2), db = Math.Min(b, r.Height / 2);
        (int sy, int sh, int dy, int dh)[] rows =
        {
            (0, t, r.Y, dt),
            (t, th - t - b, r.Y + dt, r.Height - dt - db),
            (th - b, b, r.Bottom - db, db),
        };
        (int sx, int sw, int dx, int dw)[] cols =
        {
            (0, l, r.X, dl),
            (l, tw - l - rt, r.X + dl, r.Width - dl - dr),
            (tw - rt, rt, r.Right - dr, dr),
        };
        foreach (var (sy, sh, dy, dh) in rows)
        {
            if (sh <= 0 || dh <= 0) continue;
            foreach (var (sx, sw, dx, dw) in cols)
                if (sw > 0 && dw > 0)
                    sb.Draw(p.Tex, new Rectangle(dx, dy, dw, dh), new Rectangle(sx, sy, sw, sh), c);
        }
    }

    private static void TileInto(SpriteBatch sb, Texture2D tex, Rectangle r, Color c)
    {
        for (int y = r.Y; y < r.Bottom; y += tex.Height)
            for (int x = r.X; x < r.Right; x += tex.Width)
            {
                int w = Math.Min(tex.Width, r.Right - x), h = Math.Min(tex.Height, r.Bottom - y);
                sb.Draw(tex, new Rectangle(x, y, w, h), new Rectangle(0, 0, w, h), c);
            }
    }

    public void Draw(SpriteBatch sb, string name, Rectangle r, Color? tint = null)
    {
        var p = Get(name);
        if (p != null) sb.Draw(p.Tex, r, tint ?? Color.White);
    }

    public Texture2D? Texture(string name) => Get(name)?.Tex;

    // ----- The themed controls ----------------------------------------------------------

    /// <summary>A main window: parchment body under the grey rounded frame (the 1.29 block).</summary>
    public void Window(SpriteBatch sb, Rectangle r)
    {
        TileInto(sb, Get("panel_fill")!.Tex, new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6), Color.White);
        Slice(sb, "panel_frame", r);
    }

    /// <summary>A dark translucent sub-panel / tooltip plate (bg_small_border).</summary>
    public void Panel(SpriteBatch sb, Rectangle r, bool light = false) =>
        Slice(sb, light ? "sub_light" : "sub_dark", r);

    /// <summary>The glossy orange pill. Uses the big bordered art for tall rects.</summary>
    public void Button(SpriteBatch sb, Rectangle r, bool pressed = false, bool hover = false,
        bool disabled = false)
    {
        string name = r.Height > 44
            ? (pressed ? "btn_big_pressed" : "btn_big")
            : disabled ? "btn_disabled" : pressed ? "btn_pressed" : hover ? "btn_over" : "btn";
        Slice(sb, name, r);
    }

    /// <summary>A rounded slot well; hover/selected swap in the orange states.</summary>
    public void Slot(SpriteBatch sb, Rectangle r, bool hover = false, bool selected = false)
    {
        Slice(sb, r.Width >= 56 ? "slot64" : "slot40", r);
        if (selected) Slice(sb, r.Width >= 56 ? "slot_ring" : "slot40_sel", r);
        else if (hover) Slice(sb, r.Width >= 56 ? "slot_ring" : "slot40_over", r);
    }

    /// <summary>The segmented candy gauge. Fill is CROPPED from the left, 1.29-style.</summary>
    public void Gauge(SpriteBatch sb, Rectangle r, float frac, string fill = "gauge_red")
    {
        Slice(sb, "gauge_track", r);
        var p = Get(fill);
        if (p == null || frac <= 0f) return;
        frac = Math.Clamp(frac, 0f, 1f);
        var inner = new Rectangle(r.X + 2, r.Y + (r.Height - (r.Height - 4)) / 2, r.Width - 4, r.Height - 4);
        int w = (int)(inner.Width * frac);
        if (w <= 0) return;
        int sw = (int)(p.Tex.Width * frac);
        sb.Draw(p.Tex, new Rectangle(inner.X, inner.Y, w, inner.Height),
            new Rectangle(0, 0, Math.Max(1, sw), p.Tex.Height), Color.White);
    }

    /// <summary>A rounded-top tab dome (selected = solid, unselected = outline).</summary>
    public void Tab(SpriteBatch sb, Rectangle r, bool selected, bool hover = false) =>
        Slice(sb, selected ? (hover ? "tab_on_over" : "tab_on") : (hover ? "tab_off_over" : "tab_off"), r);

    /// <summary>The dark title strip with the silver left ornament.</summary>
    public void TitleStrip(SpriteBatch sb, Rectangle r) => Slice(sb, "title_strip", r);

    /// <summary>A stat icon by key (vit/str/int/cha/agi/wis/ap/mp/ini), if the theme has it.</summary>
    public bool StatIcon(SpriteBatch sb, string key, Rectangle r)
    {
        var p = Get("charac_" + key);
        if (p == null) return false;
        sb.Draw(p.Tex, r, Color.White);
        return true;
    }

    /// <summary>A classic Dofus spell tile for a skill key, if the icon set is baked.</summary>
    public bool SpellIcon(SpriteBatch sb, string key, Rectangle r, Color? tint = null)
    {
        var p = Get("spell_" + key);
        if (p == null) return false;
        sb.Draw(p.Tex, r, tint ?? Color.White);
        return true;
    }
}

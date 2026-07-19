using DofusSlice.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DofusSlice.Game;

/// <summary>
/// The UI-limits debug scene: a region-by-region recreation of a classic Dofus 1.29
/// screenshot built from the oldUI Remake theme — dark silver-framed windows, proper inner
/// margins, native-proportion tabs, real Dofus eggs/items, and a Dofus-2-style rounded sans
/// (DejaVu Sans Bold baked to the ui_font atlas). <c>--uidemo</c> or F10; Esc exits.
/// </summary>
public sealed partial class SliceGame
{
    private bool _uiDemo;
    private int _demoTab;      // RESUME / DETAILS
    private int _demoCat = 4;  // selected inventory category
    private int _demoSlot = 13; // selected item cell

    private static readonly string[] DemoSpellKeys =
    {
        "ruin_bolt", "piercing_shot", "slam", "bastion", "flashfire", "crippling_arrow",
        "husk_strike", "marrow_spit", "grave_bite", "warden_ironhide", "mite_sap",
        "wraith_wail", "ghoul_rend", "piper_gift", "sexton_smash", "seize",
        "blood_pact", "blink",
    };

    // ----- Dofus-2-style text: baked DejaVu Sans Bold. Headings use the TRUE 26px atlas
    // (never integer-doubled 13px), so every size renders with the same crispness. -----

    private void DT(string t, int x, int y, int scale, Color c)
    {
        if (scale >= 2 && _dfontBig.Loaded) { _dfontBig.Draw(_sb, t, x, y, 1, c); return; }
        if (_dfont.Loaded) _dfont.Draw(_sb, t, x, y, scale, c);
        else _font.Draw(_sb, t, x, y, scale, c);
    }

    private void DTC(string t, int cx, int y, int scale, Color c)
    {
        if (scale >= 2 && _dfontBig.Loaded) { _dfontBig.DrawCentered(_sb, t, cx, y, 1, c); return; }
        if (_dfont.Loaded) _dfont.DrawCentered(_sb, t, cx, y, scale, c);
        else _font.DrawCentered(_sb, t, cx, y, scale, c);
    }

    private int DM(string t, int scale) =>
        scale >= 2 && _dfontBig.Loaded ? _dfontBig.Measure(t, 1)
        : _dfont.Loaded ? _dfont.Measure(t, scale) : _font.Measure(t, scale);

    private void DrawUiDemo()
    {
        _sb.Begin(samplerState: SamplerState.PointClamp);

        if (!_dof.Loaded)
        {
            _font.DrawCentered(_sb, "UI DEMO NEEDS THE DOFUS THEME BAKED (tools/bake_dofus_ui.py)",
                ScreenW / 2, ScreenH / 2, 2, Palette.Text);
            _sb.End();
            return;
        }

        var mp = new Point(_mouse.X, _mouse.Y);

        // The backdrop: the same neutral grey family as the sheet, a step darker than the bar.
        _prim.FillRect(_sb, new Rectangle(0, 0, ScreenW, ScreenH), new Color(34, 34, 34));
        for (int i = 0; i < 10; i++)
            for (int j = 0; j < 6; j++)
                _prim.DiamondAt(_sb, new Vector2(120 + i * 128, 80 + j * 128),
                    new Color(155, 143, 105) * (0.06f + ((i + j) % 2) * 0.03f));

        DrawDemoCharacterSheet(new Rectangle(20, 18, 350, 588), mp);
        DrawDemoInventory(new Rectangle(382, 18, 648, 588), mp);
        DrawDemoHud(mp);

        DT("UI-LIMITS DEBUG SCENE  -  oldUI Remake  -  F10/ESC TO EXIT", 16, 8, 1,
            new Color(240, 208, 120));
        _sb.End();
    }

    // ----- window helpers ---------------------------------------------------------------

    /// <summary>Clean header: flat dark band with a gold hairline, seated BELOW the silver
    /// rail so the frame's border stays fully visible; X and help chips live on the band.</summary>
    private void DemoTitle(Rectangle w, string title, Point mp, bool centered, bool help = false)
    {
        var band = new Rectangle(w.X + 28, w.Y + 28, w.Width - 56, 24);
        _ew.GradientV(_sb, band, new Color(30, 28, 26), new Color(16, 15, 14));
        _prim.FillRect(_sb, new Rectangle(band.X, band.Bottom - 1, band.Width, 1), new Color(146, 116, 58));
        if (centered) DTC(title, w.Center.X, band.Y + 5, 1, Color.White);
        else DT(title, band.X + 10, band.Y + 5, 1, Color.White);
        int cx = band.Right - 26;
        var close = new Rectangle(cx, band.Y + 2, 22, 20);
        _dof.Slice(_sb, close.Contains(mp) ? "btn_close_over" : "btn_close", close);
        if (help)
        {
            var h = new Rectangle(cx - 24, band.Y + 2, 20, 20);
            _dof.Draw(_sb, h.Contains(mp) ? "help_over" : "help", h);
        }
    }

    /// <summary>A dark pill row with real inner padding: icon, white label, value right-aligned
    /// clear of the rounded edge; the orange [+] spender sits OUTSIDE the row, 1.29-style.</summary>
    private void DemoRow(Rectangle r, string icon, string label, string value, Point mp,
        bool plus = false, bool big = false)
    {
        _dof.Panel(_sb, r);
        if (!_dof.StatIcon(_sb, icon, new Rectangle(r.X + 10, r.Y + (r.Height - 16) / 2, 16, 16)))
            _prim.DiscAt(_sb, new Vector2(r.X + 18, r.Center.Y), 7, new Color(180, 60, 50));
        DT(label, r.X + 34, r.Center.Y - 7, 1, Color.White);
        int vs = big ? 2 : 1;
        int vh = big ? 9 : 7; // half the glyph height, so values sit centered INSIDE the pill
        DT(value, r.Right - 16 - DM(value, vs), r.Center.Y - vh, vs, Color.White);
        if (plus)
        {
            var b = new Rectangle(r.Right + 8, r.Y + (r.Height - 20) / 2, 22, 20);
            _dof.Button(_sb, b, hover: b.Contains(mp));
            DTC("+", b.Center.X, b.Y + 3, 1, Color.White);
        }
    }

    // ----- CARACTERISTIQUES ---------------------------------------------------------------

    private void DrawDemoCharacterSheet(Rectangle a, Point mp)
    {
        // Metric: the frame's VISIBLE rails measure L22/T21/R21/B27 — every element below
        // keeps >=10px of breathing room from them and from its neighbours.
        _dof.Window(_sb, a);
        DemoTitle(a, "CARACTERISTIQUES", mp, centered: false, help: true);

        int L = a.X + 34, R = a.Right - 34, W = R - L;

        // Tabs, seated on a hairline.
        var tabs = new[] { "Resume", "Details" };
        int tabY = a.Y + 62;
        for (int i = 0; i < 2; i++)
        {
            var t = new Rectangle(L + i * 90, tabY, 86, 32);
            if (t.Contains(mp) && LeftClicked()) _demoTab = i;
            _dof.Tab(_sb, t, _demoTab == i, t.Contains(mp));
            DTC(tabs[i], t.Center.X, t.Y + 9, 1, _demoTab == i ? new Color(52, 48, 44) : WinInkDim);
        }
        var heartTab = new Rectangle(L + 190, tabY, 42, 32);
        _dof.Tab(_sb, heartTab, false, heartTab.Contains(mp));
        _dof.StatIcon(_sb, "vit", new Rectangle(heartTab.Center.X - 9, heartTab.Y + 8, 18, 18));
        _prim.FillRect(_sb, new Rectangle(L, tabY + 32, W, 1), new Color(146, 116, 58) * 0.6f);

        // Portrait + identity (one line for rank so the column breathes).
        var port = new Rectangle(L, a.Y + 106, 72, 72);
        _dof.Slot(_sb, port, hover: port.Contains(mp));
        var sheet = _sprites.GetSheet("hero", "idle", "se");
        if (sheet != null)
            SpriteDraw.Feet(_sb, sheet, new Vector2(port.Center.X + 3, port.Bottom - 9), Color.White, 54f,
                (int)(_time * 6) % sheet.FrameCount);
        _dof.Draw(_sb, "help", new Rectangle(L - 4, a.Y + 102, 18, 18));
        var plus = new Rectangle(L - 4, a.Y + 124, 18, 18);
        _dof.Button(_sb, plus, hover: plus.Contains(mp));
        DTC("+", plus.Center.X, plus.Y + 2, 1, Color.White);
        DT("Aspette", L + 86, a.Y + 108, 2, WinInk);
        DT("Omega 191  -  Beta-Major", L + 86, a.Y + 132, 1, WinInkDim);
        DT("15 080 points", L + 86, a.Y + 150, 1, WinGold);

        // Experience / Energie candy bars.
        DT("Experience", L, a.Y + 192, 1, WinInk);
        _dof.Gauge(_sb, new Rectangle(L + 92, a.Y + 192, W - 92, 12), 0.62f, "gauge_timer");
        DT("Energie", L, a.Y + 210, 1, WinInk);
        _dof.Gauge(_sb, new Rectangle(L + 92, a.Y + 210, W - 92, 12), 0.55f, "gauge_timer");

        // The three vital rows.
        DemoRow(new Rectangle(L, a.Y + 234, W, 26), "vit", "Points de vie (PV)", "2 330", mp, big: true);
        DemoRow(new Rectangle(L, a.Y + 264, W, 26), "ap", "Points d'action (PA)", "7", mp, big: true);
        DemoRow(new Rectangle(L, a.Y + 294, W, 26), "mp", "Points de mouvement (PM)", "3", mp, big: true);

        // The six characteristics; [+] spenders outside the rows.
        (string icon, string label, string val)[] stats =
        {
            ("vit", "Vitalite", "1280"), ("agi", "Agilite", "270"), ("cha", "Chance", "100"),
            ("str", "Force", "166"), ("int", "Intelligence", "167"), ("wis", "Sagesse", "209"),
        };
        for (int i = 0; i < stats.Length; i++)
            DemoRow(new Rectangle(L, a.Y + 330 + i * 26, W - 32, 23),
                stats[i].icon, stats[i].label, stats[i].val, mp, plus: true);

        // Points to spend: label + input well + round refresh.
        DT("Points a repartir :", L, a.Y + 493, 1, WinInk);
        var well = new Rectangle(L + 128, a.Y + 489, 70, 22);
        _dof.Slice(_sb, "chat_input", well);
        DT("995", well.X + 10, well.Y + 4, 1, Color.White);
        var refresh = new Rectangle(well.Right + 10, well.Y, 22, 22);
        _dof.Slice(_sb, "scroll_thumb", refresh);
        DTC("o", refresh.Center.X, refresh.Y + 3, 1, Color.White);

        // Footer pills: they end at +545, a clear 16px above the bottom rail zone (561).
        var ens = new Rectangle(L, a.Y + 519, 124, 26);
        var srt = new Rectangle(L + 144, a.Y + 519, 124, 26);
        _dof.Button(_sb, ens, hover: ens.Contains(mp));
        _dof.Button(_sb, srt, hover: srt.Contains(mp));
        DTC("Ensembles", ens.Center.X, ens.Y + 6, 1, new Color(46, 26, 10));
        DTC("Sorts", srt.Center.X, srt.Y + 6, 1, new Color(46, 26, 10));
    }

    // ----- INVENTAIRE -------------------------------------------------------------------

    private void DemoEquipSlot(Rectangle s, Point mp, string? silhouette, string? item = null)
    {
        _dof.Slot(_sb, s, hover: s.Contains(mp));
        if (item != null && _dof.Texture(item) != null)
            _dof.Draw(_sb, item, new Rectangle(s.X + 4, s.Y + 4, s.Width - 8, s.Height - 8));
        else if (silhouette != null)
            _dof.Draw(_sb, "slotsil_" + silhouette,
                new Rectangle(s.X + 5, s.Y + 5, s.Width - 10, s.Height - 10), Color.White * 0.5f);
    }

    private void DrawDemoInventory(Rectangle b, Point mp)
    {
        _dof.Window(_sb, b);
        DemoTitle(b, "INVENTAIRE", mp, centered: true);

        int L = b.X + 34;

        // The equipment doll: 3 slots left, 4 right, weapon + shield below, character centred.
        var doll = new Rectangle(L, b.Y + 62, 336, 296);
        _dof.Panel(_sb, doll);
        string[] leftSil = { "amulet", "dofus", "ring" };
        for (int i = 0; i < 3; i++)
            DemoEquipSlot(new Rectangle(doll.X + 12, doll.Y + 12 + i * 60, 50, 50), mp, leftSil[i]);
        DemoEquipSlot(new Rectangle(doll.Right - 62, doll.Y + 12, 50, 50), mp, "hat", "item_adv_amulet");
        string[] rightSil = { "cape", "belt", "boots" };
        for (int i = 0; i < 3; i++)
            DemoEquipSlot(new Rectangle(doll.Right - 62, doll.Y + 72 + i * 60, 50, 50), mp, rightSil[i]);
        DemoEquipSlot(new Rectangle(doll.X + 12, doll.Bottom - 62, 50, 50), mp, "weapon", "item_adv_blade");
        DemoEquipSlot(new Rectangle(doll.X + 72, doll.Bottom - 62, 50, 50), mp, "shield");

        var hero = _sprites.GetSheet("hero", "idle", "se");
        int midX = doll.Center.X + 2;
        int feetY = doll.Center.Y + 56;
        if (hero != null)
            SpriteDraw.Feet(_sb, hero, new Vector2(midX, feetY), Color.White, 136f,
                (int)(_time * 5) % hero.FrameCount);
        if (_dof.Texture("rot_l") != null)
        {
            _dof.Draw(_sb, "rot_l", new Rectangle(midX - 44, feetY - 26, 15, 24));
            _dof.Draw(_sb, "rot_r", new Rectangle(midX + 29, feetY - 26, 15, 24));
        }

        // The pet / consumable strip.
        for (int i = 0; i < 7; i++)
        {
            var s = new Rectangle(L + i * 50, b.Y + 366, 46, 46);
            _dof.Slot(_sb, s, hover: s.Contains(mp));
            string egg = $"egg_{(i * 5 + 3) % 24 + 1:00}";
            if (i is 0 or 1 or 5 && _dof.Texture(egg) != null)
                _dof.Draw(_sb, egg, new Rectangle(s.X + 4, s.Y + 4, 38, 38));
            if (i == 5)
            {
                _prim.FillRect(_sb, s, Color.Black * 0.45f);
                _dof.Draw(_sb, "lock", new Rectangle(s.Center.X - 8, s.Center.Y - 8, 16, 16));
            }
            if (i == 6 && _dof.Texture("egg_24") != null)
                _dof.Draw(_sb, "egg_24", new Rectangle(s.X + 4, s.Y + 4, 38, 38));
        }

        // Right pane: category tabs, dropdown, the egg grid, search, kamas — every group ends
        // well clear of the bottom rail zone (561).
        int rx = b.X + 390, ry = b.Y + 62;
        string[] cats = { "icon_cat_equip", "icon_cat_useful", "icon_cat_res", "icon_cat_quest", "icon_cat_all" };
        for (int i = 0; i < cats.Length; i++)
        {
            var c = new Rectangle(rx + i * 42, ry, 38, 30);
            if (c.Contains(mp) && LeftClicked()) _demoCat = i;
            _dof.Tab(_sb, c, _demoCat == i, c.Contains(mp));
            _dof.Draw(_sb, cats[i], new Rectangle(c.Center.X - 10, c.Y + 6, 20, 20),
                _demoCat == i ? Color.White : new Color(74, 64, 54));
        }
        var drop = new Rectangle(rx, ry + 36, 176, 22);
        _dof.Panel(_sb, drop, light: true);
        DT("Dofus", drop.X + 8, drop.Y + 4, 1, Color.White);
        var dropBtn = new Rectangle(drop.Right, ry + 36, 22, 22);
        _dof.Button(_sb, dropBtn, hover: dropBtn.Contains(mp));
        DTC("v", dropBtn.Center.X, dropBtn.Y + 3, 1, Color.White);

        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 5; col++)
            {
                int idx = row * 5 + col;
                var cell = new Rectangle(rx + col * 45, ry + 66 + row * 45, 42, 42);
                if (cell.Contains(mp) && LeftClicked()) _demoSlot = idx;
                _dof.Slot(_sb, cell, hover: cell.Contains(mp), selected: idx == _demoSlot);
                string egg = $"egg_{idx % 24 + 1:00}";
                if (_dof.Texture(egg) != null)
                    _dof.Draw(_sb, egg, new Rectangle(cell.X + 3, cell.Y + 3, 36, 36));
                if (idx is 6 or 9 or 22)
                {
                    _prim.FillRect(_sb, cell, Color.Black * 0.45f);
                    _dof.Draw(_sb, "lock", new Rectangle(cell.Center.X - 8, cell.Center.Y - 8, 16, 16));
                }
                if (idx is 7 or 18)
                    DT(((idx * 7) % 9 + 2).ToString(), cell.X + 3, cell.Y + 1, 1, Color.White);
            }

        var scis = new Rectangle(rx, b.Y + 493, 22, 20);
        _dof.Button(_sb, scis, hover: scis.Contains(mp));
        DTC("x", scis.Center.X, scis.Y + 3, 1, new Color(46, 26, 10));
        var search = new Rectangle(rx + 28, b.Y + 493, 196, 20);
        _dof.Slice(_sb, "chat_input", search);
        DT("Rechercher...", search.X + 20, search.Y + 3, 1, new Color(150, 150, 150));
        _prim.DiscAt(_sb, new Vector2(search.X + 10, search.Center.Y), 4, new Color(110, 110, 110));
        _prim.DiscAt(_sb, new Vector2(rx + 8, b.Y + 528), 7, new Color(232, 190, 60));
        _prim.DiscAt(_sb, new Vector2(rx + 8, b.Y + 528), 5, new Color(250, 214, 92));
        _dof.Gauge(_sb, new Rectangle(rx + 20, b.Y + 523, 88, 11), 0.32f, "gauge_timer");
        DT("40 753 421 K", rx + 118, b.Y + 521, 1, WinGold);
    }

    // ----- The bottom HUD ------------------------------------------------------------------

    private void DrawDemoHud(Point mp)
    {
        // A centred plate in the sheet's canvas grey; everything inside keeps >=10px of air.
        var bar = new Rectangle(350, 630, 580, 108);
        _prim.FillRect(_sb, bar, new Color(48, 48, 48));
        _prim.FillRect(_sb, new Rectangle(bar.X, bar.Y, bar.Width, 1), new Color(96, 94, 90));
        _prim.FillRect(_sb, new Rectangle(bar.X, bar.Y + 1, bar.Width, 1), new Color(24, 24, 24));
        _prim.StrokeRect(_sb, bar, 1, new Color(20, 20, 20));

        // Vitals: the theme's own heart / PA star / PM leaf.
        var heartC = new Point(448, 684);
        if (_dof.Texture("hud_hp") != null)
        {
            _dof.Draw(_sb, "hud_hp", new Rectangle(heartC.X - 50, heartC.Y - 40, 100, 84));
            DTC("2330", heartC.X, heartC.Y - 20, 1, Color.White);
            DTC("2330", heartC.X, heartC.Y - 2, 1, Color.White * 0.85f);
            _dof.Draw(_sb, "hud_ap", new Rectangle(heartC.X - 76, heartC.Y + 8, 48, 46));
            DTC("7", heartC.X - 52, heartC.Y + 21, 1, Color.White);
            _dof.Draw(_sb, "hud_mp", new Rectangle(heartC.X + 28, heartC.Y + 8, 46, 46));
            DTC("3", heartC.X + 51, heartC.Y + 21, 1, Color.White);
        }

        // The consumable/spell bar with stack counters + pager.
        string[] counts = { "1958", "1949", "", "", "37", "", "", "14", "14", "21", "", "", "3", "" };
        for (int i = 0; i < 14; i++)
        {
            var s = new Rectangle(560 + i % 7 * 46, 640 + i / 7 * 46, 42, 42);
            _dof.Slot(_sb, s, hover: s.Contains(mp));
            if (i is not (2 or 3 or 5))
                _dof.SpellIcon(_sb, DemoSpellKeys[(i * 3 + 1) % DemoSpellKeys.Length],
                    new Rectangle(s.X + 4, s.Y + 4, 34, 34));
            if (counts[i].Length > 0)
                DT(counts[i], s.X + 2, s.Y + 1, 1, Color.White);
        }
        int px = 560 + 7 * 46 + 6;
        var up = new Rectangle(px, 644, 20, 16);
        var dn = new Rectangle(px, 672, 20, 16);
        _dof.Button(_sb, up, hover: up.Contains(mp));
        _dof.Button(_sb, dn, hover: dn.Contains(mp));
        DTC("^", up.Center.X, up.Y + 1, 1, new Color(46, 26, 10));
        DTC("v", dn.Center.X, dn.Y + 1, 1, new Color(46, 26, 10));
        DTC("1", px + 10, 661, 1, Color.White);

        // The LEVEL strip, same width as the plate, clear beneath it.
        _dof.Gauge(_sb, new Rectangle(350, 744, 580, 10), 0.78f, "gauge_timer");
    }

}

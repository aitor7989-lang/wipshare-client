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

        // The original's backdrop is BLACK; a faint hint of ground gives the translucent
        // bodies some depth without changing the read.
        _prim.FillRect(_sb, new Rectangle(0, 0, ScreenW, ScreenH), new Color(6, 6, 7));
        for (int i = 0; i < 10; i++)
            for (int j = 0; j < 6; j++)
                _prim.DiamondAt(_sb, new Vector2(120 + i * 128, 80 + j * 128),
                    new Color(155, 143, 105) * (0.05f + ((i + j) % 2) * 0.03f));

        DrawDemoCharacterSheet(new Rectangle(20, 30, 350, 588), mp);
        DrawDemoInventory(new Rectangle(382, 30, 648, 588), mp);
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
        var band = new Rectangle(w.X + 22, w.Y + 20, w.Width - 44, 24);
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

    /// <summary>A dark pill row: icon, white label, white value right-aligned; the orange [+]
    /// spender sits OUTSIDE the row, 1.29-style.</summary>
    private void DemoRow(Rectangle r, string icon, string label, string value, Point mp,
        bool plus = false, bool big = false)
    {
        _dof.Panel(_sb, r);
        if (!_dof.StatIcon(_sb, icon, new Rectangle(r.X + 7, r.Y + (r.Height - 16) / 2, 16, 16)))
            _prim.DiscAt(_sb, new Vector2(r.X + 15, r.Center.Y), 7, new Color(180, 60, 50));
        DT(label, r.X + 30, r.Center.Y - 7, 1, Color.White);
        int vs = big ? 2 : 1;
        DT(value, r.Right - 12 - DM(value, vs), r.Center.Y - (big ? 12 : 7), vs, Color.White);
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
        _dof.Window(_sb, a);
        DemoTitle(a, "CARACTERISTIQUES", mp, centered: false, help: true);

        // Everything inside real margins: the frame's rail is ~14px, content starts at +28.
        int L = a.X + 28, R = a.Right - 28, W = R - L;

        // Tabs at native-ish proportions, seated on a hairline.
        var tabs = new[] { "Resume", "Details" };
        int tabY = a.Y + 50;
        for (int i = 0; i < 2; i++)
        {
            var t = new Rectangle(L + i * 92, tabY, 88, 34);
            if (t.Contains(mp) && LeftClicked()) _demoTab = i;
            _dof.Tab(_sb, t, _demoTab == i, t.Contains(mp));
            DTC(tabs[i], t.Center.X, t.Y + 10, 1, _demoTab == i ? Color.White : WinInkDim);
        }
        var heartTab = new Rectangle(L + 196, tabY, 44, 34);
        _dof.Tab(_sb, heartTab, false, heartTab.Contains(mp));
        _dof.StatIcon(_sb, "vit", new Rectangle(heartTab.Center.X - 8, heartTab.Y + 10, 16, 16));
        _prim.FillRect(_sb, new Rectangle(L, tabY + 34, W, 1), new Color(146, 116, 58) * 0.6f);

        // Portrait + identity.
        var port = new Rectangle(L, a.Y + 96, 84, 84);
        _dof.Slot(_sb, port, hover: port.Contains(mp));
        var sheet = _sprites.GetSheet("hero", "idle", "se");
        if (sheet != null)
            SpriteDraw.Feet(_sb, sheet, new Vector2(port.Center.X, port.Bottom - 8), Color.White, 66f,
                (int)(_time * 6) % sheet.FrameCount);
        _dof.Draw(_sb, "help", new Rectangle(L - 8, a.Y + 90, 18, 18));
        DT("Aspette", L + 98, a.Y + 100, 2, WinInk);
        DT("Omega 191", L + 98, a.Y + 128, 1, WinInkDim);
        DT("15 080 points", L + 98, a.Y + 146, 1, WinGold);
        DT("Beta-Major", L, a.Y + 186, 1, WinInkDim);

        // Experience / Energie candy bars.
        DT("Experience", L, a.Y + 208, 1, WinInk);
        _dof.Gauge(_sb, new Rectangle(L + 96, a.Y + 208, W - 96, 12), 0.62f, "gauge_timer");
        DT("Energie", L, a.Y + 226, 1, WinInk);
        _dof.Gauge(_sb, new Rectangle(L + 96, a.Y + 226, W - 96, 12), 0.55f, "gauge_timer");

        // The three vital rows.
        DemoRow(new Rectangle(L, a.Y + 248, W, 26), "vit", "Points de vie (PV)", "2 330", mp, big: true);
        DemoRow(new Rectangle(L, a.Y + 278, W, 26), "ap", "Points d'action (PA)", "7", mp, big: true);
        DemoRow(new Rectangle(L, a.Y + 308, W, 26), "mp", "Points de mouvement (PM)", "3", mp, big: true);

        // The six characteristics; the [+] spenders sit outside the rows like the original.
        (string icon, string label, string val)[] stats =
        {
            ("vit", "Vitalite", "1280"), ("agi", "Agilite", "270"), ("cha", "Chance", "100"),
            ("str", "Force", "166"), ("int", "Intelligence", "167"), ("wis", "Sagesse", "209"),
        };
        for (int i = 0; i < stats.Length; i++)
            DemoRow(new Rectangle(L, a.Y + 344 + i * 29, W - 32, 25),
                stats[i].icon, stats[i].label, stats[i].val, mp, plus: true);

        // Points to spend + the two footer pills.
        var pts = new Rectangle(L, a.Y + 518, 216, 22);
        _dof.Panel(_sb, pts, light: true);
        DT("Points a repartir :   995", pts.X + 10, pts.Y + 4, 1, Color.White);
        var refresh = new Rectangle(pts.Right + 10, pts.Y, 22, 22);
        _dof.Button(_sb, refresh, hover: refresh.Contains(mp));
        DTC("o", refresh.Center.X, refresh.Y + 3, 1, Color.White);

        var ens = new Rectangle(L, a.Y + 544, 130, 28);
        var srt = new Rectangle(L + 150, a.Y + 544, 130, 28);
        _dof.Button(_sb, ens, hover: ens.Contains(mp));
        _dof.Button(_sb, srt, hover: srt.Contains(mp));
        DTC("Ensembles", ens.Center.X, ens.Y + 7, 1, new Color(46, 26, 10));
        DTC("Sorts", srt.Center.X, srt.Y + 7, 1, new Color(46, 26, 10));
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

        int L = b.X + 26;

        // The equipment doll: silhouette slots around the character.
        var doll = new Rectangle(L, b.Y + 48, 344, 312);
        _dof.Panel(_sb, doll);
        string[] leftSil = { "amulet", "dofus", "ring" };
        for (int i = 0; i < 3; i++)
            DemoEquipSlot(new Rectangle(doll.X + 10, doll.Y + 12 + i * 62, 52, 52), mp, leftSil[i]);
        DemoEquipSlot(new Rectangle(doll.Right - 62, doll.Y + 12, 52, 52), mp, "hat", "item_adv_amulet");
        string[] rightSil = { "cape", "belt", "boots" };
        for (int i = 0; i < 3; i++)
            DemoEquipSlot(new Rectangle(doll.Right - 62, doll.Y + 74 + i * 62, 52, 52), mp, rightSil[i]);
        DemoEquipSlot(new Rectangle(doll.X + 10, doll.Y + 248, 52, 52), mp, "weapon", "item_adv_blade");
        DemoEquipSlot(new Rectangle(doll.X + 72, doll.Y + 248, 52, 52), mp, "shield");
        DemoEquipSlot(new Rectangle(doll.Center.X - 19, doll.Y + 256, 38, 38), mp, null);
        DemoEquipSlot(new Rectangle(doll.Right - 124, doll.Y + 248, 52, 52), mp, "pet");
        DemoEquipSlot(new Rectangle(doll.Right - 62, doll.Y + 248, 52, 52), mp, "pet", "item_pipers_whistle");

        var hero = _sprites.GetSheet("hero", "idle", "se");
        if (hero != null)
            SpriteDraw.Feet(_sb, hero, new Vector2(doll.Center.X, doll.Bottom - 72), Color.White, 150f,
                (int)(_time * 5) % hero.FrameCount);
        // The rotate arrows from the user's sprite sheet, flanking the FEET like the original.
        if (_dof.Texture("rot_l") != null)
        {
            _dof.Draw(_sb, "rot_l", new Rectangle(doll.Center.X - 64, doll.Bottom - 106, 26, 42));
            _dof.Draw(_sb, "rot_r", new Rectangle(doll.Center.X + 38, doll.Bottom - 106, 26, 42));
        }
        else
        {
            _dof.Draw(_sb, "turn_l", new Rectangle(doll.Center.X - 64, doll.Bottom - 100, 30, 28));
            _dof.Draw(_sb, "turn_r", new Rectangle(doll.Center.X + 36, doll.Bottom - 100, 30, 28));
        }

        // The pet / consumable strip: eggs, one locked, one item.
        for (int i = 0; i < 7; i++)
        {
            var s = new Rectangle(L + i * 50, b.Y + 368, 46, 46);
            _dof.Slot(_sb, s, hover: s.Contains(mp));
            string egg = $"egg_{(i * 5 + 3) % 24 + 1:00}";
            if (i is 0 or 1 or 5 && _dof.Texture(egg) != null)
                _dof.Draw(_sb, egg, new Rectangle(s.X + 4, s.Y + 4, 38, 38));
            if (i == 5)
            {
                _prim.FillRect(_sb, s, Color.Black * 0.35f);
                _dof.Draw(_sb, "lock", new Rectangle(s.Right - 18, s.Bottom - 18, 14, 14));
            }
            if (i == 6 && _dof.Texture("item_adv_belt") != null)
                _dof.Draw(_sb, "item_adv_belt", new Rectangle(s.X + 4, s.Y + 4, 38, 38));
        }

        // The oldUI Remake plaque (the screenshot's watermark corner, as a wink).
        var plaque = new Rectangle(L, b.Y + 424, 344, 128);
        _dof.Panel(_sb, plaque);
        for (int i = 0; i < 4; i++)
            _prim.DiamondAt(_sb, new Vector2(plaque.X + 60 + i % 2 * 32, plaque.Y + 50 + i / 2 * 16 + i % 2 * 8),
                new Color(196, 186, 150) * (i % 2 == 0 ? 0.9f : 0.55f));
        DT("oldUI", plaque.X + 140, plaque.Y + 28, 2, Color.White);
        DT("Remake", plaque.X + 140, plaque.Y + 62, 2, Color.White);

        // Right pane: category tabs, dropdown, the EGG grid, search, kamas.
        int rx = b.X + 388, ry = b.Y + 48;
        string[] cats = { "icon_cat_equip", "icon_cat_useful", "icon_cat_res", "icon_cat_quest", "icon_cat_all" };
        for (int i = 0; i < cats.Length; i++)
        {
            var c = new Rectangle(rx + i * 42, ry, 38, 30);
            if (c.Contains(mp) && LeftClicked()) _demoCat = i;
            _dof.Tab(_sb, c, _demoCat == i, c.Contains(mp));
            _dof.Draw(_sb, cats[i], new Rectangle(c.Center.X - 10, c.Y + 6, 20, 20),
                _demoCat == i ? Color.White : new Color(74, 64, 54));
        }
        var drop = new Rectangle(rx, ry + 36, 198, 22);
        _dof.Panel(_sb, drop, light: true);
        DT("Dofus", drop.X + 8, drop.Y + 4, 1, Color.White);
        DT("v", drop.Right - 14, drop.Y + 4, 1, Color.White);
        var gear = new Rectangle(rx + 206, ry + 36, 22, 22);
        _dof.Button(_sb, gear, hover: gear.Contains(mp));
        _dof.Draw(_sb, "icon_gear", new Rectangle(gear.X + 3, gear.Y + 3, 16, 16), new Color(46, 26, 10));

        // 5 x 8 grid of REAL Dofus eggs with counts, locks and a selected cell.
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 5; col++)
            {
                int idx = row * 5 + col;
                var cell = new Rectangle(rx + col * 47, ry + 66 + row * 47, 44, 44);
                if (cell.Contains(mp) && LeftClicked()) _demoSlot = idx;
                _dof.Slot(_sb, cell, hover: cell.Contains(mp), selected: idx == _demoSlot);
                string egg = $"egg_{idx % 24 + 1:00}";
                if (_dof.Texture(egg) != null)
                    _dof.Draw(_sb, egg, new Rectangle(cell.X + 3, cell.Y + 3, 38, 38));
                else
                    _dof.SpellIcon(_sb, DemoSpellKeys[idx % DemoSpellKeys.Length],
                        new Rectangle(cell.X + 4, cell.Y + 4, 36, 36));
                if (idx is 6 or 9 or 22)
                {
                    _prim.FillRect(_sb, cell, Color.Black * 0.35f);
                    _dof.Draw(_sb, "lock", new Rectangle(cell.Right - 16, cell.Bottom - 16, 13, 13));
                }
                if (idx is 7 or 18)
                    DT(((idx * 7) % 9 + 2).ToString(), cell.Right - 12, cell.Y + 1, 1, Color.White);
            }

        // Search + kamas (inside the frame's bottom rail).
        var scis = new Rectangle(rx, b.Bottom - 76, 22, 20);
        _dof.Button(_sb, scis, hover: scis.Contains(mp));
        DTC("x", scis.Center.X, scis.Y + 3, 1, new Color(46, 26, 10));
        var search = new Rectangle(rx + 28, b.Bottom - 76, 204, 20);
        _dof.Slice(_sb, "chat_input", search);
        DT("Rechercher dans l'inventaire", search.X + 20, search.Y + 3, 1, new Color(150, 150, 150));
        _prim.DiscAt(_sb, new Vector2(search.X + 10, search.Center.Y), 4, new Color(110, 110, 110));
        _prim.DiscAt(_sb, new Vector2(rx + 8, b.Bottom - 44), 7, new Color(232, 190, 60));
        _prim.DiscAt(_sb, new Vector2(rx + 8, b.Bottom - 44), 5, new Color(250, 214, 92));
        _dof.Gauge(_sb, new Rectangle(rx + 20, b.Bottom - 49, 88, 11), 0.32f, "gauge_timer");
        DT("40 753 421", rx + 116, b.Bottom - 52, 1, WinGold);
        DT("K", rx + 116 + DM("40 753 421 ", 1), b.Bottom - 52, 1, new Color(250, 214, 92));
    }

    // ----- The bottom HUD ------------------------------------------------------------------

    private void DrawDemoHud(Point mp)
    {
        // The 1.29 bar: flat black with a hairline top edge. No chat, no minimap (user's call).
        _prim.FillRect(_sb, new Rectangle(0, 628, ScreenW, ScreenH - 628), new Color(8, 8, 9));
        _prim.FillRect(_sb, new Rectangle(0, 628, ScreenW, 1), new Color(70, 66, 60));

        // Vitals: the theme's OWN sprites — the winged heart with the PA star and PM leaf.
        var heartC = new Point(430, 684);
        if (_dof.Texture("hud_hp") != null)
        {
            _dof.Draw(_sb, "hud_hp", new Rectangle(heartC.X - 50, heartC.Y - 42, 100, 84));
            DTC("2330", heartC.X, heartC.Y - 22, 1, Color.White);
            DTC("2330", heartC.X, heartC.Y - 4, 1, Color.White * 0.85f);
            _dof.Draw(_sb, "hud_ap", new Rectangle(heartC.X - 78, heartC.Y + 8, 50, 48));
            DTC("7", heartC.X - 53, heartC.Y + 22, 1, Color.White);
            _dof.Draw(_sb, "hud_mp", new Rectangle(heartC.X + 28, heartC.Y + 8, 48, 48));
            DTC("3", heartC.X + 52, heartC.Y + 22, 1, Color.White);
        }
        else
        {
            _ew.Badge(_sb, EwChrome.Gem.Heart, heartC.ToVector2(), 92, new Color(222, 49, 60), new Color(124, 16, 24), 1f);
            DTC("2330", heartC.X, heartC.Y - 12, 1, Color.White);
        }

        // The consumable/spell bar: two rows, stack counters, pager arrows + page number.
        string[] counts = { "1958", "1949", "", "", "37", "", "", "14", "14", "21", "", "", "3", "" };
        for (int i = 0; i < 14; i++)
        {
            var s = new Rectangle(540 + i % 7 * 46, 636 + i / 7 * 46, 42, 42);
            _dof.Slot(_sb, s, hover: s.Contains(mp));
            if (i is not (2 or 3 or 5))
                _dof.SpellIcon(_sb, DemoSpellKeys[(i * 3 + 1) % DemoSpellKeys.Length],
                    new Rectangle(s.X + 4, s.Y + 4, 34, 34));
            if (counts[i].Length > 0)
                DT(counts[i], s.X + 2, s.Y + 1, 1, Color.White);
        }
        int px = 540 + 7 * 46 + 6;
        var up = new Rectangle(px, 640, 20, 16);
        var dn = new Rectangle(px, 668, 20, 16);
        _dof.Button(_sb, up, hover: up.Contains(mp));
        _dof.Button(_sb, dn, hover: dn.Contains(mp));
        DTC("^", up.Center.X, up.Y + 1, 1, new Color(46, 26, 10));
        DTC("v", dn.Center.X, dn.Y + 1, 1, new Color(46, 26, 10));
        DTC("1", px + 10, 655, 1, Color.White);

        // Right cluster: the two rows of round option chips (the minimap is gone).
        string[] glyphs = { "icon_cat_equip", "icon_cat_useful", "icon_cat_quest", "icon_gear",
                            "icon_cat_res", "icon_cat_all", "help", "icon_gear" };
        for (int i = 0; i < 8; i++)
        {
            var o = new Rectangle(1128 + i % 4 * 28, 648 + i / 4 * 28, 24, 24);
            _dof.Slice(_sb, "scroll_thumb", o);
            _dof.Draw(_sb, glyphs[i], new Rectangle(o.X + 5, o.Y + 5, 14, 14), new Color(46, 26, 10));
        }

        // The LEVEL bar: the yellow strip flush along the very bottom, full width.
        _dof.Gauge(_sb, new Rectangle(0, ScreenH - 10, ScreenW, 10), 0.78f, "gauge_timer");
    }
}

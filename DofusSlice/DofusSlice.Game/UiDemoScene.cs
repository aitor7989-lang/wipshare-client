using DofusSlice.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DofusSlice.Game;

/// <summary>
/// The UI-limits debug scene: a faithful in-engine recreation of a classic Dofus 1.29
/// screenshot (CARACTERISTIQUES + INVENTAIRE over the play field, full bottom HUD), built
/// entirely from the oldUI Remake theme pieces. Launch with <c>--uidemo</c> or toggle F10.
/// Pure chrome exercise — nothing here touches game state.
/// </summary>
public sealed partial class SliceGame
{
    private bool _uiDemo;
    private int _demoTab;      // RESUME / DETAILS
    private int _demoCat = 2;  // selected inventory category icon
    private int _demoSlot = 5; // selected item cell

    private static readonly string[] DemoSpellKeys =
    {
        "ruin_bolt", "piercing_shot", "slam", "bastion", "flashfire", "crippling_arrow",
        "husk_strike", "marrow_spit", "grave_bite", "warden_ironhide", "mite_sap",
        "wraith_wail", "ghoul_rend", "piper_gift", "sexton_smash", "seize",
        "blood_pact", "blink",
    };

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

        // A hint of "game" behind the windows so the translucent pieces have depth to show.
        for (int i = 0; i < 10; i++)
            for (int j = 0; j < 6; j++)
                _prim.DiamondAt(_sb, new Vector2(120 + i * 128, 80 + j * 128),
                    new Color(155, 143, 105) * (0.05f + ((i + j) % 2) * 0.04f));

        DrawDemoCharacterSheet(new Rectangle(20, 30, 345, 588), mp);
        DrawDemoInventory(new Rectangle(378, 30, 648, 588), mp);
        DrawDemoHud(mp);

        _font.Draw(_sb, "UI-LIMITS DEBUG SCENE  ·  oldUI Remake  ·  F10/ESC TO EXIT", 16, 8, 1,
            new Color(240, 208, 120));
        _sb.End();
    }

    // ----- window helpers ---------------------------------------------------------------

    private void DemoTitle(Rectangle w, string title, Point mp)
    {
        _dof.TitleStrip(_sb, new Rectangle(w.X + 3, w.Y + 3, w.Width - 6, 26));
        _font.DrawCentered(_sb, title, w.Center.X, w.Y + 12, 2, Color.White);
        var close = new Rectangle(w.Right - 34, w.Y + 5, 26, 22);
        _dof.Slice(_sb, close.Contains(mp) ? "btn_close_over" : "btn_close", close);
    }

    private void DemoRow(Rectangle r, string icon, string label, string value, Point mp,
        bool plus = false, bool big = false)
    {
        _dof.Panel(_sb, r, light: true);
        if (!_dof.StatIcon(_sb, icon, new Rectangle(r.X + 6, r.Y + (r.Height - 16) / 2, 16, 16)))
            _prim.DiscAt(_sb, new Vector2(r.X + 14, r.Center.Y), 7, new Color(180, 60, 50));
        _font.Draw(_sb, label, r.X + 28, r.Center.Y - 5, 1, Color.White);
        _font.Draw(_sb, value, r.Right - (plus ? 58 : 14) - _font.Measure(value, big ? 2 : 1) ,
            r.Center.Y - (big ? 7 : 5), big ? 2 : 1, Color.White);
        if (plus)
        {
            var b = new Rectangle(r.Right - 26, r.Y + (r.Height - 18) / 2, 20, 18);
            _dof.Button(_sb, b, hover: b.Contains(mp));
            _font.DrawCentered(_sb, "+", b.Center.X, b.Y + 5, 1, new Color(46, 26, 10));
        }
    }

    // ----- CARACTERISTIQUES ---------------------------------------------------------------

    private void DrawDemoCharacterSheet(Rectangle a, Point mp)
    {
        _dof.Window(_sb, a);
        DemoTitle(a, "CARACTERISTIQUES", mp);

        // Tabs (clickable, purely visual).
        var tabs = new[] { "RESUME", "DETAILS" };
        for (int i = 0; i < 2; i++)
        {
            var t = new Rectangle(a.X + 16 + i * 100, a.Y + 32, 96, 30);
            if (t.Contains(mp) && LeftClicked()) _demoTab = i;
            _dof.Tab(_sb, t, _demoTab == i, t.Contains(mp));
            _font.DrawCentered(_sb, tabs[i], t.Center.X, t.Y + 12, 1,
                _demoTab == i ? Color.White : UiInk);
        }
        var heartTab = new Rectangle(a.X + 232, a.Y + 32, 40, 30);
        _dof.Tab(_sb, heartTab, false, heartTab.Contains(mp));
        _dof.StatIcon(_sb, "vit", new Rectangle(heartTab.Center.X - 8, heartTab.Y + 8, 16, 16));

        // Portrait + identity.
        var port = new Rectangle(a.X + 16, a.Y + 70, 84, 84);
        _dof.Panel(_sb, port);
        var sheet = _sprites.GetSheet("hero", "idle", "se");
        if (sheet != null)
            SpriteDraw.Feet(_sb, sheet, new Vector2(port.Center.X, port.Bottom - 8), Color.White, 66f,
                (int)(_time * 6) % sheet.FrameCount);
        _font.Draw(_sb, "ASPETTE", a.X + 112, a.Y + 76, 2, UiInk);
        _font.Draw(_sb, "OMEGA 191", a.X + 112, a.Y + 96, 1, UiInkDim);
        _font.Draw(_sb, "15 080 POINTS", a.X + 112, a.Y + 112, 1, DofusUi.InkGold);
        _font.Draw(_sb, "BETA-MAJOR", a.X + 16, a.Y + 160, 1, UiInkDim);

        // Experience / Energie candy bars.
        _font.Draw(_sb, "EXPERIENCE", a.X + 16, a.Y + 180, 1, UiInk);
        _dof.Gauge(_sb, new Rectangle(a.X + 110, a.Y + 178, 215, 12), 0.62f, "gauge_timer");
        _font.Draw(_sb, "ENERGIE", a.X + 16, a.Y + 198, 1, UiInk);
        _dof.Gauge(_sb, new Rectangle(a.X + 110, a.Y + 196, 215, 12), 0.55f, "gauge_timer");

        // The three vital rows.
        DemoRow(new Rectangle(a.X + 16, a.Y + 222, a.Width - 32, 26), "vit", "POINTS DE VIE (PV)", "2 330", mp, big: true);
        DemoRow(new Rectangle(a.X + 16, a.Y + 254, a.Width - 32, 26), "ap", "POINTS D'ACTION (PA)", "7", mp, big: true);
        DemoRow(new Rectangle(a.X + 16, a.Y + 286, a.Width - 32, 26), "mp", "POINTS DE MOUVEMENT (PM)", "3", mp, big: true);

        // The six characteristics with [+] spenders.
        (string icon, string label, string val)[] stats =
        {
            ("vit", "VITALITE", "1280"), ("agi", "AGILITE", "270"), ("cha", "CHANCE", "100"),
            ("str", "FORCE", "166"), ("int", "INTELLIGENCE", "167"), ("wis", "SAGESSE", "209"),
        };
        for (int i = 0; i < stats.Length; i++)
            DemoRow(new Rectangle(a.X + 16, a.Y + 322 + i * 32, a.Width - 32, 26),
                stats[i].icon, stats[i].label, stats[i].val, mp, plus: true);

        // Points to spend + the two footer pills.
        var pts = new Rectangle(a.X + 16, a.Y + 518, 210, 24);
        _dof.Panel(_sb, pts);
        _font.Draw(_sb, "POINTS A REPARTIR :   995", pts.X + 10, pts.Y + 7, 1, Color.White);
        var refresh = new Rectangle(pts.Right + 8, pts.Y + 2, 20, 20);
        _dof.Slice(_sb, "scroll_thumb", refresh);

        var ens = new Rectangle(a.X + 16, a.Bottom - 40, 140, 28);
        var srt = new Rectangle(a.X + 176, a.Bottom - 40, 140, 28);
        _dof.Button(_sb, ens, hover: ens.Contains(mp));
        _dof.Button(_sb, srt, hover: srt.Contains(mp));
        _font.DrawCentered(_sb, "ENSEMBLES", ens.Center.X, ens.Y + 9, 1, new Color(46, 26, 10));
        _font.DrawCentered(_sb, "SORTS", srt.Center.X, srt.Y + 9, 1, new Color(46, 26, 10));
    }

    // ----- INVENTAIRE -------------------------------------------------------------------

    private void DrawDemoInventory(Rectangle b, Point mp)
    {
        _dof.Window(_sb, b);
        DemoTitle(b, "INVENTAIRE", mp);

        // The equipment doll.
        var doll = new Rectangle(b.X + 16, b.Y + 40, 356, 322);
        _dof.Panel(_sb, doll);
        void Equip(int x, int y, string? icon = null, bool filled = false)
        {
            var s = new Rectangle(x, y, 54, 54);
            _dof.Slot(_sb, s, hover: s.Contains(mp), selected: false);
            if (icon != null) _dof.SpellIcon(_sb, icon, new Rectangle(x + 7, y + 7, 40, 40),
                filled ? Color.White : Color.White * 0.35f);
        }
        for (int i = 0; i < 3; i++) Equip(doll.X + 10, doll.Y + 12 + i * 64);          // amulet/ring col
        Equip(doll.Right - 64, doll.Y + 12, "warden_ironhide", filled: true);           // hat: gold shield
        for (int i = 1; i < 4; i++) Equip(doll.Right - 64, doll.Y + 12 + i * 64);       // cape/belt/boots
        Equip(doll.X + 10, doll.Y + 250, "sexton_smash", filled: true);                 // weapon
        Equip(doll.X + 74, doll.Y + 250);                                               // shield
        Equip(doll.Center.X - 22, doll.Y + 258);                                        // small center
        Equip(doll.Right - 128, doll.Y + 250);
        Equip(doll.Right - 64, doll.Y + 250);

        var hero = _sprites.GetSheet("hero", "idle", "se");
        if (hero != null)
            SpriteDraw.Feet(_sb, hero, new Vector2(doll.Center.X, doll.Bottom - 74), Color.White, 150f,
                (int)(_time * 5) % hero.FrameCount);
        _font.DrawCentered(_sb, "<", doll.Center.X - 66, doll.Bottom - 62, 3, new Color(226, 118, 40));
        _font.DrawCentered(_sb, ">", doll.Center.X + 66, doll.Bottom - 62, 3, new Color(226, 118, 40));

        // The pet / consumable strip.
        for (int i = 0; i < 7; i++)
        {
            var s = new Rectangle(b.X + 16 + i * 52, b.Y + 372, 48, 48);
            _dof.Slot(_sb, s, hover: s.Contains(mp));
            if (i is 0 or 1 or 5)
                _dof.SpellIcon(_sb, DemoSpellKeys[(i + 9) % DemoSpellKeys.Length],
                    new Rectangle(s.X + 5, s.Y + 5, 38, 38));
        }

        // The oldUI Remake plaque (the screenshot's watermark corner, as a wink).
        var plaque = new Rectangle(b.X + 16, b.Y + 432, 356, 134);
        _dof.Panel(_sb, plaque);
        for (int i = 0; i < 4; i++)
            _prim.DiamondAt(_sb, new Vector2(plaque.X + 64 + i % 2 * 32, plaque.Y + 52 + i / 2 * 16 + i % 2 * 8),
                new Color(196, 186, 150) * (i % 2 == 0 ? 0.9f : 0.55f));
        _font.Draw(_sb, "oldUI", plaque.X + 150, plaque.Y + 30, 4, Color.White);
        _font.Draw(_sb, "REMAKE", plaque.X + 150, plaque.Y + 70, 4, Color.White);

        // Right pane: categories, dropdown, the egg grid, search, kamas.
        int rx = b.X + 390, ry = b.Y + 40;
        string[] cats = { "ini", "str", "cha", "wis", "ap" };
        for (int i = 0; i < cats.Length; i++)
        {
            var c = new Rectangle(rx + i * 42, ry, 38, 30);
            if (c.Contains(mp) && LeftClicked()) _demoCat = i;
            _dof.Tab(_sb, c, _demoCat == i, c.Contains(mp));
            _dof.StatIcon(_sb, cats[i], new Rectangle(c.Center.X - 8, c.Y + 8, 16, 16));
        }
        var drop = new Rectangle(rx, ry + 36, 200, 22);
        _dof.Panel(_sb, drop, light: true);
        _font.Draw(_sb, "DOFUS", drop.X + 8, drop.Y + 7, 1, Color.White);
        _font.Draw(_sb, "v", drop.Right - 14, drop.Y + 7, 1, Color.White);
        var gear = new Rectangle(rx + 208, ry + 36, 22, 22);
        _dof.Button(_sb, gear, hover: gear.Contains(mp));
        _font.DrawCentered(_sb, "*", gear.Center.X, gear.Y + 7, 1, new Color(46, 26, 10));

        // 5 x 8 grid of "items" (our spell tiles standing in for the eggs).
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 5; col++)
            {
                int idx = row * 5 + col;
                var cell = new Rectangle(rx + col * 47, ry + 66 + row * 47, 44, 44);
                if (cell.Contains(mp) && LeftClicked()) _demoSlot = idx;
                _dof.Slot(_sb, cell, hover: cell.Contains(mp), selected: idx == _demoSlot);
                _dof.SpellIcon(_sb, DemoSpellKeys[idx % DemoSpellKeys.Length],
                    new Rectangle(cell.X + 4, cell.Y + 4, 36, 36));
                if (idx is 7 or 18 or 23) // a few stack counts, like the screenshot's "2"/"4"
                    _font.Draw(_sb, ((idx * 7) % 9 + 2).ToString(), cell.Right - 10, cell.Y + 2, 1, Color.White);
            }

        // Search + kamas.
        var search = new Rectangle(rx, b.Bottom - 52, 234, 20);
        _dof.Slice(_sb, "chat_input", search);
        _font.Draw(_sb, "RECHERCHER DANS L'INVENTAIRE", search.X + 22, search.Y + 6, 1, new Color(150, 150, 150));
        _prim.DiscAt(_sb, new Vector2(search.X + 11, search.Center.Y), 5, new Color(90, 90, 90));
        _dof.Gauge(_sb, new Rectangle(rx, b.Bottom - 26, 110, 12), 0.32f, "gauge_timer");
        _font.Draw(_sb, "40 753 421 K", rx + 122, b.Bottom - 26, 1, DofusUi.InkGold);
    }

    // ----- The bottom HUD ------------------------------------------------------------------

    private void DrawDemoHud(Point mp)
    {
        _dof.Slice(_sb, "float_bg", new Rectangle(-8, 628, ScreenW + 16, ScreenH - 628 + 14));

        // Chat corner.
        _dof.Slice(_sb, "chat_input", new Rectangle(12, 640, 300, 20));
        _font.Draw(_sb, "BIENVENUE DANS LE MONDE DES DOUZE", 20, 646, 1, new Color(150, 150, 150));
        for (int i = 0; i < 2; i++)
            _dof.Slice(_sb, "scroll_thumb", new Rectangle(12 + i * 20, 666, 16, 16));

        // Vitals: the heart with AP / MP satellites (our Emberwick gems wear the same read).
        var heartC = new Vector2(430, 692);
        _ew.Badge(_sb, EwChrome.Gem.Heart, heartC, 96, new Color(222, 49, 60), new Color(124, 16, 24), 1f);
        _font.DrawCentered(_sb, "2330", (int)heartC.X, (int)heartC.Y - 16, 2, Color.White);
        _font.DrawCentered(_sb, "2330", (int)heartC.X, (int)heartC.Y + 4, 1, Color.White * 0.85f);
        _ew.Badge(_sb, EwChrome.Gem.Star, heartC + new Vector2(-58, 26), 42, Ew.Ap, Ew.ApDeep);
        _font.DrawCentered(_sb, "7", (int)heartC.X - 58, (int)heartC.Y + 21, 2, Color.White);
        _ew.Badge(_sb, EwChrome.Gem.Diamond, heartC + new Vector2(58, 26), 40, Ew.Mp, Ew.MpDeep);
        _font.DrawCentered(_sb, "3", (int)heartC.X + 58, (int)heartC.Y + 21, 2, Color.White);

        // The spell bar: two rows with stack counters.
        string[] counts = { "1958", "1949", "", "", "37", "", "", "14", "14", "21", "", "", "3", "" };
        for (int i = 0; i < 14; i++)
        {
            var s = new Rectangle(520 + i % 7 * 46, 638 + i / 7 * 46, 42, 42);
            _dof.Slot(_sb, s, hover: s.Contains(mp));
            if (i is not (2 or 3 or 5))
                _dof.SpellIcon(_sb, DemoSpellKeys[(i * 3 + 1) % DemoSpellKeys.Length],
                    new Rectangle(s.X + 4, s.Y + 4, 34, 34));
            if (counts[i].Length > 0)
                _font.Draw(_sb, counts[i], s.X + 2, s.Y + 2, 1, Color.White);
        }

        // The right cluster: option buttons + a minimap plate.
        for (int i = 0; i < 6; i++)
            _dof.Slice(_sb, "scroll_thumb", new Rectangle(1024 + i % 3 * 22, 640 + i / 3 * 22, 18, 18));
        var map = new Rectangle(1120, 636, 148, 96);
        _dof.Panel(_sb, map);
        for (int i = 0; i < 5; i++)
            _prim.DiamondAt(_sb, new Vector2(map.X + 34 + i * 18, map.Y + 40 + i % 2 * 14),
                new Color(155, 143, 105) * 0.5f);
        _prim.DiscAt(_sb, new Vector2(map.Right - 18, map.Y + 16), 5, new Color(226, 118, 40));

        // The long XP strip along the very bottom.
        _dof.Gauge(_sb, new Rectangle(300, 744, 690, 10), 0.78f, "gauge_timer");
    }
}

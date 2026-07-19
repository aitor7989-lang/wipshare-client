using DofusSlice.Core.Content.Tithe;
using DofusSlice.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace DofusSlice.Game;

/// <summary>
/// The Leader's windows, structured exactly like the F10 Dofus recreation (the "test we
/// made") but wearing the ONE-BIT chrome: C = CHARACTERISTICS (portrait, XP, PV/PA/PM,
/// six stat rows with spenders), I = INVENTORY (the equipment doll with your character
/// standing in it, the consumable strip, the stash grid, the gold row).
/// </summary>
public partial class SliceGame
{
    private bool _charOpen, _invOpen, _spellOpen;
    private string? _panelMsg;          // one-line feedback ("ate bread", "essence learned")
    private float _panelMsgUntil;       // shown while _time is before this

    private static readonly (string key, string label)[] StatRows =
    {
        ("vit", "VIT"), ("str", "STR"), ("int", "INT"), ("cha", "CHA"), ("agi", "AGI"), ("wis", "WIS"),
    };

    private static readonly Rectangle CharWin = new(465, 60, 350, 588);
    private static readonly Rectangle InvWin = new(316, 60, 648, 588);
    private static readonly Rectangle SpellWin = new(380, 60, 520, 588);

    // ----- shared chrome -------------------------------------------------------------

    private void PanelTitle(Rectangle w, string title)
    {
        var band = new Rectangle(w.X + 2, w.Y + 2, w.Width - 4, 26);
        _prim.FillRect(_sb, band, Mono.Panel);
        _prim.FillRect(_sb, new Rectangle(band.X + 10, band.Bottom - 1, band.Width - 20, 1), Mono.Ink);
        _font.DrawCentered(_sb, title, band.Center.X, band.Y + 8, 1, Mono.Ink);
        var close = PanelCloseRect(w);
        bool hov = close.Contains(new Point(_mouse.X, _mouse.Y));
        Mono.Button(_sb, _prim, close, hover: hov);
        _font.DrawCentered(_sb, "X", close.Center.X, close.Y + 5, 1, Mono.ButtonInk(hov));
    }

    private static Rectangle PanelCloseRect(Rectangle w) => new(w.Right - 32, w.Y + 5, 22, 20);

    /// <summary>A small mono item card next to the pointer (name, slot, stat line).</summary>
    private void DrawItemCard(string title, string line1, string? line2, Point mp)
    {
        int wdt = Math.Max(_font.Measure(title, 1), Math.Max(_font.Measure(line1, 1),
            line2 == null ? 0 : _font.Measure(line2, 1))) + 20;
        int h = line2 == null ? 44 : 57;
        var r = new Rectangle(Math.Min(mp.X + 14, ScreenW - wdt - 6), Math.Max(6, mp.Y - h - 6), wdt, h);
        _prim.FillRect(_sb, r, Mono.Panel * 0.97f);
        _prim.StrokeRect(_sb, r, 1, Mono.Ink);
        _font.Draw(_sb, title, r.X + 9, r.Y + 6, 1, Mono.Ink);
        _font.Draw(_sb, line1, r.X + 9, r.Y + 20, 1, Mono.Dim);
        if (line2 != null) _font.Draw(_sb, line2, r.X + 9, r.Y + 33, 1, Mono.Dim);
    }

    private void PanelFeedback(string? msg)
    {
        if (msg == null) return;
        _panelMsg = msg; _panelMsgUntil = _time + 3f;
        _sfx.Play("heal", 0.5f);
    }

    // ----- THE CHARACTER SHEET (C) — the demo's left window, mono -----------------------

    private void DrawCharacterWindow()
    {
        var a = _campaign.Avatar;
        if (a == null) return;
        var w = CharWin;
        var mp = new Point(_mouse.X, _mouse.Y);
        Mono.Frame(_sb, _prim, w, emphasis: true, fillAlpha: 0.985f);
        PanelTitle(w, "CHARACTERISTICS");

        int L = w.X + 28, W = w.Width - 56;
        var s = TitheContent.StatsOf(a);

        // Portrait box + identity block (demo: portrait at +106; we start under the band).
        var port = new Rectangle(L, w.Y + 46, 72, 72);
        Mono.Slot(_sb, _prim, port);
        var sheet = _sprites.GetSheet(PixActor(a.ClassId).sprite, "idle", "se");
        if (sheet != null)
            SpriteDraw.Feet(_sb, sheet, new Vector2(port.Center.X, port.Bottom - 6), Mono.Ink, 64f, 0);
        _font.Draw(_sb, $"{a.Name.ToUpperInvariant()} — LEADER", L + 86, w.Y + 50, 1, Mono.Ink);
        _font.Draw(_sb, $"{a.ClassId.ToUpperInvariant()}   LVL {a.Level}", L + 86, w.Y + 66, 2, Mono.Ink);
        int need = CampaignUnit.XpForNextLevel(a.Level);
        _font.Draw(_sb, $"{a.Xp} / {need} XP", L + 86, w.Y + 92, 1, Mono.Dim);
        Mono.Bar(_sb, _prim, new Rectangle(L + 86, w.Y + 106, W - 86, 10),
            Math.Clamp(need <= 0 ? 1f : (float)a.Xp / need, 0f, 1f), Mono.Dim);

        // PV / PA / PM rows — the demo's pill rows, in ink.
        var (bAp, bMp) = TitheContent.ClassApMp(a.ClassId);
        int cur = Math.Clamp(a.CurrentHp ?? s.MaxHp, 0, s.MaxHp);
        DrawSheetRow(new Rectangle(L, w.Y + 132, W, 26), "HP", $"{cur} / {s.MaxHp}", mp);
        DrawSheetRow(new Rectangle(L, w.Y + 162, W, 26), "ACTION POINTS (AP)", $"{bAp + s.ApBonus}", mp);
        DrawSheetRow(new Rectangle(L, w.Y + 192, W, 26), "MOVEMENT POINTS (MP)", $"{bMp + s.MpBonus}", mp);
        _prim.FillRect(_sb, new Rectangle(L, w.Y + 228, W, 1), Mono.Faint);

        // The six characteristics with [+] spenders OUTSIDE the rows (demo layout).
        for (int i = 0; i < StatRows.Length; i++)
        {
            var (key, label) = StatRows[i];
            int shown = key switch
            {
                "vit" => s.Vitality, "str" => s.Strength, "int" => s.Intelligence,
                "cha" => s.Chance, "agi" => s.Agility, _ => s.Wisdom,
            };
            var row = new Rectangle(L, w.Y + 240 + i * 30, W - 32, 26);
            DrawSheetRow(row, StatLabel(key, label), $"{shown}", mp);
            if (a.StatPoints > 0)
            {
                var b = CharPlusRect(i);
                bool hov = b.Contains(mp);
                Mono.Button(_sb, _prim, b, hover: hov);
                _font.DrawCentered(_sb, "+", b.Center.X, b.Y + 6, 1, Mono.ButtonInk(hov));
            }
        }

        // Points + auto-spend, then the effective damage line the fights actually use.
        _font.Draw(_sb, a.StatPoints > 0 ? $"POINTS TO SPEND: {a.StatPoints}" : "NO POINTS BANKED",
            L, w.Y + 432, 1, a.StatPoints > 0 ? Mono.Ink : Mono.Dim);
        if (a.StatPoints > 0)
        {
            bool hov = CharAutoRect.Contains(mp);
            Mono.Button(_sb, _prim, CharAutoRect, hover: hov);
            _font.DrawCentered(_sb, "AUTO-SPEND ALL", CharAutoRect.Center.X, CharAutoRect.Y + 5, 1, Mono.ButtonInk(hov));
        }
        var elem = TitheContent.ClassElement(a.ClassId);
        _font.Draw(_sb, $"{elem.ToString().ToUpperInvariant()} POWER {TitheContent.DamageStatFor(s, elem)}"
            + $"   ·   INITIATIVE {s.Initiative}", L, w.Y + 458, 1, Mono.Dim);

        int set = TitheContent.SetPiecesEquipped(a, TitheContent.GraveyardSet);
        _font.Draw(_sb, $"ADVENTURER SET: {set}/7 PIECES", L, w.Y + 476, 1, set > 0 ? Mono.Ink : Mono.Faint);

        var mercs = _campaign.Crew.Where(c => !c.IsAvatar).ToList();
        if (mercs.Count > 0)
        {
            _font.Draw(_sb, "COMPANIONS  (they manage themselves)", L, w.Bottom - 92, 1, Mono.Faint);
            for (int i = 0; i < mercs.Count && i < 2; i++)
            {
                var c = mercs[i];
                _font.Draw(_sb, $"{c.Name.ToUpperInvariant()}  L{c.Level}  ·  {TitheContent.UnitMaxHp(c)} HP"
                    + (c.Wounded ? "  ·  WOUNDED" : ""),
                    L + 10, w.Bottom - 76 + i * 14, 1, c.Wounded ? Mono.Danger : Mono.Dim);
            }
        }

        _font.DrawCentered(_sb, "(C OR ESC TO CLOSE)", w.Center.X, w.Bottom - 22, 1, Mono.Dim);
        DrawPanelMsg(w);
    }

    private static string StatLabel(string key, string label) => key switch
    {
        "vit" => "VITALITY", "str" => "STRENGTH", "int" => "INTELLIGENCE",
        "cha" => "CHANCE", "agi" => "AGILITY", _ => "WISDOM",
    };

    private void DrawSheetRow(Rectangle r, string label, string value, Point mp)
    {
        _prim.FillRect(_sb, r, Mono.Panel);
        _prim.StrokeRect(_sb, r, 1, Mono.Faint);
        _font.Draw(_sb, label, r.X + 10, r.Y + 9, 1, Mono.Ink);
        _font.Draw(_sb, value, r.Right - 16 - _font.Measure(value, 1), r.Y + 9, 1, Mono.Ink);
    }

    private static Rectangle CharPlusRect(int i) => new(CharWin.X + 28 + (CharWin.Width - 56) - 24,
        CharWin.Y + 240 + i * 30 + 3, 22, 20);
    private static Rectangle CharAutoRect => new(CharWin.Right - 28 - 124, CharWin.Y + 428, 124, 18);

    private void ClickCharacterWindow(Point m)
    {
        var a = _campaign.Avatar;
        if (a == null) return;
        if (PanelCloseRect(CharWin).Contains(m)) { _charOpen = false; _sfx.Play("click"); return; }
        if (a.StatPoints > 0)
        {
            for (int i = 0; i < StatRows.Length; i++)
                if (CharPlusRect(i).Contains(m)) { a.SpendStat(StatRows[i].key); _sfx.Play("click"); return; }
            if (CharAutoRect.Contains(m)) { TitheContent.AutoSpendStats(a); _sfx.Play("coin"); return; }
        }
    }

    // ----- THE INVENTORY (I) — the demo's right window: doll + strip + grid + gold ------

    private static readonly (string slot, bool left, int idx)[] DollSlots =
    {
        ("amulet", true, 0), ("weapon", true, 1), ("ring", true, 2),
        ("hat", false, 0), ("cape", false, 1), ("belt", false, 2), ("boots", false, 3),
    };

    private Rectangle DollRect => new(InvWin.X + 34, InvWin.Y + 46, 336, 296);

    private Rectangle DollSlotRect((string slot, bool left, int idx) d)
    {
        var doll = DollRect;
        return d.left
            ? new Rectangle(doll.X + 12, doll.Y + 12 + d.idx * 60, 50, 50)
            : new Rectangle(doll.Right - 62, doll.Y + 12 + d.idx * 60, 50, 50);
    }

    private Rectangle StripRect(int i) => new(InvWin.X + 34 + i * 50, InvWin.Y + 366, 46, 46);
    private Rectangle GridRect(int idx) => new(InvWin.X + 390 + idx % 5 * 45, InvWin.Y + 62 + idx / 5 * 45, 42, 42);

    private void DrawInventoryWindow()
    {
        var a = _campaign.Avatar;
        if (a == null) return;
        var w = InvWin;
        var mp = new Point(_mouse.X, _mouse.Y);
        Mono.Frame(_sb, _prim, w, emphasis: true, fillAlpha: 0.985f);
        PanelTitle(w, "INVENTORY");

        // The equipment doll — your character standing among their slots (demo geometry).
        var doll = DollRect;
        _prim.FillRect(_sb, doll, new Color(10, 10, 10));
        _prim.StrokeRect(_sb, doll, 1, Mono.Faint);
        string? hoverCardTitle = null, hoverCard1 = null, hoverCard2 = null;

        var bySlot = a.Equipment.ToDictionary(TitheContent.ItemSlot, id => id);
        foreach (var d in DollSlots)
        {
            var r = DollSlotRect(d);
            bool hov = r.Contains(mp);
            bySlot.TryGetValue(d.slot, out string? worn);
            Mono.Slot(_sb, _prim, r, hover: hov, selected: false);
            if (worn != null)
            {
                _font.DrawCentered(_sb, Trunc(TitheContent.ItemName(worn).ToUpperInvariant(), 7),
                    r.Center.X, r.Y + 14, 1, Mono.Ink);
                _font.DrawCentered(_sb, d.slot.ToUpperInvariant(), r.Center.X, r.Bottom - 16, 1, Mono.Faint);
                if (hov)
                {
                    hoverCardTitle = TitheContent.ItemName(worn).ToUpperInvariant();
                    hoverCard1 = TitheContent.ItemStatLine(worn);
                    hoverCard2 = "click to unequip";
                }
            }
            else
                _font.DrawCentered(_sb, d.slot.ToUpperInvariant(), r.Center.X, r.Y + 20, 1, Mono.Faint);
        }

        // Your character stands in the middle of their gear, like the demo's hero.
        var sheet = _sprites.GetSheet(PixActor(a.ClassId).sprite, "idle", "se");
        if (sheet != null)
            SpriteDraw.Feet(_sb, sheet, new Vector2(doll.Center.X, doll.Center.Y + 62), Mono.Ink, 96f, 0);
        _font.DrawCentered(_sb, $"{a.Name.ToUpperInvariant()}  ·  LVL {a.Level}",
            doll.Center.X, doll.Bottom - 20, 1, Mono.Dim);

        // The consumable strip: bread you can EAT, draughts you carry, essences you can learn.
        var strip = new (string label, int count, string tip)[]
        {
            ("BREAD", _campaign.Bread, "eat now: mends the most-hurt member"),
            ("DRGHT", _campaign.Draughts, "healing draught (combat use: soon)"),
        };
        for (int i = 0; i < 7; i++)
        {
            var r = StripRect(i);
            bool hov = r.Contains(mp);
            Mono.Slot(_sb, _prim, r, hover: hov);
            if (i < 2)
            {
                var (label, count, tip) = strip[i];
                var inkc = count > 0 ? Mono.Ink : Mono.Faint;
                _font.DrawCentered(_sb, label, r.Center.X, r.Y + 8, 1, inkc);
                if (count > 0) _font.Draw(_sb, $"x{count}", r.X + 3, r.Y + 31, 1, Mono.Ink);
                if (hov && count > 0) { hoverCardTitle = label; hoverCard1 = tip; hoverCard2 = null; }
            }
            else if (i - 2 < _campaign.Essences.Count)
            {
                string ess = _campaign.Essences[i - 2];
                _font.DrawCentered(_sb, "ESS", r.Center.X, r.Y + 8, 1, Mono.Ink);
                _font.DrawCentered(_sb, Trunc(ess.ToUpperInvariant(), 6), r.Center.X, r.Bottom - 16, 1, Mono.Dim);
                if (hov)
                {
                    hoverCardTitle = $"ESSENCE: {ess.ToUpperInvariant()}";
                    hoverCard1 = a.HasFreeEssenceSlot ? "click: YOU learn its skill (permanent)" : "your essence slots are full";
                    hoverCard2 = $"slots {a.EssenceSlots.Count}/2";
                }
            }
        }

        // The stash grid (right pane): click a piece to wear it.
        var stash = _campaign.Stash;
        for (int idx = 0; idx < 40; idx++)
        {
            var cell = GridRect(idx);
            bool hov = cell.Contains(mp);
            Mono.Slot(_sb, _prim, cell, hover: hov);
            if (idx < stash.Count)
            {
                string id = stash[idx];
                _font.DrawCentered(_sb, Trunc(TitheContent.ItemName(id).ToUpperInvariant(), 5),
                    cell.Center.X, cell.Y + 9, 1, Mono.Ink);
                _font.DrawCentered(_sb, Trunc(TitheContent.ItemSlot(id).ToUpperInvariant(), 5),
                    cell.Center.X, cell.Bottom - 14, 1, Mono.Faint);
                if (hov)
                {
                    hoverCardTitle = TitheContent.ItemName(id).ToUpperInvariant();
                    hoverCard1 = TitheContent.ItemStatLine(id);
                    hoverCard2 = "click to equip";
                }
            }
        }

        // The kamas row, ours: one ink coin, the number, the set progress.
        _prim.DiscAt(_sb, new Vector2(w.X + 398, w.Y + 528), 7, Mono.Ink);
        _prim.DiscAt(_sb, new Vector2(w.X + 398, w.Y + 528), 5, Mono.Panel);
        _prim.DiscAt(_sb, new Vector2(w.X + 398, w.Y + 528), 3, Mono.Ink);
        _font.Draw(_sb, $"{_campaign.Gold} GOLD", w.X + 414, w.Y + 521, 2, Mono.Ink);
        int adv = TitheContent.SetPiecesEquipped(a, TitheContent.GraveyardSet);
        _font.Draw(_sb, $"ADVENTURER {adv}/7", w.X + 414, w.Y + 545, 1, adv > 0 ? Mono.Ink : Mono.Faint);
        // The panoply ladder: the tier you HOLD in ink, the next rung as the goal line.
        var tiers = TitheContent.SetTierSummaries(TitheContent.GraveyardSet).ToList();
        var held = tiers.LastOrDefault(t => t.Pieces <= adv);
        var goal = tiers.FirstOrDefault(t => t.Pieces > adv);
        int sy = w.Y + 559;
        if (held.Pieces > 0)
        { _font.Draw(_sb, Trunc($"{held.Pieces} PC: {held.Text}", 40), w.X + 414, sy, 1, Mono.Ink); sy += 12; }
        if (goal.Pieces > 0)
            _font.Draw(_sb, Trunc($"AT {goal.Pieces} PC: {goal.Text}", 40), w.X + 414, sy, 1, Mono.Faint);

        _font.DrawCentered(_sb, "(I OR ESC TO CLOSE)", w.Center.X, w.Bottom - 22, 1, Mono.Dim);
        DrawPanelMsg(w);
        if (hoverCardTitle != null) DrawItemCard(hoverCardTitle, hoverCard1 ?? "", hoverCard2, mp);
    }

    private void DrawPanelMsg(Rectangle w)
    {
        if (_time >= _panelMsgUntil || _panelMsg == null) return;
        _font.DrawCentered(_sb, _panelMsg.ToUpperInvariant(), w.Center.X, w.Bottom - 40, 1, Mono.Ink);
    }

    private void ClickInventoryWindow(Point m)
    {
        var a = _campaign.Avatar;
        if (a == null) return;
        if (PanelCloseRect(InvWin).Contains(m)) { _invOpen = false; _sfx.Play("click"); return; }

        var bySlot = a.Equipment.ToDictionary(TitheContent.ItemSlot, id => id);
        foreach (var d in DollSlots)
            if (DollSlotRect(d).Contains(m) && bySlot.TryGetValue(d.slot, out string? worn))
            { _campaign.Unequip(a, worn); _sfx.Play("click"); return; }

        if (StripRect(0).Contains(m)) { PanelFeedback(_campaign.EatBread()); return; }
        for (int i = 2; i < 7; i++)
            if (StripRect(i).Contains(m) && i - 2 < _campaign.Essences.Count)
            {
                string ess = _campaign.Essences[i - 2];
                if (_campaign.TeachEssence(a, ess))
                    PanelFeedback($"you learn {ess} — it is part of you now");
                return;
            }

        for (int idx = 0; idx < _campaign.Stash.Count && idx < 40; idx++)
            if (GridRect(idx).Contains(m)) { _campaign.Equip(a, _campaign.Stash[idx]); _sfx.Play("coin"); return; }
    }

    /// <summary>Shared per-frame upkeep + input for the two Leader windows. Returns true when
    /// a window swallowed this frame's input (the scene below should ignore it).</summary>
    private bool UpdateLeaderPanels()
    {
        if (Pressed(Keys.C)) { _charOpen = !_charOpen; _invOpen = _spellOpen = false; _openNpc = -1; return true; }
        if (Pressed(Keys.I) || Pressed(Keys.E)) { _invOpen = !_invOpen; _charOpen = _spellOpen = false; _openNpc = -1; return true; }
        if (Pressed(Keys.S)) { _spellOpen = !_spellOpen; _charOpen = _invOpen = false; _openNpc = -1; return true; }
        if (!_charOpen && !_invOpen && !_spellOpen) return false;
        if (Pressed(Keys.Escape)) { _charOpen = _invOpen = _spellOpen = false; return true; }
        if (LeftClicked())
        {
            var m = new Point(_mouse.X, _mouse.Y);
            if (_charOpen) ClickCharacterWindow(m);
            else if (_spellOpen) ClickSpellPanel(m);
            else ClickInventoryWindow(m);
        }
        return true;
    }

    // ----- THE SPELL BOOK (S) — one row per spell, next rank always visible --------------

    private static Rectangle SpellRowR(int i) => new(SpellWin.X + 26, SpellWin.Y + 66 + i * 74, SpellWin.Width - 52, 66);
    private static Rectangle SpellRankUpR(int i)
    { var r = SpellRowR(i); return new Rectangle(r.Right - 96, r.Y + 20, 84, 24); }

    private void DrawSpellPanel()
    {
        var a = _campaign.Avatar;
        if (a == null) return;
        var w = SpellWin;
        var mp = new Point(_mouse.X, _mouse.Y);
        Mono.Frame(_sb, _prim, w, emphasis: true, fillAlpha: 0.985f);
        PanelTitle(w, "SPELLS");

        _font.Draw(_sb, a.SpellPoints > 0 ? $"SPELL POINTS TO SPEND: {a.SpellPoints}" : "NO SPELL POINTS BANKED",
            w.X + 26, w.Y + 40, 1, a.SpellPoints > 0 ? Mono.Ink : Mono.Dim);

        var classKeys = new HashSet<string>(TitheContent.UnitSkillKeys(a).Take(3));
        var keys = TitheContent.UnitSkillKeys(a).ToList();
        for (int i = 0; i < keys.Count && i < 6; i++)
        {
            string key = keys[i];
            var sp = TitheContent.UnitSkill(a, key);
            int rank = a.RankOf(key), maxR = TitheContent.MaxRank(key);
            var row = SpellRowR(i);
            _prim.FillRect(_sb, row, Mono.Panel);
            _prim.StrokeRect(_sb, row, 1, Mono.Faint);

            // The letter well, like the combat bar's slots.
            var well = new Rectangle(row.X + 8, row.Y + 10, 46, 46);
            Mono.Slot(_sb, _prim, well);
            _font.DrawCentered(_sb, sp.Name[..1].ToUpperInvariant(), well.Center.X, well.Y + 11, 2, Mono.Ink);
            _font.DrawCentered(_sb, $"{sp.ApCost}", well.Center.X, well.Bottom - 15, 1, Mono.Dim);

            int tx = well.Right + 12;
            _font.Draw(_sb, sp.Name.ToUpperInvariant(), tx, row.Y + 8, 1, Mono.Ink);
            // Rank pips: filled squares for bought ranks, hollow for the rest.
            int px0 = tx + _font.Measure(sp.Name.ToUpperInvariant(), 1) + 12;
            for (int r2 = 0; r2 < maxR; r2++)
            {
                var pip = new Rectangle(px0 + r2 * 12, row.Y + 9, 8, 8);
                if (r2 < rank) _prim.FillRect(_sb, pip, Mono.Ink);
                else _prim.StrokeRect(_sb, pip, 1, Mono.Dim);
            }
            if (!classKeys.Contains(key))
                _font.Draw(_sb, "FROM ESSENCE", row.Right - 96 - _font.Measure("FROM ESSENCE", 1) - 12,
                    row.Y + 8, 1, Mono.Faint);

            string InfoOf(DofusSlice.Core.Spells.SpellDef d) =>
                string.Join("  ·  ", d.Effects.Select(e => EffectLine(e).text))
                + $"  ·  AP {d.ApCost}  ·  RANGE {d.MinRange}-{d.MaxRange}"
                + (d.Cooldown > 0 ? $"  ·  CD {d.Cooldown}" : "");
            _font.Draw(_sb, Trunc(InfoOf(sp), 52), tx, row.Y + 26, 1, Mono.Dim);
            _font.Draw(_sb, rank < maxR
                    ? Trunc($"NEXT: {InfoOf(TitheContent.SkillAtRank(key, rank + 1))}", 52)
                    : "MAX RANK",
                tx, row.Y + 42, 1, rank < maxR ? Mono.Faint : Mono.Dim);

            if (a.SpellPoints > 0 && rank < maxR)
            {
                var b = SpellRankUpR(i);
                bool hov = b.Contains(mp);
                Mono.Button(_sb, _prim, b, hover: hov);
                _font.DrawCentered(_sb, "RANK UP", b.Center.X, b.Y + 8, 1, Mono.ButtonInk(hov));
            }
        }

        _font.DrawCentered(_sb, "(S OR ESC TO CLOSE)", w.Center.X, w.Bottom - 22, 1, Mono.Dim);
        DrawPanelMsg(w);
    }

    private void ClickSpellPanel(Point m)
    {
        var a = _campaign.Avatar;
        if (a == null) return;
        if (PanelCloseRect(SpellWin).Contains(m)) { _spellOpen = false; _sfx.Play("click"); return; }
        var keys = TitheContent.UnitSkillKeys(a).ToList();
        for (int i = 0; i < keys.Count && i < 6; i++)
            if (SpellRankUpR(i).Contains(m) && TitheContent.RankUp(a, keys[i]))
            {
                PanelFeedback($"{TitheContent.UnitSkill(a, keys[i]).Name} ranked up");
                _sfx.Play("levelup", 0.55f, jitter: false);
                return;
            }
    }
}

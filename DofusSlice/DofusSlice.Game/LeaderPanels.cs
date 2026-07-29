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
    private bool _pickClass;            // the opening beat: choose who you are before the first dive
    private int _crewSel;               // which crew member the sheet / bag is acting on (0 = the leader)

    /// <summary>The unit the Leader's windows currently act on. Companions were unreachable —
    /// you could never gear them or spend what they had banked, so they read as rented NPCs.</summary>
    private CampaignUnit? Selected
    {
        get
        {
            var crew = _campaign.Crew;
            if (crew.Count == 0) return null;
            return crew[Math.Clamp(_crewSel, 0, crew.Count - 1)];
        }
    }

    /// <summary>Tabs along the top of a Leader window, one per crew member.</summary>
    private static Rectangle CrewTabRect(Rectangle w, int i) => new(w.X - 100, w.Y + 44 + i * 26, 92, 22);

    private void DrawCrewTabs(Rectangle w)
    {
        var crew = _campaign.Crew;
        if (crew.Count <= 1) return;
        var mp = new Point(_mouse.X, _mouse.Y);
        for (int i = 0; i < crew.Count; i++)
        {
            var r = CrewTabRect(w, i);
            bool sel = i == Math.Clamp(_crewSel, 0, crew.Count - 1);
            bool hov = r.Contains(mp);
            Mono.Button(_sb, _prim, r, hover: hov && !sel, disabled: false);
            if (sel) _prim.StrokeRoundRect(_sb, r, Mono.RadButton, 2, Mono.Ink);
            _font.DrawCentered(_sb, Trunc(crew[i].Name.ToUpperInvariant(), 10), r.Center.X, r.Y + 8, 1,
                sel ? Mono.Ink : Mono.ButtonInk(hov && !sel));
        }
        _font.Draw(_sb, "CREW", CrewTabRect(w, 0).X + 2, w.Y + 30, 1, Mono.Faint);
    }

    /// <summary>Returns true if a tab click was consumed.</summary>
    private bool ClickCrewTabs(Rectangle w, Point m)
    {
        var crew = _campaign.Crew;
        if (crew.Count <= 1) return false;
        for (int i = 0; i < crew.Count; i++)
            if (CrewTabRect(w, i).Contains(m)) { _crewSel = i; _sfx.Play("click"); return true; }
        return false;
    }
    private string? _panelMsg;          // one-line feedback ("ate bread", "essence learned")
    private float _panelMsgUntil;       // shown while _time is before this

    // Drag & drop between the stash and the doll (Pass 4). A press ARMS a drag; moving
    // past a small threshold makes it real; releasing without moving is a plain click.
    private string? _dragItem;
    private bool _dragFromDoll, _dragging;
    private bool _bagPress;             // the press began while the bag was open (else its release is not ours)
    private Point _dragStart;
    private Rectangle _dragSrcRect;     // where the drag began — a wobbled click released here still clicks

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
        _prim.FillRoundRect(_sb, r, Mono.RadPanel, Mono.Panel * 0.97f);
        _prim.StrokeRoundRect(_sb, r, Mono.RadPanel, 1, Mono.Ink);
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
        var a = Selected;
        if (a == null) return;
        var w = CharWin;
        var mp = new Point(_mouse.X, _mouse.Y);
        Mono.Frame(_sb, _prim, w, emphasis: true, fillAlpha: 0.985f);
        PanelTitle(w, "CHARACTERISTICS");
        DrawCrewTabs(w);

        int L = w.X + 28, W = w.Width - 56;
        var s = TitheContent.StatsOf(a);

        // Portrait box + identity block (demo: portrait at +106; we start under the band).
        var port = new Rectangle(L, w.Y + 46, 72, 72);
        Mono.Slot(_sb, _prim, port);
        var sheet = _sprites.GetSheet(PixActor(a.ClassId).sprite, "idle", "se");
        if (sheet != null)
            SpriteDraw.Feet(_sb, sheet, new Vector2(port.Center.X, port.Bottom - 6), Mono.Ink, 64f, 0);
        _font.Draw(_sb, $"{a.Name.ToUpperInvariant()} — " + (a.IsAvatar ? "LEADER"
            : a.Vetted ? a.Temperament.ToString().ToUpperInvariant() : "COMPANION"),
            L + 86, w.Y + 50, 1, Mono.Ink);
        _font.Draw(_sb, $"{a.ClassId.ToUpperInvariant()}   LVL {a.Level}", L + 86, w.Y + 66, 2, Mono.Ink);
        int need = CampaignUnit.XpForNextLevel(a.Level);
        _font.Draw(_sb, $"{a.Xp} / {need} XP", L + 86, w.Y + 92, 1, Mono.Dim);
        Mono.Bar(_sb, _prim, new Rectangle(L + 86, w.Y + 106, W - 86, 10),
            Math.Clamp(need <= 0 ? 1f : (float)a.Xp / need, 0f, 1f), Mono.Dim);

        // PV / PA / PM rows — the demo's pill rows, in ink.
        var (bAp, bMp) = TitheContent.ClassApMp(a.ClassId);
        int cur = Math.Clamp(a.CurrentHp ?? s.MaxHp, 0, s.MaxHp);
        DrawSheetRow(new Rectangle(L, w.Y + 132, W, 26), "HP", $"{cur} / {s.MaxHp}", mp, "icon_stat_vit", Mono.Hp);
        DrawSheetRow(new Rectangle(L, w.Y + 162, W, 26), "ACTION POINTS (AP)", $"{bAp + s.ApBonus}", mp, "icon_stat_ap", Mono.Ap);
        DrawSheetRow(new Rectangle(L, w.Y + 192, W, 26), "MOVEMENT POINTS (MP)", $"{bMp + s.MpBonus}", mp, "icon_stat_mp", Mono.Mp);
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
            DrawSheetRow(row, StatLabel(key, label), $"{shown}", mp, $"icon_stat_{key}",
                key == "vit" ? Mono.Hp : null);
            if (a.StatPoints > 0)
            {
                int cost = a.StatStep(key);
                bool afford = a.CanSpendStat(key);
                var b = CharPlusRect(i);
                bool hov = afford && b.Contains(mp);
                Mono.Button(_sb, _prim, b, hover: hov, disabled: !afford);
                _font.DrawCentered(_sb, "+", b.Center.X, b.Y + 6, 1, Mono.ButtonInk(hov, !afford));
                // The tiered cost shows only once it climbs past 1 — the moment specialising bites.
                if (cost > 1)
                    _font.Draw(_sb, $"{cost}", b.X - 11, b.Y + 6, 1, afford ? Mono.Dim : Mono.Faint);
            }
        }

        // Points banked, then the effective damage line the fights actually use.
        // (AUTO-SPEND is gone — Pass 3: the leader's points are spent by hand, always.)
        _font.Draw(_sb, a.StatPoints > 0
                ? $"POINTS: {a.StatPoints}  ·  COST RISES AS YOU FOCUS ONE STAT"
                : "NO POINTS BANKED",
            L, w.Y + 432, 1, a.StatPoints > 0 ? Mono.Ink : Mono.Dim);
        var elem = TitheContent.ClassElement(a.ClassId);
        _font.Draw(_sb, $"{elem.ToString().ToUpperInvariant()} POWER {TitheContent.DamageStatFor(s, elem)}"
            + $"   ·   INITIATIVE {s.Initiative}", L, w.Y + 458, 1, Mono.Dim);

        // What the numbers actually DO. The sheet listed six characteristics and explained none of
        // them, so there was no way to tell which one was your damage — or that Wisdom and Agility
        // do anything at all. The hovered row explains itself; otherwise name your damage stat.
        string lesson = StatRows.FirstOrDefault(r => CharRowRect(Array.IndexOf(StatRows, r)).Contains(mp)) is
            { key: { } hk } ? StatLesson(hk, elem)
            : $"YOUR DAMAGE COMES FROM {DamageStatName(elem)} — HOVER A ROW TO SEE WHAT IT DOES";
        _font.Draw(_sb, Trunc(lesson, 54), L, w.Y + 494, 1, Mono.Faint);

        // The class passive is a real combat rule and the word never appeared anywhere in the game.
        if (TitheContent.ClassPassive(a.ClassId) is { Length: > 0 } passive)
            _font.Draw(_sb, Trunc(passive.ToUpperInvariant(), 54), L, w.Y + 508, 1, Mono.Dim);

        int set = TitheContent.SetPiecesEquipped(a, TitheContent.GraveyardSet);
        int bone = TitheContent.SetPiecesEquipped(a, TitheContent.CryptSet);
        _font.Draw(_sb, $"ADVENTURER SET: {set}/7" + (bone > 0 ? $"   ·   BONEWROUGHT: {bone}/5" : " PIECES"),
            L, w.Y + 476, 1, set + bone > 0 ? Mono.Ink : Mono.Faint);

        _font.DrawCentered(_sb, "(C OR ESC TO CLOSE)", w.Center.X, w.Bottom - 22, 1, Mono.Dim);
        DrawPanelMsg(w);
    }

    private static string StatLabel(string key, string label) => key switch
    {
        "vit" => "VITALITY", "str" => "STRENGTH", "int" => "INTELLIGENCE",
        "cha" => "CHANCE", "agi" => "AGILITY", _ => "WISDOM",
    };

    private void DrawSheetRow(Rectangle r, string label, string value, Point mp, string? icon = null,
        Color? iconTint = null)
    {
        _prim.FillRoundRect(_sb, r, Mono.RadPanel, Mono.Panel);
        _prim.StrokeRoundRect(_sb, r, Mono.RadPanel, 1, Mono.Faint);
        int lx = r.X + 10;
        if (icon != null && DrawIconRect(icon, new Rectangle(r.X + 8, r.Center.Y - 8, 16, 16), iconTint, pad: 0))
            lx = r.X + 30;
        _font.Draw(_sb, label, lx, r.Y + 9, 1, Mono.Ink);
        _font.Draw(_sb, value, r.Right - 16 - _font.Measure(value, 1), r.Y + 9, 1, Mono.Ink);
    }

    private static Rectangle CharPlusRect(int i) => new(CharWin.X + 28 + (CharWin.Width - 56) - 24,
        CharWin.Y + 240 + i * 30 + 3, 22, 20);

    /// <summary>The clickable/hoverable row for characteristic <paramref name="i"/>.</summary>
    private static Rectangle CharRowRect(int i) =>
        new(CharWin.X + 28, CharWin.Y + 240 + i * 30, CharWin.Width - 56 - 32, 26);

    /// <summary>The stat that scales damage for an element — named, so the sheet can say it.</summary>
    private static string DamageStatName(DofusSlice.Core.Combat.Element e) => e switch
    {
        DofusSlice.Core.Combat.Element.Fire => "INTELLIGENCE",
        DofusSlice.Core.Combat.Element.Water => "CHANCE",
        DofusSlice.Core.Combat.Element.Air => "AGILITY",
        _ => "STRENGTH",
    };

    /// <summary>What one point of a characteristic actually buys, in the player's terms.</summary>
    private static string StatLesson(string key, DofusSlice.Core.Combat.Element elem) => key switch
    {
        "vit" => "VITALITY: +1 MAX HP PER POINT. NEVER COSTS MORE THAN 1.",
        "wis" => "WISDOM: MORE XP, AND DODGES AP/MP THEFT. COSTS ONE EXTRA.",
        "agi" => elem == DofusSlice.Core.Combat.Element.Air
            ? "AGILITY: YOUR AIR DAMAGE — AND THE MP IT COSTS FOES TO LEAVE YOU."
            : "AGILITY: AIR DAMAGE, AND THE MP IT COSTS FOES TO LEAVE YOUR MELEE.",
        "str" => elem == DofusSlice.Core.Combat.Element.Earth
            ? "STRENGTH: YOUR EARTH DAMAGE." : "STRENGTH: SCALES EARTH DAMAGE.",
        "int" => elem == DofusSlice.Core.Combat.Element.Fire
            ? "INTELLIGENCE: YOUR FIRE DAMAGE — AND HOW HARD YOU HEAL."
            : "INTELLIGENCE: SCALES FIRE DAMAGE AND HEALING.",
        _ => elem == DofusSlice.Core.Combat.Element.Water
            ? "CHANCE: YOUR WATER DAMAGE." : "CHANCE: SCALES WATER DAMAGE.",
    };

    private void ClickCharacterWindow(Point m)
    {
        var a = Selected;
        if (a == null) return;
        if (PanelCloseRect(CharWin).Contains(m)) { _charOpen = false; _sfx.Play("click"); return; }
        if (ClickCrewTabs(CharWin, m)) return;
        if (a.StatPoints > 0)
            for (int i = 0; i < StatRows.Length; i++)
                if (CharPlusRect(i).Contains(m)) { if (a.SpendStat(StatRows[i].key)) _sfx.Play("click"); return; }
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
        var a = Selected;
        if (a == null) return;
        var w = InvWin;
        var mp = new Point(_mouse.X, _mouse.Y);
        Mono.Frame(_sb, _prim, w, emphasis: true, fillAlpha: 0.985f);
        PanelTitle(w, "INVENTORY");
        DrawCrewTabs(w);

        // The equipment doll — your character standing among their slots (demo geometry).
        var doll = DollRect;
        _prim.FillRoundRect(_sb, doll, Mono.RadPanel, new Color(10, 10, 10));
        _prim.StrokeRoundRect(_sb, doll, Mono.RadPanel, 1, Mono.Faint);
        string? hoverCardTitle = null, hoverCard1 = null, hoverCard2 = null;

        var bySlot = a.Equipment.ToDictionary(TitheContent.ItemSlot, id => id);
        foreach (var d in DollSlots)
        {
            var r = DollSlotRect(d);
            bool hov = r.Contains(mp);
            bySlot.TryGetValue(d.slot, out string? worn);
            Mono.Slot(_sb, _prim, r, hover: hov, selected: false);
            string slotIcon = "icon_slot_" + d.slot;
            if (worn != null)
            {
                // Worn: the slot glyph in ink with the piece's name outlined at its feet.
                if (DrawIconRect(slotIcon, new Rectangle(r.X, r.Y, r.Width, r.Height - 12), Mono.Ink, pad: 2))
                    OutlinedCentered(Trunc(TitheContent.ItemName(worn).ToUpperInvariant(), 7),
                        r.Center.X, r.Bottom - 13, 1, Mono.Ink);
                else
                {
                    _font.DrawCentered(_sb, Trunc(TitheContent.ItemName(worn).ToUpperInvariant(), 7),
                        r.Center.X, r.Y + 14, 1, Mono.Ink);
                    _font.DrawCentered(_sb, d.slot.ToUpperInvariant(), r.Center.X, r.Bottom - 16, 1, Mono.Faint);
                }
                if (hov)
                {
                    hoverCardTitle = TitheContent.ItemName(worn).ToUpperInvariant();
                    hoverCard1 = TitheContent.ItemStatLine(worn);
                    hoverCard2 = "click to unequip";
                }
            }
            // Empty: the slot's ghost glyph, faint — Dofus's empty-doll language.
            else if (!DrawIconRect(slotIcon, r, Mono.Faint))
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
                if (DrawIconRect(i == 0 ? "icon_ui_bread" : "icon_ui_draught", r, inkc))
                { if (count > 0) OutlinedCentered($"x{count}", r.Right - 10, r.Bottom - 13, 1, Mono.Ink); }
                else
                {
                    _font.DrawCentered(_sb, label, r.Center.X, r.Y + 8, 1, inkc);
                    if (count > 0) _font.Draw(_sb, $"x{count}", r.X + 3, r.Y + 31, 1, Mono.Ink);
                }
                if (hov && count > 0) { hoverCardTitle = label; hoverCard1 = tip; hoverCard2 = null; }
            }
            else if (i - 2 < _campaign.Essences.Count)
            {
                string ess = _campaign.Essences[i - 2];
                if (DrawIconRect("icon_ui_essence", new Rectangle(r.X, r.Y, r.Width, r.Height - 10), Mono.Ink, pad: 2))
                    OutlinedCentered(Trunc(ess.ToUpperInvariant(), 6), r.Center.X, r.Bottom - 14, 1, Mono.Dim);
                else
                {
                    _font.DrawCentered(_sb, "ESS", r.Center.X, r.Y + 8, 1, Mono.Ink);
                    _font.DrawCentered(_sb, Trunc(ess.ToUpperInvariant(), 6), r.Center.X, r.Bottom - 16, 1, Mono.Dim);
                }
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
                // The slot's glyph carries the cell; the name rides outlined along the bottom
                // (the hover card still tells the whole story).
                if (DrawIconRect("icon_slot_" + TitheContent.ItemSlot(id), cell, Mono.Ink))
                    OutlinedCentered(Trunc(TitheContent.ItemName(id).ToUpperInvariant(), 5),
                        cell.Center.X, cell.Bottom - 13, 1, Mono.Ink);
                else
                {
                    _font.DrawCentered(_sb, Trunc(TitheContent.ItemName(id).ToUpperInvariant(), 5),
                        cell.Center.X, cell.Y + 9, 1, Mono.Ink);
                    _font.DrawCentered(_sb, Trunc(TitheContent.ItemSlot(id).ToUpperInvariant(), 5),
                        cell.Center.X, cell.Bottom - 14, 1, Mono.Faint);
                }
                if (hov)
                {
                    hoverCardTitle = TitheContent.ItemName(id).ToUpperInvariant();
                    hoverCard1 = TitheContent.ItemStatLine(id);
                    hoverCard2 = "click to equip";
                }
            }
        }

        // The Tithe-Keeper's shadow lives in the bag now (the HUD stays clean).
        int per = TitheContent.Prices.TitheEveryNDives;
        _font.Draw(_sb, _campaign.TitheDue
                ? $"TITHE DUE: {_campaign.TitheAmount} ST — THE KEEPER WAITS"
                : $"TITHE IN {per - (_campaign.Dives % per)} DIVE(S)",
            w.X + 34, w.Y + 424, 1, _campaign.TitheDue ? Mono.Danger : Mono.Dim);

        // The kamas row, ours: the pack's coin (or the old ink disc), the number, the set progress.
        if (!DrawIconRect("icon_ui_stone", new Rectangle(w.X + 382, w.Y + 512, 32, 32), pad: 0))
        {
            _prim.DiscAt(_sb, new Vector2(w.X + 398, w.Y + 528), 7, Mono.Ink);
            _prim.DiscAt(_sb, new Vector2(w.X + 398, w.Y + 528), 5, Mono.Panel);
            _prim.DiscAt(_sb, new Vector2(w.X + 398, w.Y + 528), 3, Mono.Ink);
        }
        _font.Draw(_sb, $"{_campaign.Stones} ESSENCE STONES", w.X + 414, w.Y + 521, 2, Mono.Ink);
        int adv = TitheContent.SetPiecesEquipped(a, TitheContent.GraveyardSet);
        int bone = TitheContent.SetPiecesEquipped(a, TitheContent.CryptSet);
        _font.Draw(_sb, $"ADVENTURER {adv}/7" + (bone > 0 ? $"  ·  BONEWROUGHT {bone}/5" : ""),
            w.X + 414, w.Y + 545, 1, adv + bone > 0 ? Mono.Ink : Mono.Faint);
        // The panoply ladder follows the set you're actually building (more pieces wins):
        // the tier you HOLD in ink, the next rung as the goal line.
        string ladderSet = bone > adv ? TitheContent.CryptSet : TitheContent.GraveyardSet;
        int ladderN = Math.Max(adv, bone);
        var tiers = TitheContent.SetTierSummaries(ladderSet).ToList();
        var held = tiers.LastOrDefault(t => t.Pieces <= ladderN);
        var goal = tiers.FirstOrDefault(t => t.Pieces > ladderN);
        int sy = w.Y + 559;
        if (held.Pieces > 0)
        { _font.Draw(_sb, Trunc($"{held.Pieces} PC: {held.Text}", 40), w.X + 414, sy, 1, Mono.Ink); sy += 12; }
        if (goal.Pieces > 0)
            _font.Draw(_sb, Trunc($"AT {goal.Pieces} PC: {goal.Text}", 40), w.X + 414, sy, 1, Mono.Faint);

        _font.DrawCentered(_sb, "(I OR ESC TO CLOSE)", w.Center.X, w.Bottom - 22, 1, Mono.Dim);
        DrawPanelMsg(w);
        if (hoverCardTitle != null && !_dragging) DrawItemCard(hoverCardTitle, hoverCard1 ?? "", hoverCard2, mp);

        // The piece in flight rides the cursor; its landing zone glows walk-green.
        if (_dragging && _dragItem != null)
        {
            string dSlot = TitheContent.ItemSlot(_dragItem);
            if (!_dragFromDoll)
                foreach (var d in DollSlots)
                { if (d.slot == dSlot) _prim.StrokeRoundRect(_sb, DollSlotRect(d), Mono.RadSlot, 2, Mono.Walk); }
            else
                _prim.StrokeRoundRect(_sb, new Rectangle(w.X + 390, w.Y + 62, 5 * 45, 8 * 45),
                    Mono.RadPanel, 2, Mono.Walk);
            var at = new Rectangle(mp.X - 18, mp.Y - 18, 36, 36);
            if (!DrawIconRect("icon_slot_" + dSlot, at, Mono.Ink, pad: 0))
                _prim.StrokeRoundRect(_sb, at, Mono.RadSlot, 1, Mono.Ink);
            OutlinedCentered(Trunc(TitheContent.ItemName(_dragItem).ToUpperInvariant(), 9), mp.X, mp.Y + 22, 1, Mono.Ink);
        }
    }

    private void DrawPanelMsg(Rectangle w)
    {
        if (_time >= _panelMsgUntil || _panelMsg == null) return;
        _font.DrawCentered(_sb, _panelMsg.ToUpperInvariant(), w.Center.X, w.Bottom - 40, 1, Mono.Ink);
    }

    /// <summary>The bag's input, Pass 4: press arms a drag from a stash cell or a worn doll
    /// slot, movement makes it real, release drops (stash -> doll equips, doll -> stash
    /// unequips). A release that never travelled is the old click, unchanged.</summary>
    private void UpdateInventoryDrag()
    {
        var a = Selected;
        var m = new Point(_mouse.X, _mouse.Y);
        bool released = _mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;

        if (LeftClicked() && a != null)
        {
            _bagPress = true;
            _dragStart = m; _dragItem = null; _dragFromDoll = false; _dragging = false;
            for (int idx = 0; idx < _campaign.Stash.Count && idx < 40; idx++)
                if (GridRect(idx).Contains(m)) { _dragItem = _campaign.Stash[idx]; _dragSrcRect = GridRect(idx); break; }
            if (_dragItem == null)
            {
                var bySlot = a.Equipment.ToDictionary(TitheContent.ItemSlot, id => id);
                foreach (var d in DollSlots)
                    if (DollSlotRect(d).Contains(m) && bySlot.TryGetValue(d.slot, out string? worn))
                    { _dragItem = worn; _dragFromDoll = true; _dragSrcRect = DollSlotRect(d); break; }
            }
        }
        if (_mouse.LeftButton == ButtonState.Pressed && _dragItem != null && !_dragging
            && Math.Abs(m.X - _dragStart.X) + Math.Abs(m.Y - _dragStart.Y) > 10)
            _dragging = true;

        if (!released) return;
        if (!_bagPress) return;          // the press opened the bag elsewhere — not our click
        _bagPress = false;
        var grid = new Rectangle(InvWin.X + 390, InvWin.Y + 62, 5 * 45, 8 * 45);
        if (_dragging && _dragItem != null && a != null)
        {
            if (!_dragFromDoll && DollRect.Contains(m)) { _campaign.Equip(a, _dragItem); _sfx.Play("coin"); }
            else if (_dragFromDoll && grid.Contains(m)) { _campaign.Unequip(a, _dragItem); _sfx.Play("click"); }
            // A "drag" that never left its own slot was a wobbly CLICK — honor it as one.
            else if (_dragSrcRect.Contains(m)) ClickInventoryWindow(m);
        }
        else if ((_scene == Scene.City || _scene == Scene.Graveyard) && ClickMenuButtons(m)) { }
        else ClickInventoryWindow(m);   // a plain click keeps every old behavior
        _dragging = false; _dragItem = null;
    }

    private void ClickInventoryWindow(Point m)
    {
        var a = Selected;
        if (a == null) return;
        if (PanelCloseRect(InvWin).Contains(m)) { _invOpen = false; _sfx.Play("click"); return; }
        if (ClickCrewTabs(InvWin, m)) return;

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
        if (Pressed(Keys.H)) { _helpOpen = !_helpOpen; _charOpen = _invOpen = _spellOpen = false; _openNpc = -1; return true; }
        if (Pressed(Keys.C)) { _charOpen = !_charOpen; _invOpen = _spellOpen = _helpOpen = false; _openNpc = -1; return true; }
        if (Pressed(Keys.I) || Pressed(Keys.E)) { _invOpen = !_invOpen; _charOpen = _spellOpen = _helpOpen = false; _openNpc = -1; return true; }
        if (Pressed(Keys.S)) { _spellOpen = !_spellOpen; _charOpen = _invOpen = _helpOpen = false; _openNpc = -1; return true; }
        if (!_charOpen && !_invOpen && !_spellOpen && !_helpOpen) return false;
        if (Pressed(Keys.Escape)) { _charOpen = _invOpen = _spellOpen = _helpOpen = false; return true; }
        if (_helpOpen)
        {
            // The card is read-only: any click on its X (or H/Esc) dismisses it.
            if (LeftClicked() && PanelCloseRect(new Rectangle(ScreenW / 2 - 320, 120, 640, 400))
                .Contains(new Point(_mouse.X, _mouse.Y))) _helpOpen = false;
            return true;
        }
        if (_invOpen) { UpdateInventoryDrag(); return true; }   // the bag speaks press/drag/release
        if (LeftClicked())
        {
            var m = new Point(_mouse.X, _mouse.Y);
            // The corner menu keeps working while a window is open (click = toggle shut).
            if ((_scene == Scene.City || _scene == Scene.Graveyard) && ClickMenuButtons(m)) return true;
            if (_charOpen) ClickCharacterWindow(m);
            else ClickSpellPanel(m);
        }
        return true;
    }

    // ----- THE OPENING CHOICE: which leader do you play? --------------------------------
    // The avatar is the ONLY hand-piloted unit, so this is the single biggest decision in a
    // campaign — and it used to be hardcoded to the Cannon, meaning the Archer and the Bulwark
    // could never be played at all. Element, AP, range band, passive and the whole spell ladder
    // change with it.

    private static readonly string[] PickIds = TitheContent.ClassIds.ToArray();

    private static Rectangle PickCardRect(int i) =>
        new(ScreenW / 2 - 495 + i * 335, 140, 310, 470);

    /// <summary>A level-1 preview of a class: the numbers you are choosing between.</summary>
    private static (int hp, int ap, int mp, string elem, string[] spells) PickPreview(string classId)
    {
        var probe = new CampaignUnit { Id = "probe", ClassId = classId, Name = "probe", IsAvatar = true };
        var s = TitheContent.StatsOf(probe);
        var (ap, mp) = TitheContent.ClassApMp(classId);
        return (s.MaxHp, ap + s.ApBonus, mp + s.MpBonus,
            TitheContent.ClassElement(classId).ToString().ToUpperInvariant(),
            TitheContent.ClassSkillsAt(classId, 99).Select(k => TitheContent.SkillAtRank(k, 1).Name).ToArray());
    }

    private void DrawClassPicker()
    {
        _prim.FillRect(_sb, new Rectangle(0, 0, ScreenW, ScreenH), new Color(6, 6, 6, 246));
        _font.DrawCentered(_sb, "WHO DO YOU BECOME?", ScreenW / 2, 62, 4, Mono.Ink);
        _font.DrawCentered(_sb, "YOU LEAD BY HAND — COMPANIONS FIGHT THEMSELVES. THIS IS THE ONE YOU PLAY.",
            ScreenW / 2, 108, 1, Mono.Dim);

        var mp_ = new Point(_mouse.X, _mouse.Y);
        for (int i = 0; i < PickIds.Length; i++)
        {
            string id = PickIds[i];
            var r = PickCardRect(i);
            bool hov = r.Contains(mp_);
            Mono.Frame(_sb, _prim, r, emphasis: hov, fillAlpha: 0.98f);

            var (hp, ap, mp, elem, spells) = PickPreview(id);
            var ink = Mono.Element(TitheContent.ClassElement(id));

            // The fighter themself, so you pick a body and not a word.
            var sheet = _sprites.GetSheet(PixActor(id).sprite, "idle", "se");
            if (sheet != null)
                SpriteDraw.Feet(_sb, sheet, new Vector2(r.Center.X, r.Y + 132), Mono.Ink, 104f, 0);

            _font.DrawCentered(_sb, id.ToUpperInvariant(), r.Center.X, r.Y + 146, 3, hov ? Mono.Ink : ink);
            _font.DrawCentered(_sb, $"{elem}   ·   {hp} HP   ·   {ap} AP   ·   {mp} MP",
                r.Center.X, r.Y + 178, 1, Mono.Dim);

            // The blurb, wrapped by hand into the card's width.
            int y = r.Y + 202;
            foreach (var line in WrapWords(TitheContent.Blurb(id), 38, maxLines: 5))
            { _font.DrawCentered(_sb, line.ToUpperInvariant(), r.Center.X, y, 1, Mono.Dim); y += 13; }

            y += 8;
            _font.DrawCentered(_sb, "THE LADDER", r.Center.X, y, 1, Mono.Faint); y += 15;
            for (int k = 0; k < spells.Length && k < 4; k++)
            { _font.DrawCentered(_sb, $"L{k + 1}  {spells[k].ToUpperInvariant()}", r.Center.X, y, 1, Mono.Ink); y += 14; }

            var btn = new Rectangle(r.X + 40, r.Bottom - 42, r.Width - 80, 26);
            Mono.Button(_sb, _prim, btn, hover: hov);
            _font.DrawCentered(_sb, $"CHOOSE  ({i + 1})", btn.Center.X, btn.Y + 9, 1, Mono.ButtonInk(hov));
        }
        _font.DrawCentered(_sb, "CLICK A CARD OR PRESS 1 / 2 / 3", ScreenW / 2, ScreenH - 44, 1, Mono.Dim);
    }

    /// <summary>Break a sentence into at most <paramref name="maxLines"/> lines of at most
    /// <paramref name="max"/> characters (the card has a fixed budget; the rest is dropped).</summary>
    private static List<string> WrapWords(string text, int max, int maxLines)
    {
        var lines = new List<string>();
        var cur = "";
        foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (cur.Length > 0 && cur.Length + 1 + w.Length > max)
            {
                lines.Add(cur); cur = w;
                if (lines.Count == maxLines) return lines;
            }
            else cur = cur.Length == 0 ? w : cur + " " + w;
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }

    // ----- H: the help card. There was no tutorial, no title screen and no key list anywhere
    // outside a single in-fight line, so every control in the game was folklore.

    private bool _helpOpen;

    private static readonly (string keys, string what)[] HelpRows =
    {
        ("C", "characteristics — spend banked points (cost rises as you focus one stat)"),
        ("S", "spells — rank up with spell points"),
        ("I / E", "inventory — drag gear onto the doll, eat bread, learn essences"),
        ("1-6", "cast a spell on your turn (speed keys 1/2/3 while WATCHING)"),
        ("CLICK", "move to a lit cell, or aim the armed spell at a target"),
        ("ESC", "cancel the armed spell, or close a window"),
        ("SPACE", "end your turn — or start the fight during placement"),
        ("M", "mute; F9 the HUD editor; F10 the UI demo"),
    };

    private void DrawHelpCard()
    {
        var w = new Rectangle(ScreenW / 2 - 320, 120, 640, 400);
        Mono.Frame(_sb, _prim, w, emphasis: true, fillAlpha: 0.985f);
        PanelTitle(w, "HOW TO PLAY");
        _font.Draw(_sb, "YOU PILOT THE LEADER BY HAND. COMPANIONS FIGHT THEMSELVES.",
            w.X + 28, w.Y + 44, 1, Mono.Dim);
        _font.Draw(_sb, "THE BELL RUNS THE WHOLE DIVE — IT NEVER PAUSES FOR A FIGHT.",
            w.X + 28, w.Y + 60, 1, Mono.Dim);
        int y = w.Y + 92;
        foreach (var (keys, what) in HelpRows)
        {
            _font.Draw(_sb, keys, w.X + 28, y, 1, Mono.Ink);
            _font.Draw(_sb, Trunc(what.ToUpperInvariant(), 78), w.X + 120, y, 1, Mono.Dim);
            y += 20;
        }
        _font.Draw(_sb, "AP PAYS FOR SPELLS. MP PAYS FOR STEPS. LEAVING AN ENEMY'S", w.X + 28, y + 12, 1, Mono.Faint);
        _font.Draw(_sb, "MELEE COSTS EXTRA MP — THEIR AGILITY AGAINST YOURS.", w.X + 28, y + 26, 1, Mono.Faint);
        _font.DrawCentered(_sb, "(H OR ESC TO CLOSE)", w.Center.X, w.Bottom - 22, 1, Mono.Dim);
    }

    /// <summary>Returns true while the opening choice owns the screen.</summary>
    private bool UpdateClassPicker()
    {
        if (!_pickClass) return false;
        int picked = -1;
        if (Pressed(Keys.D1)) picked = 0;
        if (Pressed(Keys.D2)) picked = 1;
        if (Pressed(Keys.D3)) picked = 2;
        if (LeftClicked())
        {
            var m = new Point(_mouse.X, _mouse.Y);
            for (int i = 0; i < PickIds.Length; i++)
                if (PickCardRect(i).Contains(m)) picked = i;
        }
        if (picked >= 0 && picked < PickIds.Length)
        {
            // Rebuild the campaign around the chosen leader — nothing has happened yet, so a
            // clean NewGame is exactly right (ClassId is init-only on the unit).
            _campaign = Campaign.NewGame(PickIds[picked]);
            _pickClass = false;
            _sfx.Play("levelup", 0.7f, jitter: false);
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
            _prim.FillRoundRect(_sb, row, Mono.RadSlot, Mono.Panel);
            _prim.StrokeRoundRect(_sb, row, Mono.RadSlot, 1, Mono.Faint);

            // The spell's glyph well, like the combat bar's slots (letter when not baked).
            var well = new Rectangle(row.X + 8, row.Y + 10, 46, 46);
            Mono.Slot(_sb, _prim, well);
            if (DrawSpellIcon(sp, well, SpellInk(sp)))
                OutlinedCentered($"{sp.ApCost}", well.Right - 9, well.Bottom - 14, 1, Mono.Ink);
            else
            {
                _font.DrawCentered(_sb, sp.Name[..1].ToUpperInvariant(), well.Center.X, well.Y + 11, 2, Mono.Ink);
                _font.DrawCentered(_sb, $"{sp.ApCost}", well.Center.X, well.Bottom - 15, 1, Mono.Dim);
            }

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
                    ? Trunc($"RANK {rank + 1}: {RankDelta(sp, TitheContent.SkillAtRank(key, rank + 1))}", 52)
                    : "MAX RANK",
                tx, row.Y + 42, 1, rank < maxR ? Mono.Cast : Mono.Dim);

            if (a.SpellPoints > 0 && rank < maxR)
            {
                var b = SpellRankUpR(i);
                bool hov = b.Contains(mp);
                Mono.Button(_sb, _prim, b, hover: hov);
                _font.DrawCentered(_sb, "RANK UP", b.Center.X, b.Y + 8, 1, Mono.ButtonInk(hov));
            }
        }

        var known = new HashSet<string>(keys);
        var locked = TitheContent.ClassSkillsAt(a.ClassId, 99)
            .Select((k, idx) => (key: k, level: idx + 1))
            .Where(x => !known.Contains(x.key))
            .ToList();
        if (locked.Count > 0)
        {
            int ly = Math.Min(w.Y + 66 + Math.Min(keys.Count, 6) * 74 + 6, w.Bottom - 78);
            _font.Draw(_sb, "STILL TO COME", w.X + 26, ly, 1, Mono.Faint);
            for (int i = 0; i < locked.Count && i < 3; i++)
            {
                var sp2 = TitheContent.SkillAtRank(locked[i].key, 1);
                _font.Draw(_sb, Trunc($"LVL {locked[i].level}  {sp2.Name.ToUpperInvariant()}"
                        + $"  ·  AP {sp2.ApCost}  ·  RANGE {sp2.MinRange}-{sp2.MaxRange}", 52),
                    w.X + 26, ly + 14 + i * 13, 1, Mono.Dim);
            }
        }

        _font.DrawCentered(_sb, "(S OR ESC TO CLOSE)", w.Center.X, w.Bottom - 22, 1, Mono.Dim);
        DrawPanelMsg(w);
    }

    /// <summary>What one spell point actually buys, as a BEFORE -> AFTER diff. Only the fields that
    /// change are listed, so the gain is readable at a glance instead of two lines to compare.</summary>
    private static string RankDelta(DofusSlice.Core.Spells.SpellDef now, DofusSlice.Core.Spells.SpellDef next)
    {
        var parts = new List<string>();
        if (next.ApCost != now.ApCost) parts.Add($"AP {now.ApCost}>{next.ApCost}");
        if (next.MaxRange != now.MaxRange || next.MinRange != now.MinRange)
            parts.Add($"RANGE {now.MinRange}-{now.MaxRange}>{next.MinRange}-{next.MaxRange}");
        if (next.Cooldown != now.Cooldown) parts.Add($"CD {now.Cooldown}>{next.Cooldown}");
        if (next.CriticalChanceOneIn != now.CriticalChanceOneIn && next.CriticalChanceOneIn > 0)
            parts.Add($"CRIT 1/{now.CriticalChanceOneIn}>1/{next.CriticalChanceOneIn}");
        // Damage/heal band, matched by effect order so a rider's numbers are compared to its own.
        for (int i = 0; i < now.Effects.Count && i < next.Effects.Count; i++)
        {
            var a2 = now.Effects[i]; var b = next.Effects[i];
            if (a2.Kind != b.Kind) continue;
            if (a2.Kind is not (DofusSlice.Core.Spells.EffectKind.Damage
                or DofusSlice.Core.Spells.EffectKind.Heal
                or DofusSlice.Core.Spells.EffectKind.Lifesteal)) continue;
            if (a2.Min != b.Min || a2.Max != b.Max) parts.Add($"{a2.Min}-{a2.Max}>{b.Min}-{b.Max}");
        }
        return parts.Count == 0 ? "no change" : string.Join("  ·  ", parts);
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

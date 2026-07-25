using DofusSlice.Core.Content.Tithe;
using DofusSlice.Game.Rendering;
using Microsoft.Xna.Framework;

namespace DofusSlice.Game;

/// <summary>
/// PASS 2 — ONE BAR, EVERYWHERE: outside combat the city and graveyard wear the SAME
/// bottom band as the fight (heart/AP/MP vitals, the well grid, TEAM on the right) —
/// but the wells hold QUICK-ACCESS items: bread you can eat on the spot, draughts for
/// the wounded, the essences you carry. Healing updates the heart live and floats a
/// green +N. The Dofus corner menu (character / spells / bag) replaces the hint text.
/// </summary>
public partial class SliceGame
{
    // Short-lived screen-space floats over the band ("+15" when bread lands).
    private readonly List<(string text, Color color, Vector2 pos, float born)> _bandFloats = new();
    // World-space floats (the graveyard party token eating bread).
    private readonly List<(string text, Color color, Vector2 pos, float born)> _worldFloats = new();

    private void SpawnBandFloat(string text, Color color, Vector2 at) =>
        _bandFloats.Add((text, color, at, _time));

    private void SpawnWorldFloat(string text, Color color, Vector2 at) =>
        _worldFloats.Add((text, color, at, _time));

    private void DrawFloatList(List<(string text, Color color, Vector2 pos, float born)> list)
    {
        const float Life = 1.4f;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var (t, c, p, born) = list[i];
            float age = _time - born;
            if (age >= Life) { list.RemoveAt(i); continue; }
            OutlinedCentered(t, (int)p.X, (int)(p.Y - age * 24f), 2, c * (1f - age / Life));
        }
    }

    // ----- The Dofus corner menu: three icon buttons where the hint text used to be -----

    private static Rectangle MenuBtnRect(int i) => new(1152 + i * 40, ScreenH - 46, 34, 34);

    private void DrawMenuButtons()
    {
        var defs = new[]
        {
            ("icon_ui_char", "C", _charOpen), ("icon_ui_book", "S", _spellOpen), ("icon_ui_bag", "I", _invOpen),
        };
        var mp = new Point(_mouse.X, _mouse.Y);
        for (int i = 0; i < defs.Length; i++)
        {
            var (icon, letter, open) = defs[i];
            var r = MenuBtnRect(i);
            bool hov = r.Contains(mp);
            Mono.Slot(_sb, _prim, r, hover: hov, selected: open);
            if (!DrawIconRect(icon, r, open || hov ? Mono.Ink : Mono.Dim))
                _font.DrawCentered(_sb, letter, r.Center.X, r.Y + 13, 1, Mono.Ink);
        }
    }

    private bool ClickMenuButtons(Point m)
    {
        if (MenuBtnRect(0).Contains(m)) { _charOpen = !_charOpen; _invOpen = _spellOpen = false; _openNpc = -1; _sfx.Play("click"); return true; }
        if (MenuBtnRect(1).Contains(m)) { _spellOpen = !_spellOpen; _charOpen = _invOpen = false; _openNpc = -1; _sfx.Play("click"); return true; }
        if (MenuBtnRect(2).Contains(m)) { _invOpen = !_invOpen; _charOpen = _spellOpen = false; _openNpc = -1; _sfx.Play("click"); return true; }
        return false;
    }

    // ----- The campaign band (the fight band's geometry, quick items in the wells) ------

    private void DrawCampaignBand()
    {
        _ew.Panel(_sb, new Rectangle(-6, HudTop + 2, ScreenW + 12, ScreenH - HudTop + 16));
        var a = _campaign.Avatar;
        if (a == null) return;
        var s = TitheContent.StatsOf(a);
        var kmp = new Point(_mouse.X, _mouse.Y);

        var plate = new Rectangle(350, HudTop + 6, 580, 106);
        _prim.FillRoundRect(_sb, plate, Mono.RadPanel, Mono.On ? new Color(14, 14, 14) : Ew.Surface);
        _prim.StrokeRoundRect(_sb, plate, Mono.RadPanel, 1, Mono.On ? Mono.Dim : Ew.Outline);
        _font.Draw(_sb, Trunc($"{a.Name.ToUpperInvariant()} — LEADER", 22), plate.X + 10, plate.Y + 5, 1, Ew.Ink);

        int hp = Math.Clamp(a.CurrentHp ?? s.MaxHp, 0, s.MaxHp);
        float hpFrac = s.MaxHp <= 0 ? 0f : (float)hp / s.MaxHp;
        var (bAp, bMp) = TitheContent.ClassApMp(a.ClassId);
        var heartC = new Vector2(452, HudTop + 58);
        var apC = new Vector2(392, HudTop + 68);
        var mpC = new Vector2(512, HudTop + 68);
        if (!DrawUiSpriteFilled("onebit_heart", heartC, 48, hpFrac, Mono.Hp))
            _ew.Badge(_sb, EwChrome.Gem.Heart, heartC, 56, Ew.Hp, Ew.HpDeep, hpFrac);
        if (!DrawUiSprite("onebit_star", apC, 38, Mono.Ap))
            _ew.Badge(_sb, EwChrome.Gem.Star, apC, 40, Ew.Ap, Ew.ApDeep);
        if (!DrawUiSprite("onebit_shield", mpC, 38, Mono.Mp))
            _ew.Badge(_sb, EwChrome.Gem.Diamond, mpC, 40, Ew.Mp, Ew.MpDeep);
        OutlinedCentered($"{hp}", (int)heartC.X, (int)heartC.Y - 8, 2, hpFrac > 0.25f ? Mono.Ink : Mono.Danger);
        OutlinedCentered($"{bAp + s.ApBonus}", (int)apC.X, (int)apC.Y - 6, 2, Mono.Ink);
        OutlinedCentered($"{bMp + s.MpBonus}", (int)mpC.X, (int)mpC.Y - 6, 2, Mono.Ink);
        _font.DrawCentered(_sb, "AP", (int)apC.X - 34, (int)apC.Y + 6, 1, Mono.On ? Mono.Ap : Ew.InkSoft);
        _font.DrawCentered(_sb, "MP", (int)mpC.X + 34, (int)mpC.Y + 6, 1, Mono.On ? Mono.Mp : Ew.InkSoft);

        // Quick-access wells: bread, draught, then the essences you carry.
        string? tipT = null, tip1 = null, tip2 = null;
        for (int i = 0; i < 14; i++)
        {
            var slotR = SpellGridRect(i);
            bool hov = slotR.Contains(kmp);
            Mono.Slot(_sb, _prim, slotR, hover: hov);
            if (i == 0)
            {
                var ink = _campaign.Bread > 0 ? Mono.Ink : Mono.Faint;
                if (!DrawIconRect("icon_ui_bread", slotR, ink))
                    _font.DrawCentered(_sb, "BR", slotR.Center.X, slotR.Y + 9, 2, ink);
                if (_campaign.Bread > 0)
                    OutlinedCentered($"x{_campaign.Bread}", slotR.Right - 9, slotR.Bottom - 13, 1, Mono.Ink);
                if (hov)
                {
                    tipT = "HARD BREAD";
                    tip1 = $"click: mends the most-hurt member +{TitheContent.Prices.BreadHeal} HP";
                    tip2 = _campaign.Bread > 0 ? null : "you carry none";
                }
            }
            else if (i == 1)
            {
                var ink = _campaign.Draughts > 0 ? Mono.Ink : Mono.Faint;
                if (!DrawIconRect("icon_ui_draught", slotR, ink))
                    _font.DrawCentered(_sb, "DR", slotR.Center.X, slotR.Y + 9, 2, ink);
                if (_campaign.Draughts > 0)
                    OutlinedCentered($"x{_campaign.Draughts}", slotR.Right - 9, slotR.Bottom - 13, 1, Mono.Ink);
                if (hov) { tipT = "HEALING DRAUGHT"; tip1 = "click: treats a WOUNDED companion"; }
            }
            else if (i >= 2 && i < 7 && i - 2 < _campaign.Essences.Count)
            {
                string ess = _campaign.Essences[i - 2];
                if (DrawIconRect("icon_ui_essence",
                        new Rectangle(slotR.X, slotR.Y, slotR.Width, slotR.Height - 10), Mono.Ink, pad: 2))
                    OutlinedCentered(Trunc(ess.ToUpperInvariant(), 5), slotR.Center.X, slotR.Bottom - 13, 1, Mono.Dim);
                else
                    _font.DrawCentered(_sb, "ESS", slotR.Center.X, slotR.Y + 9, 1, Mono.Ink);
                if (hov) { tipT = $"ESSENCE: {ess.ToUpperInvariant()}"; tip1 = "teach it from the BAG (I)"; }
            }
        }

        // TEAM right, exactly like the fight band.
        int ty = HudTop + 10;
        _font.Draw(_sb, "TEAM", 1080, ty, 1, Ew.InkSoft); ty += 14;
        foreach (var u in _campaign.Crew)
        {
            int umax = TitheContent.UnitMaxHp(u);
            int uhp = Math.Clamp(u.CurrentHp ?? umax, 0, umax);
            _font.Draw(_sb, Trunc(u.Name.ToUpperInvariant(), 13), 1080, ty, 1, u.Wounded ? Mono.Danger : Ew.Ink);
            Mono.Bar(_sb, _prim, new Rectangle(1080, ty + 11, 120, 6), umax <= 0 ? 0f : (float)uhp / umax, Mono.Hp);
            _font.Draw(_sb, u.Wounded ? "WOUNDED" : $"{uhp}/{umax}", 1206, ty + 7, 1,
                u.Wounded ? Ew.Danger : Ew.InkSoft);
            ty += 26;
        }

        // The leader's XP rides the plate-wide strip (the fight's timer-bar slot).
        int need = CampaignUnit.XpForNextLevel(a.Level);
        Mono.Bar(_sb, _prim, new Rectangle(350, HudTop + 118, 580, 8),
            Math.Clamp(need <= 0 ? 1f : (float)a.Xp / need, 0f, 1f), Mono.Dim);
        _font.DrawCentered(_sb, $"LVL {a.Level}  —  {a.Xp}/{need} XP", 640, HudTop + 130, 1, Ew.InkSoft);

        DrawMenuButtons();
        DrawFloatList(_bandFloats);
        if (tipT != null) DrawItemCard(tipT, tip1 ?? "", tip2, kmp);
    }

    /// <summary>Band input for the campaign scenes. True = the click belonged to the band.</summary>
    private bool ClickCampaignBand(Point m)
    {
        if (ClickMenuButtons(m)) return true;
        if (m.Y < HudTop) return false;

        if (SpellGridRect(0).Contains(m))
        {
            string? line = _campaign.EatBread();
            if (line != null)
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, @"\+(\d+)");
                string amount = match.Success ? $"+{match.Groups[1].Value}" : "+HP";
                SpawnBandFloat(amount, Mono.Heal, new Vector2(452, HudTop + 30));
                if (_scene == Scene.Graveyard)
                    SpawnWorldFloat(amount, Mono.Heal, _partyWorld + new Vector2(0, -46));
                _sfx.Play("heal", 0.6f);
            }
            else _sfx.Play("click", 0.3f);
            return true;
        }
        if (SpellGridRect(1).Contains(m))
        {
            var w = _campaign.Crew.FirstOrDefault(u => u.Wounded);
            if (w != null && _campaign.TreatWounded(w))
            {
                SpawnBandFloat("TREATED", Mono.Heal, new Vector2(452, HudTop + 30));
                _sfx.Play("heal", 0.6f);
            }
            else _sfx.Play("click", 0.3f);
            return true;
        }
        return true; // the rest of the band is dead space — never a world click
    }
}

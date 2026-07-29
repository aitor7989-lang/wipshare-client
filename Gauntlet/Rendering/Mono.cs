using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Gauntlet.Rendering;

/// <summary>
/// The ONE-BIT reset: a deliberately tiny visual language so the game reads as pure play.
/// Two tones of ink on near-black, ONE red accent for the enemy/danger, sharp 1px frames,
/// no gradients, no gloss. Everything here is procedural; the Hexany/Batuhan kit sprites
/// (local-only, gitignored) ride on top via the usual asset folders.
/// </summary>
public static class Mono
{
    /// <summary>The master switch: true = the whole game wears the 1-bit skin.</summary>
    public const bool On = true;

    public static readonly Color Bg = new(8, 8, 8);            // the void / screen clear
    public static readonly Color Panel = new(12, 12, 12);      // window body
    public static readonly Color Ink = new(236, 236, 230);     // primary lines + text
    public static readonly Color Dim = new(122, 122, 118);     // secondary text / soft lines
    public static readonly Color Faint = new(52, 52, 50);      // hairlines, grid seams
    public static readonly Color Danger = new(224, 60, 48);    // THE accent: enemies, damage, alarms

    // The tactical board: a readable checker (the old tones were near-twins) + a seam
    // that actually seams. The grid should read at a glance, like 1.29's tactical mode.
    public static readonly Color Floor = new(23, 23, 22);
    public static readonly Color FloorAlt = new(15, 15, 14);
    public static readonly Color Seam = new(52, 52, 50);

    // FUNCTIONAL color (designer's call): board information may speak green/blue —
    // walk = green, cast = blue, heal = green, AP = blue — while art stays ink-on-black.
    public static readonly Color Walk = new(110, 180, 105);
    public static readonly Color Cast = new(110, 170, 240);
    public static readonly Color Heal = new(96, 190, 96);
    public static readonly Color ApInk = new(96, 150, 220);
    public static readonly Color MpInk = new(110, 180, 105);

    // The vitals law, everywhere in the UI: HP is RED, AP is BLUE, MP is GREEN.
    public static readonly Color Hp = new(214, 68, 56);
    public static readonly Color Ap = ApInk;
    public static readonly Color Mp = MpInk;
    // Your side is BLUE, the enemy's is red (Danger) — halos, turn order, team marks.
    public static readonly Color Ally = new(96, 150, 220);

    /// <summary>The elements keep their Dofus voice even in mono: fire orange, water blue,
    /// air green, earth ochre. Neutral (and anything else) stays plain ink.</summary>
    public static Color Element(DofusSlice.Core.Combat.Element e) => e switch
    {
        DofusSlice.Core.Combat.Element.Fire => new Color(232, 116, 60),
        DofusSlice.Core.Combat.Element.Water => new Color(86, 160, 232),
        DofusSlice.Core.Combat.Element.Air => new Color(120, 196, 104),
        DofusSlice.Core.Combat.Element.Earth => new Color(198, 158, 80),
        _ => Ink,
    };

    /// <summary>A 1-bit frame: near-black fill, crisp 1px border.</summary>
    public static void Frame(SpriteBatch sb, Primitives prim, Rectangle r,
        bool emphasis = false, float fillAlpha = 0.94f)
    {
        prim.FillRect(sb, r, Panel * fillAlpha);
        prim.StrokeRect(sb, r, 2, emphasis ? Ink : Dim);
    }

    /// <summary>A 1-bit button: border box; hover INVERTS (white fill, black text expected).</summary>
    public static void Button(SpriteBatch sb, Primitives prim, Rectangle r,
        bool hover = false, bool disabled = false)
    {
        if (hover && !disabled) { prim.FillRect(sb, r, Ink); prim.StrokeRect(sb, r, 2, Ink); }
        else { prim.FillRect(sb, r, Panel); prim.StrokeRect(sb, r, 2, disabled ? Faint : Ink); }
    }

    /// <summary>Ink for a label sitting on a <see cref="Button"/> in the given state.</summary>
    public static Color ButtonInk(bool hover, bool disabled = false) =>
        disabled ? Faint : hover ? Panel : Ink;

    /// <summary>A 1-bit gauge: 1px track, solid fill, no segments, no gloss.</summary>
    public static void Bar(SpriteBatch sb, Primitives prim, Rectangle r, float frac,
        Color? fill = null)
    {
        prim.FillRect(sb, r, Panel);
        prim.StrokeRect(sb, r, 2, Dim);
        int w = (int)((r.Width - 4) * Math.Clamp(frac, 0f, 1f));
        if (w > 0) prim.FillRect(sb, new Rectangle(r.X + 2, r.Y + 2, w, r.Height - 4), fill ?? Ink);
    }

    /// <summary>An item/spell slot: sharp 1px cell; selected inverts the border weight.</summary>
    public static void Slot(SpriteBatch sb, Primitives prim, Rectangle r,
        bool hover = false, bool selected = false)
    {
        prim.FillRect(sb, r, Panel);
        prim.StrokeRect(sb, r, selected ? 3 : 2, selected ? Ink : hover ? Ink : Faint);
    }
}

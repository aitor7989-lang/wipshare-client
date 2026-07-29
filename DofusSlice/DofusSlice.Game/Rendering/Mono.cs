using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DofusSlice.Game.Rendering;

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

    public static readonly Color Bg = new(0, 0, 0);            // the void / screen clear (true black)
    public static readonly Color Panel = new(12, 12, 12);      // window body
    public static readonly Color Ink = new(240, 233, 214);     // primary lines + text (warm bone)
    public static readonly Color Dim = new(122, 122, 118);     // secondary text / soft lines
    public static readonly Color Faint = new(52, 52, 50);      // hairlines, grid seams
    public static readonly Color Empty = new(72, 66, 63);      // the mark in an empty cell
    public static readonly Color Danger = new(224, 60, 48);    // THE accent: enemies, damage, alarms

    // The tactical board: two barely-different floor tones + a visible seam.
    public static readonly Color Floor = new(20, 20, 19);
    public static readonly Color FloorAlt = new(16, 16, 15);
    public static readonly Color Seam = new(46, 46, 44);

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

    // Corner radii, in screen px and always multiples of the 2px grid. Panels get the
    // biggest step, the small interactive chrome less, so a 20px button doesn't turn into a
    // pill. FitRadius clamps anything that would eat more than a third of the shorter side.
    public const int RadPanel = 6;
    public const int RadButton = 2;   // buttons are short; 4 turned them into lozenges
    public const int RadSlot = 4;
    public const int RadBar = 2;

    /// <summary>A 1-bit frame: near-black fill, hairline border, ornate corner brackets.</summary>
    public static void Frame(SpriteBatch sb, Primitives prim, Rectangle r,
        bool emphasis = false, float fillAlpha = 0.94f)
    {
        prim.FillRoundRect(sb, r, RadPanel, Panel * fillAlpha);
        prim.BracketRect(sb, r, emphasis ? Ink : Ink * 0.8f);
    }

    /// <summary>A 1-bit button: border box; hover INVERTS (white fill, black text expected).</summary>
    public static void Button(SpriteBatch sb, Primitives prim, Rectangle r,
        bool hover = false, bool disabled = false)
    {
        if (hover && !disabled)
        {
            prim.FillRoundRect(sb, r, RadButton, Ink);
            prim.StrokeRoundRect(sb, r, RadButton, 1, Ink);
        }
        else
        {
            prim.FillRoundRect(sb, r, RadButton, Panel);
            prim.StrokeRoundRect(sb, r, RadButton, 1, disabled ? Faint : Ink);
        }
    }

    /// <summary>Ink for a label sitting on a <see cref="Button"/> in the given state.</summary>
    public static Color ButtonInk(bool hover, bool disabled = false) =>
        disabled ? Faint : hover ? Panel : Ink;

    /// <summary>A 1-bit gauge: 1px track, solid fill, no segments, no gloss.</summary>
    public static void Bar(SpriteBatch sb, Primitives prim, Rectangle r, float frac,
        Color? fill = null)
    {
        prim.FillRoundRect(sb, r, RadBar, Panel);
        prim.StrokeRoundRect(sb, r, RadBar, 1, Dim);
        int w = (int)((r.Width - 4) * Math.Clamp(frac, 0f, 1f));
        // The fill stays square: a rounded sliver at low HP would read as less than it is,
        // and the track's own corners already carry the shape.
        if (w > 0) prim.FillRect(sb, new Rectangle(r.X + 2, r.Y + 2, w, r.Height - 4), fill ?? Ink);
    }

    /// <summary>An item/spell slot: 1px cell with stepped corners; selected thickens the border.
    /// An <paramref name="empty"/> cell gets the generated quatrefoil mark, so a bare grid reads
    /// as deliberate furniture instead of a row of blank holes.</summary>
    public static void Slot(SpriteBatch sb, Primitives prim, Rectangle r,
        bool hover = false, bool selected = false, bool empty = false)
    {
        prim.FillRoundRect(sb, r, RadSlot, Panel);
        prim.StrokeRoundRect(sb, r, RadSlot, selected ? 2 : 1, selected ? Ink : hover ? Ink : Faint);
        if (empty) prim.GlyphIn(sb, prim.SlotGlyph(), r, hover ? Dim : Empty, pad: 5);
    }
}

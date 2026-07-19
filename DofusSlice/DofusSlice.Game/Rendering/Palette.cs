using Microsoft.Xna.Framework;

namespace DofusSlice.Game.Rendering;

/// <summary>Central colour set so the whole slice reads as one look.</summary>
public static class Palette
{
    // Under the ONE-BIT reset (Mono.On) the shared tokens collapse to ink-on-black so every
    // screen that reads through Palette falls into the 1-bit language without local edits.
    public static readonly Color Background = Mono.On ? Mono.Bg : new Color(24, 26, 33);
    public static readonly Color HudPanel = Mono.On ? Mono.Panel : new Color(16, 17, 22);
    public static readonly Color HudPanelLight = Mono.On ? new Color(24, 24, 23) : new Color(34, 37, 46);

    public static readonly Color TileA = Mono.On ? Mono.Floor : new Color(70, 96, 74);   // checker light
    public static readonly Color TileB = Mono.On ? Mono.FloorAlt : new Color(60, 84, 66); // checker dark
    public static readonly Color TileEdge = Mono.On ? Mono.Seam : new Color(38, 52, 42);
    public static readonly Color Obstacle = new(96, 84, 72); // rock
    public static readonly Color ObstacleTop = new(120, 106, 92);

    // Functional cell colours, matching the usual tactical convention (as in Dofus):
    // green = movement (PM), red = spell/attack range (PA), orange = area of effect.
    // Mono keeps the grammar with two inks: walk = pale ink, strike = the red accent.
    public static readonly Color PlacementCell = new(86, 148, 240, 150); // pre-fight placement zone
    public static readonly Color MoveRange = Mono.On ? new Color(236, 236, 230, 48) : new Color(90, 205, 100, 150);
    public static readonly Color CastReach = Mono.On ? new Color(122, 122, 118, 58) : new Color(214, 74, 62, 78);
    public static readonly Color CastRange = Mono.On ? new Color(224, 60, 48, 140) : new Color(236, 92, 74, 168);
    public static readonly Color Aoe = Mono.On ? new Color(224, 60, 48, 205) : new Color(240, 160, 60, 180);
    public static readonly Color Hover = new(255, 255, 255, 90);
    public static readonly Color CurrentRing = Mono.On ? Mono.Ink : new Color(255, 220, 120);

    public static readonly Color HeroColor = new(214, 92, 68);   // Iop red
    public static readonly Color EnemyColor = Mono.On ? Mono.Danger : new Color(150, 120, 200);
    public static readonly Color Gobball = new(226, 222, 214);
    public static readonly Color Boar = new(150, 110, 84);
    public static readonly Color Piou = new(240, 214, 92);
    public static readonly Color Shadow = new(0, 0, 0, 80);

    public static readonly Color Text = Mono.On ? Mono.Ink : new Color(232, 234, 240);
    public static readonly Color TextDim = Mono.On ? Mono.Dim : new Color(150, 156, 168);
    public static readonly Color HpFill = Mono.On ? Mono.Ink : new Color(96, 200, 108);
    public static readonly Color HpBack = Mono.On ? Mono.Panel : new Color(30, 30, 34);
    public static readonly Color ApPip = new(120, 180, 255);
    public static readonly Color MpPip = new(120, 235, 150);
    public static readonly Color PipEmpty = new(50, 54, 62);

    public static Color CreatureColor(string name) => name switch
    {
        "Gobball" => Gobball,
        "Boar" => Boar,
        "Piou" => Piou,
        _ => EnemyColor,
    };
}

using Microsoft.Xna.Framework;

namespace DofusSlice.Game.Rendering;

/// <summary>Central colour set so the whole slice reads as one look.</summary>
public static class Palette
{
    public static readonly Color Background = new(24, 26, 33);
    public static readonly Color HudPanel = new(16, 17, 22);
    public static readonly Color HudPanelLight = new(34, 37, 46);

    public static readonly Color TileA = new(70, 96, 74);   // grass, checker light
    public static readonly Color TileB = new(60, 84, 66);   // grass, checker dark
    public static readonly Color TileEdge = new(38, 52, 42);
    public static readonly Color Obstacle = new(96, 84, 72); // rock
    public static readonly Color ObstacleTop = new(120, 106, 92);

    // Functional cell colours, matching the usual tactical convention (as in Dofus):
    // green = movement (PM), red = spell/attack range (PA), orange = area of effect.
    public static readonly Color MoveRange = new(90, 205, 100, 150);
    public static readonly Color CastReach = new(214, 74, 62, 78);    // spell's full geometric reach (dim)
    public static readonly Color CastRange = new(236, 92, 74, 168);   // cells that are valid targets right now
    public static readonly Color Aoe = new(240, 160, 60, 180);
    public static readonly Color Hover = new(255, 255, 255, 90);
    public static readonly Color CurrentRing = new(255, 220, 120);

    public static readonly Color HeroColor = new(214, 92, 68);   // Iop red
    public static readonly Color EnemyColor = new(150, 120, 200);
    public static readonly Color Gobball = new(226, 222, 214);
    public static readonly Color Boar = new(150, 110, 84);
    public static readonly Color Piou = new(240, 214, 92);
    public static readonly Color Shadow = new(0, 0, 0, 80);

    public static readonly Color Text = new(232, 234, 240);
    public static readonly Color TextDim = new(150, 156, 168);
    public static readonly Color HpFill = new(96, 200, 108);
    public static readonly Color HpBack = new(30, 30, 34);
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

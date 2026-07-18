using DofusSlice.Core.Combat;
using DofusSlice.Core.Grid;

namespace DofusSlice.Core.Content;

/// <summary>Builds the ready-to-fight Incarnam combat sandbox: map, hero and mob group.</summary>
public static class Encounter
{
    public const int Width = 15;
    public const int Height = 13;

    public static CombatEngine CreateIncarnamSandbox(IRng rng)
    {
        var field = new Battlefield(Width, Height);

        // A scatter of rocks/bushes for cover — enough to make line of sight and
        // pathfinding matter without walling the arena into corridors.
        foreach (var rock in new[]
        {
            new CellCoord(7, 6), new CellCoord(7, 5), new CellCoord(7, 7),
            new CellCoord(5, 3), new CellCoord(9, 9), new CellCoord(10, 2),
            new CellCoord(4, 9), new CellCoord(11, 10),
        })
        {
            field.SetObstacle(rock);
        }

        var fighters = new List<Fighter>
        {
            Bestiary.MakeIop("Iop", new CellCoord(2, 6)),
            Bestiary.MakeGobball("mob_gobball", new CellCoord(12, 6)),
            Bestiary.MakeBoar("mob_boar_1", new CellCoord(13, 4)),
            Bestiary.MakeBoar("mob_boar_2", new CellCoord(13, 8)),
        };

        return new CombatEngine(field, fighters, rng);
    }
}

using DofusSlice.Core.Grid;

namespace DofusSlice.Core.Content;

/// <summary>A parsed map: its tiles plus where the hero and mobs spawn. Content, not code.</summary>
public sealed class MapData
{
    public string Name { get; init; } = "";
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required TileKind[] Tiles { get; init; }
    public CellCoord PlayerSpawn { get; set; }
    public List<(string kind, CellCoord cell)> Enemies { get; } = new();

    /// <summary>Cells the player may place the hero on before the fight begins.</summary>
    public List<CellCoord> PlayerStartCells { get; } = new();

    public bool InBounds(CellCoord c) => c.X >= 0 && c.X < Width && c.Y >= 0 && c.Y < Height;
    public bool IsWalkable(CellCoord c) => InBounds(c) && TileKindInfo.IsWalkable(Tile(c.X, c.Y));

    public TileKind Tile(int x, int y) => Tiles[y * Width + x];

    /// <summary>
    /// Ensure a valid hero spawn and a placement zone: pick a free cell if the map defined no
    /// player spawn, and fill in a start zone around the spawn if none was marked.
    /// </summary>
    public void FinalizeSpawns(bool hasPlayerSpawn)
    {
        var taken = new HashSet<CellCoord>(Enemies.Select(e => e.cell));
        if (hasPlayerSpawn) taken.Add(PlayerSpawn);

        if (!hasPlayerSpawn)
        {
            if (FirstWalkableCell(taken) is not CellCoord ok)
                throw new FormatException("map: no free cell for the player");
            PlayerSpawn = ok;
            PlayerStartCells.Add(ok);
        }
        if (!PlayerStartCells.Contains(PlayerSpawn)) PlayerStartCells.Add(PlayerSpawn);

        if (PlayerStartCells.Count <= 1)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    var cell = PlayerSpawn.Offset(dx, dy);
                    if (PlayerSpawn.DistanceTo(cell) <= 2 && IsWalkable(cell)
                        && !taken.Contains(cell) && !PlayerStartCells.Contains(cell))
                        PlayerStartCells.Add(cell);
                }
        }
    }

    /// <summary>First walkable cell not already claimed by a spawn (used as a hero-spawn fallback).</summary>
    public CellCoord? FirstWalkableCell(ISet<CellCoord> taken)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var cell = new CellCoord(x, y);
                if (TileKindInfo.IsWalkable(Tile(x, y)) && !taken.Contains(cell))
                    return cell;
            }
        return null;
    }

    /// <summary>Build the battlefield geometry from the tiles (walkability/LoS follow the kind).</summary>
    public Battlefield ToBattlefield()
    {
        var field = new Battlefield(Width, Height);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                field.SetTile(new CellCoord(x, y), Tile(x, y));
        return field;
    }
}

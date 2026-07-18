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

    public TileKind Tile(int x, int y) => Tiles[y * Width + x];

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

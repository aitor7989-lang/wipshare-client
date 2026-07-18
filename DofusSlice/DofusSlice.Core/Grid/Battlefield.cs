namespace DofusSlice.Core.Grid;

/// <summary>
/// The static tactical map: a rectangular lattice of cells, each of which may be
/// walkable and/or block line of sight. Occupancy by fighters is tracked
/// separately by the combat engine, so the battlefield stays purely geometric.
/// </summary>
public sealed class Battlefield
{
    public int Width { get; }
    public int Height { get; }

    private readonly bool[] _walkable;
    private readonly bool[] _blocksLos;
    private readonly TileKind[] _kind;

    public Battlefield(int width, int height)
    {
        Width = width;
        Height = height;
        _walkable = new bool[width * height];
        _blocksLos = new bool[width * height];
        _kind = new TileKind[width * height];
        Array.Fill(_walkable, true);
        Array.Fill(_kind, TileKind.Grass);
    }

    public int CellCount => Width * Height;

    public bool InBounds(CellCoord c) => c.X >= 0 && c.X < Width && c.Y >= 0 && c.Y < Height;

    private int Index(CellCoord c) => c.Y * Width + c.X;

    public bool IsWalkable(CellCoord c) => InBounds(c) && _walkable[Index(c)];

    public bool BlocksLineOfSight(CellCoord c) => InBounds(c) && _blocksLos[Index(c)];

    public TileKind TileAt(CellCoord c) => InBounds(c) ? _kind[Index(c)] : TileKind.Void;

    /// <summary>Set a cell's tile kind; walkability and line-of-sight follow from the kind.</summary>
    public void SetTile(CellCoord c, TileKind kind)
    {
        if (!InBounds(c)) return;
        int i = Index(c);
        _kind[i] = kind;
        _walkable[i] = TileKindInfo.IsWalkable(kind);
        _blocksLos[i] = TileKindInfo.BlocksLineOfSight(kind);
    }

    /// <summary>Marks a cell as a solid obstacle (not walkable, blocks sight) — e.g. a rock or wall.</summary>
    public void SetObstacle(CellCoord c, bool obstacle = true) => SetTile(c, obstacle ? TileKind.Rock : TileKind.Grass);

    /// <summary>A hole/void a fighter cannot stand on but can still see across.</summary>
    public void SetHole(CellCoord c) => SetTile(c, TileKind.Void);

    public IEnumerable<CellCoord> Orthogonal(CellCoord c)
    {
        foreach (var d in CellCoord.Directions)
        {
            var n = c.Offset(d.X, d.Y);
            if (InBounds(n)) yield return n;
        }
    }

    public IEnumerable<CellCoord> AllCells()
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                yield return new CellCoord(x, y);
    }
}

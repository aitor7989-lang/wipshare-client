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

    public Battlefield(int width, int height)
    {
        Width = width;
        Height = height;
        _walkable = new bool[width * height];
        _blocksLos = new bool[width * height];
        Array.Fill(_walkable, true);
    }

    public int CellCount => Width * Height;

    public bool InBounds(CellCoord c) => c.X >= 0 && c.X < Width && c.Y >= 0 && c.Y < Height;

    private int Index(CellCoord c) => c.Y * Width + c.X;

    public bool IsWalkable(CellCoord c) => InBounds(c) && _walkable[Index(c)];

    public bool BlocksLineOfSight(CellCoord c) => InBounds(c) && _blocksLos[Index(c)];

    /// <summary>Marks a cell as a solid obstacle (not walkable, blocks sight) — e.g. a rock or wall.</summary>
    public void SetObstacle(CellCoord c, bool obstacle = true)
    {
        if (!InBounds(c)) return;
        _walkable[Index(c)] = !obstacle;
        _blocksLos[Index(c)] = obstacle;
    }

    /// <summary>A hole/void a fighter cannot stand on but can still see across.</summary>
    public void SetHole(CellCoord c)
    {
        if (!InBounds(c)) return;
        _walkable[Index(c)] = false;
        _blocksLos[Index(c)] = false;
    }

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

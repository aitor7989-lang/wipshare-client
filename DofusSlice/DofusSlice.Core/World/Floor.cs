using DofusSlice.Core.Grid;

namespace DofusSlice.Core.World;

/// <summary>A room that is more than corridor. Placed against the floor as a whole, which is why
/// the generator produces a FLOOR and not a row stream: "the Warden is at the end" and "no two
/// shrines adjacent" are statements about the whole, and a generator that only knows the last row
/// cannot make them.</summary>
public enum RoomKind
{
    None,
    /// <summary>A carve site: where an essence bearer waits.</summary>
    Shrine,
    /// <summary>Loot, and a reason to enter a room with one exit.</summary>
    Vault,
    /// <summary>The floor boss. Always the last chamber, always the way out.</summary>
    Warden,
}

/// <summary>
/// One generated floor: the ground, in full, from entry to Warden.
///
/// Everything is generated up front. Fog of war is NOT a property of this type — the floor simply
/// exists, and what the player has seen is a separate mask over it (<see cref="Fog"/>). One truth,
/// one derived view. The earlier design generated lazily at a frontier to make "it raises from the
/// deeps" literal, which cost the ability to place anything globally and bought nothing: a floor is
/// a few thousand cells, so there was never a memory problem to solve.
/// </summary>
public sealed class Floor
{
    public int Width { get; }
    public int Length { get; }

    /// <summary>[x, y]. y increases with depth.</summary>
    public TileKind[,] Tiles { get; }

    public SegmentKind[] RowKind { get; }
    public RoomKind[] RowRoom { get; }

    /// <summary>Which segment each row belongs to. Kind alone cannot identify a segment — two
    /// Throats in a row are two segments, and anything detecting boundaries by "the kind changed"
    /// merges them. That is not hypothetical: dropping this field in a refactor silently zeroed the
    /// chained-ambush metric for the second time.</summary>
    public int[] RowSegment { get; }

    /// <summary>Which floor of the dungeon this is, 1-based. Drives the difficulty ramp.</summary>
    public int Number { get; }

    /// <summary>0 open .. 100 single-file, per row. THE scalar every other system reads.</summary>
    public int[] Constriction { get; }

    /// <summary>Column the hero starts on, and the column of the Warden's exit.</summary>
    public int EntryX { get; }
    public int ExitX { get; internal set; }

    public Floor(int width, int length, int entryX, int number = 1)
    {
        Width = width; Length = length; EntryX = entryX; Number = number;
        Tiles = new TileKind[width, length];
        RowKind = new SegmentKind[length];
        RowRoom = new RoomKind[length];
        RowSegment = new int[length];
        Constriction = new int[length];
    }

    /// <summary>Water is NOT walkable — you see and shoot across it, you do not wade it. It was
    /// missing from this test, which would have counted a flooded cistern as open floor and let the
    /// passability proof pass through water.</summary>
    public bool Walkable(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Length &&
        Tiles[x, y] is not (TileKind.Void or TileKind.Rock or TileKind.Water);

    /// <summary>First walkable column on a row, or -1.</summary>
    public int FirstWalkable(int y)
    {
        for (int x = 0; x < Width; x++) if (Walkable(x, y)) return x;
        return -1;
    }

    /// <summary>
    /// Is there an orthogonally-connected path from the entry row to the last row?
    ///
    /// This is the invariant that actually matters, and it is NOT the same as "every row has a
    /// walkable cell" — the check this replaced. Movement here is orthogonal, so a single-cell row
    /// at column 5 followed by a single-cell row at column 6 leaves two cells that are diagonally
    /// adjacent and mutually unreachable. Every row passes "has a walkable cell"; the floor is
    /// unwinnable. On a one-way descent with nothing behind you, that is a dead run.
    /// </summary>
    public bool IsPassable(out int reachedDepth)
    {
        var seen = new bool[Width, Length];
        var queue = new Queue<(int X, int Y)>();
        reachedDepth = -1;

        for (int x = 0; x < Width; x++)
            if (Walkable(x, 0)) { seen[x, 0] = true; queue.Enqueue((x, 0)); }

        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            reachedDepth = Math.Max(reachedDepth, cy);
            Span<(int dx, int dy)> steps = stackalloc (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            foreach (var (dx, dy) in steps)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!Walkable(nx, ny) || seen[nx, ny]) continue;
                seen[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }
        return reachedDepth >= Length - 1;
    }
}

/// <summary>
/// What the player has seen, kept apart from what exists. Deliberately dumb: the floor is the
/// single source of truth about ground, and this is a mask over it.
/// </summary>
public sealed class Fog
{
    private readonly bool[,] _seen;
    private readonly Floor _floor;

    public Fog(Floor floor)
    {
        _floor = floor;
        _seen = new bool[floor.Width, floor.Length];
    }

    public bool Seen(int x, int y) =>
        x >= 0 && x < _floor.Width && y >= 0 && y < _floor.Length && _seen[x, y];

    /// <summary>Light everything within <paramref name="radius"/> of a point, by Chebyshev
    /// distance so the lit area is a square-ish pool rather than a diamond — a diamond of
    /// torchlight reads as a game rule, and this one wants to read as a lamp.</summary>
    public void Reveal(int cx, int cy, int radius)
    {
        for (int y = Math.Max(0, cy - radius); y <= Math.Min(_floor.Length - 1, cy + radius); y++)
            for (int x = Math.Max(0, cx - radius); x <= Math.Min(_floor.Width - 1, cx + radius); x++)
                _seen[x, y] = true;
    }

    public int SeenCount()
    {
        int n = 0;
        for (int y = 0; y < _floor.Length; y++)
            for (int x = 0; x < _floor.Width; x++)
                if (_seen[x, y]) n++;
        return n;
    }
}

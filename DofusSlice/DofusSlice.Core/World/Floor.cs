using DofusSlice.Core.Grid;

namespace DofusSlice.Core.World;

/// <summary>A room that is more than corridor. Placed against the floor as a whole, which is why
/// the generator produces a FLOOR and not a stream: "the Warden is at the end" and "no two shrines
/// adjacent" are statements about the whole.</summary>
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

/// <summary>A special room's footprint on the grid. Recorded as a RECTANGLE, not inferred from
/// which path cells happen to be tagged: a room is a place with an area, and a consumer that only
/// knows "these path steps were in a shrine" cannot draw the shrine — it can only draw the walk
/// through it.</summary>
public readonly record struct RoomRect(int X, int Y, int W, int H, RoomKind Kind)
{
    public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

/// <summary>The four headings a corridor can run. The path turns between segments, so a floor is a
/// wandering network rather than a strip — an earlier version hardcoded depth to the Y axis, which
/// meant the corridor could only ever run one direction.</summary>
public enum Heading { North, East, South, West }

/// <summary>One step of the walk: where the corridor's centre is, and what it is there.</summary>
public readonly struct PathStep
{
    public int X { get; init; }
    public int Y { get; init; }
    public Heading Dir { get; init; }
    public SegmentKind Kind { get; init; }
    public RoomKind Room { get; init; }
    public int Segment { get; init; }

    /// <summary>0 open .. 100 single-file, measured ACROSS the heading at this step. THE scalar
    /// every other system reads, so tightness means one number everywhere.</summary>
    public int Constriction { get; init; }
}

/// <summary>
/// One generated floor: a 2D grid of ground, plus the path that was walked through it.
///
/// Everything is generated up front. Fog of war is not a property of this type — the floor exists,
/// and what the player has seen is a separate mask over it (<see cref="Fog"/>). One truth, one
/// derived view.
/// </summary>
public sealed class Floor
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>[x, y]. Everything not carved is <see cref="TileKind.Void"/> — the deeps.</summary>
    public TileKind[,] Tiles { get; }

    /// <summary>The walk, in order. Index into this is what "depth" means now: distance along the
    /// path rather than a Y coordinate. Light radius, telegraphs and fog all key off it.</summary>
    public IReadOnlyList<PathStep> Path { get; }

    /// <summary>Every special room's footprint, so rooms can be drawn as rooms.</summary>
    public IReadOnlyList<RoomRect> Rooms { get; }

    public int Number { get; }
    public (int X, int Y) Entry => (Path[0].X, Path[0].Y);
    public (int X, int Y) Exit => (Path[^1].X, Path[^1].Y);

    public Floor(int width, int height, IReadOnlyList<PathStep> path, TileKind[,] tiles, int number,
                 IReadOnlyList<RoomRect> rooms)
    {
        Width = width; Height = height; Path = path; Tiles = tiles; Number = number; Rooms = rooms;
    }

    /// <summary>Water is NOT walkable — you see and shoot across it, you do not wade it. It was
    /// missing from this test once, which let the passability proof cross a flooded room.</summary>
    public bool Walkable(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height &&
        Tiles[x, y] is not (TileKind.Void or TileKind.Rock or TileKind.Water);

    /// <summary>
    /// Is there an orthogonally-connected route from the entry to the Warden?
    ///
    /// Not the same as "every step has floor near it": movement is orthogonal, so two cells that are
    /// merely diagonally adjacent are mutually unreachable. On a one-way descent an unreachable exit
    /// is a dead run the player cannot even walk back to diagnose.
    /// </summary>
    public bool IsPassable(out int reachedSteps)
    {
        var seen = new bool[Width, Height];
        var q = new Queue<(int X, int Y)>();
        var (ex, ey) = Entry;
        reachedSteps = 0;
        if (!Walkable(ex, ey)) return false;

        seen[ex, ey] = true; q.Enqueue((ex, ey));
        while (q.Count > 0)
        {
            var (cx, cy) = q.Dequeue();
            Span<(int, int)> steps = stackalloc (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            foreach (var (dx, dy) in steps)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!Walkable(nx, ny) || seen[nx, ny]) continue;
                seen[nx, ny] = true;
                q.Enqueue((nx, ny));
            }
        }

        // How far along the walk is reachable — a partial figure localises the break.
        for (int i = 0; i < Path.Count; i++)
        {
            if (!seen[Path[i].X, Path[i].Y]) break;
            reachedSteps = i + 1;
        }
        return reachedSteps == Path.Count;
    }
}

/// <summary>What the player has seen, kept apart from what exists.</summary>
public sealed class Fog
{
    private readonly bool[,] _seen;
    private readonly Floor _floor;

    public Fog(Floor floor)
    {
        _floor = floor;
        _seen = new bool[floor.Width, floor.Height];
    }

    public bool Seen(int x, int y) =>
        x >= 0 && x < _floor.Width && y >= 0 && y < _floor.Height && _seen[x, y];

    /// <summary>Light a pool around a point, by Chebyshev distance so it reads as a lamp rather than
    /// as a game rule — a diamond of torchlight announces itself as geometry.</summary>
    public void Reveal(int cx, int cy, int radius)
    {
        for (int y = Math.Max(0, cy - radius); y <= Math.Min(_floor.Height - 1, cy + radius); y++)
            for (int x = Math.Max(0, cx - radius); x <= Math.Min(_floor.Width - 1, cx + radius); x++)
                _seen[x, y] = true;
    }

    /// <summary>Walk the whole path with a torch — the reveal a player would have earned.</summary>
    public void WalkWithTorch(int radius)
    {
        foreach (var s in _floor.Path) Reveal(s.X, s.Y, radius);
    }
}

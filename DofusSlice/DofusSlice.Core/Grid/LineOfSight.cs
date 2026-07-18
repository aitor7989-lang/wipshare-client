namespace DofusSlice.Core.Grid;

public static class LineOfSight
{
    /// <summary>
    /// Bresenham line-of-sight check between two cells. Every intermediate cell
    /// (endpoints excluded) must not block sight. Fighters standing between the
    /// two cells also block sight, matching Dofus — the <paramref name="occupied"/>
    /// predicate reports those cells.
    /// </summary>
    public static bool HasLineOfSight(Battlefield field, CellCoord from, CellCoord to,
        Func<CellCoord, bool>? occupied = null)
    {
        if (from == to) return true;

        int x0 = from.X, y0 = from.Y, x1 = to.X, y1 = to.Y;
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }

            if (x0 == x1 && y0 == y1) break; // reached target — its own cell never blocks

            var cell = new CellCoord(x0, y0);
            if (field.BlocksLineOfSight(cell)) return false;
            if (occupied != null && occupied(cell)) return false;
        }

        return true;
    }
}

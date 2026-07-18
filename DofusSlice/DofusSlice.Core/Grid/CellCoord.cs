namespace DofusSlice.Core.Grid;

/// <summary>
/// A cell position on the tactical grid, in logical (non-pixel) coordinates.
/// In Dofus the playfield is an isometric diamond; here we keep the logic on a
/// plain rectangular lattice and let the renderer project it to iso pixels.
/// The four grid neighbours (up/down/left/right) map to the four iso diagonals
/// the player actually moves along, which matches Dofus's orthogonal movement.
/// </summary>
public readonly record struct CellCoord(int X, int Y)
{
    public static readonly CellCoord Invalid = new(-1, -1);

    /// <summary>Manhattan (grid) distance — the number of orthogonal steps between two cells.</summary>
    public int DistanceTo(CellCoord other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    public bool IsAdjacentTo(CellCoord other) => DistanceTo(other) == 1;

    /// <summary>True when both cells share a row or a column (needed for line-only spells).</summary>
    public bool IsAlignedWith(CellCoord other) => X == other.X || Y == other.Y;

    public CellCoord Offset(int dx, int dy) => new(X + dx, Y + dy);

    public static readonly CellCoord[] Directions =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };

    public override string ToString() => $"({X},{Y})";
}

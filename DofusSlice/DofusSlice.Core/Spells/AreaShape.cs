using DofusSlice.Core.Grid;

namespace DofusSlice.Core.Spells;

public enum AreaKind { Single, Circle, Cross, Line, Cone }

/// <summary>
/// Area-of-effect footprint around a spell's impact cell. Line and Cone open out in the
/// direction of the cast (from the caster toward the impact), so they need that direction;
/// the others ignore it.
/// </summary>
public readonly record struct AreaShape(AreaKind Kind, int Radius = 0)
{
    public static readonly AreaShape Single = new(AreaKind.Single);
    public static AreaShape Circle(int r) => new(AreaKind.Circle, r);
    public static AreaShape Cross(int r) => new(AreaKind.Cross, r);
    public static AreaShape Line(int length) => new(AreaKind.Line, length);
    public static AreaShape Cone(int depth) => new(AreaKind.Cone, depth);

    /// <summary>Every cell struck when the spell lands on <paramref name="center"/>, casting along <paramref name="dir"/>.</summary>
    public IEnumerable<CellCoord> CellsAround(CellCoord center, CellCoord dir = default)
    {
        switch (Kind)
        {
            case AreaKind.Single:
                yield return center;
                break;

            case AreaKind.Circle:
                for (int dx = -Radius; dx <= Radius; dx++)
                    for (int dy = -Radius; dy <= Radius; dy++)
                        if (Math.Abs(dx) + Math.Abs(dy) <= Radius)
                            yield return center.Offset(dx, dy);
                break;

            case AreaKind.Cross:
                yield return center;
                for (int i = 1; i <= Radius; i++)
                {
                    yield return center.Offset(i, 0);
                    yield return center.Offset(-i, 0);
                    yield return center.Offset(0, i);
                    yield return center.Offset(0, -i);
                }
                break;

            case AreaKind.Line:
                yield return center;
                if (dir != default)
                    for (int i = 1; i <= Radius; i++)
                        yield return center.Offset(dir.X * i, dir.Y * i);
                break;

            case AreaKind.Cone:
                yield return center;
                if (dir != default)
                {
                    var perp = new CellCoord(dir.Y, dir.X); // perpendicular step
                    for (int d = 1; d <= Radius; d++)
                        for (int k = -d; k <= d; k++)
                            yield return center.Offset(dir.X * d + perp.X * k, dir.Y * d + perp.Y * k);
                }
                break;
        }
    }
}

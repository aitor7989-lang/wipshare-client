using DofusSlice.Core.Grid;

namespace DofusSlice.Core.Spells;

public enum AreaKind { Single, Circle, Cross, Line }

/// <summary>Area-of-effect footprint around a spell's impact cell.</summary>
public readonly record struct AreaShape(AreaKind Kind, int Radius = 0)
{
    public static readonly AreaShape Single = new(AreaKind.Single);
    public static AreaShape Circle(int r) => new(AreaKind.Circle, r);
    public static AreaShape Cross(int r) => new(AreaKind.Cross, r);

    /// <summary>Every cell struck when the spell lands on <paramref name="center"/>.</summary>
    public IEnumerable<CellCoord> CellsAround(CellCoord center)
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
                break;
        }
    }
}

namespace DofusSlice.Core.Grid;

/// <summary>
/// The visual + functional type of a map cell. Movement/line-of-sight flags are derived from
/// the kind (see <see cref="Battlefield"/>), so a map is described purely by its tiles.
/// </summary>
public enum TileKind
{
    Grass,   // walkable
    Grass2,  // walkable, alt shade
    Dirt,    // walkable
    Path,    // walkable
    Rock,    // obstacle: blocks movement and line of sight
    Void,    // hole: not walkable, but you can see across it
}

public static class TileKindInfo
{
    public static bool IsWalkable(TileKind k) => k is not (TileKind.Rock or TileKind.Void);
    public static bool BlocksLineOfSight(TileKind k) => k is TileKind.Rock;
}

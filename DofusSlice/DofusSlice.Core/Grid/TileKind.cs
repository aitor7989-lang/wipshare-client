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
    Tree,    // obstacle: blocks movement and line of sight (taller prop)
    Void,    // hole: not walkable, but you can see across it
    Water,   // not walkable, but you can see (and shoot) across it
    Spikes,  // walkable hazard: bone spikes gash whoever enters (damage applied by the engine)
}

public static class TileKindInfo
{
    public static bool IsWalkable(TileKind k) => k is not (TileKind.Rock or TileKind.Tree or TileKind.Void or TileKind.Water);
    public static bool BlocksLineOfSight(TileKind k) => k is TileKind.Rock or TileKind.Tree;
}

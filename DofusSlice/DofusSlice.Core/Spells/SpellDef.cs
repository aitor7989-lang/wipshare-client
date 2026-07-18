namespace DofusSlice.Core.Spells;

/// <summary>
/// A spell definition — immutable data, close in spirit to a datamined Dofus spell row.
/// Range, line-of-sight, cast limits and effects together describe how it can be used.
/// </summary>
public sealed class SpellDef
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required int ApCost { get; init; }
    public int MinRange { get; init; } = 1;
    public required int MaxRange { get; init; }

    /// <summary>Requires clear line of sight to the target cell.</summary>
    public bool RequiresLineOfSight { get; init; } = true;

    /// <summary>Can only be cast in a straight line (same row/column) from the caster.</summary>
    public bool LineOnly { get; init; }

    /// <summary>The target cell must be empty (used by teleport-style spells).</summary>
    public bool NeedsFreeCell { get; init; }

    /// <summary>The target cell must currently hold a fighter (most damage spells).</summary>
    public bool NeedsTarget { get; init; } = true;

    public AreaShape Area { get; init; } = AreaShape.Single;

    /// <summary>Turns before it can be recast (0 = every turn).</summary>
    public int Cooldown { get; init; }

    /// <summary>Maximum casts of this spell during one of the caster's turns.</summary>
    public int MaxCastsPerTurn { get; init; } = int.MaxValue;

    public IReadOnlyList<SpellEffect> Effects { get; init; } = Array.Empty<SpellEffect>();

    public string Description { get; init; } = "";
}

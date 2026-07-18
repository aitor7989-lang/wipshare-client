using DofusSlice.Core.Grid;
using DofusSlice.Core.Spells;

namespace DofusSlice.Core.Combat;

public enum Team { Player, Enemy }

/// <summary>
/// A combatant on the grid — the player's Iop or a mob. Holds the mutable fight state
/// (HP, position, points spent this turn) plus the characteristics that scale damage.
/// </summary>
public sealed class Fighter
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required Team Team { get; init; }

    public int MaxHp { get; init; }
    public int Hp { get; set; }

    public int BaseAp { get; init; } = 6;
    public int BaseMp { get; init; } = 3;
    public int CurrentAp { get; set; }
    public int CurrentMp { get; set; }

    // Primary characteristics — drive elemental damage.
    public int Strength { get; init; }
    public int Intelligence { get; init; }
    public int Chance { get; init; }
    public int Agility { get; init; }

    /// <summary>Percent elemental resistance, keyed by element (0-100).</summary>
    public Dictionary<Element, int> Resistances { get; } = new();

    public int Initiative { get; init; }

    public CellCoord Pos { get; set; }

    public IReadOnlyList<SpellDef> Spells { get; init; } = Array.Empty<SpellDef>();

    // Per-turn / cooldown bookkeeping.
    private readonly Dictionary<int, int> _readyOnRound = new();  // spellId -> earliest round castable
    private readonly Dictionary<int, int> _castsThisTurn = new(); // spellId -> casts used this turn

    public bool IsAlive => Hp > 0;

    public int PrimaryStatFor(Element element) => element switch
    {
        Element.Fire => Intelligence,
        Element.Water => Chance,
        Element.Air => Agility,
        _ => Strength, // Earth + Neutral
    };

    public int ResistanceFor(Element element) => Resistances.TryGetValue(element, out int r) ? r : 0;

    public void BeginTurn()
    {
        CurrentAp = BaseAp;
        CurrentMp = BaseMp;
        _castsThisTurn.Clear();
    }

    public bool IsOnCooldown(SpellDef spell, int round) =>
        _readyOnRound.TryGetValue(spell.Id, out int ready) && round < ready;

    public int CastsUsed(SpellDef spell) =>
        _castsThisTurn.TryGetValue(spell.Id, out int n) ? n : 0;

    public bool HasCastsLeft(SpellDef spell) => CastsUsed(spell) < spell.MaxCastsPerTurn;

    public void RecordCast(SpellDef spell, int round)
    {
        _castsThisTurn[spell.Id] = CastsUsed(spell) + 1;
        if (spell.Cooldown > 0) _readyOnRound[spell.Id] = round + spell.Cooldown;
    }
}

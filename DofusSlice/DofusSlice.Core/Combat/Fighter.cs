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

    /// <summary>The human drives this fighter (the hero). Allied summons are AI-run.</summary>
    public bool PlayerControlled { get; init; }

    /// <summary>A creature summoned into the fight (vanishes cleanly, doesn't count as the hero).</summary>
    public bool IsSummon { get; init; }

    // ----- TITHE watched-combat fields (unused by the Dofus slice) -------------------

    /// <summary>Class / mob id this unit was built from (e.g. "archer", "barrow_husk") — drives rendering and flavour.</summary>
    public string Archetype { get; init; } = "";

    /// <summary>How the autobattler plays this unit when combat is watched rather than piloted.</summary>
    public AiPolicy Policy { get; init; } = AiPolicy.Bruiser;

    /// <summary>Class passive id (e.g. "long_shot", "rage_below", "overchannel") — hooks the damage pipeline. "" = none.</summary>
    public string Passive { get; init; } = "";

    /// <summary>Range band a ranged policy tries to hold from its nearest enemy (kiting). 0/0 = melee.</summary>
    public int PreferredRangeMin { get; init; }
    public int PreferredRangeMax { get; init; }

    /// <summary>Hired mortal: dies permanently at 0 HP. A player-managed unit is dragged out Wounded instead.</summary>
    public bool IsMercenary { get; init; }

    public int Level { get; set; } = 1;
    public int Xp { get; set; }

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

    // Secondary combat characteristics (Dofus-style).
    public int Power { get; init; }         // adds to damage scaling like a primary stat
    public int DamagePercent { get; init; } // flat % damage bonus (from gear)
    public int FlatDamage { get; init; }    // added after % scaling
    public int Wisdom { get; init; }        // resistance to AP/MP loss (dodge)

    /// <summary>Percent elemental resistance, keyed by element (0-100).</summary>
    public Dictionary<Element, int> Resistances { get; } = new();

    /// <summary>Flat elemental resistance subtracted after % reduction.</summary>
    public Dictionary<Element, int> FlatResistances { get; } = new();

    public int FlatResistanceFor(Element element) => FlatResistances.TryGetValue(element, out int r) ? r : 0;

    public int Initiative { get; init; }

    public CellCoord Pos { get; set; }

    /// <summary>Unit grid direction this fighter faces — the backstab arc's anchor. The engine
    /// turns a fighter along its last step and toward whatever it casts at.</summary>
    public CellCoord Facing { get; set; } = new(1, 0);

    // ----- Item/essence hooks (the Gauntlet's covenant rules; all default to inert) ----

    /// <summary>Extra cells added to every push this fighter causes (Husk's Grip, heavy blades).</summary>
    public int PushBonus { get; set; }

    /// <summary>Extra collision damage when this fighter slams a victim into an obstacle.</summary>
    public int SlamBonus { get; set; }

    /// <summary>Spikes (and kindred ground hazards) no longer wound this fighter (THE REVENANT).</summary>
    public bool HazardImmune { get; set; }

    /// <summary>SOFT hazards (ember graves and kindred non-lethal <see cref="CombatEngine.TileDanger"/>
    /// tiles) don't singe this fighter — the game sets it on a victim its own hazard rules exempt (the
    /// warm/BONE avatar) so <see cref="CombatEngine.ForecastShift"/> can predict a shove honestly. It is
    /// separate from <see cref="HazardImmune"/> because embers ignore that (the engine's spikes don't).</summary>
    public bool SoftHazardImmune { get; set; }

    /// <summary>Cannot be shoved or dragged into the void: a push toward a coastline stops at
    /// the rim and slams instead (the Sexton — you must earn the kill, not toss him off the map).</summary>
    public bool VoidAnchored { get; set; }

    public IReadOnlyList<SpellDef> Spells { get; init; } = Array.Empty<SpellDef>();

    /// <summary>Active timed states (buffs, shields, poison, drains).</summary>
    public List<StatusEffect> Statuses { get; } = new();

    // Net outgoing-damage swing: buffs minus weaken debuffs.
    public int DamageBuffPercent => Statuses.Where(s => s.Kind == StatusKind.DamageBuff).Sum(s => s.Magnitude)
                                  - Statuses.Where(s => s.Kind == StatusKind.DamageDebuff).Sum(s => s.Magnitude);
    public int ShieldAmount => Statuses.Where(s => s.Kind == StatusKind.Shield).Sum(s => s.Magnitude);
    // Net incoming-damage armor: DefenseBuff adds %resist, Vulnerable subtracts it (takes more).
    public int DefensePercent => Statuses.Where(s => s.Kind == StatusKind.DefenseBuff).Sum(s => s.Magnitude)
                               - Statuses.Where(s => s.Kind == StatusKind.Vulnerable).Sum(s => s.Magnitude);
    // Net spell-range modifier: RangeBuff extends, RangeDebuff (e.g. stolen range) shortens.
    public int RangeBonus => Statuses.Where(s => s.Kind == StatusKind.RangeBuff).Sum(s => s.Magnitude)
                           - Statuses.Where(s => s.Kind == StatusKind.RangeDebuff).Sum(s => s.Magnitude);
    public int ReflectPercent => Statuses.Where(s => s.Kind == StatusKind.Reflect).Sum(s => s.Magnitude);
    public bool IsRooted => Statuses.Any(s => s.Kind == StatusKind.Rooted);
    public bool IsStabilized => Statuses.Any(s => s.Kind == StatusKind.Stabilized);

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

    /// <summary>Rounds until this spell is castable again (0 = ready). The HUD needs the number,
    /// not just the boolean, or a greyed-out well tells the player nothing about when to try again.</summary>
    public int TurnsUntilReady(SpellDef spell, int round) =>
        _readyOnRound.TryGetValue(spell.Id, out int ready) ? Math.Max(0, ready - round) : 0;

    public int CastsUsed(SpellDef spell) =>
        _castsThisTurn.TryGetValue(spell.Id, out int n) ? n : 0;

    public bool HasCastsLeft(SpellDef spell) => CastsUsed(spell) < spell.MaxCastsPerTurn;

    public void RecordCast(SpellDef spell, int round)
    {
        _castsThisTurn[spell.Id] = CastsUsed(spell) + 1;
        if (spell.Cooldown > 0) _readyOnRound[spell.Id] = round + spell.Cooldown;
    }
}

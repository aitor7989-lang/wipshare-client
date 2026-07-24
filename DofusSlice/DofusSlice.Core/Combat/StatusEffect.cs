namespace DofusSlice.Core.Combat;

/// <summary>A timed state on a fighter, in the spirit of Dofus buffs/debuffs.</summary>
public enum StatusKind
{
    None,
    DamageBuff,   // +Magnitude% to outgoing damage
    DamageDebuff, // -Magnitude% to outgoing damage (weaken)
    Shield,       // -Magnitude flat to incoming damage
    DefenseBuff,  // +Magnitude% resistance to ALL incoming damage (armor)
    Vulnerable,   // -Magnitude% resistance = takes that much MORE damage (defense broken)
    RangeBuff,    // +Magnitude to the bearer's spell range
    RangeDebuff,  // -Magnitude to the bearer's spell range
    Poison,       // Magnitude damage at the start of each of the bearer's turns
    Regen,        // +Magnitude HP at the start of each of the bearer's turns
    MpDrain,      // -Magnitude MP at the start of each of the bearer's turns
    Rooted,       // cannot move
    Stabilized,   // cannot be pushed or pulled
    Reflect,      // returns Magnitude% of spell damage taken to the attacker
}

/// <summary>One active status instance: its strength and how many turns remain.</summary>
public sealed class StatusEffect
{
    public StatusKind Kind { get; }
    public int Magnitude { get; set; }
    public int Remaining { get; set; }

    public StatusEffect(StatusKind kind, int magnitude, int turns)
    {
        Kind = kind;
        Magnitude = magnitude;
        Remaining = turns;
    }
}

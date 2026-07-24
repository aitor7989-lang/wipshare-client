using DofusSlice.Core.Combat;

namespace DofusSlice.Core.Spells;

public enum EffectKind
{
    Damage,
    Heal,
    Push,        // shove the target away from the caster
    Pull,        // drag the target toward the caster
    Swap,        // swap the caster's and target's positions
    Teleport,    // move the caster onto the targeted cell (Iop's Jump)
    Lifesteal,   // damage that heals the caster for half
    StealAp,     // remove AP from the target and give it to the caster
    StealMp,     // remove MP from the target and give it to the caster
    StealRange,  // shorten the target's spell range and lengthen the caster's (Dofus vol de portée)
    GrantAp,     // give AP to an allied target this turn (Bone Piper, Blood Pact)
    ApplyStatus, // add a timed status to the affected fighter(s)
    Summon,      // summon a creature (SummonKind) onto the targeted free cell
    SelfHpCost,  // the caster pays Min HP (a sacrifice — wounds, never kills)
}

/// <summary>
/// One elemental/utility effect carried by a spell. A spell may bundle several
/// (e.g. damage + push). Damage/Heal roll uniformly in [Min, Max]; Push uses Min as
/// the number of cells; ApplyStatus uses Min as magnitude and Max as duration in turns.
/// </summary>
public sealed record SpellEffect(
    EffectKind Kind, Element Element = Element.Neutral, int Min = 0, int Max = 0,
    StatusKind Status = StatusKind.None, string SummonKind = "")
{
    public static SpellEffect Summon(string kind) => new(EffectKind.Summon, SummonKind: kind);

    public static SpellEffect Damage(Element element, int min, int max) => new(EffectKind.Damage, element, min, max);
    public static SpellEffect Heal(int min, int max) => new(EffectKind.Heal, Element.Water, min, max);
    public static SpellEffect Push(int cells) => new(EffectKind.Push, Element.Neutral, cells, cells);
    public static SpellEffect Pull(int cells) => new(EffectKind.Pull, Element.Neutral, cells, cells);
    public static SpellEffect Swap() => new(EffectKind.Swap);
    public static SpellEffect Teleport() => new(EffectKind.Teleport);
    public static SpellEffect Lifesteal(Element element, int min, int max) => new(EffectKind.Lifesteal, element, min, max);
    // Max carries the DURATION: 1 = this turn only (the loss is refilled at BeginTurn), >1 leaves a
    // drain status so the theft rides for that many turns, the 1.29 way.
    public static SpellEffect StealAp(int amount, int turns = 1) => new(EffectKind.StealAp, Element.Neutral, amount, turns);
    public static SpellEffect StealMp(int amount, int turns = 1) => new(EffectKind.StealMp, Element.Neutral, amount, turns);
    public static SpellEffect StealRange(int amount, int turns) => new(EffectKind.StealRange, Element.Neutral, amount, turns);
    public static SpellEffect GrantAp(int amount) => new(EffectKind.GrantAp, Element.Neutral, amount, amount);
    public static SpellEffect SelfHpCost(int amount) => new(EffectKind.SelfHpCost, Element.Neutral, amount, amount);
    public static SpellEffect ApplyStatus(StatusKind status, int magnitude, int turns) =>
        new(EffectKind.ApplyStatus, Element.Neutral, magnitude, turns, status);
}

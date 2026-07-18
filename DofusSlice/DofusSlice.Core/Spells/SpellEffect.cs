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
    ApplyStatus, // add a timed status to the affected fighter(s)
}

/// <summary>
/// One elemental/utility effect carried by a spell. A spell may bundle several
/// (e.g. damage + push). Damage/Heal roll uniformly in [Min, Max]; Push uses Min as
/// the number of cells; ApplyStatus uses Min as magnitude and Max as duration in turns.
/// </summary>
public sealed record SpellEffect(
    EffectKind Kind, Element Element = Element.Neutral, int Min = 0, int Max = 0,
    StatusKind Status = StatusKind.None)
{
    public static SpellEffect Damage(Element element, int min, int max) => new(EffectKind.Damage, element, min, max);
    public static SpellEffect Heal(int min, int max) => new(EffectKind.Heal, Element.Water, min, max);
    public static SpellEffect Push(int cells) => new(EffectKind.Push, Element.Neutral, cells, cells);
    public static SpellEffect Pull(int cells) => new(EffectKind.Pull, Element.Neutral, cells, cells);
    public static SpellEffect Swap() => new(EffectKind.Swap);
    public static SpellEffect Teleport() => new(EffectKind.Teleport);
    public static SpellEffect Lifesteal(Element element, int min, int max) => new(EffectKind.Lifesteal, element, min, max);
    public static SpellEffect StealAp(int amount) => new(EffectKind.StealAp, Element.Neutral, amount, amount);
    public static SpellEffect StealMp(int amount) => new(EffectKind.StealMp, Element.Neutral, amount, amount);
    public static SpellEffect ApplyStatus(StatusKind status, int magnitude, int turns) =>
        new(EffectKind.ApplyStatus, Element.Neutral, magnitude, turns, status);
}

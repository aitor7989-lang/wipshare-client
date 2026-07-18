using DofusSlice.Core.Combat;

namespace DofusSlice.Core.Spells;

public enum EffectKind
{
    Damage,
    Heal,
    Push,        // shove the target away from the caster
    Teleport,    // move the caster onto the targeted cell (Iop's Jump)
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
    public static SpellEffect Teleport() => new(EffectKind.Teleport);
    public static SpellEffect ApplyStatus(StatusKind status, int magnitude, int turns) =>
        new(EffectKind.ApplyStatus, Element.Neutral, magnitude, turns, status);
}

using DofusSlice.Core.Combat;
using DofusSlice.Core.Spells;

namespace DofusSlice.Core.Content;

/// <summary>
/// Hand-authored spell data approximating low-level Dofus 1.29 spells. Values are tuned
/// for a fun slice rather than lifted verbatim; the shape mirrors a datamined spell row so
/// real data can replace these later without touching the engine.
/// </summary>
public static class SpellLibrary
{
    // ----- Iop -----------------------------------------------------------------------

    public static readonly SpellDef Pressure = new()
    {
        Id = 101,
        Name = "Pressure",
        ApCost = 3,
        MinRange = 1,
        MaxRange = 6,
        RequiresLineOfSight = true,
        Effects = new[] { SpellEffect.Damage(Element.Neutral, 12, 16) },
        Description = "Ranged neutral strike. The Iop's reliable poke.",
    };

    public static readonly SpellDef IopsWrath = new()
    {
        Id = 102,
        Name = "Iop's Wrath",
        ApCost = 5,
        MinRange = 1,
        MaxRange = 1,
        Cooldown = 2,
        MaxCastsPerTurn = 1,
        Effects = new[] { SpellEffect.Damage(Element.Earth, 25, 35) },
        Description = "A devastating melee earth blow. Long cooldown.",
    };

    public static readonly SpellDef Jump = new()
    {
        Id = 103,
        Name = "Jump",
        ApCost = 3,
        MinRange = 1,
        MaxRange = 6,
        RequiresLineOfSight = true,
        NeedsTarget = false,
        NeedsFreeCell = true,
        MaxCastsPerTurn = 1,
        Effects = new[] { SpellEffect.Teleport() },
        Description = "Leap to a free cell in sight. The Iop's gap-closer.",
    };

    public static readonly SpellDef Intimidation = new()
    {
        Id = 104,
        Name = "Intimidation",
        ApCost = 2,
        MinRange = 1,
        MaxRange = 1,
        Effects = new SpellEffect[] { SpellEffect.Damage(Element.Neutral, 6, 10), SpellEffect.Push(2) },
        Description = "Melee shove: small damage and knock the enemy back two cells.",
    };

    public static readonly SpellDef Power = new()
    {
        Id = 105,
        Name = "Power",
        ApCost = 3,
        MinRange = 0,
        MaxRange = 0,
        NeedsTarget = false,
        Cooldown = 4,
        MaxCastsPerTurn = 1,
        Effects = new[] { SpellEffect.ApplyStatus(StatusKind.DamageBuff, 50, 3) },
        Description = "Self-buff: +50% damage for 3 turns.",
    };

    public static IReadOnlyList<SpellDef> IopSpells => new[] { Pressure, IopsWrath, Jump, Intimidation, Power };

    // ----- Mobs ----------------------------------------------------------------------

    public static readonly SpellDef BoarCharge = new()
    {
        Id = 201,
        Name = "Charge",
        ApCost = 3,
        MinRange = 1,
        MaxRange = 1,
        Effects = new[] { SpellEffect.Damage(Element.Earth, 8, 12) },
        Description = "Boar's tusk charge.",
    };

    public static readonly SpellDef GobballHeadbutt = new()
    {
        Id = 202,
        Name = "Headbutt",
        ApCost = 3,
        MinRange = 1,
        MaxRange = 1,
        Effects = new[]
        {
            SpellEffect.Damage(Element.Neutral, 10, 15),
            SpellEffect.ApplyStatus(StatusKind.Poison, 4, 2),
        },
        Description = "Gobball's woolly headbutt — leaves a lingering irritation.",
    };

    public static readonly SpellDef PiouPeck = new()
    {
        Id = 203,
        Name = "Peck",
        ApCost = 3,
        MinRange = 1,
        MaxRange = 4,
        RequiresLineOfSight = true,
        Effects = new[] { SpellEffect.Damage(Element.Air, 6, 10) },
        Description = "Piou's darting ranged peck.",
    };
}

using DofusSlice.Core.Combat;
using DofusSlice.Core.Grid;

namespace DofusSlice.Core.Content;

/// <summary>Factory for the hero and the Incarnam mobs used in the slice.</summary>
public static class Bestiary
{
    /// <summary>Create a creature by kind ("boar", "gobball", "piou"); unknown/empty -> boar.</summary>
    public static Fighter Create(string? kind, string id, CellCoord pos, Team team = Team.Enemy, bool isSummon = false)
        => (kind ?? "").ToLowerInvariant() switch
        {
            "gobball" => MakeGobball(id, pos, team, isSummon),
            "piou" => MakePiou(id, pos, team, isSummon),
            _ => MakeBoar(id, pos, team, isSummon),
        };

    public static Fighter MakeIop(string name, CellCoord pos) => new()
    {
        Id = "hero",
        Name = name,
        Team = Team.Player,
        PlayerControlled = true,
        MaxHp = 57,
        Hp = 57,
        BaseAp = 6,
        BaseMp = 3,
        Strength = 42,
        Initiative = 100,
        Pos = pos,
        // A little all-round resistance so being outnumbered in Incarnam is survivable.
        Resistances =
        {
            [Element.Neutral] = 4, [Element.Earth] = 4, [Element.Fire] = 4,
            [Element.Water] = 4, [Element.Air] = 4,
        },
        Spells = SpellLibrary.IopSpells,
    };

    public static Fighter MakeGobball(string id, CellCoord pos, Team team = Team.Enemy, bool isSummon = false) => new()
    {
        Id = id,
        Name = "Gobball",
        Team = team,
        IsSummon = isSummon,
        MaxHp = 45,
        Hp = 45,
        BaseAp = 4,
        BaseMp = 3,
        Strength = 22,
        Initiative = 45,
        Pos = pos,
        Spells = new[] { SpellLibrary.GobballHeadbutt },
    };

    public static Fighter MakeBoar(string id, CellCoord pos, Team team = Team.Enemy, bool isSummon = false) => new()
    {
        Id = id,
        Name = "Boar",
        Team = team,
        IsSummon = isSummon,
        MaxHp = 32,
        Hp = 32,
        BaseAp = 4,
        BaseMp = 4,
        Strength = 15,
        Initiative = 60,
        Pos = pos,
        Spells = new[] { SpellLibrary.BoarCharge },
    };

    public static Fighter MakePiou(string id, CellCoord pos, Team team = Team.Enemy, bool isSummon = false) => new()
    {
        Id = id,
        Name = "Piou",
        Team = team,
        IsSummon = isSummon,
        MaxHp = 20,
        Hp = 20,
        BaseAp = 4,
        BaseMp = 4,
        Agility = 18,
        Initiative = 90,
        Pos = pos,
        Spells = new[] { SpellLibrary.PiouPeck },
    };
}

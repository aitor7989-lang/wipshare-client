using DofusSlice.Core.Combat;
using DofusSlice.Core.Grid;

namespace DofusSlice.Core.Content;

/// <summary>Factory for the hero and the Incarnam mobs used in the slice.</summary>
public static class Bestiary
{
    /// <summary>Create a mob by its map-legend kind ("boar", "gobball", "piou").</summary>
    public static Fighter Create(string kind, string id, CellCoord pos) => kind.ToLowerInvariant() switch
    {
        "gobball" => MakeGobball(id, pos),
        "piou" => MakePiou(id, pos),
        _ => MakeBoar(id, pos),
    };

    public static Fighter MakeIop(string name, CellCoord pos) => new()
    {
        Id = "hero",
        Name = name,
        Team = Team.Player,
        MaxHp = 55,
        Hp = 55,
        BaseAp = 6,
        BaseMp = 3,
        Strength = 40,
        Initiative = 100,
        Pos = pos,
        Spells = SpellLibrary.IopSpells,
    };

    public static Fighter MakeGobball(string id, CellCoord pos) => new()
    {
        Id = id,
        Name = "Gobball",
        Team = Team.Enemy,
        MaxHp = 45,
        Hp = 45,
        BaseAp = 4,
        BaseMp = 3,
        Strength = 22,
        Initiative = 45,
        Pos = pos,
        Spells = new[] { SpellLibrary.GobballHeadbutt },
    };

    public static Fighter MakeBoar(string id, CellCoord pos) => new()
    {
        Id = id,
        Name = "Boar",
        Team = Team.Enemy,
        MaxHp = 32,
        Hp = 32,
        BaseAp = 4,
        BaseMp = 4,
        Strength = 15,
        Initiative = 60,
        Pos = pos,
        Spells = new[] { SpellLibrary.BoarCharge },
    };

    public static Fighter MakePiou(string id, CellCoord pos) => new()
    {
        Id = id,
        Name = "Piou",
        Team = Team.Enemy,
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

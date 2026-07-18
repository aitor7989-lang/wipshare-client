using DofusSlice.Core.Combat;

namespace DofusSlice.Core.Content;

/// <summary>
/// Builds a ready-to-fight combat from map data. The default Incarnam map is embedded so the
/// headless sim needs no files; the game may load an external map (see <c>maps/</c>) instead.
/// </summary>
public static class Encounter
{
    /// <summary>Default map — an original clearing layout (not copied from any real Dofus map).</summary>
    public const string DefaultMapJson = """
    {
      "name": "Incarnam Clearing",
      "legend": { "1": "boar", "2": "gobball", "3": "piou" },
      "rows": [
        "...............",
        "...............",
        "..........#.3..",
        "...,.#...,.....",
        "....,........1.",
        "...d...#.......",
        "..Pdddd#....2..",
        "...d...#..,....",
        "........,....1.",
        "..,.#....#.....",
        "..www.oo...#,..",
        "...w...........",
        "..............."
      ]
    }
    """;

    public static MapData DefaultMap() => MapLoader.Parse(DefaultMapJson);

    /// <summary>Assemble the fight: battlefield from tiles, hero at the player spawn, mobs at theirs.</summary>
    public static CombatEngine FromMap(MapData map, IRng rng)
    {
        var field = map.ToBattlefield();
        var fighters = new List<Fighter> { Bestiary.MakeIop("Iop", map.PlayerSpawn) };

        int i = 0;
        foreach (var (kind, cell) in map.Enemies)
            fighters.Add(Bestiary.Create(kind, $"mob_{kind}_{i++}", cell));

        // Summon factory keeps the engine decoupled from the bestiary.
        return new CombatEngine(field, fighters, rng,
            (kind, team, cell, id) => Bestiary.Create(kind, id, cell, team, isSummon: true));
    }

    public static CombatEngine CreateIncarnamSandbox(IRng rng) => FromMap(DefaultMap(), rng);
}

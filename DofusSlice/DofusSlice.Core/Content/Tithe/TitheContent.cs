using System.Text.Json;
using DofusSlice.Core.Combat;
using DofusSlice.Core.Grid;
using DofusSlice.Core.Spells;

namespace DofusSlice.Core.Content.Tithe;

/// <summary>
/// Parses the <see cref="TitheTables"/> data rows into engine objects (spells, fighters, a
/// battlefield, a ready-to-watch <see cref="CombatEngine"/>). This is the "assets are renderers
/// and executors of data" discipline from the Bible: nothing here hardcodes a stat or a number —
/// it all comes from the tables. Combat is <b>watched</b>: every unit gets an <see cref="AiPolicy"/>.
/// </summary>
public static class TitheContent
{
    private static readonly JsonSerializerOptions J = new() { PropertyNameCaseInsensitive = true };

    // ----- DTOs (shape of the JSON rows) --------------------------------------------

    private sealed record EffectDto(string Kind, string? Element, int Min, int Max, int Cells,
                                    string? Status, int Mag, int Turns);
    private sealed record SkillDto(string Key, string Name, int Ap, int Min, int Max, bool Los,
                                   EffectDto[] Effects);
    private sealed record ClassDto(string Id, string Name, string Policy, int MaxHp, int Ap, int Mp,
                                   int Strength, int Agility, int Initiative, int PrefRangeMin,
                                   int PrefRangeMax, string[] Skills, string? Blurb);
    private sealed record MobDto(string Id, string Name, string Policy, int MaxHp, int Ap, int Mp,
                                 int Strength, int Agility, int Initiative, int PrefRangeMin,
                                 int PrefRangeMax, string[] Skills, int Xp);
    private sealed record SpawnDto(string Mob, int X, int Y);
    private sealed record EncounterDto(string Name, SpawnDto[] Spawns);

    // ----- Loaded, id-indexed tables (parsed once) ----------------------------------

    private static Dictionary<string, SpellDef>? _skills;
    private static Dictionary<string, ClassDto>? _classes;
    private static Dictionary<string, MobDto>? _mobs;

    private static Dictionary<string, SpellDef> Skills => _skills ??= LoadSkills();
    private static Dictionary<string, ClassDto> Classes => _classes ??=
        JsonSerializer.Deserialize<ClassDto[]>(TitheTables.ClassesJson, J)!.ToDictionary(c => c.Id);
    private static Dictionary<string, MobDto> Mobs => _mobs ??=
        JsonSerializer.Deserialize<MobDto[]>(TitheTables.MobsJson, J)!.ToDictionary(m => m.Id);

    public static IEnumerable<string> ClassIds => Classes.Keys;
    public static string Blurb(string classId) => Classes.TryGetValue(classId, out var c) ? c.Blurb ?? "" : "";

    private static Dictionary<string, SpellDef> LoadSkills()
    {
        var rows = JsonSerializer.Deserialize<SkillDto[]>(TitheTables.SkillsJson, J)!;
        var map = new Dictionary<string, SpellDef>();
        int id = 100; // ids are engine bookkeeping (cooldowns/cast-caps); keys are how data refers to them
        foreach (var s in rows)
            map[s.Key] = new SpellDef
            {
                Id = id++, Name = s.Name, ApCost = s.Ap, MinRange = s.Min, MaxRange = s.Max,
                RequiresLineOfSight = s.Los, NeedsTarget = true,
                Effects = s.Effects.Select(ToEffect).ToArray(),
            };
        return map;
    }

    private static SpellEffect ToEffect(EffectDto e) => e.Kind switch
    {
        "damage" => SpellEffect.Damage(ParseElement(e.Element), e.Min, e.Max),
        "heal" => SpellEffect.Heal(e.Min, e.Max),
        "push" => SpellEffect.Push(e.Cells),
        "pull" => SpellEffect.Pull(e.Cells),
        "teleport" => SpellEffect.Teleport(),
        "steal_ap" => SpellEffect.StealAp(e.Min),
        "steal_mp" => SpellEffect.StealMp(e.Min),
        "status" => SpellEffect.ApplyStatus(ParseStatus(e.Status), e.Mag, e.Turns),
        _ => throw new FormatException($"tithe: unknown effect kind '{e.Kind}'"),
    };

    private static Element ParseElement(string? s) => s?.ToLowerInvariant() switch
    {
        "fire" => Element.Fire, "water" => Element.Water,
        "air" => Element.Air, "earth" => Element.Earth, _ => Element.Neutral,
    };

    private static StatusKind ParseStatus(string? s) => s?.ToLowerInvariant() switch
    {
        "rooted" or "seized" => StatusKind.Rooted,
        "stabilized" => StatusKind.Stabilized,
        "poison" => StatusKind.Poison,
        "regen" => StatusKind.Regen,
        "mpdrain" or "sapped" => StatusKind.MpDrain,
        "shield" or "ironhide" => StatusKind.Shield,
        "damagebuff" => StatusKind.DamageBuff,
        _ => StatusKind.None,
    };

    private static AiPolicy ParsePolicy(string s) => s.ToLowerInvariant() switch
    {
        "skirmisher" or "ranged" => AiPolicy.Skirmisher,
        "artillery" => AiPolicy.Artillery,
        "flanker" => AiPolicy.Flanker,
        _ => AiPolicy.Bruiser, // "bruiser" / "melee"
    };

    private static SpellDef[] SkillsFor(IEnumerable<string> keys) =>
        keys.Select(k => Skills.TryGetValue(k, out var s)
            ? s : throw new FormatException($"tithe: unknown skill '{k}'")).ToArray();

    // ----- Fighter factories --------------------------------------------------------

    /// <summary>Build a player-managed crew member of the given class at a cell.</summary>
    public static Fighter MakeCrewMember(string classId, string unitId, CellCoord pos, bool isMercenary)
    {
        var c = Classes[classId];
        return new Fighter
        {
            Id = unitId, Name = c.Name, Team = Team.Player, Archetype = c.Id,
            PlayerControlled = true, IsMercenary = isMercenary,
            Policy = ParsePolicy(c.Policy),
            PreferredRangeMin = c.PrefRangeMin, PreferredRangeMax = c.PrefRangeMax,
            MaxHp = c.MaxHp, Hp = c.MaxHp, BaseAp = c.Ap, BaseMp = c.Mp,
            Strength = c.Strength, Agility = c.Agility, Initiative = c.Initiative,
            Pos = pos, Spells = SkillsFor(c.Skills),
        };
    }

    /// <summary>Build a skeleton mob of the given id at a cell.</summary>
    public static Fighter MakeMob(string mobId, string unitId, CellCoord pos)
    {
        var m = Mobs[mobId];
        return new Fighter
        {
            Id = unitId, Name = m.Name, Team = Team.Enemy, Archetype = m.Id,
            Policy = ParsePolicy(m.Policy),
            PreferredRangeMin = m.PrefRangeMin, PreferredRangeMax = m.PrefRangeMax,
            MaxHp = m.MaxHp, Hp = m.MaxHp, BaseAp = m.Ap, BaseMp = m.Mp,
            Strength = m.Strength, Agility = m.Agility, Initiative = m.Initiative,
            Pos = pos, Spells = SkillsFor(m.Skills),
        };
    }

    public static int MobXp(string mobId) => Mobs.TryGetValue(mobId, out var m) ? m.Xp : 0;

    // ----- Encounter assembly -------------------------------------------------------

    public static MapData Arena() => MapLoader.Parse(TitheTables.ArenaJson);

    /// <summary>
    /// Assemble a ready-to-watch fight: the graveyard arena, a crew placed on the start cells,
    /// and the skeleton pack from the encounter table. <paramref name="crewClasses"/> is the
    /// player's chosen line-up (first is the avatar, the rest are mercenaries).
    /// </summary>
    public static CombatEngine BuildFight(IReadOnlyList<string> crewClasses, IRng rng)
    {
        var map = Arena();
        var field = map.ToBattlefield();

        var startCells = map.PlayerStartCells
            .OrderBy(c => c.X).ThenBy(c => c.Y).ToList();

        var fighters = new List<Fighter>();
        for (int i = 0; i < crewClasses.Count; i++)
        {
            var cell = i < startCells.Count ? startCells[i] : map.PlayerSpawn;
            fighters.Add(MakeCrewMember(crewClasses[i], $"crew_{i}_{crewClasses[i]}", cell, isMercenary: i > 0));
        }

        var enc = JsonSerializer.Deserialize<EncounterDto>(TitheTables.EncounterJson, J)!;
        int n = 0;
        foreach (var s in enc.Spawns)
        {
            var cell = new CellCoord(s.X, s.Y);
            if (!map.IsWalkable(cell) || fighters.Any(f => f.Pos == cell)) continue;
            fighters.Add(MakeMob(s.Mob, $"mob_{n++}_{s.Mob}", cell));
        }

        return new CombatEngine(field, fighters, rng,
            (kind, team, cell, id) => MakeMob(kind, id, cell));
    }

    /// <summary>Default crew line-up for the prototype: the three archetypes, avatar first.</summary>
    public static readonly IReadOnlyList<string> DefaultCrew = new[] { "cannon", "bulwark", "archer" };
}

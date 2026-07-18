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
                                   int Cooldown, int CastsPerTurn, EffectDto[] Effects);
    private sealed record GrowthDto(int Vitality, int Strength, int Intelligence, int Chance, int Agility, int Wisdom);
    private sealed record ClassDto(string Id, string Name, string Policy, string? Passive, string? Element, int BaseHp, int Ap, int Mp,
                                   int Vitality, int Strength, int Intelligence, int Chance, int Agility, int Wisdom, int Initiative,
                                   int PrefRangeMin, int PrefRangeMax, GrowthDto? Growth, string[] Skills, string? Blurb);
    private sealed record MobDto(string Id, string Name, string Policy, int MaxHp, int Ap, int Mp,
                                 int Strength, int Intelligence, int Chance, int Agility, int Initiative,
                                 int PrefRangeMin, int PrefRangeMax, string[] Skills, int Xp, int Gold,
                                 string? Essence, int Drop, int Gear,
                                 int ResEarth, int ResFire, int ResAir, int ResWater);
    private sealed record SpawnDto(string Mob, int X, int Y);
    private sealed record EncounterDto(string Name, SpawnDto[] Spawns);

    // Equipment + panoply rows (Bible §6.10). ItemDto carries the live prototype stats; SetDto is a
    // tier table keyed by equipped-piece count. Power adds to every element's damage (Dofus Power),
    // so a set empowers any class regardless of its element. Ap/Mp are the behavior-changing gear
    // bonuses (the +1 MP boots / full-set idiom).
    public sealed record ItemDto(string Id, string Name, string Slot, string? Set,
                                 int Vitality, int Strength, int Intelligence, int Chance, int Agility,
                                 int Wisdom, int Power, int Ap, int Mp);
    private sealed record SetTierDto(int Pieces, int Vitality, int Strength, int Intelligence, int Chance,
                                     int Agility, int Wisdom, int Power, int Ap, int Mp);
    private sealed record SetDto(string Id, string Name, SetTierDto[] Tiers);

    /// <summary>Effective Dofus stat block for a unit (class base + level growth + gear + set bonus).</summary>
    public readonly record struct UnitStats(int Vitality, int Strength, int Intelligence, int Chance,
                                            int Agility, int Wisdom, int Power, int Initiative, int MaxHp,
                                            int ApBonus, int MpBonus);

    public sealed record PriceTable(int HardBread, int BreadHeal, int Draught, int HireBasePerLevel,
                                    int EssenceSell, int TitheEveryNDives, int TitheBase, int TitheGrowth);
    public sealed record PackDef(string Id, string[] Comp, int Reach, bool Hunts);
    public sealed record GraveyardDef(int ClockSeconds, int CryptReach, PackDef[] Packs);

    // ----- Loaded, id-indexed tables (parsed once) ----------------------------------

    private static Dictionary<string, SpellDef>? _skills;
    private static Dictionary<string, ClassDto>? _classes;
    private static Dictionary<string, MobDto>? _mobs;
    private static Dictionary<string, ItemDto>? _items;
    private static Dictionary<string, SetDto>? _sets;
    private static PriceTable? _prices;
    private static GraveyardDef? _graveyard;

    public static PriceTable Prices => _prices ??= JsonSerializer.Deserialize<PriceTable>(TitheTables.PricesJson, J)!;
    public static GraveyardDef Graveyard => _graveyard ??= JsonSerializer.Deserialize<GraveyardDef>(TitheTables.GraveyardJson, J)!;

    private static Dictionary<string, SpellDef> Skills => _skills ??= LoadSkills();
    private static Dictionary<string, ClassDto> Classes => _classes ??=
        JsonSerializer.Deserialize<ClassDto[]>(TitheTables.ClassesJson, J)!.ToDictionary(c => c.Id);
    private static Dictionary<string, MobDto> Mobs => _mobs ??=
        JsonSerializer.Deserialize<MobDto[]>(TitheTables.MobsJson, J)!.ToDictionary(m => m.Id);
    private static Dictionary<string, ItemDto> Items => _items ??=
        JsonSerializer.Deserialize<ItemDto[]>(TitheTables.ItemsJson, J)!.ToDictionary(i => i.Id);
    private static Dictionary<string, SetDto> Sets => _sets ??=
        JsonSerializer.Deserialize<SetDto[]>(TitheTables.SetsJson, J)!.ToDictionary(s => s.Id);

    public static IEnumerable<string> ClassIds => Classes.Keys;
    public static string Blurb(string classId) => Classes.TryGetValue(classId, out var c) ? c.Blurb ?? "" : "";
    public static AiPolicy ClassPolicyOf(string classId) => ParsePolicy(Classes[classId].Policy);

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
                Cooldown = s.Cooldown,
                MaxCastsPerTurn = s.CastsPerTurn > 0 ? s.CastsPerTurn : int.MaxValue,
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
        "grant_ap" => SpellEffect.GrantAp(e.Min),
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
        "support" => AiPolicy.Support,
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
        int maxHp = c.BaseHp + c.Vitality;
        return new Fighter
        {
            Id = unitId, Name = c.Name, Team = Team.Player, Archetype = c.Id,
            PlayerControlled = true, IsMercenary = isMercenary,
            Policy = ParsePolicy(c.Policy), Passive = c.Passive ?? "",
            PreferredRangeMin = c.PrefRangeMin, PreferredRangeMax = c.PrefRangeMax,
            MaxHp = maxHp, Hp = maxHp, BaseAp = c.Ap, BaseMp = c.Mp,
            Strength = c.Strength, Intelligence = c.Intelligence, Chance = c.Chance,
            Agility = c.Agility, Wisdom = c.Wisdom, Initiative = c.Initiative,
            Pos = pos, Spells = SkillsFor(c.Skills),
        };
    }

    /// <summary>Build a skeleton mob of the given id at a cell.</summary>
    public static Fighter MakeMob(string mobId, string unitId, CellCoord pos)
    {
        var m = Mobs[mobId];
        var f = new Fighter
        {
            Id = unitId, Name = m.Name, Team = Team.Enemy, Archetype = m.Id,
            Policy = ParsePolicy(m.Policy),
            PreferredRangeMin = m.PrefRangeMin, PreferredRangeMax = m.PrefRangeMax,
            MaxHp = m.MaxHp, Hp = m.MaxHp, BaseAp = m.Ap, BaseMp = m.Mp,
            Strength = m.Strength, Intelligence = m.Intelligence, Chance = m.Chance,
            Agility = m.Agility, Initiative = m.Initiative,
            Pos = pos, Spells = SkillsFor(m.Skills),
        };
        // Elemental armor (Bible §6.6 resist term). Percent reductions per element from the mob table.
        if (m.ResEarth != 0) f.Resistances[Element.Earth] = m.ResEarth;
        if (m.ResFire != 0) f.Resistances[Element.Fire] = m.ResFire;
        if (m.ResAir != 0) f.Resistances[Element.Air] = m.ResAir;
        if (m.ResWater != 0) f.Resistances[Element.Water] = m.ResWater;
        return f;
    }

    public static int MobXp(string mobId) => Mobs.TryGetValue(mobId, out var m) ? m.Xp : 0;

    /// <summary>The essence a mob can drop and its percent chance (Bible §5), or (null, 0).</summary>
    public static (string? essence, int rate) MobDrop(string mobId) =>
        Mobs.TryGetValue(mobId, out var m) ? (m.Essence, m.Drop) : (null, 0);

    /// <summary>Percent chance this mob drops an Adventurer-set piece (Bible §5 drop-table peaks).</summary>
    public static int MobGear(string mobId) => Mobs.TryGetValue(mobId, out var m) ? m.Gear : 0;

    // ----- Equipment + panoply (Bible §6.10) ----------------------------------------

    /// <summary>The Graveyard's one starter panoply (Bible §5): the set its mobs and the Sexton drop.</summary>
    public const string GraveyardSet = "adventurer";

    public static ItemDto? Item(string itemId) => Items.TryGetValue(itemId, out var i) ? i : null;
    public static string ItemName(string itemId) => Item(itemId)?.Name ?? itemId;
    public static string ItemSlot(string itemId) => Item(itemId)?.Slot ?? "";

    /// <summary>Pick a random set piece not in <paramref name="owned"/>, or null if the set is complete.</summary>
    public static string? RandomUnownedPiece(string setId, Func<string, bool> owned, IRng rng)
    {
        var pool = SetPieceIds(setId).Where(id => !owned(id)).ToList();
        return pool.Count == 0 ? null : pool[rng.Roll(1, pool.Count) - 1];
    }

    /// <summary>All item ids belonging to a set (the drop pool for that panoply).</summary>
    public static IReadOnlyList<string> SetPieceIds(string setId) =>
        Items.Values.Where(i => i.Set == setId).Select(i => i.Id).ToList();

    /// <summary>Slots that may hold more than one item (Dofus allows two rings).</summary>
    public static int SlotCapacity(string slot) => slot == "ring" ? 2 : 1;

    /// <summary>
    /// Effective stat block for a persistent unit (Bible §6.2): class base, plus the auto-spent
    /// characteristic points from every level gained, plus the sum of equipped-item stats and the
    /// set-bonus tier reached. MaxHp is baseHp + total Vitality (1 HP per point, the 1.29 rule).
    /// </summary>
    public static UnitStats StatsOf(CampaignUnit u)
    {
        var c = Classes[u.ClassId];
        var g = c.Growth ?? new GrowthDto(0, 0, 0, 0, 0, 0);
        int lv = Math.Max(0, u.Level - 1);

        int vit = c.Vitality + g.Vitality * lv;
        int str = c.Strength + g.Strength * lv;
        int intel = c.Intelligence + g.Intelligence * lv;
        int cha = c.Chance + g.Chance * lv;
        int agi = c.Agility + g.Agility * lv;
        int wis = c.Wisdom + g.Wisdom * lv;
        int pow = 0, ap = 0, mp = 0;

        // Gear: sum the equipped pieces, then the highest set tier whose piece-count is satisfied.
        var counts = new Dictionary<string, int>();
        foreach (var id in u.Equipment)
        {
            if (Item(id) is not { } it) continue;
            vit += it.Vitality; str += it.Strength; intel += it.Intelligence; cha += it.Chance;
            agi += it.Agility; wis += it.Wisdom; pow += it.Power; ap += it.Ap; mp += it.Mp;
            if (it.Set != null) counts[it.Set] = counts.GetValueOrDefault(it.Set) + 1;
        }
        foreach (var (setId, n) in counts)
        {
            if (!Sets.TryGetValue(setId, out var set)) continue;
            var tier = set.Tiers.Where(t => t.Pieces <= n).OrderByDescending(t => t.Pieces).FirstOrDefault();
            if (tier != null)
            {
                vit += tier.Vitality; str += tier.Strength; intel += tier.Intelligence; cha += tier.Chance;
                agi += tier.Agility; wis += tier.Wisdom; pow += tier.Power; ap += tier.Ap; mp += tier.Mp;
            }
        }

        return new UnitStats(vit, str, intel, cha, agi, wis, pow, c.Initiative, c.BaseHp + vit, ap, mp);
    }

    /// <summary>Effective max HP for a persistent unit (baseHp + total Vitality).</summary>
    public static int UnitMaxHp(CampaignUnit u) => StatsOf(u).MaxHp;

    /// <summary>A class's damage element (Bible §6.6). Defaults to Neutral (Strength) if unthemed.</summary>
    public static Element ClassElement(string classId) =>
        Classes.TryGetValue(classId, out var c) ? ParseElement(c.Element) : Element.Neutral;

    /// <summary>The characteristic that scales a given element's damage, plus universal Power.</summary>
    public static int DamageStatFor(UnitStats s, Element element) => element switch
    {
        Element.Fire => s.Intelligence,
        Element.Water => s.Chance,
        Element.Air => s.Agility,
        _ => s.Strength,
    } + s.Power;

    /// <summary>Number of pieces of a given set the unit has equipped (for readouts).</summary>
    public static int SetPiecesEquipped(CampaignUnit u, string setId) =>
        u.Equipment.Count(id => Item(id)?.Set == setId);

    /// <summary>Coin a mob drops (Bible §6.11). Decoupled from XP since XP went to the Dofus band.</summary>
    public static int MobGold(string mobId) => Mobs.TryGetValue(mobId, out var m) ? m.Gold : 0;

    /// <summary>The Sexton's court — the Crypt's boss encounter, as a mob composition.</summary>
    public static IReadOnlyList<string> CryptComp() =>
        JsonSerializer.Deserialize<EncounterDto>(TitheTables.EncounterBossJson, J)!.Spawns.Select(s => s.Mob).ToList();

    /// <summary>The Crypt's linear room chain (Bible §6.8): escalating packs, the last a boss room.</summary>
    public sealed record CryptRoom(string Name, string[] Comp, bool Boss);
    public static IReadOnlyList<CryptRoom> CryptRooms() =>
        JsonSerializer.Deserialize<CryptRoom[]>(TitheTables.CryptJson, J)!;

    /// <summary>Level-1 max HP of a class (baseHp + starting Vitality), for UI and defaults.</summary>
    public static int ClassMaxHp(string classId) =>
        Classes.TryGetValue(classId, out var c) ? c.BaseHp + c.Vitality : 0;

    /// <summary>Build a combat fighter from a persistent campaign unit: applies its effective Dofus
    /// stat block (level growth + gear + set bonus), carried HP, and the Wounded −1 AP/−1 MP.</summary>
    public static Fighter MakeCrewMember(CampaignUnit u, CellCoord pos)
    {
        var c = Classes[u.ClassId];
        var s = StatsOf(u);
        int hp = Math.Clamp(u.CurrentHp ?? s.MaxHp, 1, s.MaxHp);
        return new Fighter
        {
            Id = u.Id, Name = c.Name, Team = Team.Player, Archetype = c.Id,
            PlayerControlled = true, IsMercenary = !u.IsAvatar,
            Policy = ParsePolicy(c.Policy), Passive = c.Passive ?? "",
            PreferredRangeMin = c.PrefRangeMin, PreferredRangeMax = c.PrefRangeMax,
            MaxHp = s.MaxHp, Hp = hp,
            BaseAp = c.Ap + s.ApBonus - (u.Wounded ? 1 : 0), BaseMp = c.Mp + s.MpBonus - (u.Wounded ? 1 : 0),
            Strength = s.Strength, Intelligence = s.Intelligence, Chance = s.Chance,
            Agility = s.Agility, Wisdom = s.Wisdom, Power = s.Power, Initiative = s.Initiative,
            Level = u.Level, Xp = u.Xp,
            Pos = pos, Spells = SkillsFor(c.Skills),
        };
    }

    // ----- Encounter assembly -------------------------------------------------------

    public static MapData Arena() => MapLoader.Parse(TitheTables.ArenaJson);

    /// <summary>
    /// Assemble a ready-to-watch fight: the graveyard arena, a crew placed on the start cells,
    /// and the skeleton pack from the encounter table. <paramref name="crewClasses"/> is the
    /// player's chosen line-up (first is the avatar, the rest are mercenaries).
    /// </summary>
    public static CombatEngine BuildFight(IReadOnlyList<string> crewClasses, IRng rng, bool boss = false)
    {
        var map = Arena();
        var field = map.ToBattlefield();

        // Default "marching order" placement (Bible §6.13): fill the start cells in a tight
        // top-corner cluster. Clustering lets the tank and archer cover one another, and a corner
        // naturally hides the backline from one of the two flanking Gravehounds. The player can
        // re-place freely — this is just a sensible starting layout, and the sim's reference. How
        // well the squishy backline is tucked away is what separates a clean win from a wipe.
        var startCells = map.PlayerStartCells.OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
        var fighters = new List<Fighter>();
        for (int i = 0; i < crewClasses.Count; i++)
        {
            var cell = i < startCells.Count ? startCells[i] : map.PlayerSpawn;
            fighters.Add(MakeCrewMember(crewClasses[i], $"crew_{i}_{crewClasses[i]}", cell, isMercenary: i > 0));
        }

        var enc = JsonSerializer.Deserialize<EncounterDto>(
            boss ? TitheTables.EncounterBossJson : TitheTables.EncounterJson, J)!;
        int n = 0;
        foreach (var s in enc.Spawns)
        {
            var cell = new CellCoord(s.X, s.Y);
            // Robust placement: a spawn on a blocked/occupied cell slides to the nearest free one
            // rather than silently vanishing (a boss on a tombstone should still show up).
            if (!map.IsWalkable(cell) || fighters.Any(f => f.Pos == cell))
                cell = NearestFreeCell(map, fighters, cell);
            if (cell == CellCoord.Invalid) continue;
            fighters.Add(MakeMob(s.Mob, $"mob_{n++}_{s.Mob}", cell));
        }

        return new CombatEngine(field, fighters, rng,
            (kind, team, cell, id) => MakeMob(kind, id, cell));
    }

    /// <summary>
    /// Build a dive fight: the campaign party on the start cells versus a pack of mobs spread on
    /// the far side of the arena. Used by <see cref="DiveSession"/> so every graveyard encounter
    /// runs through the same watched-combat engine.
    /// </summary>
    public static CombatEngine BuildDiveFight(IReadOnlyList<CampaignUnit> party,
                                              IReadOnlyList<string> packMobs, IRng rng)
    {
        var map = Arena();
        var field = map.ToBattlefield();

        var startCells = map.PlayerStartCells.OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
        var fighters = new List<Fighter>();
        for (int i = 0; i < party.Count; i++)
            fighters.Add(MakeCrewMember(party[i], i < startCells.Count ? startCells[i] : map.PlayerSpawn));

        // Role-aware pack placement so the fight plays like the tuned encounter: fast flankers
        // (Gravehounds) start close on the top/bottom flanks to dive the backline, everything else
        // comes from the far side. Each anchor slides to the nearest free cell if blocked.
        var flankAnchors = new[]
        {
            new CellCoord(7, 2), new CellCoord(7, 10), new CellCoord(6, 4), new CellCoord(6, 8),
            new CellCoord(8, 2), new CellCoord(8, 10),
        };
        var farAnchors = new[]
        {
            new CellCoord(11, 4), new CellCoord(11, 8), new CellCoord(13, 6), new CellCoord(9, 3),
            new CellCoord(9, 9), new CellCoord(13, 3), new CellCoord(13, 9), new CellCoord(12, 6),
        };
        int n = 0, fi = 0, ri = 0;
        foreach (var mob in packMobs)
        {
            bool flanker = ParsePolicy(Mobs[mob].Policy) == AiPolicy.Flanker;
            var anchors = flanker ? flankAnchors : farAnchors;
            var want = anchors[(flanker ? fi++ : ri++) % anchors.Length];
            var cell = (map.IsWalkable(want) && fighters.All(f => f.Pos != want))
                ? want : NearestFreeCell(map, fighters, want);
            if (cell == CellCoord.Invalid) continue;
            fighters.Add(MakeMob(mob, $"mob_{n++}_{mob}", cell));
        }

        return new CombatEngine(field, fighters, rng, (kind, team, cell, id) => MakeMob(kind, id, cell));
    }

    /// <summary>Nearest walkable, unoccupied cell to <paramref name="from"/> (ring search), or Invalid.</summary>
    private static CellCoord NearestFreeCell(MapData map, List<Fighter> taken, CellCoord from)
    {
        for (int r = 1; r <= 6; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue; // only the ring at radius r
                    var c = from.Offset(dx, dy);
                    if (map.IsWalkable(c) && taken.All(f => f.Pos != c)) return c;
                }
        return CellCoord.Invalid;
    }

    /// <summary>Default crew line-up for the prototype: the three archetypes, avatar first.</summary>
    public static readonly IReadOnlyList<string> DefaultCrew = new[] { "cannon", "bulwark", "archer" };
}

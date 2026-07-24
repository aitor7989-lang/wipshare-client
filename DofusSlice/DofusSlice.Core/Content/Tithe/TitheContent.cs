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
    private sealed record RankDto(int? Ap, int? Min, int? Max, int? Cooldown, int? CastsPerTurn,
                                  int? Crit = null);
    private sealed record SkillDto(string Key, string Name, int Ap, int Min, int Max, bool Los,
                                   int Cooldown, int CastsPerTurn, bool TargetsGround,
                                   RankDto[]? Ranks, EffectDto[] Effects,
                                   int Crit = 0, int CritFail = 0);
    private sealed record GrowthDto(int Vitality, int Strength, int Intelligence, int Chance, int Agility, int Wisdom);
    private sealed record ClassDto(string Id, string Name, string Policy, string? Passive, string? Element, int BaseHp, int Ap, int Mp,
                                   int Vitality, int Strength, int Intelligence, int Chance, int Agility, int Wisdom, int Initiative,
                                   int PrefRangeMin, int PrefRangeMax, GrowthDto? Growth, string[] Skills, string? Blurb);
    private sealed record MobDto(string Id, string Name, string Policy, int MaxHp, int Ap, int Mp,
                                 int Strength, int Intelligence, int Chance, int Agility, int Initiative,
                                 int PrefRangeMin, int PrefRangeMax, string[] Skills, int Xp, int Stones,
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
                                    int EssenceSell, int EssenceBuy, int EssenceRemoval, int VetFee,
                                    int TitheEveryNDives, int TitheBase, int TitheGrowth);
    public sealed record PackDef(string Id, string[] Comp, int Reach, bool Hunts, int Grade = 1);
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

    private static Dictionary<string, SkillDto>? _skillRows;
    private static Dictionary<string, SkillDto> SkillRows => _skillRows ??=
        JsonSerializer.Deserialize<SkillDto[]>(TitheTables.SkillsJson, J)!.ToDictionary(s => s.Key);
    private static readonly Dictionary<string, int> SkillIds = new(); // key -> stable engine id

    private static Dictionary<string, SpellDef> LoadSkills()
    {
        var map = new Dictionary<string, SpellDef>();
        foreach (var key in SkillRows.Keys) map[key] = BuildSkill(key, 1);
        return map;
    }

    private static int IdFor(string key)
    {
        // Ids are engine bookkeeping (cooldowns/cast-caps) and must be stable across ranks of the
        // same skill so a rank-up doesn't reset its cooldown identity.
        if (!SkillIds.TryGetValue(key, out int id)) SkillIds[key] = id = 100 + SkillIds.Count;
        return id;
    }

    /// <summary>Build a skill at a rank: each rank row is a cumulative override (Dofus spell levels).</summary>
    private static SpellDef BuildSkill(string key, int rank)
    {
        var s = SkillRows[key];
        int ap = s.Ap, min = s.Min, max = s.Max, cd = s.Cooldown, cpt = s.CastsPerTurn, crit = s.Crit;
        for (int r = 2; r <= rank && s.Ranks != null && r - 2 < s.Ranks.Length; r++)
        {
            var o = s.Ranks[r - 2];
            ap = o.Ap ?? ap; min = o.Min ?? min; max = o.Max ?? max;
            cd = o.Cooldown ?? cd; cpt = o.CastsPerTurn ?? cpt; crit = o.Crit ?? crit;
        }
        return new SpellDef
        {
            Id = IdFor(key),
            Name = s.Name + rank switch { 1 => "", 2 => " II", 3 => " III", _ => $" {rank}" },
            ApCost = ap, MinRange = min, MaxRange = max,
            RequiresLineOfSight = s.Los,
            NeedsTarget = !s.TargetsGround, NeedsFreeCell = s.TargetsGround,
            Cooldown = cd,
            MaxCastsPerTurn = cpt > 0 ? cpt : int.MaxValue,
            // 1.29 crits: "crit" is the 1-in-N chance to land a critical (and ranking a spell
            // sharpens it); "critFail" is the 1-in-N chance to fizzle. Both default to 0 = never.
            CriticalChanceOneIn = crit,
            CriticalFailureOneIn = s.CritFail,
            Effects = s.Effects.Select(ToEffect).ToArray(),
        };
    }

    /// <summary>Highest buyable rank of a skill (1 + its authored rank rows).</summary>
    public static int MaxRank(string key) =>
        SkillRows.TryGetValue(key, out var s) ? 1 + (s.Ranks?.Length ?? 0) : 1;

    /// <summary>Reverse lookup: the data key of a built skill (icons and UI want the key).</summary>
    public static string? SkillKeyById(int id) =>
        SkillIds.FirstOrDefault(kv => kv.Value == id).Key;

    /// <summary>Does this skill key exist in the content tables? Callers validate
    /// save-loaded keys against it so a renamed/removed skill can't crash the build.</summary>
    public static bool HasSkill(string key) => SkillRows.ContainsKey(key);

    /// <summary>The class kit as a LADDER (Pass 3): one spell at level 1, a new one
    /// unlocked at every level after, until the class list runs dry.</summary>
    public static IEnumerable<string> ClassSkillsAt(string classId, int level) =>
        Classes[classId].Skills.Take(Math.Max(1, level));

    /// <summary>A unit's spell keys in kit order: the class signature, then taught essences.</summary>
    public static IEnumerable<string> UnitSkillKeys(CampaignUnit u) =>
        ClassSkillsAt(u.ClassId, u.Level)
            .Concat(u.EssenceSlots.Select(EssenceSkill).Where(k => k != null).Cast<string>());

    /// <summary>Build a unit's skill at its current bought rank (the kit screen's view).</summary>
    public static SpellDef UnitSkill(CampaignUnit u, string key) => BuildSkill(key, u.RankOf(key));

    /// <summary>The class's base AP/MP (the character sheet shows base + gear bonuses).</summary>
    public static (int Ap, int Mp) ClassApMp(string classId) =>
        (Classes[classId].Ap, Classes[classId].Mp);

    /// <summary>Preview a skill one rank up (so the spend button can show what changes).</summary>
    public static SpellDef SkillAtRank(string key, int rank) => BuildSkill(key, rank);

    /// <summary>Manually spend one spell point ranking a skill up (the 1.29 spend screen).</summary>
    public static bool RankUp(CampaignUnit u, string key)
    {
        if (u.SpellPoints <= 0 || u.RankOf(key) >= MaxRank(key)) return false;
        u.SpellPoints--;
        u.SpellRanks[key] = u.RankOf(key) + 1;
        return true;
    }

    /// <summary>
    /// Spend a unit's banked spell points by the class template (Bible §6.3: 1 point per level;
    /// mercs auto-spend, the avatar too until the manual spend screen ships): signature skills
    /// first, then taught essence skills, each to its max authored rank. Returns "ranked up" lines.
    /// </summary>
    public static List<string> AutoSpendSpellPoints(CampaignUnit u)
    {
        var log = new List<string>();
        var order = ClassSkillsAt(u.ClassId, u.Level)
            .Concat(u.EssenceSlots.Select(EssenceSkill).Where(k => k != null).Cast<string>());
        foreach (var key in order)
        {
            while (u.SpellPoints > 0 && u.RankOf(key) < MaxRank(key))
            {
                u.SpellPoints--;
                u.SpellRanks[key] = u.RankOf(key) + 1;
                log.Add($"{u.Name} ranked {BuildSkill(key, u.RankOf(key)).Name}");
            }
        }
        return log;
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
        "steal_range" => SpellEffect.StealRange(e.Cells, e.Turns),
        "grant_ap" => SpellEffect.GrantAp(e.Min),
        "status" => SpellEffect.ApplyStatus(ParseStatus(e.Status), e.Mag, e.Turns),
        "self_damage" => SpellEffect.SelfHpCost(e.Min),
        "lifesteal" => SpellEffect.Lifesteal(ParseElement(e.Element), e.Min, e.Max),
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
        "damagedebuff" or "weaken" => StatusKind.DamageDebuff,
        "defensebuff" or "armor" => StatusKind.DefenseBuff,
        "vulnerable" or "vuln" => StatusKind.Vulnerable,
        "rangebuff" => StatusKind.RangeBuff,
        "rangedebuff" => StatusKind.RangeDebuff,
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

    // Dofus 1.29 mob grades: a grade step is +30% HP, +15% stats, +25% XP/gold. Deep packs and
    // Crypt rooms field higher grades — the depth gradient wearing mob levels (Bible §5, §8).
    private static int GradeHp(int v, int g) => v * (100 + 30 * (Math.Max(1, g) - 1)) / 100;
    private static int GradeStat(int v, int g) => v * (100 + 15 * (Math.Max(1, g) - 1)) / 100;
    private static int GradeReward(int v, int g) => v * (100 + 25 * (Math.Max(1, g) - 1)) / 100;

    /// <summary>Build a skeleton mob of the given id and grade at a cell.</summary>
    public static Fighter MakeMob(string mobId, string unitId, CellCoord pos, int grade = 1)
    {
        var m = Mobs[mobId];
        var f = new Fighter
        {
            Id = unitId, Name = m.Name, Team = Team.Enemy, Archetype = m.Id,
            Policy = ParsePolicy(m.Policy),
            PreferredRangeMin = m.PrefRangeMin, PreferredRangeMax = m.PrefRangeMax,
            MaxHp = GradeHp(m.MaxHp, grade), Hp = GradeHp(m.MaxHp, grade), BaseAp = m.Ap, BaseMp = m.Mp,
            Strength = GradeStat(m.Strength, grade), Intelligence = GradeStat(m.Intelligence, grade),
            Chance = GradeStat(m.Chance, grade), Agility = GradeStat(m.Agility, grade),
            Initiative = m.Initiative,
            Level = Math.Max(1, grade), // a mob's Level IS its grade (drives reward scaling)
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

    /// <summary>Grade-aware rewards for a defeated mob fighter (its Level carries the grade).</summary>
    public static int MobXpOf(Fighter mob) => GradeReward(MobXp(mob.Archetype), mob.Level);
    public static int MobStonesOf(Fighter mob) => GradeReward(MobStones(mob.Archetype), mob.Level);

    /// <summary>The essence a mob can drop and its percent chance (Bible §5), or (null, 0).</summary>
    public static (string? essence, int rate) MobDrop(string mobId) =>
        Mobs.TryGetValue(mobId, out var m) ? (m.Essence, m.Drop) : (null, 0);

    /// <summary>Percent chance this mob drops an Adventurer-set piece (Bible §5 drop-table peaks).</summary>
    public static int MobGear(string mobId) => Mobs.TryGetValue(mobId, out var m) ? m.Gear : 0;

    // ----- Equipment + panoply (Bible §6.10) ----------------------------------------

    /// <summary>The Graveyard's one starter panoply (Bible §5): the set its mobs and the Sexton drop.</summary>
    public const string GraveyardSet = "adventurer";
    /// <summary>The Crypt's panoply — five Bonewrought pieces, +1 AP at the full set.</summary>
    public const string CryptSet = "bonewrought";

    // ----- Essences (Bible §6.5): consumables that teach their mob's signature skill -----

    public sealed record EssenceDto(string Name, string Skill, string? Blurb);
    private static EssenceDto[]? _essenceList;
    private static EssenceDto[] EssenceList => _essenceList ??=
        JsonSerializer.Deserialize<EssenceDto[]>(TitheTables.EssencesJson, J)!;
    private static Dictionary<string, EssenceDto>? _essences;
    private static Dictionary<string, EssenceDto> EssenceRows => _essences ??=
        EssenceList.ToDictionary(e => e.Name);

    /// <summary>The Temple's shelf rotates one essence per city return (Bible: she sells everything,
    /// expensively — gambling is the discount path, never the only path).</summary>
    public static string EssenceForSale(int dives) => EssenceList[((dives % EssenceList.Length) + EssenceList.Length) % EssenceList.Length].Name;

    /// <summary>The skill an essence teaches when consumed, or null for an unknown essence.</summary>
    public static string? EssenceSkill(string essence) =>
        EssenceRows.TryGetValue(essence, out var e) ? e.Skill : null;

    /// <summary>Display name of the skill an essence teaches (for shop rows and toasts).</summary>
    public static string EssenceSkillName(string essence) =>
        EssenceSkill(essence) is { } key && Skills.TryGetValue(key, out var s) ? s.Name : essence;

    public static ItemDto? Item(string itemId) => Items.TryGetValue(itemId, out var i) ? i : null;
    public static string ItemName(string itemId) => Item(itemId)?.Name ?? itemId;
    public static string ItemSlot(string itemId) => Item(itemId)?.Slot ?? "";

    /// <summary>Compact readout of a piece's stats for the equip screen ("+6 STR +6 INT +3 POW").</summary>
    public static string ItemStatLine(string itemId)
    {
        if (Item(itemId) is not { } it) return "";
        var parts = new List<string>();
        void Add(int v, string tag) { if (v != 0) parts.Add($"+{v} {tag}"); }
        Add(it.Vitality, "VIT"); Add(it.Strength, "STR"); Add(it.Intelligence, "INT");
        Add(it.Chance, "CHA"); Add(it.Agility, "AGI"); Add(it.Wisdom, "WIS");
        Add(it.Power, "POW"); Add(it.Ap, "AP"); Add(it.Mp, "MP");
        return string.Join(" ", parts);
    }

    /// <summary>Pick a random set piece not in <paramref name="owned"/>, or null if the set is complete.</summary>
    public static string? RandomUnownedPiece(string setId, Func<string, bool> owned, IRng rng)
    {
        var pool = SetPieceIds(setId).Where(id => !owned(id)).ToList();
        return pool.Count == 0 ? null : pool[rng.Roll(1, pool.Count) - 1];
    }

    /// <summary>The set-less behavior-rule uniques (the +1 MP boots idiom).</summary>
    public static IReadOnlyList<string> UniqueIds() =>
        Items.Values.Where(i => i.Set == null).Select(i => i.Id).ToList();

    /// <summary>
    /// Resolve a successful gear roll into a concrete unowned piece: usually the named set, but
    /// ~15% of finds are a UNIQUE — the screaming kind. Null when everything is owned already.
    /// </summary>
    public static string? RandomUnownedGear(string setId, Func<string, bool> owned, IRng rng)
    {
        var uniques = UniqueIds().Where(id => !owned(id)).ToList();
        if (uniques.Count > 0 && rng.Roll(1, 100) <= 15)
            return uniques[rng.Roll(1, uniques.Count) - 1];
        return RandomUnownedPiece(setId, owned, rng) ??
               (uniques.Count > 0 ? uniques[rng.Roll(1, uniques.Count) - 1] : null);
    }

    // Which slots each family tends to carry to its grave (Bible §5 flavor): the drop pool
    // a mob's gear roll draws from. The Sexton draws from the whole panoply.
    private static readonly Dictionary<string, string[]> GearPools = new()
    {
        ["barrow_husk"] = new[] { "adv_belt", "adv_boots" },
        ["gravehound"] = new[] { "adv_boots", "adv_ring" },
        ["marrow_spitter"] = new[] { "adv_amulet", "adv_ring" },
        ["grave_mite"] = new[] { "adv_ring" },
        ["bone_piper"] = new[] { "adv_amulet", "adv_hat", "bone_crown", "bone_loop" },
        ["tomb_wraith"] = new[] { "adv_cape", "adv_amulet", "bone_mantle", "bone_loop" },
        ["grave_ghoul"] = new[] { "adv_blade", "adv_belt", "bone_girdle", "bone_maul" },
        ["crypt_warden"] = new[] { "adv_blade", "adv_hat", "bone_maul", "bone_crown" },
        ["sexton"] = new[] { "bone_maul", "bone_crown", "bone_mantle", "bone_loop", "bone_girdle" },
    };

    /// <summary>Resolve a gear roll AGAINST THE FAMILY'S POOL: ~15% a unique, else an unowned
    /// piece the family carries, else any unowned Adventurer piece (pool exhausted).</summary>
    public static string? RandomUnownedGearFor(string archetype, Func<string, bool> owned, IRng rng)
    {
        var uniques = UniqueIds().Where(id => !owned(id)).ToList();
        if (uniques.Count > 0 && rng.Roll(1, 100) <= 15)
            return uniques[rng.Roll(1, uniques.Count) - 1];
        var pool = (GearPools.TryGetValue(archetype, out var p) ? p : SetPieceIds(GraveyardSet).ToArray())
            .Where(id => !owned(id)).ToList();
        if (pool.Count > 0) return pool[rng.Roll(1, pool.Count) - 1];
        return RandomUnownedGear(GraveyardSet, owned, rng);
    }

    /// <summary>Consumable drop rates per family (bread% , draught%): the dead carry supplies.</summary>
    public static (int Bread, int Draught) MobSundries(string archetype) => archetype switch
    {
        "barrow_husk" => (15, 0), "gravehound" => (10, 0), "grave_mite" => (12, 0),
        "grave_ghoul" => (10, 0), "marrow_spitter" => (0, 8), "bone_piper" => (0, 10),
        "tomb_wraith" => (0, 8), "crypt_warden" => (0, 10), "sexton" => (0, 100),
        _ => (0, 0),
    };

    /// <summary>The set's tier table as display lines ("3 PC: +18 VIT +3 STR ...").</summary>
    public static IEnumerable<(int Pieces, string Text)> SetTierSummaries(string setId)
    {
        if (!Sets.TryGetValue(setId, out var set)) yield break;
        foreach (var t in set.Tiers.OrderBy(t => t.Pieces))
        {
            var parts = new List<string>();
            if (t.Vitality != 0) parts.Add($"+{t.Vitality} VIT");
            if (t.Strength != 0) parts.Add($"+{t.Strength} STR");
            if (t.Intelligence != 0) parts.Add($"+{t.Intelligence} INT");
            if (t.Chance != 0) parts.Add($"+{t.Chance} CHA");
            if (t.Agility != 0) parts.Add($"+{t.Agility} AGI");
            if (t.Wisdom != 0) parts.Add($"+{t.Wisdom} WIS");
            if (t.Power != 0) parts.Add($"+{t.Power} POW");
            if (t.Ap != 0) parts.Add($"+{t.Ap} AP");
            if (t.Mp != 0) parts.Add($"+{t.Mp} MP");
            yield return (t.Pieces, string.Join(" ", parts));
        }
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

        // 1.29 rule: characteristic points are the PLAYER'S to spend (five per level, held on
        // the unit until allocated). The class contributes only its level-1 base.
        int vit = c.Vitality + u.SpentOn("vit");
        int str = c.Strength + u.SpentOn("str");
        int intel = c.Intelligence + u.SpentOn("int");
        int cha = c.Chance + u.SpentOn("cha");
        int agi = c.Agility + u.SpentOn("agi");
        int wis = c.Wisdom + u.SpentOn("wis");
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

    /// <summary>
    /// Spend all of a unit's banked characteristic points along its class growth ratios —
    /// the headless sims' stand-in for a player, and the "auto" button in the kit screen.
    /// </summary>
    public static void AutoSpendStats(CampaignUnit u)
    {
        var g = Classes[u.ClassId].Growth ?? new GrowthDto(1, 1, 1, 1, 1, 1);
        var weights = new (string key, int w)[]
        {
            ("vit", g.Vitality), ("str", g.Strength), ("int", g.Intelligence),
            ("cha", g.Chance), ("agi", g.Agility), ("wis", g.Wisdom),
        };
        int total = weights.Sum(x => x.w);
        if (total == 0) { weights = new[] { ("vit", 1), ("str", 1), ("int", 1), ("cha", 1), ("agi", 1), ("wis", 1) }; total = 6; }
        // Tiered costs mean a weighted key can become unaffordable while points remain; keep
        // spending by weight only while something lands, then sweep any remainder into Vitality
        // (always 1:1) so a merc never banks dead points it will never use.
        int guard = 10_000;
        bool spent = true;
        while (spent && u.StatPoints > 0 && guard-- > 0)
        {
            spent = false;
            foreach (var (key, w) in weights)
                for (int i = 0; i < w && u.StatPoints > 0; i++)
                    if (u.SpendStat(key)) spent = true;
        }
        while (u.StatPoints > 0 && u.SpendStat("vit")) { }
    }

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
    public static int MobStones(string mobId) => Mobs.TryGetValue(mobId, out var m) ? m.Stones : 0;

    /// <summary>The Sexton's court — the Crypt's boss encounter, as a mob composition.</summary>
    public static IReadOnlyList<string> CryptComp() =>
        JsonSerializer.Deserialize<EncounterDto>(TitheTables.EncounterBossJson, J)!.Spawns.Select(s => s.Mob).ToList();

    /// <summary>The Crypt's linear room chain (Bible §6.8): escalating packs, the last a boss room.</summary>
    public sealed record CryptRoom(string Name, string[] Comp, bool Boss, int Grade = 1);
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
            Pos = pos,
            // Combat kit = class skills + everything taught by consumed essences (Bible §6.5),
            // each at the unit's bought rank (Bible §6.3 spell points).
            Spells = ClassSkillsAt(u.ClassId, u.Level)
                .Concat(u.EssenceSlots.Select(EssenceSkill).Where(k => k != null).Cast<string>())
                .Select(k => BuildSkill(k, u.RankOf(k))).ToArray(),
        };
    }

    // ----- Encounter assembly -------------------------------------------------------

    /// <summary>Arena by variant — now always a generated ground (the player asked for proper
    /// auto-generated maps); the handcrafted tables remain as reference data.</summary>
    public static MapData Arena(int variant = 0) => GenerateArena(unchecked(variant * 2654435761u).GetHashCode());

    /// <summary>
    /// Procedurally generate an irregular fight ground, deterministic by seed:
    /// the silhouette is a union of overlapping ellipses on a void sea (never a plain rectangle),
    /// obstacles grow in small CLUSTERS, and a flood-fill pass guarantees the crew's west landing
    /// can always reach the pack's east ground — clustered, but never sealed. Unreachable pockets
    /// become void so nothing can ever spawn where no path leads.
    /// </summary>
    public static MapData GenerateArena(int seed)
    {
        const int W = 15, H = 13;
        var rng = new Random(seed);
        var west = new CellCoord(2, H / 2);
        var east = new CellCoord(W - 3, H / 2);

        // 1. The landmass: one broad ellipse that always spans west→east, plus a few side blobs.
        bool[,] land = new bool[W, H];
        void Blob(double cx, double cy, double rx, double ry)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    double dx = (x - cx) / rx, dy = (y - cy) / ry;
                    if (dx * dx + dy * dy <= 1.0) land[x, y] = true;
                }
        }
        Blob(W * 0.5 + rng.NextDouble() - 0.5, H * 0.5 + rng.NextDouble() - 0.5,
             W * 0.40 + rng.NextDouble() * 1.6, H * 0.36 + rng.NextDouble() * 1.4);
        int blobs = 2 + rng.Next(3);
        for (int i = 0; i < blobs; i++)
            Blob(2.5 + rng.NextDouble() * (W - 5), 2.0 + rng.NextDouble() * (H - 4),
                 2.0 + rng.NextDouble() * 2.8, 1.6 + rng.NextDouble() * 2.4);
        for (int y = H / 2 - 2; y <= H / 2 + 2; y++)             // west landing + east ground,
            for (int x = 1; x <= 3; x++)                          // always solid
            { land[x, y] = true; land[W - 1 - x, y] = true; }

        var tiles = new char[W, H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                tiles[x, y] = land[x, y] ? '.' : 'o';

        // 2. Obstacle clusters: a few seeds, each grown by random adjacent steps — accumulation
        //    here and there, open ground elsewhere. The start strip and both anchors stay clear.
        bool NearStart(int x, int y) => x <= 3 && Math.Abs(y - H / 2) <= 2;
        bool Protected(int x, int y) => NearStart(x, y)
            || (Math.Abs(x - east.X) <= 1 && Math.Abs(y - east.Y) <= 1);
        int clusters = 3 + rng.Next(3), placed = 0;
        for (int c = 0; c < clusters; c++)
        {
            int cx = 4 + rng.Next(W - 6), cy = 1 + rng.Next(H - 2);
            if (!land[cx, cy] || Protected(cx, cy)) continue;
            int size = 2 + rng.Next(4);                          // 2-5 stones per cluster
            char kind = rng.NextDouble() < 0.30 ? 'T' : '#';
            for (int s = 0; s < size && placed < 14; s++)
            {
                if (land[cx, cy] && !Protected(cx, cy) && tiles[cx, cy] == '.')
                { tiles[cx, cy] = rng.NextDouble() < 0.8 ? kind : (kind == '#' ? 'T' : '#'); placed++; }
                int step = rng.Next(4);
                cx = Math.Clamp(cx + (step == 0 ? 1 : step == 1 ? -1 : 0), 1, W - 2);
                cy = Math.Clamp(cy + (step == 2 ? 1 : step == 3 ? -1 : 0), 1, H - 2);
            }
        }

        // 3. Connectivity guarantee: flood from the west landing; chip away frontier obstacles
        //    until the east ground is reached, then drown any leftover unreachable pockets.
        bool Walk(char t) => t == '.';
        HashSet<CellCoord> Flood()
        {
            var seen = new HashSet<CellCoord> { west };
            var q = new Queue<CellCoord>(); q.Enqueue(west);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = new CellCoord(c.X + dx, c.Y + dy);
                    if (n.X < 0 || n.Y < 0 || n.X >= W || n.Y >= H) continue;
                    if (!Walk(tiles[n.X, n.Y]) || !seen.Add(n)) continue;
                    q.Enqueue(n);
                }
            }
            return seen;
        }
        var reach = Flood();
        while (!reach.Contains(east))
        {
            // Clear one obstacle that borders the reached region (opens the shortest new ground).
            var openable = new List<CellCoord>();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (tiles[x, y] is '#' or 'T'
                        && new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }
                           .Any(d => reach.Contains(new CellCoord(x + d.Item1, y + d.Item2))))
                        openable.Add(new CellCoord(x, y));
            if (openable.Count == 0) break;                       // fully open land — cannot happen
            var pick = openable[rng.Next(openable.Count)];
            tiles[pick.X, pick.Y] = '.';
            reach = Flood();
        }
        for (int y = 0; y < H; y++)                               // pockets nothing can reach → void
            for (int x = 0; x < W; x++)
                if (tiles[x, y] == '.' && !reach.Contains(new CellCoord(x, y)))
                    tiles[x, y] = 'o';

        // 4. The crew's placement zone: 'P' on the landing, 's' on its nearest walkable ring.
        tiles[west.X, west.Y] = 'P';
        int starts = 0;
        foreach (var c in reach.OrderBy(c => c.DistanceTo(west)).ThenBy(c => c.X))
        {
            if (starts >= 8) break;
            if (c == west || tiles[c.X, c.Y] != '.') continue;
            if (c.X > 4) continue;                                // the zone hugs the west edge
            tiles[c.X, c.Y] = 's'; starts++;
        }

        var rows = new string[H];
        for (int y = 0; y < H; y++)
        {
            var sb = new System.Text.StringBuilder(W);
            for (int x = 0; x < W; x++) sb.Append(tiles[x, y]);
            rows[y] = sb.ToString();
        }
        return MapLoader.Parse(JsonSerializer.Serialize(new { name = "The Yard", rows }));
    }

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
                                              IReadOnlyList<string> packMobs, IRng rng, int grade = 1,
                                              bool jumped = false, int arenaVariant = 0)
    {
        var map = Arena(arenaVariant);
        var field = map.ToBattlefield();

        // Prepared tier: the tidy start-zone cluster (the player may re-place). Jumped tier
        // (Bible §6.6: "Jumped, with enemy-defined scattered cells, arrives with aggro-catch"):
        // a hunting pack caught the crew mid-yard — they start scattered in the open middle,
        // no placement phase, the pack already around them.
        var startCells = jumped
            ? new List<CellCoord> { new(6, 5), new(7, 7), new(5, 7), new(8, 4) }
            : map.PlayerStartCells.OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
        var fighters = new List<Fighter>();
        for (int i = 0; i < party.Count; i++)
        {
            var want = i < startCells.Count ? startCells[i] : map.PlayerSpawn;
            if (!map.IsWalkable(want) || fighters.Any(f => f.Pos == want))
                want = NearestFreeCell(map, fighters, want);
            fighters.Add(MakeCrewMember(party[i], want));
        }

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
            fighters.Add(MakeMob(mob, $"mob_{n++}_{mob}", cell, grade));
        }

        return new CombatEngine(field, fighters, rng, (kind, team, cell, id) => MakeMob(kind, id, cell, grade));
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

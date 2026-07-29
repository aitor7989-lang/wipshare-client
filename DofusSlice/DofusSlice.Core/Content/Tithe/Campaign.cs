namespace DofusSlice.Core.Content.Tithe;

/// <summary>A survivor's hidden nature (Bible §6.12). Regular hires are None; survivors found
/// below are Loyal or Grasping — and a Grasping one leaves with a cut of the haul when the take
/// is heavy and the bell is low. The Temple vets it for a fee; betrayal reveals it for free.</summary>
public enum Temperament { None, Loyal, Grasping }

/// <summary>
/// A player-managed unit that persists across dives (Bible §6.1): the avatar or a hired
/// mercenary. Carries level/XP and the meta-persistent <see cref="Wounded"/> flag (-1 PA / -1 PM
/// in combat until cured in the city).
/// </summary>
public sealed class CampaignUnit
{
    public required string Id { get; init; }
    public required string ClassId { get; init; }
    public required string Name { get; init; }
    public bool IsAvatar { get; init; }
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public bool Wounded { get; set; }

    /// <summary>Carried HP within a dive (null = full). Rests to full in the city; Hard Bread mends it mid-dive.</summary>
    public int? CurrentHp { get; set; }

    /// <summary>Equipped item ids (Bible §6.10 slots). Only the avatar re-gears; mercs keep their hire kit.
    /// The unit's effective stats fold these in via <see cref="TitheContent.StatsOf"/>.</summary>
    public List<string> Equipment { get; init; } = new();

    /// <summary>Consumed essences (Bible §6.5): two campaign-permanent slots. Each adds its skill
    /// to this unit's combat kit; essences never check class — a bad fit is allowed and wasted.</summary>
    public List<string> EssenceSlots { get; init; } = new();

    public const int MaxEssenceSlots = 2;
    public bool HasFreeEssenceSlot => EssenceSlots.Count < MaxEssenceSlots;

    /// <summary>A survivor's hidden nature; None for the avatar and regular hires (Bible §6.12).</summary>
    public Temperament Temperament { get; set; } = Temperament.None;

    /// <summary>Has the Temple vetted this unit (temperament revealed to the player)?</summary>
    public bool Vetted { get; set; }

    /// <summary>Unspent spell points (Bible §6.3: +1 per level; ranks change shape/economics).</summary>
    public int SpellPoints { get; set; }

    /// <summary>Unspent characteristic points (1.29: five per level, spent by the PLAYER).</summary>
    public int StatPoints { get; set; }

    /// <summary>Manually allocated characteristic points: keys vit/str/int/cha/agi/wis.</summary>
    public Dictionary<string, int> SpentStats { get; init; } = new();

    public int SpentOn(string key) => SpentStats.GetValueOrDefault(key);

    /// <summary>How many invested points buy ONE more tier before the cost climbs. Scaled to the
    /// slice's ~95-point run so the classic 1.29 soft-cap actually bites within 20 levels.</summary>
    public const int StatTierSize = 20;

    /// <summary>The Dofus 1.29 soft-cap: specialising a characteristic costs more per point the
    /// deeper you pour in. Cost to raise <paramref name="key"/> by ONE more, in characteristic
    /// points — 1 for the first tier, then 2, 3, 4, 5 as you climb. Vitality never scales (the
    /// classic 1:1 HP dump); Wisdom is premium (XP gain + AP/MP-loss dodge) and costs one extra.</summary>
    public int StatStep(string key)
    {
        if (key == "vit") return 1;                 // Vitality is the eternal 1:1 dump
        int cost = 1 + SpentOn(key) / StatTierSize; // 0-19 → 1, 20-39 → 2, 40-59 → 3, …
        if (key == "wis") cost += 1;                // Wisdom always costs one above its tier
        return cost;
    }

    /// <summary>True if the next point in <paramref name="key"/> is affordable right now.</summary>
    public bool CanSpendStat(string key) => StatPoints >= StatStep(key);

    /// <summary>Spend the tiered cost to raise one characteristic by one. Fails (no change) when
    /// the banked points can't cover the current tier's cost.</summary>
    public bool SpendStat(string key)
    {
        int cost = StatStep(key);
        if (StatPoints < cost) return false;
        StatPoints -= cost;
        SpentStats[key] = SpentOn(key) + 1;
        return true;
    }

    /// <summary>Bought spell ranks, key → rank (absent = rank 1).</summary>
    public Dictionary<string, int> SpellRanks { get; init; } = new();
    public int RankOf(string skill) => SpellRanks.TryGetValue(skill, out int r) ? r : 1;

    /// <summary>
    /// Cumulative XP to reach each level, index = level — the classic Dofus 1.29 table, levels
    /// 1–20 (Bible §6.3: adopt the 1.29 curve verbatim; cross-check against an emulator dump in
    /// the §9 pass). Landed TOGETHER with Dofus-scale per-mob XP values, since curve and mob XP
    /// only pace correctly as a pair. Mob stones are decoupled from XP (mobs carry a "stones" column).
    /// </summary>
    private static readonly int[] XpCurve =
    {
        0, 0, 110, 650, 1500, 2800, 4800, 7300, 10500, 14500, 19200,
        25200, 32600, 41000, 50500, 61000, 75000, 91000, 115000, 142000, 171000,
        // Levels 21-30 continue THIS curve's own shape (its per-level step was growing ~10-13%
        // through the teens, so the steps keep widening at that rate). The band exists so the
        // campaign has somewhere to put a level-30 ceiling and a rank-5 spell ladder; the numbers
        // are ours, extrapolated from the rows above, not lifted from anywhere.
        202000, 236000, 273000, 314000, 359000, 408000, 462000, 521000, 586000, 657000,
    };

    /// <summary>The campaign's level ceiling — the last level the XP curve actually measures.</summary>
    public const int MaxLevel = 30;

    /// <summary>XP needed to advance from <paramref name="level"/> to the next (the 1.29 per-level cost).</summary>
    public static int XpForNextLevel(int level)
    {
        if (level < 1) level = 1;
        if (level + 1 < XpCurve.Length) return XpCurve[level + 1] - XpCurve[level];
        // Past the mined band: keep climbing on the curve's final measured step.
        return XpCurve[^1] - XpCurve[^2];
    }

    public void GainXp(int xp)
    {
        Xp += Math.Max(0, xp);
        while (Level < MaxLevel && Xp >= XpForNextLevel(Level))
        {
            Xp -= XpForNextLevel(Level); Level++;
            SpellPoints++;   // 1.29: one spell point per level (Bible §6.3)
            StatPoints += 5; // 1.29: five characteristic points per level, player-spent
            CurrentHp = null; // the ding restores you whole (Pass 3: level up = full life)
        }
    }
}

/// <summary>
/// The whole campaign state (Bible §6.1, §6.11, §6.15): the crew, the ledger, consumables, the
/// essence stash and the tithe schedule. Pure data + operations so the loop is testable headless.
/// "Campaign over" is Bible §3.1.4: no player-managed unit remains (the avatar was lost in a wipe).
/// </summary>
public sealed class Campaign
{
    public List<CampaignUnit> Crew { get; init; } = new();
    public int Stones { get; set; }
    public int Bread { get; set; }        // Hard Bread: restores HP outside combat
    public int Draughts { get; set; }     // Physicker's Draught: cures Wounded
    public int Dives { get; set; }        // completed dives — drives the tithe cadence
    public int TithesPaid { get; set; }
    public int TitheDebt { get; set; }
    public List<string> Essences { get; init; } = new();

    /// <summary>Unequipped gear held between dives (Bible §6.10 shared stash).</summary>
    public List<string> Stash { get; init; } = new();

    public CampaignUnit? Avatar => Crew.FirstOrDefault(u => u.IsAvatar);
    public IEnumerable<CampaignUnit> Mercenaries => Crew.Where(u => !u.IsAvatar);
    public bool Over => Avatar == null;

    /// <summary>Rest in the city: HP back to full (wounds are not cured — that needs a Draught).</summary>
    public void RestCrew() { foreach (var u in Crew) u.CurrentHp = null; }

    /// <summary>A crypt breather between sealing doors: recover a fraction of each member's max
    /// HP (never past full, never below where they stand). Not a full heal — attrition still
    /// bites across a dive — but enough that the next room isn't a death sentence. Returns the
    /// total HP mended so the rest screen can show it.</summary>
    public int RestCrewPartial(double frac)
    {
        int healed = 0;
        foreach (var u in Crew)
        {
            int max = TitheContent.UnitMaxHp(u);
            int before = u.CurrentHp ?? max;
            int after = System.Math.Min(max, before + (int)System.Math.Ceiling(max * frac));
            if (after > before) { u.CurrentHp = after; healed += after - before; }
        }
        return healed;
    }

    /// <summary>Up to three units dive together — the avatar leads, mercenaries fill the rest.</summary>
    public List<CampaignUnit> DiveParty =>
        Crew.OrderByDescending(u => u.IsAvatar).Take(3).ToList();

    public static Campaign NewGame(string avatarClass)
    {
        // You start ALONE (the Dofus way): one avatar, enough essence stones to hire a crew at the
        // Hiring Post if you choose to — building the party is the player's first decision.
        var c = new Campaign { Stones = 160, Bread = 2, Draughts = 0 };
        c.Crew.Add(new CampaignUnit { Id = "avatar", ClassId = avatarClass, Name = "You", IsAvatar = true });
        return c;
    }

    // ----- City services (Bible §4, §5, §6.10, §6.11) -------------------------------

    public bool BuyBread()
    {
        if (Stones < TitheContent.Prices.HardBread) return false;
        Stones -= TitheContent.Prices.HardBread; Bread++;
        return true;
    }

    public bool BuyDraught()
    {
        if (Stones < TitheContent.Prices.Draught) return false;
        Stones -= TitheContent.Prices.Draught; Draughts++;
        return true;
    }

    /// <summary>Cure one Wounded unit with a Draught (Bible §3.1.4). Returns false if none to spend/cure.</summary>
    public bool TreatWounded(CampaignUnit u)
    {
        if (Draughts <= 0 || !u.Wounded) return false;
        Draughts--; u.Wounded = false;
        return true;
    }

    public int HirePrice(int level) => TitheContent.Prices.HireBasePerLevel * level;

    /// <summary>Names for hired company, so a companion is a PERSON and not "{class}-merc".
    /// Drawn from the campaign's own RNG-free counter, so a run reads the same way twice.</summary>
    private static readonly string[] HireNames =
    {
        "Corbin", "Mera", "Halgrim", "Silt", "Odile", "Brann", "Vess", "Tolm",
        "Ysra", "Ganne", "Peregrin", "Ash", "Nettle", "Wren", "Quill", "Rooke",
    };

    public static string HireNameFor(int seatIndex) => HireNames[Math.Abs(seatIndex) % HireNames.Length];

    public bool Hire(string classId, string name, int level, Temperament temperament = Temperament.None)
    {
        if (Crew.Count >= 3) return false;               // slice caps the party at three
        int price = HirePrice(level);
        if (Stones < price) return false;
        Stones -= price;
        // A hire arrives with its level's banked points. Temperament is the system that gives a
        // companion character — a Hiring Post hire never had one set, so only survivors could ever
        // betray you and the Temple's "vet them" service had nothing to read.
        Crew.Add(new CampaignUnit
        { Id = $"merc_{Crew.Count}_{classId}", ClassId = classId, Name = name, Level = level,
          SpellPoints = level - 1, StatPoints = (level - 1) * 5, Temperament = temperament });
        return true;
    }

    /// <summary>Crush a held essence into essence stones at the Tithe-Keeper's wheel — the
    /// dark bargain: knowledge ground back into coin. Same substance, lesser form.</summary>
    public bool CrushEssence(string essence)
    {
        if (!Essences.Remove(essence)) return false;
        Stones += TitheContent.Prices.EssenceSell;
        return true;
    }

    /// <summary>Buy the Temple's shelf essence at her painful price (Bible §6.5: gambling for the
    /// drop is the discount path — the Temple is the certain, expensive one).</summary>
    public bool BuyEssence(string essence)
    {
        if (TitheContent.EssenceSkill(essence) == null) return false;
        if (Stones < TitheContent.Prices.EssenceBuy) return false;
        Stones -= TitheContent.Prices.EssenceBuy;
        Essences.Add(essence);
        return true;
    }

    /// <summary>Eat one Hard Bread NOW (the leader's bag): mends the most-hurt crew member.
    /// Returns a report line, or null when there is no bread or nobody is hurt.</summary>
    public string? EatBread()
    {
        if (Bread <= 0) return null;
        var hurt = Crew
            .Where(u => (u.CurrentHp ?? int.MaxValue) < TitheContent.UnitMaxHp(u))
            .OrderBy(u => (float)(u.CurrentHp ?? int.MaxValue) / TitheContent.UnitMaxHp(u))
            .FirstOrDefault();
        if (hurt == null) return null;
        Bread--;
        int max = TitheContent.UnitMaxHp(hurt);
        int before = hurt.CurrentHp ?? max;
        hurt.CurrentHp = Math.Min(max, before + TitheContent.Prices.BreadHeal);
        return $"{hurt.Name} eats hard bread (+{hurt.CurrentHp.Value - before} HP)";
    }

    /// <summary>Temple surgery (Bible §6.5): strip an essence out of a unit's slot. Very expensive
    /// and the essence is DESTROYED (the Bible leans destroyed, not refunded). Frees the slot.</summary>
    public bool RemoveEssence(CampaignUnit u, string essence)
    {
        if (!u.EssenceSlots.Contains(essence)) return false;
        if (Stones < TitheContent.Prices.EssenceRemoval) return false;
        Stones -= TitheContent.Prices.EssenceRemoval;
        u.EssenceSlots.Remove(essence);
        return true;
    }

    /// <summary>
    /// Consume a held essence to teach its skill to a unit (Bible §6.5): learning IS consumption,
    /// the slot is campaign-permanent, and no class check is made — a wasted fit is the player's
    /// to commit to. Fails if the unit's two slots are full, it already knows this essence, or the
    /// essence is unknown/not held.
    /// </summary>
    public bool TeachEssence(CampaignUnit u, string essence)
    {
        if (!u.HasFreeEssenceSlot || u.EssenceSlots.Contains(essence)) return false;
        if (TitheContent.EssenceSkill(essence) == null || !Essences.Contains(essence)) return false;
        Essences.Remove(essence);
        u.EssenceSlots.Add(essence);
        return true;
    }

    // ----- Equipment (Bible §6.10) --------------------------------------------------

    /// <summary>Does the campaign already hold this exact piece (equipped or stashed)?</summary>
    public bool OwnsGear(string itemId) =>
        Stash.Contains(itemId) || Crew.Any(u => u.Equipment.Contains(itemId));

    /// <summary>
    /// Take a dropped gear piece into the stash, then let the avatar auto-equip any upgrade (mercs
    /// never re-gear, Bible §6.6.9). Duplicates are ignored in the slice — each Adventurer piece is
    /// unique. Returns false if the piece is already owned or unknown.
    /// </summary>
    /// <summary>Stones paid for a drop you already own — the chase must never dry up.</summary>
    public const int SalvageStones = 45;

    public bool AddGear(string itemId)
    {
        if (TitheContent.Item(itemId) == null) return false;
        // A duplicate used to be dropped on the floor. With only 14 pieces in the game that meant
        // a finished collection turned the Sexton's GUARANTEED drop into a guaranteed no-op —
        // the reward for the hardest fight was nothing. Duplicates are salvaged for stones.
        if (OwnsGear(itemId)) { Stones += SalvageStones; return true; }
        Stash.Add(itemId);
        AutoEquipAvatar();
        return true;
    }

    /// <summary>A rough "how good is this piece" score. Every damage stat counts the same (the four
    /// elemental channels are equivalent in Dofus); Power counts extra since it feeds all of them.</summary>
    private static int GearWeight(string itemId)
    {
        var it = TitheContent.Item(itemId);
        return it == null ? 0
            : it.Vitality + (it.Strength + it.Intelligence + it.Chance + it.Agility) * 2 + it.Wisdom + it.Power * 3
            + (it.Ap + it.Mp) * 45; // behavior-rule bonuses outrank stat sticks (the +1 MP idiom)
    }

    /// <summary>Equip a stashed piece on a unit, filling a free slot or displacing a weaker one back
    /// to the stash. Returns true if it was equipped.</summary>
    private bool EquipFromStash(CampaignUnit u, string itemId)
    {
        string slot = TitheContent.ItemSlot(itemId);
        int cap = TitheContent.SlotCapacity(slot);
        var inSlot = u.Equipment.Where(id => TitheContent.ItemSlot(id) == slot).ToList();

        if (inSlot.Count < cap)
        {
            Stash.Remove(itemId); u.Equipment.Add(itemId); return true;
        }
        // Slot full: swap out the weakest piece if this one beats it.
        string weakest = inSlot.OrderBy(GearWeight).First();
        if (GearWeight(itemId) <= GearWeight(weakest)) return false;
        u.Equipment.Remove(weakest); Stash.Add(weakest);
        Stash.Remove(itemId); u.Equipment.Add(itemId);
        return true;
    }

    /// <summary>Let the avatar pull every upgrade out of the stash (watched-game convenience; the
    /// City equip screen is where a player would do this by hand).</summary>
    public void AutoEquipAvatar()
    {
        var a = Avatar;
        if (a == null) return;
        // Best-first so a slot lands its strongest available piece in one pass.
        foreach (var id in Stash.OrderByDescending(GearWeight).ToList())
            EquipFromStash(a, id);
    }

    /// <summary>Manually equip a stashed piece on a unit (City equip screen, Bible §6.13).</summary>
    public bool Equip(CampaignUnit u, string itemId) =>
        Stash.Contains(itemId) && EquipFromStash(u, itemId);

    /// <summary>Manually strip an equipped piece back to the stash.</summary>
    public bool Unequip(CampaignUnit u, string itemId)
    {
        if (!u.Equipment.Remove(itemId)) return false;
        Stash.Add(itemId);
        return true;
    }

    // ----- The tithe (Bible §3.1.3, §5) ---------------------------------------------

    /// <summary>The tithe falls due every Nth return to the city; the amount escalates.</summary>
    public bool TitheDue => Dives > 0 && Dives % TitheContent.Prices.TitheEveryNDives == 0;

    /// <summary>The Keeper's cut stops growing after this many payments. Without a plateau the
    /// tithe escalates forever against a flat dive income, so failure stops being a risk and
    /// becomes arithmetic — measured at 100% of runs dead by dive ~14.</summary>
    public const int TitheGrowthSteps = 4;

    /// <summary>What the Keeper wants this time. NOTE the missing TitheDebt term: rolling the
    /// unpaid amount into the next bill compounded a single early miss into certain death
    /// (100 -> 240 -> 420 -> collected). The STRIKE counter is the pressure now; the debt is a
    /// ledger the UI shames you with, not a spiral.</summary>
    public int TitheAmount =>
        TitheContent.Prices.TitheBase
        + TitheContent.Prices.TitheGrowth * Math.Min(TithesPaid, TitheGrowthSteps);

    /// <summary>Consecutive tithes missed. The game is NAMED for this debt and it had no teeth:
    /// an unpaid tithe only sized the next bill, forever, so nothing in the campaign could ever
    /// actually fail. Miss <see cref="TitheGrace"/> in a row and the Keeper collects in person.</summary>
    public int TitheStrikes { get; set; }

    /// <summary>How many consecutive misses the Keeper tolerates before taking everything.</summary>
    public const int TitheGrace = 3;

    /// <summary>Missed tithes remaining before the campaign ends (for the UI to warn with).</summary>
    public int TitheWarningsLeft => Math.Max(0, TitheGrace - TitheStrikes);

    /// <summary>
    /// Pay the Keeper. Paying in full clears the ledger and the strikes. A PARTIAL payment of at
    /// least half is good faith: the remainder is recorded as debt but costs you no strike — that
    /// is the difference between "poor this cycle" and "not paying", and without it a play-style
    /// that banks nothing (greedy) died 100% of the time with no way back.
    /// Paying under half is a strike; <see cref="TitheGrace"/> in a row and the Keeper collects.
    /// </summary>
    public bool PayTithe()
    {
        int due = TitheAmount;
        if (Stones >= due) { Stones -= due; TitheDebt = 0; TithesPaid++; TitheStrikes = 0; return true; }

        int paid = Math.Max(0, Stones);
        Stones -= paid;
        TitheDebt = due - paid;
        if (paid * 2 >= due) return false;              // good faith: debt recorded, no strike
        TitheStrikes++;
        if (TitheStrikes >= TitheGrace) Crew.Clear();   // the Keeper collects — campaign over
        return false;
    }

    /// <summary>The Sexton has been put down this many times. Each felling is a real ending: it
    /// banks a victory and the next Crypt comes back harder, so the boss is a climax and not a
    /// respawning checkpoint.</summary>
    public int SextonsFelled { get; set; }
}

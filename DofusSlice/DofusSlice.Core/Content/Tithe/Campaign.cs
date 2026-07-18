namespace DofusSlice.Core.Content.Tithe;

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

    /// <summary>
    /// Cumulative XP to reach each level, index = level — the classic Dofus 1.29 table, levels
    /// 1–20 (Bible §6.3: adopt the 1.29 curve verbatim; cross-check against an emulator dump in
    /// the §9 pass). Landed TOGETHER with Dofus-scale per-mob XP values, since curve and mob XP
    /// only pace correctly as a pair. Mob gold is decoupled from XP (mobs carry a "gold" column).
    /// </summary>
    private static readonly int[] XpCurve =
    {
        0, 0, 110, 650, 1500, 2800, 4800, 7300, 10500, 14500, 19200,
        25200, 32600, 41000, 50500, 61000, 75000, 91000, 115000, 142000, 171000,
    };

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
        while (Xp >= XpForNextLevel(Level)) { Xp -= XpForNextLevel(Level); Level++; }
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
    public int Gold { get; set; }
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

    /// <summary>Up to three units dive together — the avatar leads, mercenaries fill the rest.</summary>
    public List<CampaignUnit> DiveParty =>
        Crew.OrderByDescending(u => u.IsAvatar).Take(3).ToList();

    public static Campaign NewGame(string avatarClass)
    {
        var c = new Campaign { Gold = 160, Bread = 2, Draughts = 0 };
        c.Crew.Add(new CampaignUnit { Id = "avatar", ClassId = avatarClass, Name = "You", IsAvatar = true });

        // Start with a viable party of three: the avatar plus the two other archetypes as hired
        // mercenaries. The campaign's economy is then about replacing the ones you lose.
        int m = 0;
        foreach (var cls in new[] { "bulwark", "archer", "cannon" })
        {
            if (cls == avatarClass || c.Crew.Count >= 3) continue;
            c.Crew.Add(new CampaignUnit { Id = $"merc_{m}_{cls}", ClassId = cls, Name = $"{cls}-merc" });
            m++;
        }
        return c;
    }

    // ----- City services (Bible §4, §5, §6.10, §6.11) -------------------------------

    public bool BuyBread()
    {
        if (Gold < TitheContent.Prices.HardBread) return false;
        Gold -= TitheContent.Prices.HardBread; Bread++;
        return true;
    }

    public bool BuyDraught()
    {
        if (Gold < TitheContent.Prices.Draught) return false;
        Gold -= TitheContent.Prices.Draught; Draughts++;
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

    public bool Hire(string classId, string name, int level)
    {
        if (Crew.Count >= 3) return false;               // slice caps the party at three
        int price = HirePrice(level);
        if (Gold < price) return false;
        Gold -= price;
        Crew.Add(new CampaignUnit { Id = $"merc_{Crew.Count}_{classId}", ClassId = classId, Name = name, Level = level });
        return true;
    }

    public bool SellEssence(string essence)
    {
        if (!Essences.Remove(essence)) return false;
        Gold += TitheContent.Prices.EssenceSell;
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
    public bool AddGear(string itemId)
    {
        if (TitheContent.Item(itemId) == null || OwnsGear(itemId)) return false;
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
            : it.Vitality + (it.Strength + it.Intelligence + it.Chance + it.Agility) * 2 + it.Wisdom + it.Power * 3;
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

    // ----- The tithe (Bible §3.1.3, §5) ---------------------------------------------

    /// <summary>The tithe falls due every Nth return to the city; the amount escalates.</summary>
    public bool TitheDue => Dives > 0 && Dives % TitheContent.Prices.TitheEveryNDives == 0;

    public int TitheAmount =>
        TitheContent.Prices.TitheBase + TitheContent.Prices.TitheGrowth * TithesPaid + TitheDebt;

    /// <summary>Pay what's due if able, else it rolls into the debt ledger (slice behaviour).</summary>
    public bool PayTithe()
    {
        int due = TitheAmount;
        if (Gold >= due) { Gold -= due; TitheDebt = 0; TithesPaid++; return true; }
        TitheDebt = due; // unpaid → escalating ledger
        return false;
    }
}

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

    /// <summary>Placeholder curve (Bible §9 will mine the real 1.29 table): 80·level XP per level.</summary>
    public static int XpForNextLevel(int level) => 80 * level;

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

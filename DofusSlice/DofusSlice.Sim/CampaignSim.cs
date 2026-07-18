using DofusSlice.Core.Combat;
using DofusSlice.Core.Content.Tithe;

namespace DofusSlice.Sim;

/// <summary>
/// Headless play-through of the whole TITHE loop (Bible §4, M2): city prep → dive the Graveyard →
/// ejection → city, repeating until the campaign ends or a dive cap is hit. A simple "city AI"
/// stands in for the player's spending so the loop's economics are legible on paper — the Door
/// Test before any scene art. Combat, rewards, wounds, tithe and permadeath all run for real.
/// </summary>
public static class CampaignSim
{
    // Candidate hires for the post (the rotating board is data in the real game; fixed here).
    private static readonly string[] HireClasses = { "bulwark", "archer", "cannon" };

    public static int RunLoop(int seed, int maxDives, bool verbose)
    {
        var rng = new SystemRng(seed);
        var campaign = Campaign.NewGame("cannon");

        if (verbose)
        {
            Console.WriteLine($"TITHE — campaign loop (seed {seed})");
            Console.WriteLine($"Avatar: {campaign.Avatar!.ClassId}.  Starting gold {campaign.Gold}.\n");
        }

        int dive = 0;
        while (!campaign.Over && dive < maxDives)
        {
            dive++;
            CityPrep(campaign, verbose, dive);

            var session = new DiveSession(campaign, rng);
            var report = session.RunAuto(greedy: false); // cautious stand-in; greed is a player choice

            if (verbose) PrintDive(dive, report, campaign);
        }

        if (verbose)
        {
            Console.WriteLine("\n=== campaign end ===");
            Console.WriteLine(campaign.Over
                ? $"The avatar fell. Campaign over after {dive} dives."
                : $"Survived {dive} dives.");
            Console.WriteLine($"Gold {campaign.Gold}, tithes paid {campaign.TithesPaid}, debt {campaign.TitheDebt}.");
            Console.WriteLine($"Crew: {string.Join(", ", campaign.Crew.Select(u => $"{u.Name}({u.ClassId} L{u.Level}{(u.Wounded ? " wounded" : "")}))"))}");
            Console.WriteLine($"Essences held: {(campaign.Essences.Count == 0 ? "none" : string.Join(", ", campaign.Essences))}");
        }
        // Non-zero only if the loop never actually dived (a smoke-test signal).
        return dive > 0 ? 0 : 1;
    }

    /// <summary>The player's stand-in spending before a dive: pay the tithe, mend, restock, hire.</summary>
    private static void CityPrep(Campaign c, bool verbose, int dive)
    {
        var log = new List<string>();
        c.RestCrew(); // the city is safe rest — HP back to full (wounds still need a Draught)

        // 1. The tithe comes first — an unpaid tithe escalates as debt.
        if (c.TitheDue)
        {
            int due = c.TitheAmount;
            log.Add(c.PayTithe() ? $"paid the tithe ({due}g)" : $"COULD NOT PAY the tithe ({due}g) — debt grows");
        }

        // 2. Mend the wounded while a Draught is affordable (leave a working reserve).
        foreach (var u in c.Crew.Where(u => u.Wounded).ToList())
        {
            if (c.Draughts == 0 && c.Gold >= TitheContent.Prices.Draught + 80) c.BuyDraught();
            if (c.TreatWounded(u)) log.Add($"treated {u.Name}'s wounds");
        }

        // 3. Restock Hard Bread — it mends the party between fights on the dive.
        while (c.Bread < 5 && c.Gold >= TitheContent.Prices.HardBread + 60) c.BuyBread();

        // 4. Fill the party to three if a hire is affordable, keeping a small reserve.
        while (c.Crew.Count < 3)
        {
            int level = Math.Max(1, c.Avatar!.Level);
            int price = c.HirePrice(level);
            if (c.Gold < price + 40) break;
            var cls = HireClasses[c.Crew.Count % HireClasses.Length];
            if (c.Hire(cls, $"{cls}-merc", level)) log.Add($"hired a {cls} ({price}g)");
            else break;
        }

        if (verbose)
        {
            Console.WriteLine($"--- City (before dive {dive}) --- gold {c.Gold}, party {c.Crew.Count}"
                + (c.TitheDue ? "  [TITHE DUE]" : ""));
            if (log.Count > 0) Console.WriteLine("  " + string.Join("; ", log));
        }
    }

    private static void PrintDive(int dive, DiveSession.DiveReport r, Campaign c)
    {
        Console.WriteLine($"Dive {dive}: {r.EndReason}.  cleared {r.PacksCleared} packs, "
            + $"+{r.Gold}g, +{r.Xp}xp"
            + (r.Essences.Count > 0 ? $", essences: {string.Join("/", r.Essences)}" : "")
            + (r.Lost.Count > 0 ? $"  LOST: {string.Join(", ", r.Lost)}" : ""));
    }

    /// <summary>
    /// Sample campaigns under both risk profiles (Bible Pillar 4: ruin traces to a choice). Cautious
    /// play skims the shallow safe packs; greedy play chases the deep, loot-rich, lethal ones.
    /// </summary>
    public static int Survey(int trials)
    {
        foreach (bool greedy in new[] { false, true })
        {
            int totalDives = 0, wipeouts = 0, gold = 0;
            for (int i = 1; i <= trials; i++)
            {
                var rng = new SystemRng(i * 131 + 7);
                var campaign = Campaign.NewGame("cannon");
                int dive = 0;
                while (!campaign.Over && dive < 40)
                {
                    dive++;
                    CityPrep(campaign, false, dive);
                    new DiveSession(campaign, rng).RunAuto(greedy);
                }
                totalDives += dive; gold += campaign.Gold;
                if (campaign.Over) wipeouts++;
            }
            Console.WriteLine($"{(greedy ? "GREEDY" : "CAUTIOUS")} play over {trials} runs (cap 40 dives): "
                + $"avg {(double)totalDives / trials:0.0} dives, {wipeouts} wipes "
                + $"({100.0 * wipeouts / trials:0}%), avg end gold {gold / trials}");
        }
        return 0;
    }
}

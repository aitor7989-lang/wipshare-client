using DofusSlice.Core.AI;
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
            Console.WriteLine($"Avatar: {campaign.Avatar!.ClassId}.  Starting stones {campaign.Stones}.\n");
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
            Console.WriteLine($"Stones {campaign.Stones}, tithes paid {campaign.TithesPaid}, debt {campaign.TitheDebt}.");
            Console.WriteLine($"Crew: {string.Join(", ", campaign.Crew.Select(u => $"{u.Name}({u.ClassId} L{u.Level}{(u.Wounded ? " wounded" : "")})"))}");
            Console.WriteLine($"Essences held: {(campaign.Essences.Count == 0 ? "none" : string.Join(", ", campaign.Essences))}");
            if (campaign.Avatar is { } av)
            {
                var s = TitheContent.StatsOf(av);
                var el = TitheContent.ClassElement(av.ClassId);
                int set = TitheContent.SetPiecesEquipped(av, TitheContent.GraveyardSet);
                Console.WriteLine($"Avatar kit: {set}/7 Adventurer pieces  →  {s.MaxHp} HP, "
                    + $"{el.ToString().ToUpperInvariant()} {TitheContent.DamageStatFor(s, el)}, AGI {s.Agility}, WIS {s.Wisdom}"
                    + (av.Equipment.Count > 0 ? $"  [{string.Join(", ", av.Equipment.Select(TitheContent.ItemName))}]" : ""));
            }
        }
        // Non-zero only if the loop never actually dived (a smoke-test signal).
        return dive > 0 ? 0 : 1;
    }

    /// <summary>The player's stand-in spending before a dive: pay the tithe, mend, restock, hire.</summary>
    private static void CityPrep(Campaign c, bool verbose, int dive)
    {
        // The sim stands in for the player: spend banked characteristic points by class growth.
        foreach (var u in c.Crew) TitheContent.AutoSpendStats(u);

        var log = new List<string>();
        c.RestCrew(); // the city is safe rest — HP back to full (wounds still need a Draught)

        // 1. The tithe comes first — an unpaid tithe escalates as debt.
        if (c.TitheDue)
        {
            int due = c.TitheAmount;
            log.Add(c.PayTithe() ? $"paid the tithe ({due} st)" : $"COULD NOT PAY the tithe ({due} st) — debt grows");
            // Missing the tithe enough times ends the campaign outright: everything below this
            // point assumes a living leader (c.Avatar!), so stop preparing a crew that is gone.
            if (c.Over) { if (verbose) foreach (var l in log) Console.WriteLine("    " + l); return; }
        }

        // 2. Mend the wounded while a Draught is affordable (leave a working reserve).
        foreach (var u in c.Crew.Where(u => u.Wounded).ToList())
        {
            if (c.Draughts == 0 && c.Stones >= TitheContent.Prices.Draught + 80) c.BuyDraught();
            if (c.TreatWounded(u)) log.Add($"treated {u.Name}'s wounds");
        }

        // 3. Consume essences (Bible §6.5): teach each held essence to the first unit with a free
        //    slot, avatar first — the stand-in never sells what it can learn.
        foreach (var ess in c.Essences.ToList())
        {
            var learner = c.Crew.OrderByDescending(u => u.IsAvatar)
                .FirstOrDefault(u => u.HasFreeEssenceSlot && !u.EssenceSlots.Contains(ess));
            if (learner != null && c.TeachEssence(learner, ess))
                log.Add($"{learner.Name} consumed {ess} (learned {TitheContent.EssenceSkillName(ess)})");
        }

        // 3b. When flush, buy the Temple's shelf essence for an empty slot (her painful price is
        //     the certain path to the exclusives — Blood Pact and Blink never drop from mobs).
        if (c.Stones > 900 && c.Crew.Any(u => u.HasFreeEssenceSlot))
        {
            string shelf = TitheContent.EssenceForSale(c.Dives);
            var buyer = c.Crew.OrderByDescending(u => u.IsAvatar)
                .FirstOrDefault(u => u.HasFreeEssenceSlot && !u.EssenceSlots.Contains(shelf));
            if (buyer != null && c.BuyEssence(shelf) && c.TeachEssence(buyer, shelf))
                log.Add($"bought {shelf} at the Temple ({TitheContent.Prices.EssenceBuy} st) — {buyer.Name} learned it");
        }

        // 4. Restock Hard Bread — it mends the party between fights on the dive.
        while (c.Bread < 5 && c.Stones >= TitheContent.Prices.HardBread + 60) c.BuyBread();

        // 5. Spend banked spell points by the class template (also covers essence skills just taught).
        foreach (var u in c.Crew) log.AddRange(TitheContent.AutoSpendSpellPoints(u));

        // 6. Fill the party to three if a hire is affordable, keeping a small reserve.
        while (c.Crew.Count < 3)
        {
            int level = Math.Max(1, c.Avatar!.Level);
            int price = c.HirePrice(level);
            if (c.Stones < price + 40) break;
            var cls = HireClasses[c.Crew.Count % HireClasses.Length];
            if (c.Hire(cls, $"{cls}-merc", level)) log.Add($"hired a {cls} ({price} st)");
            else break;
        }

        if (verbose)
        {
            Console.WriteLine($"--- City (before dive {dive}) --- stones {c.Stones}, party {c.Crew.Count}"
                + (c.TitheDue ? "  [TITHE DUE]" : ""));
            if (log.Count > 0) Console.WriteLine("  " + string.Join("; ", log));
        }
    }

    private static void PrintDive(int dive, DiveSession.DiveReport r, Campaign c)
    {
        Console.WriteLine($"Dive {dive}: {r.EndReason}.  cleared {r.PacksCleared} packs, "
            + $"+{r.Stones}g, +{r.Xp}xp"
            + (r.Essences.Count > 0 ? $", essences: {string.Join("/", r.Essences)}" : "")
            + (r.Gear.Count > 0 ? $"  GEAR: {string.Join("/", r.Gear)}" : "")
            + (r.Lost.Count > 0 ? $"  LOST: {string.Join(", ", r.Lost)}" : ""));
        foreach (var n in r.Notes) Console.WriteLine($"  ~ {n}");
    }

    /// <summary>Run a level-3 crew through the Crypt's sealing-door room chain to the Sexton.</summary>
    public static int Crypt(int seed)
    {
        var rng = new SystemRng(seed);
        var campaign = Campaign.NewGame("cannon");
        foreach (var u in campaign.Crew) u.Level = 3; // as if progressed enough to enter

        Console.WriteLine($"TITHE — the Crypt (seed {seed}, crew at L3)\n");
        var dive = new DiveSession(campaign, rng);
        var rooms = TitheContent.CryptRooms();

        for (int i = 0; i < rooms.Count; i++)
        {
            // THE BREATHER between sealing doors, exactly as the game plays it (SliceGame's
            // crypt rest beat). Without it this loop measured a crypt the player never fights —
            // pure uncompensated attrition — and every balance read off it was too harsh.
            if (i > 0) { foreach (var u in campaign.Crew) u.Wounded = false; campaign.RestCrewPartial(0.70); }

            var room = rooms[i];
            var pack = new DiveSession.PackState
            { Def = new TitheContent.PackDef($"crypt_{i}", room.Comp, 0, false, room.Grade) };

            var engine = dive.BeginFight(pack, chargeTravel: false);
            engine.Start();
            while (engine.Outcome == FightOutcome.Ongoing && engine.Round <= 40)
            { Policy.TakeTurn(engine, engine.Current); engine.EndTurn(); }
            var r = dive.ApplyResult(pack, engine);

            Console.WriteLine($"  Room {i + 1}/{rooms.Count} {room.Name,-20} {r.Outcome}"
                + $"  +{r.Stones}g +{r.Xp}xp"
                + (r.Drops.Count > 0 ? $"  essences: {string.Join("/", r.Drops)}" : "")
                + (r.Gear.Count > 0 ? $"  GEAR: {string.Join("/", r.Gear)}" : "")
                + (r.Wounded.Count > 0 ? $"  wounded: {string.Join(",", r.Wounded)}" : "")
                + (r.Lost.Count > 0 ? $"  LOST: {string.Join(",", r.Lost)}" : ""));
            if (r.Outcome != FightOutcome.Victory) { Console.WriteLine("\n  The crew fell in the Crypt — campaign over."); break; }
        }

        if (!campaign.Over)
            Console.WriteLine($"\n  The altar tears the crew out. Stones {campaign.Stones}, "
                + $"essences: {(campaign.Essences.Count == 0 ? "none" : string.Join(", ", campaign.Essences))}");
        return 0;
    }

    /// <summary>
    /// Show the Dofus stat block, leveling, and gear empowering spells (Bible §6.2/§6.3/§6.6/§6.10)
    /// on paper: an avatar's characteristics grow with level (auto-spent by the class ratio) and with
    /// the Adventurer set, and every point of its element's stat (+Power) lifts its spell damage by
    /// 1% — watch Ruin Bolt's range climb. The Door Test for progression before any equip UI.
    /// </summary>
    public static int Progression(int seed)
    {
        var c = Campaign.NewGame("cannon");
        var avatar = c.Avatar!;

        // The Cannon is the Fire class: Ruin Bolt (18-24) scales on Intelligence + Power,
        // base × (100 + INT + POW)/100 — the same formula every element runs through.
        var elem = TitheContent.ClassElement("cannon");
        int dmg0 = TitheContent.DamageStatFor(TitheContent.StatsOf(avatar), elem);

        Console.WriteLine($"TITHE — stats, leveling & gear (the Cannon avatar, {elem} class)\n");
        Console.WriteLine($"{"STATE",-26} {"LVL",3} {"HP",4} {"INT",4} {"POW",4} {"AGI",4} {"WIS",4}  {"SET",3}  RUIN BOLT");
        void Row(string label)
        {
            var s = TitheContent.StatsOf(avatar);
            int set = TitheContent.SetPiecesEquipped(avatar, TitheContent.GraveyardSet);
            int dmg = TitheContent.DamageStatFor(s, elem);
            int lo = 18 * (100 + dmg) / 100, hi = 24 * (100 + dmg) / 100;
            Console.WriteLine($"{label,-26} {avatar.Level,3} {s.MaxHp,4} {s.Intelligence,4} {s.Power,4} {s.Agility,4} {s.Wisdom,4}  {set,2}/7  {lo}-{hi}");
        }

        Row("fresh (level 1, naked)");

        // Level up by granting the 1.29 per-level XP; the class auto-spends 5 points into Str/Vit/Agi.
        foreach (int target in new[] { 2, 3, 5, 8, 12 })
        {
            while (avatar.Level < target)
                avatar.GainXp(CampaignUnit.XpForNextLevel(avatar.Level) - avatar.Xp);
            Row($"leveled to {avatar.Level}");
        }

        Console.WriteLine();
        // Now assemble the Adventurer set piece by piece (as drops would) — the full-set spike.
        foreach (var piece in TitheContent.SetPieceIds(TitheContent.GraveyardSet))
        {
            c.AddGear(piece);
            Row($"+ {TitheContent.ItemName(piece)}");
        }

        var final = TitheContent.StatsOf(avatar);
        int dmgNow = TitheContent.DamageStatFor(final, elem);
        // Damage lift = (100+stat_now)/(100+stat_base) − 1, the actual multiplier on its spells.
        int dmgLift = 100 * (100 + dmgNow) / (100 + dmg0) - 100;
        Console.WriteLine($"\n  Full kit vs fresh L1: +{final.MaxHp - TitheContent.ClassMaxHp("cannon")} HP, "
            + $"{elem} damage stat {dmg0} → {dmgNow} (its spells hit ~{dmgLift}% harder than a naked L1)"
            + (final.MpBonus > 0 ? $", and the full set grants +{final.MpBonus} MP — the screaming find." : "."));

        // Essences (Bible §6.5): consume two — learning is permanent, and the combat kit grows.
        c.Essences.AddRange(new[] { "Ironhide", "Pounce" });
        c.TeachEssence(avatar, "Ironhide");
        c.TeachEssence(avatar, "Pounce");

        // Spell points (Bible §6.3): +1 per level, auto-spent by the class template. Ranks change
        // shape/economics — Ruin Bolt III reaches further AND costs 3 AP (two casts a turn).
        var ranked = TitheContent.AutoSpendSpellPoints(avatar);
        var fighter = TitheContent.MakeCrewMember(avatar, new DofusSlice.Core.Grid.CellCoord(0, 0));
        Console.WriteLine($"  Essences consumed: {string.Join(" + ", avatar.EssenceSlots)}; "
            + $"{ranked.Count} spell rank(s) bought ({avatar.SpellPoints} points banked)");
        Console.WriteLine("  Combat kit: " + string.Join(", ",
            fighter.Spells.Select(sp => $"{sp.Name} (AP{sp.ApCost}, {sp.MinRange}-{sp.MaxRange})")));

        // The equip screen's manual ops (Bible §6.13): strip a piece, watch the block fall, re-equip.
        c.Unequip(avatar, "adv_blade");
        int stripped = TitheContent.DamageStatFor(TitheContent.StatsOf(avatar), elem);
        c.Equip(avatar, "adv_blade");
        int restored = TitheContent.DamageStatFor(TitheContent.StatsOf(avatar), elem);
        Console.WriteLine($"  Equip screen ops: strip the Blade {restored} → {stripped}, re-equip → {restored}.");

        // Temple exclusives (Bible §6.5): buy Blink for the archer and Blood Pact for the bulwark,
        // then run a real jumped fight and count the exclusive casts in the combat log — proof the
        // AI actually flies them (Blink = the skirmisher's escape; Blood Pact = blood for AP).
        // A FRESH level-1 crew, so the g3 hounds genuinely reach melee and force the escapes.
        var c2 = Campaign.NewGame("cannon");
        c2.Stones += 600;
        // Campaigns start SOLO since the fun pivot — the sim hires its crew like a player would.
        c2.Hire("archer", "Wren", 1);
        c2.Hire("bulwark", "Bern", 1);
        var archer = c2.Crew.First(u => u.ClassId == "archer");
        var bulwark = c2.Crew.First(u => u.ClassId == "bulwark");
        c2.BuyEssence("Blink"); c2.TeachEssence(archer, "Blink");
        c2.BuyEssence("Blood Pact"); c2.TeachEssence(bulwark, "Blood Pact");

        var rng = new SystemRng(seed);
        var dive = new DiveSession(c2, rng);
        var pack = dive.Packs.First(p => p.Def.Id == "hound-pack");
        var engine = dive.BeginFight(pack, chargeTravel: false, jumped: true);
        var lines = new List<string>();
        engine.Logged += lines.Add;
        engine.Start();
        while (engine.Outcome == FightOutcome.Ongoing && engine.Round <= 40)
        { Policy.TakeTurn(engine, engine.Current); engine.EndTurn(); }
        int blinks = lines.Count(l => l.Contains("leaps to"));
        int pacts = lines.Count(l => l.Contains("sacrifice"));
        Console.WriteLine($"  Temple exclusives in a jumped g3 fight: Blood Pact sacrifices {pacts}, "
            + $"Blink escapes {blinks} (situational — deterministic proof lives in `effects`), outcome {engine.Outcome}.");
        return 0;
    }

    /// <summary>
    /// Prove the survivor system (Bible §6.12) on paper: a short-handed crew dives; when the floor
    /// has a wandering survivor they're hired mid-dive cheap with their nature hidden — and the
    /// Grasping ones walk off with a cut when the haul is heavy and the bell is low.
    /// </summary>
    public static int Survivors(int tries)
    {
        int offered = 0, hired = 0, betrayed = 0;
        var samples = new List<string>();
        for (int seed = 1; seed <= tries; seed++)
        {
            // Campaigns start SOLO since the fun pivot, so a fresh game IS the survivor's
            // opening — a crew of one is as short-handed as it gets. This used to remove a
            // member on top, which deleted the avatar from a crew of one and turned every
            // "dive" into a no-op: the hire-path proof below never ran and the harness failed
            // while the feature worked.
            var c = Campaign.NewGame("cannon");
            c.Stones += 100;
            var dive = new DiveSession(c, new SystemRng(seed * 977 + 5));
            if (dive.Survivor != null) offered++;
            var r = dive.RunAuto(greedy: false);
            foreach (var n in r.Notes)
            {
                if (n.Contains("hired")) hired++;
                if (n.Contains("slips away")) betrayed++;
                if (samples.Count < 6) samples.Add($"seed {seed}: {n}");
            }
        }
        Console.WriteLine($"TITHE — survivors over {tries} short-handed dives:");
        Console.WriteLine($"  offered {offered}, hired {hired}, betrayals {betrayed}\n");
        foreach (var s in samples) Console.WriteLine("  " + s);
        return hired > 0 ? 0 : 1; // the hire path must actually run
    }

    /// <summary>
    /// Sample campaigns under both risk profiles (Bible Pillar 4: ruin traces to a choice). Cautious
    /// play skims the shallow safe packs; greedy play chases the deep, loot-rich, lethal ones.
    /// </summary>
    public static int Survey(int trials)
    {
        foreach (bool greedy in new[] { false, true })
        {
            int totalDives = 0, wipeouts = 0, stones = 0;
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
                totalDives += dive; stones += campaign.Stones;
                if (campaign.Over) wipeouts++;
            }
            Console.WriteLine($"{(greedy ? "GREEDY" : "CAUTIOUS")} play over {trials} runs (cap 40 dives): "
                + $"avg {(double)totalDives / trials:0.0} dives, {wipeouts} wipes "
                + $"({100.0 * wipeouts / trials:0}%), avg end stones {stones / trials}");
        }
        return 0;
    }
}

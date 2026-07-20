using DofusSlice.Core.AI;
using DofusSlice.Core.Combat;

namespace DofusSlice.Core.Content.Tithe;

/// <summary>
/// One trip through the Lychgate onto the Graveyard floor (Bible §4, §6.7–6.9). A real-time
/// <see cref="Clock"/> runs the whole time; the crew engages skeleton packs (each a watched fight
/// through the shared combat engine), banking essence stones / XP / essences and taking wounds. When the
/// clock runs out the labyrinth ejects everyone to the city with what they carry; a fight lost
/// outright ends the campaign. Exposes per-step methods for the game and a headless auto-dive for
/// the sim, so the loop's economics are testable before any scene art (the Door Test on paper).
/// </summary>
public sealed class DiveSession
{
    public sealed class PackState
    {
        public required TitheContent.PackDef Def { get; init; }
        public bool Cleared { get; set; }
        /// <summary>Bell reading when the pack fell — the respawn timer counts from here.</summary>
        public float ClearedAtClock { get; set; }
    }

    /// <summary>The most recent fight's per-unit resolution (XP shares, fates) for the UI.</summary>
    public TitheResolution.Result? LastResolution { get; private set; }

    public sealed record FightReport(string PackId, FightOutcome Outcome, int Stones, int Xp,
                                     IReadOnlyList<string> Drops, IReadOnlyList<string> Gear,
                                     IReadOnlyList<string> Lost, IReadOnlyList<string> Wounded)
    {
        /// <summary>The party's pay: each living member's stones cut (the leader banks only theirs).</summary>
        public IReadOnlyList<(string Name, int Stones)> Shares { get; init; } = Array.Empty<(string, int)>();
    }

    public sealed record DiveReport(int PacksCleared, int Stones, int Xp, IReadOnlyList<string> Essences,
                                    IReadOnlyList<string> Gear, IReadOnlyList<string> Lost,
                                    bool CampaignOver, string EndReason, IReadOnlyList<FightReport> Fights,
                                    IReadOnlyList<string> Notes);

    // Real-time cost estimates so the headless clock paces like the watched game (Bible's 12-min
    // floor; the prototype uses a short clock). Tunable in M5.
    private const float FightBaseSeconds = 22f, FightPerEnemy = 6f;

    private readonly Campaign _campaign;
    private readonly IRng _rng;

    public float Clock { get; private set; }
    public List<PackState> Packs { get; }
    public bool Ended { get; private set; }
    public int XpBanked { get; private set; }

    /// <summary>Stones banked this dive — the "carried haul" a Grasping merc weighs (Bible §6.12).</summary>
    public int StonesGained { get; private set; }

    /// <summary>A survivor found below (Bible §6.12): class and level visible, price cheap,
    /// temperament HIDDEN until the Temple vets them — or until they show you themselves.</summary>
    public sealed record SurvivorOffer(string ClassId, int Level, int Price, Temperament Temperament);
    public SurvivorOffer? Survivor { get; private set; }

    /// <summary>A betrayal note ("X slips away with Ng"), set when a Grasping merc departs.</summary>
    public string? Departure { get; private set; }

    /// <summary>The visual game raises this while a fight is on the grid: the clock keeps running
    /// (Bible §3.1.3) but nobody walks out of a battle line — the Grasping exit waits for the
    /// fight to fold. Headless flows never set it (their checks already sit between fights).</summary>
    public bool InFight { get; set; }
    public string? ConsumeDeparture() { var d = Departure; Departure = null; return d; }

    // The Grasping calculus (Bible §6.12), placeholders for M5: leave when the haul crosses
    // GraspThreshold stones and the bell is under GraspClock seconds, taking GraspSharePct of it.
    private const int GraspThreshold = 100, GraspClock = 75, GraspSharePct = 30;
    private const int SurvivorChancePct = 30, GraspingOddsPct = 50;

    public DiveSession(Campaign campaign, IRng rng)
    {
        _campaign = campaign;
        _rng = rng;
        Clock = TitheContent.Graveyard.ClockSeconds;
        Packs = TitheContent.Graveyard.Packs.Select(p => new PackState { Def = p }).ToList();

        // A survivor may be wandering the floor: cheap help, hidden knife.
        if (rng.Roll(1, 100) <= SurvivorChancePct)
        {
            var classes = new[] { "bulwark", "archer", "cannon" };
            string cls = classes[rng.Roll(1, classes.Length) - 1];
            int level = Math.Max(1, (campaign.Avatar?.Level ?? 1) + rng.Roll(-1, 1));
            Survivor = new SurvivorOffer(cls, level,
                Math.Max(10, TitheContent.Prices.HireBasePerLevel * level / 3),
                rng.Roll(1, 100) <= GraspingOddsPct ? Temperament.Grasping : Temperament.Loyal);
        }
    }

    /// <summary>Hire the wandering survivor mid-dive (cheap; temperament stays hidden).</summary>
    public bool HireSurvivor()
    {
        if (Survivor == null || Ended || _campaign.Crew.Count >= 3 || _campaign.Stones < Survivor.Price)
            return false;
        _campaign.Stones -= Survivor.Price;
        _campaign.Crew.Add(new CampaignUnit
        {
            Id = $"surv_{_campaign.Crew.Count}_{Survivor.ClassId}", ClassId = Survivor.ClassId,
            Name = $"{Survivor.ClassId}-survivor", Level = Survivor.Level,
            SpellPoints = Survivor.Level - 1, Temperament = Survivor.Temperament,
        });
        Survivor = null;
        return true;
    }

    /// <summary>
    /// The Grasping exit (Bible §6.12): a Grasping merc leaves the party with a cut of the haul
    /// when the take is heavy and the bell is low — a simple despawn plus ledger note in the
    /// slice (the huntable chase is a later flourish). Betrayal reveals the temperament for free.
    /// </summary>
    private void CheckBetrayal()
    {
        if (Ended || InFight || StonesGained < GraspThreshold || Clock > GraspClock) return;
        var traitor = _campaign.Crew.FirstOrDefault(u => !u.IsAvatar && u.Temperament == Temperament.Grasping);
        if (traitor == null) return;
        int cut = StonesGained * GraspSharePct / 100;
        _campaign.Stones = Math.Max(0, _campaign.Stones - cut);
        _campaign.Crew.Remove(traitor);
        Departure = $"{traitor.Name} slips away toward the Lychgate with {cut} stones of the haul";
    }

    public float FightCost(TitheContent.PackDef p) => FightBaseSeconds + FightPerEnemy * p.Comp.Length;

    /// <summary>Do we have clock enough to travel to this pack and fight it?</summary>
    public bool CanAfford(PackState p) => !p.Cleared && Clock - p.Def.Reach - FightCost(p.Def) > 0;

    /// <summary>Advance the clock (real-time in the game); ejects when it runs out.</summary>
    // The grave refills (Pass 3): a cleared yard pack crawls back out after this long.
    private const float PackRespawnSeconds = 75f;
    private string? _respawnNote;

    /// <summary>One-shot UI message when a pack has respawned (mirrors ConsumeDeparture).</summary>
    public string? ConsumeRespawn() { var n = _respawnNote; _respawnNote = null; return n; }

    public void Tick(float seconds)
    {
        if (Ended) return;
        Clock -= seconds;
        foreach (var p in Packs.Where(p => p.Cleared && p.ClearedAtClock - Clock >= PackRespawnSeconds))
        {
            p.Cleared = false;
            _respawnNote = $"THE GRAVE REFILLS — {p.Def.Id.Replace('_', ' ').ToUpperInvariant()} CRAWLS BACK OUT";
        }
        CheckBetrayal(); // the bell sinking below the threshold can trigger the exit on its own
        if (Clock <= 0) { Clock = 0; Eject("the bell — clock expired"); }
    }

    /// <summary>The labyrinth ejects the crew to the city with everything carried.</summary>
    public void Eject(string reason)
    {
        if (Ended) return;
        Ended = true;
        EndReason = reason;
        if (!_campaign.Over) _campaign.Dives++;   // a survived dive counts toward the tithe cadence
    }

    public string EndReason { get; private set; } = "";

    /// <summary>
    /// Travel to a pack, fight it through the combat engine, and fold the result into the
    /// campaign: gold + XP + essences on a win (downed mercs die, a downed avatar is Wounded),
    /// or campaign-over on a wipe. The clock pays for the travel and the fight.
    /// </summary>
    public FightReport? Engage(PackState pack, bool jumped = false)
    {
        if (Ended || pack.Cleared) return null;
        var engine = BeginFight(pack, chargeTravel: !jumped, jumped: jumped);
        RunCombat(engine);
        Clock -= FightCost(pack.Def);   // headless fight-time estimate (the game ticks real time)
        return ApplyResult(pack, engine);
    }

    /// <summary>
    /// Headless abstraction of the real-time hunt (Bible §6.6 aggro-catch; the visual game moves
    /// hunting packs by actual proximity): after a fight, a still-roaming hunter may catch the
    /// crew JUMPED — scattered in the open, no placement — but only if the fight happened NEAR its
    /// territory (its reach within <see cref="HuntTerritory"/> of the fought pack's). Skimming the
    /// shallows only ever wakes the nearest hunter; going deep wakes the deep ones.
    /// </summary>
    private const int CatchChancePct = 4;
    private const int HuntTerritory = 10;

    /// <summary>Roll the ONE hunter nearest the fight just fought (a single fight wakes a single
    /// hunter, however deep you are); returns a forced jumped fight, if any.</summary>
    public FightReport? RollHunters(TitheContent.PackDef justFought)
    {
        if (Ended) return null;
        var hunter = Packs
            .Where(p => !p.Cleared && p.Def.Hunts && p.Def != justFought
                        && p.Def.Reach - justFought.Reach <= HuntTerritory)
            .OrderBy(p => Math.Abs(p.Def.Reach - justFought.Reach))
            .FirstOrDefault();
        if (hunter != null && _rng.Roll(1, 100) <= CatchChancePct)
            return Engage(hunter, jumped: true);
        return null;
    }

    /// <summary>
    /// Build the fight for a pack: mend with bread, pay the travel time, and return a fresh engine
    /// for the party versus the pack. The game plays this out visually then calls
    /// <see cref="ApplyResult"/>; the headless <see cref="Engage"/> runs it immediately.
    /// </summary>
    public CombatEngine BeginFight(PackState pack, bool chargeTravel = true, bool jumped = false)
    {
        MendWithBread();
        if (chargeTravel) Clock -= pack.Def.Reach; // headless charges travel; the visual walks it in real time
        // Each pack brawls on its own ground: the arena variant keys off the pack id.
        int variant = Math.Abs(pack.Def.Id.GetHashCode()) % 4;
        return TitheContent.BuildDiveFight(_campaign.DiveParty, pack.Def.Comp, _rng, pack.Def.Grade, jumped, variant);
    }

    /// <summary>Fold a finished fight into the campaign: rewards + wounds on a win, or campaign-over.</summary>
    public FightReport ApplyResult(PackState pack, CombatEngine engine)
    {
        InFight = false; // folding results IS the fight ending — the exit may fire again now
        var res = TitheResolution.Resolve(engine);
        LastResolution = res; // per-unit XP shares for the end-of-fight window
        var lost = new List<string>();
        var wounded = new List<string>();
        var gearGot = new List<string>();

        if (res.Outcome == FightOutcome.Victory)
        {
            pack.Cleared = true;
            pack.ClearedAtClock = Clock; // starts the respawn timer (the grave refills)
            int stones = engine.Fighters.Where(f => f.Team == Team.Enemy && !f.IsAlive)
                .Sum(TitheContent.MobStonesOf); // grade-aware: deep packs pay their risk premium

            // The party works for SHARES: gold splits evenly across living members; the
            // mercs' cuts leave with them (their pay), the leader banks only their own —
            // plus the remainder, the leader holds the purse. A dead member's share is lost.
            var living = res.Units.Where(u => !u.Died).ToList();
            int share = living.Count > 0 ? stones / living.Count : 0;
            var shares = living.Select(u => (u.Name, share)).ToList();
            var avatarUnit = _campaign.Avatar;
            bool avatarPaid = avatarUnit != null && living.Any(u => u.Id == avatarUnit.Id);
            int banked = avatarPaid ? share + (living.Count > 0 ? stones % living.Count : stones) : 0;
            _campaign.Stones += banked;
            StonesGained += banked;
            _campaign.Essences.AddRange(res.Drops);
            XpBanked += res.XpPool;

            // Fold each gear roll into a concrete unowned piece FROM THE KILLER'S FAMILY POOL
            // (husks carry belts and boots, wardens weapons...); the avatar auto-equips upgrades.
            foreach (var family in res.GearDrops)
            {
                var piece = TitheContent.RandomUnownedGearFor(family, _campaign.OwnsGear, _rng);
                if (piece != null && _campaign.AddGear(piece)) gearGot.Add(TitheContent.ItemName(piece));
            }

            // Sundries: the dead carry supplies too — bread from the shambling, bottles from
            // the learned. They surface in the report's FOUND line alongside gear.
            foreach (var f in engine.Fighters.Where(x => x.Team == Team.Enemy && !x.IsAlive))
            {
                var (bPct, dPct) = TitheContent.MobSundries(f.Archetype);
                if (bPct > 0 && _rng.Roll(1, 100) <= bPct) { _campaign.Bread++; gearGot.Add("Hard Bread"); }
                if (dPct > 0 && _rng.Roll(1, 100) <= dPct) { _campaign.Draughts++; gearGot.Add("Healing Draught"); }
            }

            bool avatarDown = false;
            foreach (var u in res.Units)
            {
                var cu = _campaign.Crew.FirstOrDefault(x => x.Id == u.Id);
                if (cu == null) continue;
                cu.GainXp(u.XpGained); // the AVATAR banks points for the player to spend...
                if (!cu.IsAvatar)      // ...companions manage themselves, silently
                {
                    TitheContent.AutoSpendStats(cu);
                    TitheContent.AutoSpendSpellPoints(cu);
                }
                if (u.Died)
                {
                    // Downed MERCS die for good. A downed AVATAR is dragged out: wounded,
                    // at death's door, the dive over — but the campaign continues.
                    if (!cu.IsAvatar) { _campaign.Crew.Remove(cu); lost.Add(cu.Name); continue; }
                    cu.Wounded = true; cu.CurrentHp = 1; wounded.Add(cu.Name); avatarDown = true;
                    continue;
                }
                cu.CurrentHp = engine.Fighters.First(f => f.Id == u.Id).Hp; // carry damage into the next fight
                if (u.Wounded) { cu.Wounded = true; wounded.Add(cu.Name); }
            }

            CheckBetrayal(); // a heavy haul under a low bell is when a Grasping merc walks
            if (avatarDown) Eject("you are carried out cold — the dive ends");
            else if (Clock <= 0) Eject("the bell — clock expired");
            return new FightReport(pack.Def.Id, res.Outcome, stones, res.XpPool, res.Drops, gearGot, lost, wounded)
                { Shares = shares };
        }

        // A fight lost outright. Dead mercenaries are gone for good; the AVATAR is dragged
        // out of the graveyard instead of dying — wounded, HP 1, this fight's spoils lost.
        foreach (var u in _campaign.DiveParty.ToList())
        {
            if (u.IsAvatar) { u.Wounded = true; u.CurrentHp = 1; wounded.Add(u.Name); }
            else { _campaign.Crew.Remove(u); lost.Add(u.Name); }
        }
        Eject("the crew fell — your body is dragged to the lychgate");
        return new FightReport(pack.Def.Id, res.Outcome, 0, 0,
            Array.Empty<string>(), Array.Empty<string>(), lost, wounded);
    }

    private string? _mendMsg;

    /// <summary>One-shot report of the last bread mend, for the game to show as a yard message
    /// (the player asked what bread is FOR — this is the answer, made visible).</summary>
    public string? ConsumeMend() { var m = _mendMsg; _mendMsg = null; return m; }

    /// <summary>Spend Hard Bread to mend the party's most-hurt units before a fight (Bible §4).</summary>
    private void MendWithBread()
    {
        var mends = new Dictionary<string, int>();
        while (_campaign.Bread > 0)
        {
            var hurt = _campaign.DiveParty
                .Where(u => (u.CurrentHp ?? int.MaxValue) < TitheContent.UnitMaxHp(u))
                .OrderBy(u => u.CurrentHp).FirstOrDefault();
            if (hurt == null) break;
            _campaign.Bread--;
            int max = TitheContent.UnitMaxHp(hurt);
            int before = hurt.CurrentHp ?? max;
            hurt.CurrentHp = Math.Min(max, before + TitheContent.Prices.BreadHeal);
            mends[hurt.Name] = mends.GetValueOrDefault(hurt.Name) + (hurt.CurrentHp.Value - before);
        }
        if (mends.Count > 0)
            _mendMsg = "HARD BREAD: " + string.Join("  ", mends.Select(m => $"{m.Key} +{m.Value} HP"))
                + $"  ({_campaign.Bread} left)";
    }

    private static void RunCombat(CombatEngine e, int maxRounds = 40)
    {
        e.Start();
        while (e.Outcome == FightOutcome.Ongoing && e.Round <= maxRounds)
        {
            Policy.TakeTurn(e, e.Current);
            e.EndTurn();
        }
    }

    /// <summary>
    /// Headless auto-dive: greedily engage the nearest affordable pack until the clock can't fit
    /// another (or the floor is cleared, or the crew falls), then eject. A stand-in for the
    /// player's route choices, enough to test loop pacing and economy.
    /// </summary>
    public DiveReport RunAuto(bool greedy = false)
    {
        int stones0 = _campaign.Stones, cleared0 = Packs.Count(p => p.Cleared), ess0 = _campaign.Essences.Count;
        var fights = new List<FightReport>();
        var lostAll = new List<string>();
        var gearAll = new List<string>();
        var notes = new List<string>();

        while (!Ended)
        {
            // Cautious: skim the nearest packs and get out. Greedy: chase the fattest packs (most
            // loot, deepest) — more gold and essences, but the dangerous fights that get you killed.
            var affordable = Packs.Where(CanAfford);
            var pack = greedy
                ? affordable.OrderByDescending(p => p.Def.Comp.Length).ThenByDescending(p => p.Def.Reach).FirstOrDefault()
                : affordable.OrderBy(p => p.Def.Reach).FirstOrDefault();
            if (pack == null)
            {
                Eject(Packs.All(p => p.Cleared) ? "cleared the floor, walked out" : "the bell — clock expired");
                break;
            }
            var fr = Engage(pack);
            if (fr != null) { fights.Add(fr); lostAll.AddRange(fr.Lost); gearAll.AddRange(fr.Gear); }

            // A short-handed crew takes the wandering survivor's cheap help (found mid-dive —
            // typically right after a funeral). Class and level visible; nature not.
            if (Survivor is { } offer && _campaign.Crew.Count < 3 && HireSurvivor())
                notes.Add($"hired a wandering {offer.ClassId}-survivor (L{offer.Level}, {offer.Price}g, temperament unknown)");

            // The hunting packs answer the noise: a hunter near that fight may catch the crew JUMPED.
            var caught = fr != null ? RollHunters(pack.Def) : null;
            if (caught != null) { fights.Add(caught); lostAll.AddRange(caught.Lost); gearAll.AddRange(caught.Gear); }

            if (ConsumeDeparture() is { } dep) notes.Add(dep); // the Grasping exit, if it happened
        }

        return new DiveReport(
            Packs.Count(p => p.Cleared) - cleared0, _campaign.Stones - stones0, XpBanked,
            _campaign.Essences.Skip(ess0).ToList(), gearAll, lostAll, _campaign.Over, EndReason, fights, notes);
    }
}

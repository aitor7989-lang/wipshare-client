using DofusSlice.Core.AI;
using DofusSlice.Core.Combat;

namespace DofusSlice.Core.Content.Tithe;

/// <summary>
/// One trip through the Lychgate onto the Graveyard floor (Bible §4, §6.7–6.9). A real-time
/// <see cref="Clock"/> runs the whole time; the crew engages skeleton packs (each a watched fight
/// through the shared combat engine), banking gold / XP / essences and taking wounds. When the
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
    }

    public sealed record FightReport(string PackId, FightOutcome Outcome, int Gold, int Xp,
                                     IReadOnlyList<string> Drops, IReadOnlyList<string> Gear,
                                     IReadOnlyList<string> Lost, IReadOnlyList<string> Wounded);

    public sealed record DiveReport(int PacksCleared, int Gold, int Xp, IReadOnlyList<string> Essences,
                                    IReadOnlyList<string> Gear, IReadOnlyList<string> Lost,
                                    bool CampaignOver, string EndReason, IReadOnlyList<FightReport> Fights);

    // Real-time cost estimates so the headless clock paces like the watched game (Bible's 12-min
    // floor; the prototype uses a short clock). Tunable in M5.
    private const float FightBaseSeconds = 22f, FightPerEnemy = 6f;

    private readonly Campaign _campaign;
    private readonly IRng _rng;

    public float Clock { get; private set; }
    public List<PackState> Packs { get; }
    public bool Ended { get; private set; }
    public int XpBanked { get; private set; }

    public DiveSession(Campaign campaign, IRng rng)
    {
        _campaign = campaign;
        _rng = rng;
        Clock = TitheContent.Graveyard.ClockSeconds;
        Packs = TitheContent.Graveyard.Packs.Select(p => new PackState { Def = p }).ToList();
    }

    public float FightCost(TitheContent.PackDef p) => FightBaseSeconds + FightPerEnemy * p.Comp.Length;

    /// <summary>Do we have clock enough to travel to this pack and fight it?</summary>
    public bool CanAfford(PackState p) => !p.Cleared && Clock - p.Def.Reach - FightCost(p.Def) > 0;

    /// <summary>Advance the clock (real-time in the game); ejects when it runs out.</summary>
    public void Tick(float seconds)
    {
        if (Ended) return;
        Clock -= seconds;
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
    public FightReport? Engage(PackState pack)
    {
        if (Ended || pack.Cleared) return null;
        var engine = BeginFight(pack);
        RunCombat(engine);
        Clock -= FightCost(pack.Def);   // headless fight-time estimate (the game ticks real time)
        return ApplyResult(pack, engine);
    }

    /// <summary>
    /// Build the fight for a pack: mend with bread, pay the travel time, and return a fresh engine
    /// for the party versus the pack. The game plays this out visually then calls
    /// <see cref="ApplyResult"/>; the headless <see cref="Engage"/> runs it immediately.
    /// </summary>
    public CombatEngine BeginFight(PackState pack, bool chargeTravel = true)
    {
        MendWithBread();
        if (chargeTravel) Clock -= pack.Def.Reach; // headless charges travel; the visual walks it in real time
        return TitheContent.BuildDiveFight(_campaign.DiveParty, pack.Def.Comp, _rng);
    }

    /// <summary>Fold a finished fight into the campaign: rewards + wounds on a win, or campaign-over.</summary>
    public FightReport ApplyResult(PackState pack, CombatEngine engine)
    {
        var res = TitheResolution.Resolve(engine);
        var lost = new List<string>();
        var wounded = new List<string>();
        var gearGot = new List<string>();

        if (res.Outcome == FightOutcome.Victory)
        {
            pack.Cleared = true;
            int gold = engine.Fighters.Where(f => f.Team == Team.Enemy && !f.IsAlive)
                .Sum(f => TitheContent.MobGold(f.Archetype));
            _campaign.Gold += gold;
            _campaign.Essences.AddRange(res.Drops);
            XpBanked += res.XpPool;

            // Fold each gear roll into a concrete unowned set piece; the avatar auto-equips upgrades.
            foreach (var setId in res.GearDrops)
            {
                var piece = TitheContent.RandomUnownedPiece(setId, _campaign.OwnsGear, _rng);
                if (piece != null && _campaign.AddGear(piece)) gearGot.Add(TitheContent.ItemName(piece));
            }

            foreach (var u in res.Units)
            {
                var cu = _campaign.Crew.FirstOrDefault(x => x.Id == u.Id);
                if (cu == null) continue;
                cu.GainXp(u.XpGained);
                TitheContent.AutoSpendSpellPoints(cu); // ranks buy themselves until the spend screen ships
                if (u.Died) { _campaign.Crew.Remove(cu); lost.Add(cu.Name); continue; }
                cu.CurrentHp = engine.Fighters.First(f => f.Id == u.Id).Hp; // carry damage into the next fight
                if (u.Wounded) { cu.Wounded = true; wounded.Add(cu.Name); }
            }

            if (Clock <= 0) Eject("the bell — clock expired");
            return new FightReport(pack.Def.Id, res.Outcome, gold, res.XpPool, res.Drops, gearGot, lost, wounded);
        }

        // A fight lost outright: the whole party is gone. No player-managed unit remains → over.
        foreach (var u in _campaign.DiveParty.ToList()) { _campaign.Crew.Remove(u); lost.Add(u.Name); }
        Eject("the crew fell — campaign over");
        return new FightReport(pack.Def.Id, res.Outcome, 0, 0,
            Array.Empty<string>(), Array.Empty<string>(), lost, Array.Empty<string>());
    }

    /// <summary>Spend Hard Bread to mend the party's most-hurt units before a fight (Bible §4).</summary>
    private void MendWithBread()
    {
        while (_campaign.Bread > 0)
        {
            var hurt = _campaign.DiveParty
                .Where(u => (u.CurrentHp ?? int.MaxValue) < TitheContent.UnitMaxHp(u))
                .OrderBy(u => u.CurrentHp).FirstOrDefault();
            if (hurt == null) break;
            _campaign.Bread--;
            int max = TitheContent.UnitMaxHp(hurt);
            hurt.CurrentHp = Math.Min(max, (hurt.CurrentHp ?? max) + TitheContent.Prices.BreadHeal);
        }
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
        int gold0 = _campaign.Gold, cleared0 = Packs.Count(p => p.Cleared), ess0 = _campaign.Essences.Count;
        var fights = new List<FightReport>();
        var lostAll = new List<string>();
        var gearAll = new List<string>();

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
        }

        return new DiveReport(
            Packs.Count(p => p.Cleared) - cleared0, _campaign.Gold - gold0, XpBanked,
            _campaign.Essences.Skip(ess0).ToList(), gearAll, lostAll, _campaign.Over, EndReason, fights);
    }
}

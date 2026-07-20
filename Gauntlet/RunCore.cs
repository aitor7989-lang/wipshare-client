using DofusSlice.Core.AI;
using DofusSlice.Core.Combat;
using DofusSlice.Core.Content.Tithe;
using DofusSlice.Core.Grid;
using DofusSlice.Core.Spells;

namespace Gauntlet;

// ============================================================================
// THE RUN, AS PURE RULES (g10). Everything mechanical about a gauntlet run —
// gear, essences, waves, boards, blessings, the bell, the covenant's verbs —
// lives here, engine-typed and renderer-free, so the game DRAWS a run and the
// balance sim (dotnet run -- --sim) MEASURES one, and they can never drift.
// ============================================================================

public sealed record GearDef(string Slot, string Name, string Family, int Power, string? Keyword, string Blurb);

/// <summary>One card of a pick hand: what it says, what it is, what it does.</summary>
public sealed record PickCard(string Title, string Body, string Kind, Action Apply);

/// <summary>Presentation callbacks the mechanics raise mid-fight. The game floats,
/// splatters and rings bells off these; the sim passes null and hears nothing.</summary>
public interface IRunFx
{
    void EmberBurn(Fighter f, CellCoord at);
    void Afflicted(Fighter f, bool burn);
    void StonesPaid(int amount, CellCoord at);
    void FedByMite(Fighter avatar, int amount);
    void TollsLow(int tolls);
    void SextonComes();
    void RitualTurns(List<CellCoord> grown);
    void Transformed(string theme, string form);
    void Narrate(string line);
}

/// <summary>Every mechanical fact of one run. The game renders it; the sim tallies it.</summary>
public sealed class RunState
{
    public int RunStones, FightIndex, Kills, Falls, PendingLevels;
    public int Tolls = RunRules.TollStart;
    public bool SextonNow, ExtraPick, EventDone, RopePulled, BossFight, RitualTurned;
    public string Road = "";

    public readonly Dictionary<string, GearDef> Gear = new();      // slot -> the worn piece
    public readonly List<string> Essences = new();
    public readonly HashSet<string> Announced = new();             // transformations already sung
    public List<string> Learned = new();       // words ahead of your years — the GAME shares its meta list here
    public int BonusHp, BonusDmg, BonusMove, BonusAp;

    public readonly HashSet<CellCoord> Embers = new();
    public readonly HashSet<string> Fallen = new();                // took the void exit

    public int FamilyCount(string family) => Gear.Values.Count(g => g.Family == family);
    public bool HasKeyword(string k) => Gear.Values.Any(g => g.Keyword == k);
    public int ThemeCount(string theme) => Essences.Count(e => RunRules.ThemeOf(e) == theme);
    public bool Transformed(string theme) => ThemeCount(theme) >= 3;
}

public static class RunRules
{
    public const int Cols = 13, Rows = 8;
    public const int TollStart = 20;    // turn-priced: thinking is free, stalling is not
    public const int MateCost = 30;

    // ----- the covenant's pools ---------------------------------------------------------
    // Isaac's law: an item is a RULE on a shared verb, and rules stack. Mewgenics' law:
    // pieces belong to FAMILIES, and three of a family pays a set bonus.

    public static readonly GearDef[] GearPool =
    {
        new("BLADE", "GRAVE BLADE",  "GRAVE", 3, null,       "+3 DMG"),
        new("BLADE", "EMBER BLADE",  "EMBER", 2, "burning",  "+2 DMG\nstrikes BURN 2\nfor 2 turns"),
        new("BLADE", "BONE CLEAVER", "BONE",  2, "heavy",    "+2 DMG\nyour pushes\ngo +1 cell"),
        new("PLATE", "GRAVE PLATE",  "GRAVE", 8, null,       "+8 MAX HP"),
        new("PLATE", "EMBER PLATE",  "EMBER", 6, "warm",     "+6 MAX HP\nember graves\ndon't burn you"),
        new("PLATE", "BONE PLATE",   "BONE",  6, "thorns",   "+6 MAX HP\nreflect 15% of\nspell damage"),
        new("BOOTS", "GRAVE BOOTS",  "GRAVE", 1, null,       "+1 MP"),
        new("BOOTS", "BONE HOBNAILS","BONE",  1, "surefoot", "+1 MP\nbone spikes\nspare you"),
        new("CHARM", "GRAVE CHARM",  "GRAVE", 1, null,       "+1 AP"),
        new("CHARM", "EMBER CHARM",  "EMBER", 1, "kindling", "+1 AP\n+1 more when\nbloodied"),
        new("CHARM", "BONE KNUCKLE", "BONE",  1, "slam",     "+1 AP\nwall slams\ndeal +4"),
    };

    public static readonly (string Name, string Theme, string Blurb)[] EssencePool =
    {
        ("SPITTER'S GIFT", "MARROW", "your strikes\nPOISON 1, 2 turns"),
        ("GRAVE DAMP",     "MARROW", "+1 to every poison\nand burn you inflict"),
        ("WRAITH'S BREATH","MARROW", "your poisons and burns\nlast +1 turn"),
        ("MITE'S HUNGER",  "MARROW", "heal 2 when a\npoisoned enemy dies"),
        ("PIPER'S ROT",    "MARROW", "poisoned enemies spread\n1 poison on death"),
        ("HUSK'S GRIP",    "BONE",   "+1 cell on\nevery push"),
        ("WARDEN'S HIDE",  "BONE",   "SHIELD 3 at\nevery fight's start"),
        ("GHOUL'S WEIGHT", "BONE",   "wall slams\ndeal +4"),
        ("OSSUARY DUST",   "BONE",   "the void pays\nDOUBLE stones"),
        ("MASON'S FIST",   "BONE",   "+2 DMG"),
        ("TOLL KEEPER",    "BELL",   "+1 toll on\nevery kill"),
        ("GRASP'S COIN",   "BELL",   "+1 stone on\nevery kill"),
        ("HOUR THIEF",     "BELL",   "+1 AP"),
        ("QUIET STEP",     "BELL",   "+1 MP"),
        ("LAST ECHO",      "BELL",   "enter every fight with\na quarter of you, at least"),
    };

    public static string ThemeOf(string essence) => EssencePool.First(p => p.Name == essence).Theme;

    public static string FormOf(string theme) => theme switch
    { "MARROW" => "THE BLIGHTED", "BONE" => "THE REVENANT", _ => "THE TITHED" };

    // ----- the class weapon (g10: the AP purse is back — the weapon costs AP like Dofus) --

    public static SpellDef Strike(string classId) => classId switch
    {
        "archer" => new SpellDef
        {
            Id = 951, Name = "Loosed Arrow", ApCost = 3, MinRange = 2, MaxRange = 5,
            RequiresLineOfSight = true, MaxCastsPerTurn = 2,
            Effects = new[] { SpellEffect.Damage(Element.Air, 5, 7) },
        },
        "bulwark" => new SpellDef
        {
            // The sim's verdict (0% winrate, half its graves dug in fight 1): the wall
            // needed a heavier hand AND a way to last. Shove hits like a door and the
            // door drinks — the blood it takes is blood it keeps.
            Id = 952, Name = "Shove", ApCost = 3, MinRange = 1, MaxRange = 1,
            RequiresLineOfSight = true, MaxCastsPerTurn = 2,
            Effects = new[] { SpellEffect.Lifesteal(Element.Earth, 8, 11), SpellEffect.Push(1) },
        },
        _ => new SpellDef
        {
            Id = 950, Name = "Spark", ApCost = 3, MinRange = 1, MaxRange = 3,
            RequiresLineOfSight = true, MaxCastsPerTurn = 2,
            Effects = new[] { SpellEffect.Damage(Element.Fire, 5, 7) },
        },
    };

    /// <summary>Extra AP folded into the avatar's purse from charms and essences.</summary>
    public static int ApBonus(RunState st) => st.BonusAp
        + st.Gear.Values.Where(p => p.Slot == "CHARM").Sum(p => p.Power)
        + (st.Essences.Contains("HOUR THIEF") ? 1 : 0);

    /// <summary>The avatar carries the run's covenant: gear numbers, family sets, essences,
    /// transformations — all folded into one fighter at each fight's dawn.</summary>
    public static Fighter Bless(Fighter f, RunState st, CampaignUnit you)
    {
        int hp = st.BonusHp, dmg = st.BonusDmg, move = st.BonusMove;
        // The wall's birthright (sim-priced): a melee leader eats every answer the host
        // has, so the bulwark alone walks in with a thicker hide and a heavier arm —
        // one more AP than its table says, so Slam and Shove fit in the same breath.
        int classAp = you.ClassId == "bulwark" ? 1 : 0;
        if (you.ClassId == "bulwark") hp += 14;
        foreach (var piece in st.Gear.Values)
            switch (piece.Slot)
            {
                case "PLATE": hp += piece.Power; break;
                case "BLADE": dmg += piece.Power; break;
                case "BOOTS": move += piece.Power; break;
            }
        if (st.FamilyCount("GRAVE") >= 3) hp += 12;   // the dirt holds its own
        if (st.FamilyCount("EMBER") >= 3) dmg += 2;   // and the fire feeds
        if (st.Essences.Contains("MASON'S FIST")) dmg += 2;
        if (st.Essences.Contains("QUIET STEP")) move += 1;

        var g = new Fighter
        {
            Id = f.Id, Name = f.Name, Team = f.Team, PlayerControlled = true,
            Archetype = f.Archetype, Policy = f.Policy, Passive = f.Passive,
            PreferredRangeMin = f.PreferredRangeMin, PreferredRangeMax = f.PreferredRangeMax,
            Level = f.Level, MaxHp = f.MaxHp + hp,
            Hp = Math.Min(f.Hp + hp, f.MaxHp + hp),
            BaseAp = f.BaseAp + ApBonus(st) + classAp, BaseMp = f.BaseMp + move,
            Strength = f.Strength, Intelligence = f.Intelligence,
            Chance = f.Chance, Agility = f.Agility,
            // Damage bonuses ride on Power: it scales EVERY element, where the old
            // Intelligence route silently fed only the cannon's fire.
            Power = f.Power + dmg, Wisdom = f.Wisdom, Initiative = f.Initiative,
            Pos = f.Pos,
            // The kit: class ladder (built by MakeCrewMember at bought ranks), then words
            // learned ahead of the ladder — deduped in case the years caught up — then the weapon.
            Spells = f.Spells
                .Concat(st.Learned.Select(k => TitheContent.UnitSkill(you, k))
                    .Where(sp => f.Spells.All(o => o.Id != sp.Id)))
                .Concat(new[] { Strike(you.ClassId) }).ToArray(),
            PushBonus = (st.HasKeyword("heavy") ? 1 : 0)
                        + (st.Essences.Contains("HUSK'S GRIP") ? 1 : 0)
                        + (st.FamilyCount("BONE") >= 3 ? 1 : 0),
            SlamBonus = (st.HasKeyword("slam") ? 4 : 0) + (st.Essences.Contains("GHOUL'S WEIGHT") ? 4 : 0),
            HazardImmune = st.HasKeyword("surefoot") || st.Transformed("BONE"),
        };
        foreach (var (k, v) in f.Resistances) g.Resistances[k] = v;
        if (st.Essences.Contains("WARDEN'S HIDE"))
            g.Statuses.Add(new StatusEffect(StatusKind.Shield, 3, 99));
        if (st.HasKeyword("thorns"))
            g.Statuses.Add(new StatusEffect(StatusKind.Reflect, 15, 99));
        if (st.Essences.Contains("LAST ECHO") && g.Hp * 4 < g.MaxHp)
            g.Hp = g.MaxHp / 4;   // the echo carries what the body cannot
        return g;
    }

    // ----- the host ----------------------------------------------------------------------

    public readonly record struct WaveSlot(string Id, int Grade);

    /// <summary>The host is drafted, not memorized: each fight spends a points budget on a
    /// varied comp from the whole bestiary. Deeper fights buy heavier bodies and higher
    /// grades, and the third pack always fields a grade-3 CHAMPION.</summary>
    public static WaveSlot[] BuildWave(Random rnd, bool boss, RunState st)
    {
        if (boss) return new[] { new WaveSlot("sexton", 1), new WaveSlot("barrow_husk", 2) };

        (string id, int cost)[] pool =
        {
            ("grave_mite", 1), ("barrow_husk", 2), ("marrow_spitter", 2), ("gravehound", 2),
            ("tomb_wraith", 2), ("cairn_brute", 3), ("grave_ghoul", 3), ("crypt_warden", 3),
        };
        int budget = 3 + st.FightIndex * 2;   // 3 / 5 / 7 points across the run
        if (st.FightIndex == 1 && st.Road == "screaming") budget += 1;   // sim-priced: +2 was a cliff
        if (st.FightIndex == 1 && st.Road == "quiet") budget -= 1;
        int gradeFloor = st.FightIndex == 1 && st.Road == "screaming" ? 2 : 1;
        int gradeLift = st.FightIndex == 2 && st.RopePulled ? 1 : 0;   // the rope answered

        var comp = new List<WaveSlot>();
        if (st.FightIndex == 2)
        {
            // The champion: one of the heavy half, grown to grade 3 (+60% HP, +30% stats).
            var c = pool[4 + rnd.Next(4)];
            comp.Add(new WaveSlot(c.id, 3 + gradeLift));
            budget -= c.cost;
        }
        while (budget > 0 && comp.Count < 5)
        {
            var pick = pool[rnd.Next(pool.Length)];
            if (pick.cost > budget) { budget--; continue; }   // small change buys nothing
            int grade = Math.Max(gradeFloor, st.FightIndex >= 1 && rnd.Next(3) == 0 ? 2 : 1) + gradeLift;
            comp.Add(new WaveSlot(pick.id, grade));
            budget -= pick.cost;
        }
        if (comp.Count == 0) comp.Add(new WaveSlot("barrow_husk", gradeFloor));
        return comp.ToArray();
    }

    /// <summary>Bite the rectangle ragged with void from the rim, then drop 2-3 CLUSTERS
    /// of grave-stones grown by random walk — never a perfect square, never bare.</summary>
    public static Battlefield BuildBoard(Random rnd)
    {
        var f = new Battlefield(Cols, Rows);
        int bites = 4 + rnd.Next(3);
        for (int b = 0; b < bites; b++)
        {
            int edge = rnd.Next(4);
            var c = new CellCoord(
                edge == 0 ? 0 : edge == 1 ? Cols - 1 : rnd.Next(Cols),
                edge == 2 ? 0 : edge == 3 ? Rows - 1 : rnd.Next(Rows));
            int size = 2 + rnd.Next(4);
            for (int i = 0; i < size; i++)
            {
                f.SetHole(c);
                var opts = f.Orthogonal(c).ToList();
                if (opts.Count == 0) break;
                c = opts[rnd.Next(opts.Count)];
            }
        }
        int clusters = 2 + rnd.Next(2);
        for (int k = 0; k < clusters; k++)
        {
            var c = new CellCoord(3 + rnd.Next(Cols - 6), 1 + rnd.Next(Math.Max(1, Rows - 2)));
            int stones = 2 + rnd.Next(4);
            for (int i = 0; i < stones; i++)
            {
                if (f.TileAt(c) != TileKind.Void) f.SetObstacle(c);
                var opts = f.Orthogonal(c).ToList();
                if (opts.Count == 0) break;
                c = opts[rnd.Next(opts.Count)];
            }
        }
        // Bone spikes: a few open graves' worth of teeth, mid-board where the fighting happens.
        for (int i = 0, laid = 0; i < 24 && laid < 3; i++)
        {
            var c = new CellCoord(2 + rnd.Next(Cols - 4), rnd.Next(Rows));
            if (f.TileAt(c) is TileKind.Grass or TileKind.Grass2) { f.SetTile(c, TileKind.Spikes); laid++; }
        }
        return f;
    }

    /// <summary>A walkable cell as far to one side as the island allows, spread by slot.</summary>
    public static CellCoord Spawn(Battlefield f, bool left, int idx, HashSet<CellCoord> taken)
    {
        int yTarget = (Rows / 2 - 1 + idx * 2) % Rows;
        for (int dx = 0; dx < Cols; dx++)
        {
            int x = left ? 1 + dx : Cols - 2 - dx;
            if (x < 0 || x >= Cols) continue;
            var best = Enumerable.Range(0, Rows).Select(y => new CellCoord(x, y))
                .Where(c => f.IsWalkable(c) && !taken.Contains(c))
                .OrderBy(c => Math.Abs(c.Y - yTarget)).ToList();
            if (best.Count > 0) { taken.Add(best[0]); return best[0]; }
        }
        return new CellCoord(Cols / 2, Rows / 2);
    }

    /// <summary>Build one fight exactly as the game does: the ragged island (regenerated until
    /// every grave is reachable), the drafted host, the embers, the blessed avatar. The caller
    /// subscribes to Emitted and calls Start().</summary>
    public static (CombatEngine Engine, Fighter Avatar) CreateFight(
        RunState st, CampaignUnit you, CampaignUnit? mate, ref int seed)
    {
        bool boss = st.SextonNow || st.FightIndex >= 3;
        var comp = BuildWave(new Random(seed * 31 + st.FightIndex), boss, st);

        var rnd = new Random(++seed);
        Battlefield field;
        CellCoord aSpawn; CellCoord mateSpawn = default;
        var mobSpawns = new List<CellCoord>();
        for (int attempt = 0; ; attempt++)
        {
            field = BuildBoard(rnd);
            var taken = new HashSet<CellCoord>();
            aSpawn = Spawn(field, left: true, 0, taken);
            if (mate != null) mateSpawn = Spawn(field, left: true, 2, taken);
            mobSpawns.Clear();
            for (int i = 0; i < comp.Length; i++) mobSpawns.Add(Spawn(field, left: false, i, taken));
            bool ok = mobSpawns.All(mc => Pathfinding.FindPath(field, aSpawn, mc,
                _ => false, allowOccupiedGoal: true) != null);
            if (mate != null) ok &= Pathfinding.FindPath(field, aSpawn, mateSpawn,
                _ => false, allowOccupiedGoal: true) != null;
            if (ok) break;
            if (attempt > 24)
            {
                // Never ship a sealed island: the last resort is bare ground.
                field = new Battlefield(Cols, Rows);
                var t2 = new HashSet<CellCoord>();
                aSpawn = Spawn(field, left: true, 0, t2);
                if (mate != null) mateSpawn = Spawn(field, left: true, 2, t2);
                mobSpawns.Clear();
                for (int i = 0; i < comp.Length; i++) mobSpawns.Add(Spawn(field, left: false, i, t2));
                break;
            }
        }

        st.Embers.Clear();
        for (int i = 0; i < 24 && st.Embers.Count < 5; i++)
        {
            var c = new CellCoord(2 + rnd.Next(Cols - 4), rnd.Next(Rows));
            if (field.IsWalkable(c) && field.TileAt(c) != TileKind.Spikes
                && c != aSpawn && !mobSpawns.Contains(c)) st.Embers.Add(c);
        }
        st.Fallen.Clear();
        st.BossFight = boss; st.RitualTurned = false;

        var avatar = Bless(TitheContent.MakeCrewMember(you, aSpawn), st, you);
        var fighters = new List<Fighter> { avatar };
        if (mate != null) fighters.Add(TitheContent.MakeCrewMember(mate, mateSpawn));
        for (int i = 0; i < comp.Length; i++)
            fighters.Add(TitheContent.MakeMob(comp[i].Id, $"mob_{st.FightIndex}_{i}", mobSpawns[i], comp[i].Grade));

        var engine = new CombatEngine(field, fighters, new SystemRng(seed))
        // The Gauntlet's rules of engagement: coastlines kill, backs are worth finding.
        { LethalVoid = true, Backstabs = true };
        // Ember graves are the game's hazard, not the board's — teach the AI they're hot.
        engine.TileDanger = c => st.Embers.Contains(c) ? 2 : 0;
        return (engine, avatar);
    }

    public static string FightLabel(RunState st) => st.SextonNow || st.FightIndex >= 3 ? "THE SEXTON"
        : $"FIGHT {st.FightIndex + 1} OF 3";

    public static int XpForFight(int fightIndex) => 45 + 30 * Math.Min(fightIndex, 3);

    // ----- the verbs (Isaac's law, event-driven) ------------------------------------------

    /// <summary>Rot or fire, it ticks the same: apply a poison the MARROW rules then deepen.</summary>
    public static void Afflict(RunState st, Fighter target, int magnitude)
    {
        magnitude += st.Essences.Contains("GRAVE DAMP") ? 1 : 0;
        int turns = 2 + (st.Essences.Contains("WRAITH'S BREATH") ? 1 : 0);
        var existing = target.Statuses.FirstOrDefault(s => s.Kind == StatusKind.Poison);
        if (existing != null)
        {
            existing.Magnitude = Math.Max(existing.Magnitude, magnitude);
            existing.Remaining = Math.Max(existing.Remaining, turns);
        }
        else target.Statuses.Add(new StatusEffect(StatusKind.Poison, magnitude, turns));
    }

    /// <summary>Take an essence; the third of a theme is a TRANSFORMATION (Isaac's Guppy law).</summary>
    public static void TakeEssence(RunState st, string name, IRunFx? fx)
    {
        st.Essences.Add(name);
        string theme = ThemeOf(name);
        if (st.ThemeCount(theme) == 3 && st.Announced.Add(theme))
        {
            if (theme == "BELL") st.Tolls += 6;   // THE TITHED: the bell owes you
            fx?.Transformed(theme, FormOf(theme));
        }
    }

    /// <summary>Everything mechanical that hangs off the fight's events: the AP economy's
    /// covenant hooks, ember graves, the bell's tolls, the Sexton's ritual, the essence verbs.
    /// The game and the sim both route every CombatEvent through here.</summary>
    public static void HandleMechanics(CombatEngine engine, CombatEvent e,
        RunState st, Fighter avatar, IRunFx? fx)
    {
        switch (e)
        {
            case TurnStarted ts:
                // Kindling burns brighter in a bleeding hand: +1 AP while bloodied.
                if (ts.Fighter == avatar && st.HasKeyword("kindling") && avatar.Hp * 2 <= avatar.MaxHp)
                    ts.Fighter.CurrentAp += 1;
                // THE WALL BRACES (sim-priced: the bulwark reached HIM at 26% health):
                // the shield-class starts every turn with 3 points of stone between
                // it and the host. Chip damage pays the toll before blood does.
                if (ts.Fighter == avatar && avatar.Archetype == "bulwark")
                {
                    var brace = avatar.Statuses.FirstOrDefault(x => x.Kind == StatusKind.Shield);
                    if (brace == null) avatar.Statuses.Add(new StatusEffect(StatusKind.Shield, 4, 2));
                    else { brace.Magnitude = Math.Max(brace.Magnitude, 4); brace.Remaining = Math.Max(brace.Remaining, 2); }
                }
                // The dead swing ONCE a turn (plus small change): you carry a full Dofus
                // purse, but you are one body against a host. Full mob AP meant two or
                // three blows to your one — the sim put every class under 8% on it.
                if (ts.Fighter.Team == Team.Enemy && ts.Fighter.Spells.Count > 0)
                    ts.Fighter.CurrentAp = Math.Min(ts.Fighter.CurrentAp,
                        ts.Fighter.Spells.Max(s => s.ApCost) + 1);
                // THE BLIGHTED: the rot is an aura — whatever stands beside you sickens.
                if (ts.Fighter == avatar && st.Transformed("MARROW"))
                    foreach (var near in engine.Fighters.Where(x => x.IsAlive && x.Team == Team.Enemy
                                 && x.Pos.DistanceTo(ts.Fighter.Pos) == 1))
                    { Afflict(st, near, 1); fx?.Afflicted(near, burn: false); }
                // Standing on an ember grave cooks you at dawn (never to death).
                if (st.Embers.Contains(ts.Fighter.Pos) && ts.Fighter.IsAlive && ts.Fighter.Hp > 2
                    && !(ts.Fighter == avatar && (st.HasKeyword("warm") || st.Transformed("BONE"))))
                {
                    ts.Fighter.Hp = Math.Max(1, ts.Fighter.Hp - 2);
                    fx?.EmberBurn(ts.Fighter, ts.Fighter.Pos);
                }
                // The bell is turn-priced: each of YOUR turns tolls it once.
                if (ts.Fighter == avatar && !st.SextonNow && st.FightIndex < 3)
                {
                    st.Tolls--;
                    if (st.Tolls <= 0) { st.Tolls = 0; st.SextonNow = true; fx?.SextonComes(); }
                    else if (st.Tolls <= 5) fx?.TollsLow(st.Tolls);
                }
                break;

            case FighterMoved fm:
                // g10: you WALK now — so an ember grave crossed is an ember grave felt.
                if (!(fm.Fighter == avatar && (st.HasKeyword("warm") || st.Transformed("BONE"))))
                    foreach (var c in fm.Path.Skip(1))
                        if (st.Embers.Contains(c) && fm.Fighter.IsAlive && fm.Fighter.Hp > 2)
                        {
                            fm.Fighter.Hp = Math.Max(1, fm.Fighter.Hp - 2);
                            fx?.EmberBurn(fm.Fighter, c);
                        }
                break;

            case DamageDealt d:
                // THE RITUAL TURNS: at half health the Sexton stops digging your grave
                // politely — +25% to his blows, and the graves themselves open.
                if (st.BossFight && !st.RitualTurned && d.Target.Archetype == "sexton"
                    && d.RemainingHp > 0 && d.RemainingHp * 2 <= d.Target.MaxHp)
                {
                    st.RitualTurned = true;
                    d.Target.Statuses.Add(new StatusEffect(StatusKind.DamageBuff, 25, 99));
                    var rr = new Random(unchecked(st.FightIndex * 7919 + engine.Round * 31 + 1));
                    var grown = new List<CellCoord>();
                    for (int i = 0; i < 40 && grown.Count < 3; i++)
                    {
                        var c = new CellCoord(rr.Next(Cols), rr.Next(Rows));
                        if (engine.Field.TileAt(c) is TileKind.Grass or TileKind.Grass2
                            && !engine.Fighters.Any(x => x.IsAlive && x.Pos == c))
                        { engine.Field.SetTile(c, TileKind.Spikes); grown.Add(c); }
                    }
                    fx?.RitualTurns(grown);
                }
                // On-strike rules (Isaac's verbs): your elemental hits carry rot and fire.
                if (engine.Outcome == FightOutcome.Ongoing && engine.Current == avatar
                    && d.Target.Team == Team.Enemy && d.Target.IsAlive
                    && d.Element != Element.Neutral && d.Amount > 0)
                {
                    if (st.Essences.Contains("SPITTER'S GIFT"))
                    { Afflict(st, d.Target, 1); fx?.Afflicted(d.Target, burn: false); }
                    if (st.HasKeyword("burning"))
                    { Afflict(st, d.Target, 2); fx?.Afflicted(d.Target, burn: true); }
                }
                break;

            case FighterFell ff:
                st.Fallen.Add(ff.Fighter.Id);
                if (ff.Fighter.Team == Team.Enemy) st.Falls++;
                break;

            case FighterDied fd:
                if (fd.Fighter.Team == Team.Enemy)
                {
                    bool fell = st.Fallen.Contains(fd.Fighter.Id);
                    if (!fell) st.Kills++;   // the fallen are the void's ledger, not the earth's
                    int pay = TitheContent.MobStonesOf(fd.Fighter)
                              + (st.Essences.Contains("GRASP'S COIN") ? 1 : 0);
                    if (fell && st.Essences.Contains("OSSUARY DUST")) pay *= 2;   // the void pays double
                    st.RunStones += pay;
                    fx?.StonesPaid(pay, fd.At);
                    if (st.Essences.Contains("TOLL KEEPER")) st.Tolls += 1;
                    if (st.Transformed("BELL")) st.Tolls += 1;   // THE TITHED: every death buys time
                    bool wasPoisoned = fd.Fighter.Statuses.Any(s => s.Kind == StatusKind.Poison);
                    if (wasPoisoned && st.Essences.Contains("MITE'S HUNGER") && avatar.IsAlive)
                    {
                        int fed = Math.Min(2, avatar.MaxHp - avatar.Hp);
                        if (fed > 0) { avatar.Hp += fed; fx?.FedByMite(avatar, fed); }
                    }
                    if (wasPoisoned && st.Essences.Contains("PIPER'S ROT"))
                        foreach (var near in engine.Fighters.Where(x => x.IsAlive && x.Team == Team.Enemy
                                     && x.Pos.DistanceTo(fd.At) == 1))
                        { Afflict(st, near, 1); fx?.Afflicted(near, burn: false); }
                }
                break;
        }
    }

    // ----- the picks (shared hands: the game draws them, the sim draws FROM them) ---------

    public static List<PickCard> RollSpoils(RunState st, CampaignUnit you, Random rnd, IRunFx? fx)
    {
        var cards = new List<PickCard>();

        // 1) An essence — Isaac's rule-card, one per hand while any remain untaken.
        var essChoices = EssencePool.Where(p => !st.Essences.Contains(p.Name)).ToList();
        if (essChoices.Count > 0)
        {
            var p = essChoices[rnd.Next(essChoices.Count)];
            cards.Add(new PickCard(p.Name, $"{p.Blurb}\n\n{p.Theme} {st.ThemeCount(p.Theme) + 1} OF 3\nforever",
                "ESSENCE", () => TakeEssence(st, p.Name, fx)));
        }

        // 2) Gear — Loop Hero's one number, Mewgenics' families. Shows what it replaces.
        // The hand before HIM keeps a seat free for the mend (it used to be crowded out
        // by two gear cards — the sim caught the "guaranteed" last supper never arriving).
        int gearSeats = st.FightIndex >= 3 ? 1 : 2;
        var gearChoices = GearPool.Where(g => st.Gear.GetValueOrDefault(g.Slot)?.Name != g.Name).ToList();
        for (int k = 0; k < gearSeats && cards.Count < 3 && gearChoices.Count > 0; k++)
        {
            var g = gearChoices[rnd.Next(gearChoices.Count)];
            gearChoices.RemoveAll(x => x.Name == g.Name);
            var worn = st.Gear.GetValueOrDefault(g.Slot);
            // Set progress counts what would be worn AFTER the take (the replaced slot drops out).
            int famAfter = st.Gear.Values.Count(x => x.Family == g.Family && x.Slot != g.Slot) + 1;
            string body = g.Blurb + $"\n\n{g.Slot} · {g.Family} SET {famAfter}/3"
                + (worn != null ? $"\nsheds {worn.Name}" : "");
            cards.Add(new PickCard(g.Name, body, "GEAR", () => st.Gear[g.Slot] = g));
        }

        // 3) A blessing tops the hand off — instant relief, no strings.
        var blessings = new List<PickCard>
        {
            new("MEND", "FULL HEAL NOW\n\nthe bread is\nstale. eat.", "BLESSING", () => you.CurrentHp = null),
            new("COIN OF THE GRASP", "+25 STONES NOW\n\nit hums when\nyou hold it", "BLESSING", () => st.RunStones += 25),
            new("OIL FOR THE BELL", "+5 TOLLS\n\nhe waits.\nbarely.", "BLESSING", () => st.Tolls += 5),
        };
        // The last supper: the hand before HIM always offers the mend.
        if (st.FightIndex >= 3 && cards.Count < 3) cards.Add(blessings[0]);
        while (cards.Count < 3)
        {
            var c = blessings[rnd.Next(blessings.Count)];
            if (!cards.Contains(c)) cards.Add(c);
        }
        return cards;
    }

    /// <summary>The Mewgenics ding: every level drafts three words of ruin — and the real
    /// words are SPELLS. Learn the class's next skill ahead of your years, or deepen one
    /// you know to its next rank.</summary>
    public static List<PickCard> RollLevelCards(RunState st, CampaignUnit you, Random rnd)
    {
        static string Cost(SpellDef s) =>
            (s.ApCost == 0 ? "FREE" : $"{s.ApCost} AP") + $" · RANGE {s.MinRange}-{s.MaxRange}";
        static string EffectsLine(SpellDef s) => GauntletText.EffectsLine(s);

        var spellCards = new List<PickCard>();
        var known = TitheContent.ClassSkillsAt(you.ClassId, you.Level).Concat(st.Learned).Distinct().ToList();

        // LEARN: the next word the ladder hasn't taught yet.
        var next = TitheContent.ClassSkillsAt(you.ClassId, 99).FirstOrDefault(k => !known.Contains(k));
        if (next != null)
        {
            var sp = TitheContent.SkillAtRank(next, 1);
            spellCards.Add(new PickCard($"LEARN {sp.Name.ToUpperInvariant()}",
                $"{EffectsLine(sp)}\n{Cost(sp)}\n\na word ahead\nof your years", "RUIN",
                () => st.Learned.Add(next)));
        }

        // DEEPEN: any known word still below its top rank.
        foreach (var key in known.Where(k => you.RankOf(k) < TitheContent.MaxRank(k)).Take(2))
        {
            int rank = you.RankOf(key) + 1;
            var sp = TitheContent.SkillAtRank(key, rank);
            spellCards.Add(new PickCard($"DEEPEN {TitheContent.SkillAtRank(key, 1).Name.ToUpperInvariant()}",
                $"{sp.Name.ToUpperInvariant()}\n{EffectsLine(sp)}\n{Cost(sp)}\n\nthe word deepens", "RUIN",
                () => you.SpellRanks[key] = rank));
        }

        var statWords = new List<PickCard>
        {
            new("IRON MARROW", "+10 MAX HP\n\nthe grave gives\nback a little", "RUIN", () => st.BonusHp += 10),
            new("KILLING WORD", "+5 DAMAGE\n\nsay it and\nsomething breaks", "RUIN", () => st.BonusDmg += 5),
            new("SECOND WIND", "+1 MP\n\nthe body learns\nwhat the bell asks", "RUIN", () => st.BonusMove += 1),
            new("DEEP WELL", "+1 AP\n\nyou drink where\nno water is", "RUIN", () => st.BonusAp += 1),
            new("OLD BLOOD", "FULL HEAL\n+4 MAX HP\n\nyours, again", "RUIN", () => { st.BonusHp += 4; you.CurrentHp = null; }),
        };

        var cards = new List<PickCard>();
        foreach (var c in spellCards.Take(2)) cards.Add(c);   // the real words lead the draft
        while (cards.Count < 3)
        {
            var c = statWords[rnd.Next(statWords.Count)];
            if (!cards.Contains(c)) cards.Add(c);
        }
        return cards;
    }

    /// <summary>The midpoint fork: two rows lead to the Sexton. Walk one.</summary>
    public static List<PickCard> RollRoadCards(RunState st, IRunFx? fx) => new()
    {
        new("THE QUIET ROW", "the long way round.\n\nfewer teeth waiting,\nthinner spoils", "ROAD",
            () => { st.Road = "quiet"; fx?.Narrate("the quiet row. even the crows whisper."); }),
        new("THE SCREAMING ROW", "every grave open.\n\na grade-2 host —\nand an EXTRA pick\nif you walk out", "ROAD",
            () => { st.Road = "screaming"; fx?.Narrate("the screaming row. they know you're coming."); }),
    };

    /// <summary>One grim little choice a run, before the last pack (Loop Hero's campfire beat).</summary>
    public static (string Title, string Sub, List<PickCard> Cards) RollEventCards(
        RunState st, CampaignUnit you, Random rnd)
    {
        st.EventDone = true;
        int hurt = Math.Max(1, (you.CurrentHp ?? TitheContent.UnitMaxHp(you)) - 8);
        (string title, string sub, PickCard[] options)[] events =
        {
            ("THE OPEN OSSUARY", "a stone lid, already ajar. something glints.", new[]
            {
                new PickCard("PRY IT WIDE", "+25 STONES\n\nand the lid takes\n8 of your blood", "EVENT",
                    () => { st.RunStones += 25; you.CurrentHp = hurt; }),
                new PickCard("SEAL IT", "+2 TOLLS\n\nthe dead sleep\neasier. so do you", "EVENT",
                    () => st.Tolls += 2),
            }),
            ("THE WELL OF WAX", "old candle-water, still warm. it smells of church.", new[]
            {
                new PickCard("DRINK DEEP", "FULL HEAL\n\nbut the wax takes\n2 tolls to swallow", "EVENT",
                    () => { you.CurrentHp = null; st.Tolls = Math.Max(0, st.Tolls - 2); }),
                new PickCard("PASS BY", "+8 STONES\n\nscraped from\nthe rim", "EVENT",
                    () => st.RunStones += 8),
            }),
            ("THE LOOSE ROPE", "a bell-rope, frayed, swaying. it wants pulling.", new[]
            {
                new PickCard("PULL IT", "+5 TOLLS\n\nbut the last pack\nwakes a grade angrier", "EVENT",
                    () => { st.Tolls += 5; st.RopePulled = true; }),
                new PickCard("LET IT SWAY", "+1 TOLL\n\nrestraint is\nits own coin", "EVENT",
                    () => st.Tolls += 1),
            }),
        };
        var ev = events[rnd.Next(events.Length)];
        return (ev.title, ev.sub, ev.options.ToList());
    }
}

/// <summary>Spell text shared by cards, tooltips and the inspector.</summary>
public static class GauntletText
{
    /// <summary>One readable clause per effect — the Loop Hero promise: numbers, no fine print.</summary>
    public static string EffectsLine(SpellDef s) => string.Join(" · ", s.Effects.Select(e => e.Kind switch
    {
        EffectKind.Damage => $"{e.Min}-{e.Max} {e.Element}".ToUpperInvariant(),
        EffectKind.Lifesteal => $"LEECH {e.Min}-{e.Max} {e.Element}".ToUpperInvariant(),
        EffectKind.Heal => $"HEAL {e.Min}-{e.Max}",
        EffectKind.Push => $"PUSH {e.Min}",
        EffectKind.Pull => $"PULL {e.Min}",
        EffectKind.StealAp => $"STEAL {e.Min} AP",
        EffectKind.StealMp => $"STEAL {e.Min} MP",
        EffectKind.GrantAp => $"GRANT {e.Min} AP",
        // Binary states (Rooted, Stabilized) have no magnitude worth printing.
        EffectKind.ApplyStatus => (e.Min > 0 ? $"{e.Status} {e.Min}, {e.Max} TURNS" : $"{e.Status}, {e.Max} TURNS").ToUpperInvariant(),
        EffectKind.Teleport => "LEAP THERE",
        EffectKind.Swap => "TRADE PLACES",
        EffectKind.SelfHpCost => $"BLOOD PRICE {e.Min}",
        EffectKind.Summon => "CALL A SERVANT",
        _ => "",
    }).Where(t => t.Length > 0));
}

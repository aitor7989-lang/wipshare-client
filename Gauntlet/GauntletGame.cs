using DofusSlice.Core.AI;
using DofusSlice.Core.Combat;
using DofusSlice.Core.Content.Tithe;
using DofusSlice.Core.Grid;
using DofusSlice.Core.Spells;
using Gauntlet.Audio;
using Gauntlet.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Gauntlet;

/// <summary>
/// THE GAUNTLET OF THE BELL — TITHE reborn as fight -> pick -> fight (see
/// DofusSlice/docs/GAUNTLET-DESIGN.md). A clean-slate game over the battle-tested
/// DofusSlice.Core engine: Mewgenics' turn economy (move once, strike free, spells
/// on a mana trickle), Loop Hero picks after every fight, Isaac-mold essences, and
/// Crawl's brutality (hit-stop, smears, blood that stays, a narrator).
/// </summary>
public sealed class GauntletGame : Game
{
    // ----- shell ---------------------------------------------------------------------
    private const int W = 1280, H = 760;
    private const int Cols = 13, Rows = 8;
    private const int TW = 80, TH = 40;   // the iso diamond footprint
    private const int OX = W / 2 - (Cols - Rows) * TW / 4, OY = 96;

    private readonly GraphicsDeviceManager _gfx;
    private SpriteBatch _sb = null!;
    private Primitives _prim = null!;
    private PixelFont _font = null!;
    private SpriteBank _sprites = null!;
    private SoundBank _sfx = null!;

    private KeyboardState _keys, _prevKeys;
    private MouseState _mouse, _prevMouse;
    private float _time;

    private enum Scene { City, Fight, Pick, End }
    private Scene _scene = Scene.City;

    // ----- the run -------------------------------------------------------------------
    private int _banked;                 // stones safe at home, across runs
    private int _runStones;              // carried this run — lost in part if you fall
    private int _tolls;                  // the bell: every turn YOU take tolls once; at zero HE comes
    private int _fightIndex;             // 0..2 packs, 3 = THE SEXTON
    private bool _runWon, _sextonNow;
    private CampaignUnit _you = NewYou();
    private CampaignUnit? _mate;         // the hired Sellsword — rides every run until the day he dies
    private readonly List<string> _essences = new();
    private int _bonusHp, _bonusDmg, _bonusMove, _bonusRegen;
    private int _pendingLevels;          // dings earned this run, each owed a draft of 3
    private const int TollStart = 20;    // turn-priced, not wall-clock: thinking is free, stalling is not
    private const int MateCost = 30;

    private static CampaignUnit NewYou(string classId = "cannon") =>
        new() { Id = "avatar", ClassId = classId, Name = "You", IsAvatar = true };

    // ----- the fight -----------------------------------------------------------------
    private CombatEngine _engine = null!;
    private Fighter _avatar = null!;
    private int _seed = Environment.TickCount;
    private readonly Dictionary<string, int> _manaCarry = new();
    private readonly HashSet<CellCoord> _embers = new();
    private int _selected = -1;          // armed spell index into _avatar.Spells
    private string _turnOwner = "";
    private float _aiTimer, _endPause;
    private bool _aiActed, _resolved;
    private Dictionary<CellCoord, int> _moveRange = new();

    // ----- the feel (the Crawl layer) -------------------------------------------------
    private float _freeze, _shake;
    private readonly List<(Vector2 p, int seed)> _blood = new();
    private readonly List<(Vector2 from, Vector2 to, float ttl)> _smears = new();
    private readonly List<(string t, Color c, Vector2 p, float born)> _floats = new();
    private readonly List<(Fighter f, CellCoord at)> _corpses = new();
    private readonly HashSet<string> _fallen = new();   // took the void exit: no corpse, no blood
    private string _narration = ""; private float _narrationUntil;
    private string _banner = ""; private float _bannerUntil; private Color _bannerInk;
    private bool _firstBlood, _firstSpike;
    private int _kills, _falls;                          // the run's ledger, read at the end
    private Texture2D _vignette = null!;                 // Crawl's candle-dark, baked once
    private static readonly Color Gold = new(232, 192, 88);   // the backstab's one glint of color
    private readonly Random _rng = new();

    // ----- the pick ------------------------------------------------------------------
    private List<(string title, string body, string kind, Action apply)> _cards = new();
    private string _pickTitle = "", _pickSub = "";
    private Color _pickInk = Mono.Ink;

    // ----- the road (g7): the fork, the events, what survives the night ---------------
    private string _road = "";           // "" until the fork; then "quiet" or "screaming"
    private bool _extraPick, _eventDone; // the screaming row's bonus hand; one event a run
    private bool _ropePulled;            // the loose rope: fight 3 comes a grade angrier
    private bool _bossFight, _ritualTurned;  // the Sexton's stage, and his half-health turn

    // ----- the covenant (g5): gear in four slots, essences in three themes -------------
    // Isaac's law: an item is a RULE on a shared verb (strike/push/kill/toll), and rules
    // stack. Mewgenics' law: pieces belong to FAMILIES, and three of a family pays a set
    // bonus. Essences follow Isaac (three of a THEME transforms you); gear follows both.
    private sealed record GearDef(string Slot, string Name, string Family, int Power, string? Keyword, string Blurb);
    private readonly Dictionary<string, GearDef> _gear = new();   // slot -> the worn piece
    private readonly HashSet<string> _transformed = new();        // themes already announced

    private static readonly GearDef[] GearPool =
    {
        new("BLADE", "GRAVE BLADE",  "GRAVE", 3, null,       "+3 DMG"),
        new("BLADE", "EMBER BLADE",  "EMBER", 2, "burning",  "+2 DMG\nstrikes BURN 2\nfor 2 turns"),
        new("BLADE", "BONE CLEAVER", "BONE",  2, "heavy",    "+2 DMG\nyour pushes\ngo +1 cell"),
        new("PLATE", "GRAVE PLATE",  "GRAVE", 8, null,       "+8 MAX HP"),
        new("PLATE", "EMBER PLATE",  "EMBER", 6, "warm",     "+6 MAX HP\nember graves\ndon't burn you"),
        new("PLATE", "BONE PLATE",   "BONE",  6, "thorns",   "+6 MAX HP\nreflect 15% of\nspell damage"),
        new("BOOTS", "GRAVE BOOTS",  "GRAVE", 1, null,       "+1 MOVE"),
        new("BOOTS", "BONE HOBNAILS","BONE",  1, "surefoot", "+1 MOVE\nbone spikes\nspare you"),
        new("CHARM", "GRAVE CHARM",  "GRAVE", 1, null,       "+1 MANA A TURN"),
        new("CHARM", "EMBER CHARM",  "EMBER", 1, "kindling", "+1 MANA A TURN\n+1 more when\nbloodied"),
        new("CHARM", "BONE KNUCKLE", "BONE",  1, "slam",     "+1 MANA A TURN\nwall slams\ndeal +4"),
    };

    private static readonly (string Name, string Theme, string Blurb)[] EssencePool =
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
        ("HOUR THIEF",     "BELL",   "+1 mana\na turn"),
        ("QUIET STEP",     "BELL",   "+1 MOVE"),
        ("LAST ECHO",      "BELL",   "enter every fight with\na quarter of you, at least"),
    };

    private int FamilyCount(string family) => _gear.Values.Count(g => g.Family == family);
    private bool HasKeyword(string k) => _gear.Values.Any(g => g.Keyword == k);
    private static string ThemeOf(string essence) => EssencePool.First(p => p.Name == essence).Theme;
    private int ThemeCount(string theme) => _essences.Count(e => ThemeOf(e) == theme);
    private bool Transformed(string theme) => ThemeCount(theme) >= 3;
    private static Color ThemeInk(string theme) => theme switch
    { "MARROW" => Mono.Heal, "BELL" => Gold, _ => Mono.Ink };

    public GauntletGame()
    {
        _gfx = new GraphicsDeviceManager(this)
        { PreferredBackBufferWidth = W, PreferredBackBufferHeight = H };
        IsMouseVisible = true;
        Window.Title = "THE GAUNTLET OF THE BELL";
    }

    protected override void LoadContent()
    {
        _sb = new SpriteBatch(GraphicsDevice);
        _prim = new Primitives(GraphicsDevice, TW, TH, 24);
        var px = new Texture2D(GraphicsDevice, 1, 1);
        px.SetData(new[] { Color.White });
        _font = new PixelFont(px);
        _sprites = new SpriteBank(GraphicsDevice);
        _sfx = new SoundBank();

        // The vignette: the arena is lit from its middle and the dark leans in from the rim.
        _vignette = new Texture2D(GraphicsDevice, W, H);
        var vd = new Color[W * H];
        float cx = W / 2f, cy = H / 2f, maxD = MathF.Sqrt(cx * cx + cy * cy);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxD;
                float a = Math.Clamp((d - 0.52f) / 0.48f, 0f, 1f);
                vd[y * W + x] = Color.Black * (a * a * 0.5f);
            }
        _vignette.SetData(vd);
        LoadMeta();
    }

    // ----- what survives the night: a tiny save of the meta (g7) -----------------------

    private static string SavePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "gauntlet-of-the-bell.json");

    private void SaveMeta()
    {
        try
        {
            File.WriteAllText(SavePath, System.Text.Json.JsonSerializer.Serialize(new
            { banked = _banked, classId = _you.ClassId, level = _you.Level, xp = _you.Xp, mate = _mate?.ClassId ?? "" }));
        }
        catch { /* a lost save must never take the game down with it */ }
    }

    private void LoadMeta()
    {
        try
        {
            if (!File.Exists(SavePath)) return;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(SavePath));
            var r = doc.RootElement;
            _banked = r.GetProperty("banked").GetInt32();
            _you = NewYou(r.GetProperty("classId").GetString() ?? "cannon");
            _you.Level = Math.Max(1, r.GetProperty("level").GetInt32());
            _you.Xp = Math.Max(0, r.GetProperty("xp").GetInt32());
            string mate = r.GetProperty("mate").GetString() ?? "";
            if (mate != "") _mate = new CampaignUnit { Id = "mate", ClassId = mate, Name = "Sellsword" };
        }
        catch { /* an unreadable save is a fresh start, not a crash */ }
    }

    // ================= RUN FLOW ========================================================

    private void StartRun()
    {
        _runStones = 0; _tolls = TollStart; _fightIndex = 0; _sextonNow = false; _runWon = false;
        _essences.Clear(); _gear.Clear(); _transformed.Clear();
        _bonusHp = _bonusDmg = _bonusMove = _bonusRegen = 0;
        _pendingLevels = 0; _kills = 0; _falls = 0;
        _road = ""; _extraPick = false; _eventDone = false; _ropePulled = false;
        _you.CurrentHp = null;
        if (_mate != null) _mate.CurrentHp = null;   // the city rests everyone
        StartFight();
    }

    private void StartFight()
    {
        _blood.Clear(); _smears.Clear(); _floats.Clear(); _corpses.Clear(); _fallen.Clear();
        _manaCarry.Clear(); _selected = -1; _turnOwner = ""; _resolved = false;
        _firstBlood = false; _firstSpike = false;

        bool boss = _sextonNow || _fightIndex >= 3;
        var comp = BuildWave(new Random(_seed * 31 + _fightIndex), boss);

        // A ragged island with clustered stones — regenerated until every grave can
        // be reached from the crew's ground (no sealed pockets, ever).
        var rnd = new Random(++_seed);
        Battlefield field;
        CellCoord aSpawn; CellCoord mateSpawn = default;
        var mobSpawns = new List<CellCoord>();
        for (int attempt = 0; ; attempt++)
        {
            field = BuildBoard(rnd);
            var taken = new HashSet<CellCoord>();
            aSpawn = Spawn(field, left: true, 0, taken);
            if (_mate != null) mateSpawn = Spawn(field, left: true, 2, taken);
            mobSpawns.Clear();
            for (int i = 0; i < comp.Length; i++) mobSpawns.Add(Spawn(field, left: false, i, taken));
            bool ok = mobSpawns.All(mc => Pathfinding.FindPath(field, aSpawn, mc,
                _ => false, allowOccupiedGoal: true) != null);
            if (_mate != null) ok &= Pathfinding.FindPath(field, aSpawn, mateSpawn,
                _ => false, allowOccupiedGoal: true) != null;
            if (ok) break;
            if (attempt > 24)
            {
                // Never ship a sealed island: a mob nobody can reach is a fight nobody can
                // win. The last resort is bare ground — ugly beats unwinnable.
                field = new Battlefield(Cols, Rows);
                var t2 = new HashSet<CellCoord>();
                aSpawn = Spawn(field, left: true, 0, t2);
                if (_mate != null) mateSpawn = Spawn(field, left: true, 2, t2);
                mobSpawns.Clear();
                for (int i = 0; i < comp.Length; i++) mobSpawns.Add(Spawn(field, left: false, i, t2));
                break;
            }
        }

        _embers.Clear();
        for (int i = 0; i < 24 && _embers.Count < 5; i++)
        {
            var c = new CellCoord(2 + rnd.Next(Cols - 4), rnd.Next(Rows));
            if (field.IsWalkable(c) && field.TileAt(c) != TileKind.Spikes
                && c != aSpawn && !mobSpawns.Contains(c)) _embers.Add(c);
        }

        _avatar = Bless(TitheContent.MakeCrewMember(_you, aSpawn));
        var fighters = new List<Fighter> { _avatar };
        if (_mate != null) fighters.Add(TitheContent.MakeCrewMember(_mate, mateSpawn));
        for (int i = 0; i < comp.Length; i++)
            fighters.Add(TitheContent.MakeMob(comp[i].Id, $"mob_{_fightIndex}_{i}", mobSpawns[i], comp[i].Grade));

        _engine = new CombatEngine(field, fighters, new SystemRng(_seed))
        // The Gauntlet's rules of engagement: coastlines kill, backs are worth finding.
        { LethalVoid = true, Backstabs = true };
        _engine.Emitted += OnCombatEvent;
        _engine.Start();
        _scene = Scene.Fight;
        _bossFight = boss; _ritualTurned = false;
        _sfx.SetAmbient(boss ? "dirge" : "wind", 0.12f);
        Narrate(boss ? "the bell falls silent. HE is here." : $"the dead notice you. ({FightLabel()})");
        if (boss) { Banner("HE IS HERE", Mono.Danger); _sfx.Play("bell", 0.8f, jitter: false); }
    }

    /// <summary>Bite the rectangle ragged with void from the rim, then drop 2-3 CLUSTERS
    /// of grave-stones grown by random walk — never a perfect square, never bare.</summary>
    private static Battlefield BuildBoard(Random rnd)
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
    private static CellCoord Spawn(Battlefield f, bool left, int idx, HashSet<CellCoord> taken)
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

    private string FightLabel() => _sextonNow || _fightIndex >= 3 ? "THE SEXTON"
        : $"FIGHT {_fightIndex + 1} OF 3";

    private readonly record struct WaveSlot(string Id, int Grade);

    /// <summary>The host is drafted, not memorized: each fight spends a points budget on a
    /// varied comp from the whole bestiary. Deeper fights buy heavier bodies and higher
    /// grades, and the third pack always fields a grade-3 CHAMPION.</summary>
    private WaveSlot[] BuildWave(Random rnd, bool boss)
    {
        if (boss) return new[] { new WaveSlot("sexton", 1), new WaveSlot("barrow_husk", 2) };

        (string id, int cost)[] pool =
        {
            ("grave_mite", 1), ("barrow_husk", 2), ("marrow_spitter", 2), ("gravehound", 2),
            ("tomb_wraith", 2), ("cairn_brute", 3), ("grave_ghoul", 3), ("crypt_warden", 3),
        };
        int budget = 3 + _fightIndex * 2;   // 3 / 5 / 7 points across the run
        // The fork prices fight 2: the quiet row spares you a body, the screaming row
        // buys two more and grades the whole host up.
        if (_fightIndex == 1 && _road == "screaming") budget += 2;
        if (_fightIndex == 1 && _road == "quiet") budget -= 1;
        int gradeFloor = _fightIndex == 1 && _road == "screaming" ? 2 : 1;
        int gradeLift = _fightIndex == 2 && _ropePulled ? 1 : 0;   // the rope answered

        var comp = new List<WaveSlot>();
        if (_fightIndex == 2)
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
            int grade = Math.Max(gradeFloor, _fightIndex >= 1 && rnd.Next(3) == 0 ? 2 : 1) + gradeLift;
            comp.Add(new WaveSlot(pick.id, grade));
            budget -= pick.cost;
        }
        if (comp.Count == 0) comp.Add(new WaveSlot("barrow_husk", gradeFloor));
        return comp.ToArray();
    }

    /// <summary>The avatar carries the run's covenant: gear numbers, family sets, essences,
    /// transformations — all folded into one fighter at each fight's dawn.</summary>
    private Fighter Bless(Fighter f)
    {
        int hp = _bonusHp, dmg = _bonusDmg, move = _bonusMove;
        foreach (var piece in _gear.Values)
            switch (piece.Slot)
            {
                case "PLATE": hp += piece.Power; break;
                case "BLADE": dmg += piece.Power; break;
                case "BOOTS": move += piece.Power; break;
            }
        if (FamilyCount("GRAVE") >= 3) hp += 12;   // the dirt holds its own
        if (FamilyCount("EMBER") >= 3) dmg += 2;   // and the fire feeds
        if (_essences.Contains("MASON'S FIST")) dmg += 2;
        if (_essences.Contains("QUIET STEP")) move += 1;

        var g = new Fighter
        {
            Id = f.Id, Name = f.Name, Team = f.Team, PlayerControlled = true,
            Archetype = f.Archetype, Policy = f.Policy, Passive = f.Passive,
            PreferredRangeMin = f.PreferredRangeMin, PreferredRangeMax = f.PreferredRangeMax,
            Level = f.Level, MaxHp = f.MaxHp + hp,
            Hp = Math.Min(f.Hp + hp, f.MaxHp + hp),
            BaseAp = f.BaseAp, BaseMp = f.BaseMp + move,
            Strength = f.Strength, Intelligence = f.Intelligence,
            Chance = f.Chance, Agility = f.Agility,
            // Damage bonuses ride on Power: it scales EVERY element, where the old
            // Intelligence route silently fed only the cannon's fire.
            Power = f.Power + dmg, Wisdom = f.Wisdom, Initiative = f.Initiative,
            Pos = f.Pos,
            Spells = f.Spells.Concat(new[] { Strike() }).ToArray(),
            PushBonus = (HasKeyword("heavy") ? 1 : 0)
                        + (_essences.Contains("HUSK'S GRIP") ? 1 : 0)
                        + (FamilyCount("BONE") >= 3 ? 1 : 0),
            SlamBonus = (HasKeyword("slam") ? 4 : 0) + (_essences.Contains("GHOUL'S WEIGHT") ? 4 : 0),
            HazardImmune = HasKeyword("surefoot") || Transformed("BONE"),
        };
        foreach (var (k, v) in f.Resistances) g.Resistances[k] = v;
        if (_essences.Contains("WARDEN'S HIDE"))
            g.Statuses.Add(new StatusEffect(StatusKind.Shield, 3, 99));
        if (HasKeyword("thorns"))
            g.Statuses.Add(new StatusEffect(StatusKind.Reflect, 15, 99));
        if (_essences.Contains("LAST ECHO") && g.Hp * 4 < g.MaxHp)
            g.Hp = g.MaxHp / 4;   // the echo carries what the body cannot
        return g;
    }

    /// <summary>Mana granted to the avatar on top of the base trickle, from charms and essences.</summary>
    private int RegenBonus() => _bonusRegen
        + _gear.Values.Where(p => p.Slot == "CHARM").Sum(p => p.Power)
        + (_essences.Contains("HOUR THIEF") ? 1 : 0)
        + (HasKeyword("kindling") && _avatar.Hp * 2 <= _avatar.MaxHp ? 1 : 0);

    /// <summary>Rot or fire, it ticks the same: apply a poison the MARROW rules then deepen.</summary>
    private void Afflict(Fighter target, int magnitude)
    {
        magnitude += _essences.Contains("GRAVE DAMP") ? 1 : 0;
        int turns = 2 + (_essences.Contains("WRAITH'S BREATH") ? 1 : 0);
        var existing = target.Statuses.FirstOrDefault(s => s.Kind == StatusKind.Poison);
        if (existing != null)
        {
            existing.Magnitude = Math.Max(existing.Magnitude, magnitude);
            existing.Remaining = Math.Max(existing.Remaining, turns);
        }
        else target.Statuses.Add(new StatusEffect(StatusKind.Poison, magnitude, turns));
    }

    /// <summary>The free basic attack (Mewgenics: costs nothing, once a turn), per class:
    /// the Cannon's spark, the Archer's loosed arrow, the Bulwark's shove.</summary>
    private SpellDef Strike() => _you.ClassId switch
    {
        "archer" => new SpellDef
        {
            Id = 951, Name = "Loosed Arrow", ApCost = 0, MinRange = 2, MaxRange = 5,
            RequiresLineOfSight = true, MaxCastsPerTurn = 1,
            Effects = new[] { SpellEffect.Damage(Element.Air, 5, 7) },
        },
        "bulwark" => new SpellDef
        {
            Id = 952, Name = "Shove", ApCost = 0, MinRange = 1, MaxRange = 1,
            RequiresLineOfSight = true, MaxCastsPerTurn = 1,
            Effects = new[] { SpellEffect.Damage(Element.Earth, 6, 8), SpellEffect.Push(1) },
        },
        _ => new SpellDef
        {
            Id = 950, Name = "Spark", ApCost = 0, MinRange = 1, MaxRange = 3,
            RequiresLineOfSight = true, MaxCastsPerTurn = 1,
            Effects = new[] { SpellEffect.Damage(Element.Fire, 5, 7) },
        },
    };

    // ================= COMBAT EVENTS (the feel hangs off these) ========================

    private void OnCombatEvent(CombatEvent e)
    {
        switch (e)
        {
            case TurnStarted ts:
                // The mana trickle replaces the AP purse — for YOUR side.
                if (ts.Fighter.Team == Team.Player)
                    ts.Fighter.CurrentAp = Math.Min(ts.Fighter.BaseAp,
                        _manaCarry.GetValueOrDefault(ts.Fighter.Id, 3)
                        + 2 + (ts.Fighter == _avatar ? RegenBonus() : 0));
                // THE BLIGHTED: the rot is an aura now — whatever stands beside you sickens.
                if (ts.Fighter == _avatar && Transformed("MARROW"))
                    foreach (var near in _engine.Fighters.Where(x => x.IsAlive && x.Team == Team.Enemy
                                 && x.Pos.DistanceTo(ts.Fighter.Pos) == 1))
                    { Afflict(near, 1); Float("rot", Mono.Heal, Center(near.Pos)); }
                // The dead swing ONCE a turn, like you: full Dofus AP let a husk land two
                // or three blows to your one (a 2 HP hound mauled a 44 HP bulwark dead in
                // a single phase in QA). Cap their purse at their priciest skill.
                else
                    ts.Fighter.CurrentAp = Math.Min(ts.Fighter.CurrentAp,
                        ts.Fighter.Spells.Count > 0 ? ts.Fighter.Spells.Max(s => s.ApCost) : ts.Fighter.CurrentAp);
                if (_embers.Contains(ts.Fighter.Pos) && ts.Fighter.IsAlive && ts.Fighter.Hp > 2
                    && !(ts.Fighter == _avatar && (HasKeyword("warm") || Transformed("BONE"))))
                {
                    ts.Fighter.Hp = Math.Max(1, ts.Fighter.Hp - 2);
                    Splatter(Center(ts.Fighter.Pos), 3);
                    Float("-2", Mono.Element(Element.Fire), Center(ts.Fighter.Pos));
                    _sfx.Play("hit_fire", 0.5f);
                }
                if (ts.Fighter == _avatar)
                {
                    _sfx.Play("yourturn", 0.55f, jitter: false);
                    Banner("YOUR TURN", Mono.Ink);
                    // The bell is turn-priced: each of YOUR turns tolls it once. Think all
                    // you like — the stopwatch is dead — but stalling has a bill.
                    if (!_sextonNow && _fightIndex < 3)
                    {
                        _tolls--;
                        if (_tolls <= 0)
                        {
                            _tolls = 0; _sextonNow = true;
                            _sfx.Play("bell", 0.8f, jitter: false);
                            Narrate("the last toll dies away. HE is coming.");
                        }
                        else if (_tolls <= 5) _sfx.Play("bell", 0.35f, jitter: false);
                    }
                }
                else if (ts.Fighter.Archetype == "sexton") Banner("HE MOVES", Mono.Danger);
                break;

            case DamageDealt d:
                _freeze = Math.Max(_freeze, d.RemainingHp <= 0 ? 0.16f : 0.07f);
                _shake = Math.Max(_shake, Math.Min(10f, 2f + d.Amount * 0.25f));
                Splatter(Center(d.At), 4 + Math.Min(8, d.Amount / 3));
                Float(d.Backstab ? $"-{d.Amount}!" : $"-{d.Amount}", d.Backstab ? Gold : Mono.Danger, Center(d.At));
                if (_engine.Current != null && _engine.Current.Pos != d.At)
                    _smears.Add((Center(_engine.Current.Pos), Center(d.At), 0.11f));
                _sfx.Play("hit_" + d.Element.ToString().ToLowerInvariant(), 0.8f);
                if (!_firstBlood) { _firstBlood = true; Narrate("first blood feeds the ground."); }
                if (!_firstSpike && _engine.Field.TileAt(d.At) == TileKind.Spikes && d.Element == Element.Neutral)
                { _firstSpike = true; Narrate("the bones underfoot are hungry."); }
                // THE RITUAL TURNS: at half health the Sexton stops digging your grave
                // politely — +25% to his blows, and the graves themselves open.
                if (_bossFight && !_ritualTurned && d.Target.Archetype == "sexton"
                    && d.RemainingHp > 0 && d.RemainingHp * 2 <= d.Target.MaxHp)
                {
                    _ritualTurned = true;
                    d.Target.Statuses.Add(new StatusEffect(StatusKind.DamageBuff, 25, 99));
                    var rr = new Random(_seed * 7 + 1);
                    for (int i = 0, grown = 0; i < 40 && grown < 3; i++)
                    {
                        var c = new CellCoord(rr.Next(Cols), rr.Next(Rows));
                        if (_engine.Field.TileAt(c) is TileKind.Grass or TileKind.Grass2
                            && !_engine.Fighters.Any(x => x.IsAlive && x.Pos == c))
                        { _engine.Field.SetTile(c, TileKind.Spikes); Splatter(Center(c), 6); grown++; }
                    }
                    Banner("THE RITUAL TURNS", Mono.Danger);
                    Narrate("the ground answers him. the graves open.");
                    _freeze = Math.Max(_freeze, 0.25f); _shake = Math.Max(_shake, 12f);
                    _sfx.Play("bell", 0.8f, jitter: false);
                }
                // On-strike rules (Isaac's verbs): your elemental hits carry rot and fire.
                if (_engine.Outcome == FightOutcome.Ongoing && _engine.Current == _avatar
                    && d.Target.Team == Team.Enemy && d.Target.IsAlive
                    && d.Element != Element.Neutral && d.Amount > 0)
                {
                    if (_essences.Contains("SPITTER'S GIFT"))
                    { Afflict(d.Target, 1); Float("rot", Mono.Heal, Center(d.At) + new Vector2(18, -6)); }
                    if (HasKeyword("burning"))
                    { Afflict(d.Target, 2); Float("burns", Mono.Element(Element.Fire), Center(d.At) + new Vector2(-18, -6)); }
                }
                break;

            case HealApplied h:
                Float($"+{h.Amount}", Mono.Heal, Center(h.At));
                _sfx.Play("heal", 0.6f);
                break;

            case FighterFell ff:
                // The void takes them whole: no blood, no corpse — just the long quiet.
                _fallen.Add(ff.Fighter.Id);
                if (ff.Fighter.Team == Team.Enemy) _falls++;
                _freeze = Math.Max(_freeze, 0.26f); _shake = Math.Max(_shake, 14f);
                Float("GONE", Mono.Danger, Center(ff.At));
                _sfx.Play("crush", 0.9f);
                Narrate(ff.Fighter == _avatar
                    ? "the dark has a floor. you never find it."
                    : "the island sheds its dead.");
                break;

            case FighterDied fd:
                bool fell = _fallen.Contains(fd.Fighter.Id);
                _freeze = Math.Max(_freeze, 0.2f); _shake = Math.Max(_shake, 12f);
                if (!fell)
                {
                    Splatter(Center(fd.At), 16);
                    _corpses.Add((fd.Fighter, fd.At));
                    _sfx.Play("death", 0.85f);
                }
                if (fd.Fighter.Team == Team.Enemy)
                {
                    if (!fell) _kills++;   // the fallen are the void's ledger, not the earth's
                    int pay = TitheContent.MobStonesOf(fd.Fighter)
                              + (_essences.Contains("GRASP'S COIN") ? 1 : 0);
                    if (fell && _essences.Contains("OSSUARY DUST")) pay *= 2;   // the void pays double
                    _runStones += pay;
                    Float($"+{pay} st", Mono.Ink, Center(fd.At) + new Vector2(0, -20));
                    if (_essences.Contains("TOLL KEEPER")) _tolls += 1;
                    if (Transformed("BELL")) _tolls += 1;   // THE TITHED: every death buys time
                    bool wasPoisoned = fd.Fighter.Statuses.Any(s => s.Kind == StatusKind.Poison);
                    if (wasPoisoned && _essences.Contains("MITE'S HUNGER") && _avatar.IsAlive)
                    {
                        int fed = Math.Min(2, _avatar.MaxHp - _avatar.Hp);
                        if (fed > 0) { _avatar.Hp += fed; Float($"+{fed}", Mono.Heal, Center(_avatar.Pos)); }
                    }
                    if (wasPoisoned && _essences.Contains("PIPER'S ROT"))
                        foreach (var near in _engine.Fighters.Where(x => x.IsAlive && x.Team == Team.Enemy
                                     && x.Pos.DistanceTo(fd.At) == 1))
                        { Afflict(near, 1); Float("rot", Mono.Heal, Center(near.Pos)); }
                    if (!fell) Narrate(fd.Fighter.Archetype == "sexton"
                        ? "the gravedigger digs his own."
                        : "another mouth for the earth.");
                }
                else if (!fell) Narrate(fd.Fighter == _avatar
                    ? "the ground remembers your name."
                    : "the sellsword's contract ends here.");
                break;

            case FighterPushed p when p.CollisionDamage > 0:
                _shake = Math.Max(_shake, 6f);
                break;
        }
    }

    // ================= UPDATE ==========================================================

    protected override void Update(GameTime gt)
    {
        float dt = (float)gt.ElapsedGameTime.TotalSeconds;
        _time += dt;

        // Hit-stop BEFORE input polling: a click or key landed mid-freeze is still an
        // un-consumed transition when the world breathes again, never silently eaten.
        if (_freeze > 0) { _freeze -= dt; base.Update(gt); return; }

        _prevKeys = _keys; _keys = Keyboard.GetState();
        _prevMouse = _mouse; _mouse = Mouse.GetState();
        if (Pressed(Keys.M)) _sfx.Muted = !_sfx.Muted;
        _shake = Math.Max(0, _shake - dt * 40f);
        for (int i = _smears.Count - 1; i >= 0; i--)
        { var s = _smears[i]; s.ttl -= dt; if (s.ttl <= 0) _smears.RemoveAt(i); else _smears[i] = s; }

        switch (_scene)
        {
            case Scene.City: UpdateCity(); break;
            case Scene.Fight: UpdateFight(dt); break;
            case Scene.Pick: UpdatePick(); break;
            case Scene.End: UpdateEnd(); break;
        }
        base.Update(gt);
    }

    private static readonly string[] ClassIds = { "cannon", "archer", "bulwark" };
    private static Rectangle ClassRect(int i) => new(W / 2 - 205 + i * 140, 430, 130, 26);

    /// <summary>The hire is always a class you are not — a second pair of hands, not a mirror.</summary>
    private string MateClassId() => _you.ClassId == "bulwark" ? "archer" : "bulwark";

    private void UpdateCity()
    {
        _sfx.SetAmbient("dirge", 0.09f);
        if (Clicked())
        {
            for (int i = 0; i < ClassIds.Length; i++)
                if (ClassRect(i).Contains(MP) && _you.ClassId != ClassIds[i])
                { _you = NewYou(ClassIds[i]); _sfx.Play("click"); return; }   // the hired sword stays hired
            if (_mate == null && _banked >= MateCost && HireRect.Contains(MP))
            {
                _banked -= MateCost;
                _mate = new CampaignUnit { Id = "mate", ClassId = MateClassId(), Name = "Sellsword" };
                _sfx.Play("coin", 0.85f);
                SaveMeta();
                return;
            }
        }
        if (Pressed(Keys.Space) || Pressed(Keys.Enter) || (Clicked() && DepartRect.Contains(MP)))
        { _sfx.Play("bell", 0.7f, jitter: false); StartRun(); }
    }

    private void UpdateFight(float dt)
    {
        if (_engine.Outcome != FightOutcome.Ongoing)
        {
            if (!_resolved)
            {
                _resolved = true; _endPause = 1.1f;
                _sfx.Play(_engine.Outcome == FightOutcome.Victory ? "victory" : "defeat", 0.8f, jitter: false);
                // The Sellsword's contract is written in his own blood: dead is dead.
                if (_mate != null && _engine.Fighters.FirstOrDefault(f => f.Id == _mate.Id) is { } mf)
                {
                    if (!mf.IsAlive) _mate = null;
                    else _mate.CurrentHp = mf.Hp;
                }
                if (_engine.Outcome == FightOutcome.Victory)
                {
                    _you.CurrentHp = Math.Max(1, _avatar.Hp);
                    int before = _you.Level;
                    _you.GainXp(45 + 30 * Math.Min(_fightIndex, 3));
                    if (_you.Level > before)
                    {
                        _pendingLevels += _you.Level - before;   // each ding owes a draft of three
                        Narrate($"you grow harder. LEVEL {_you.Level} — a new word of ruin.");
                        _sfx.Play("levelup", 0.8f, jitter: false);
                    }
                }
            }
            _endPause -= dt;
            if (_endPause <= 0)
            {
                if (_engine.Outcome != FightOutcome.Victory)
                { _banked += _runStones / 2; _runWon = false; _scene = Scene.End; _sfx.SetAmbient(null); }
                else if (_sextonNow && _fightIndex < 3 || _fightIndex >= 3)
                {
                    if (_fightIndex >= 3 || _sextonNow && _fightIndex == 3)
                    { _banked += _runStones; _runWon = true; _scene = Scene.End; _sfx.SetAmbient(null); }
                    else { _fightIndex = 3; RollCards(); _scene = Scene.Pick; }
                }
                else
                {
                    _fightIndex++;
                    // The screaming row keeps its word: fight 2 survived pays a second hand.
                    if (_fightIndex == 2 && _road == "screaming") _extraPick = true;
                    RollCards(); _scene = Scene.Pick;
                }
            }
            return;
        }

        var cur = _engine.Current;
        if (cur.Id != _turnOwner)
        {
            // bank the leaving fighter's unspent mana before the engine wipes it
            if (_turnOwner != "" && _engine.Fighters.FirstOrDefault(f => f.Id == _turnOwner) is { } left)
                _manaCarry[left.Id] = Math.Min(left.BaseAp, Math.Max(0, left.CurrentAp));
            _turnOwner = cur.Id; _selected = -1; _aiTimer = 0.55f; _aiActed = false;
        }

        bool myTurn = cur == _avatar;
        _moveRange = myTurn ? _engine.MovementRange(cur) : new();

        if (!myTurn)
        {
            _aiTimer -= dt;
            if (_aiTimer <= 0 && !_aiActed) { _aiActed = true; Policy.TakeTurn(_engine, cur); _aiTimer = 0.45f; }
            else if (_aiTimer <= 0 && _aiActed && _engine.Outcome == FightOutcome.Ongoing) _engine.EndTurn();
            return;
        }

        var spells = _avatar.Spells;
        for (int i = 0; i < spells.Count && i < 7; i++)
            if (Pressed(Keys.D1 + i)) { _selected = _selected == i ? -1 : i; _sfx.Play("click", 0.4f); }
        if (Pressed(Keys.Escape)) _selected = -1;
        if (Pressed(Keys.Enter) || Pressed(Keys.Space)) { _engine.EndTurn(); return; }

        if (!Clicked()) return;
        var m = MP;
        for (int i = 0; i < spells.Count && i < 7; i++)
            if (WellRect(i).Contains(m)) { _selected = _selected == i ? -1 : i; _sfx.Play("click", 0.4f); return; }
        if (EndTurnRect.Contains(m)) { _engine.EndTurn(); return; }

        var cell = CellAt(m);
        if (cell is not { } c) return;
        if (_selected >= 0 && _selected < spells.Count)
        {
            var sp = spells[_selected];
            if (_engine.CanCast(_avatar, sp, c, out string? why))
            {
                _engine.TryCast(_avatar, sp, c);
                if (!_engine.CanCast(_avatar, sp, c, out _)) _selected = -1;
            }
            else if (why != null) Narrate(why.ToLowerInvariant());
        }
        else if (_moveRange.ContainsKey(c)) _engine.TryMove(_avatar, c);
    }

    private void UpdatePick()
    {
        for (int i = 0; i < _cards.Count; i++)
            if ((Clicked() && CardRect(i).Contains(MP)) || Pressed(Keys.D1 + i))
            {
                _sfx.Play("coin", 0.8f);
                _cards[i].apply();
                Advance();
                return;
            }
    }

    /// <summary>The road between fights, one screen at a time: dings first, then the
    /// screaming row's bonus hand, then the fork, then the night's one event — then blood.</summary>
    private void Advance()
    {
        if (_pendingLevels > 0) { _pendingLevels--; RollLevelCards(); return; }
        if (_extraPick) { _extraPick = false; RollCards(); return; }
        if (_fightIndex == 1 && _road == "") { RollRoadCards(); return; }
        if (_fightIndex == 2 && !_eventDone) { RollEventCards(); return; }
        StartFight();
    }

    private void UpdateEnd()
    {
        if (Pressed(Keys.Space) || Pressed(Keys.Enter) || Clicked())
        {
            if (!_runWon) _you = NewYou(_you.ClassId); // the fallen leader is buried; kin answer
            SaveMeta();
            _scene = Scene.City;
        }
    }

    // ================= THE PICK ========================================================

    private void RollCards()
    {
        var rnd = new Random(++_seed);
        _pickTitle = "THE SPOILS"; _pickSub = "take ONE. the rest sink into the dirt."; _pickInk = Mono.Ink;
        _cards = new();

        // 1) An essence — Isaac's rule-card, one per hand while any remain untaken.
        var essChoices = EssencePool.Where(p => !_essences.Contains(p.Name)).ToList();
        if (essChoices.Count > 0)
        {
            var p = essChoices[rnd.Next(essChoices.Count)];
            _cards.Add((p.Name, $"{p.Blurb}\n\n{p.Theme} {ThemeCount(p.Theme) + 1} OF 3\nforever", "ESSENCE",
                () => TakeEssence(p.Name)));
        }

        // 2) Gear — Loop Hero's one number, Mewgenics' families. Shows what it replaces.
        var gearChoices = GearPool.Where(g => _gear.GetValueOrDefault(g.Slot)?.Name != g.Name).ToList();
        for (int k = 0; k < 2 && _cards.Count < 3 && gearChoices.Count > 0; k++)
        {
            var g = gearChoices[rnd.Next(gearChoices.Count)];
            gearChoices.RemoveAll(x => x.Name == g.Name);
            var worn = _gear.GetValueOrDefault(g.Slot);
            // Set progress counts what would be worn AFTER the take (the replaced slot drops out).
            int famAfter = _gear.Values.Count(x => x.Family == g.Family && x.Slot != g.Slot) + 1;
            string body = g.Blurb + $"\n\n{g.Slot} · {g.Family} SET {famAfter}/3"
                + (worn != null ? $"\nsheds {worn.Name}" : "");
            _cards.Add((g.Name, body, "GEAR", () => { _gear[g.Slot] = g; _sfx.Play("coin", 0.7f); }));
        }

        // 3) A blessing tops the hand off — instant relief, no strings.
        var blessings = new List<(string t, string b, string k, Action a)>
        {
            ("MEND", "FULL HEAL NOW\n\nthe bread is\nstale. eat.", "BLESSING", () => _you.CurrentHp = null),
            ("COIN OF THE GRASP", "+25 STONES NOW\n\nit hums when\nyou hold it", "BLESSING", () => _runStones += 25),
            ("OIL FOR THE BELL", "+5 TOLLS\n\nhe waits.\nbarely.", "BLESSING", () => _tolls += 5),
        };
        // The last supper: the hand before HIM always offers the mend.
        if (_fightIndex >= 3 && _cards.Count < 3) _cards.Add(blessings[0]);
        while (_cards.Count < 3)
        {
            var c = blessings[rnd.Next(blessings.Count)];
            if (!_cards.Contains(c)) _cards.Add(c);
        }
    }

    /// <summary>Take an essence; the third of a theme is a TRANSFORMATION (Isaac's Guppy law).</summary>
    private void TakeEssence(string name)
    {
        _essences.Add(name);
        _sfx.Play("chime", 0.7f, jitter: false);
        string theme = ThemeOf(name);
        if (ThemeCount(theme) == 3 && _transformed.Add(theme))
        {
            string form = theme switch
            { "MARROW" => "THE BLIGHTED", "BONE" => "THE REVENANT", _ => "THE TITHED" };
            if (theme == "BELL") _tolls += 6;   // THE TITHED: the bell owes you
            Banner(form, ThemeInk(theme));
            Narrate(theme switch
            {
                "MARROW" => "the rot takes root in you. you are THE BLIGHTED.",
                "BONE" => "half-dead already — the ground has no claim. you are THE REVENANT.",
                _ => "the bell tolls for you now. you are THE TITHED.",
            });
            _sfx.Play("levelup", 0.85f, jitter: false);
        }
    }

    /// <summary>The Mewgenics ding: every level earned drafts three words of ruin — keep one.</summary>
    private void RollLevelCards()
    {
        var pool = new List<(string t, string b, string k, Action a)>
        {
            ("IRON MARROW", "+10 MAX HP\n\nthe grave gives\nback a little", "RUIN", () => _bonusHp += 10),
            ("KILLING WORD", "+5 DAMAGE\n\nsay it and\nsomething breaks", "RUIN", () => _bonusDmg += 5),
            ("SECOND WIND", "+1 MOVE\n\nthe body learns\nwhat the bell asks", "RUIN", () => _bonusMove += 1),
            ("DEEP WELL", "+1 MANA A TURN\n\nyou drink where\nno water is", "RUIN", () => _bonusRegen += 1),
            ("OLD BLOOD", "FULL HEAL\n+4 MAX HP\n\nyours, again", "RUIN", () => { _bonusHp += 4; _you.CurrentHp = null; }),
        };
        var rnd = new Random(++_seed);
        _pickTitle = $"A WORD OF RUIN — LEVEL {_you.Level}"; _pickSub = "you grow harder. learn ONE."; _pickInk = Gold;
        _cards = new();
        while (_cards.Count < 3)
        {
            var c = pool[rnd.Next(pool.Count)];
            if (!_cards.Contains(c)) _cards.Add(c);
        }
        _scene = Scene.Pick;
    }

    /// <summary>The midpoint fork: two rows lead to the Sexton. Walk one.</summary>
    private void RollRoadCards()
    {
        _pickTitle = "THE FORK"; _pickSub = "two rows lead to him. walk one."; _pickInk = Mono.Ink;
        _cards = new()
        {
            ("THE QUIET ROW", "the long way round.\n\nfewer teeth waiting,\nthinner spoils", "ROAD",
                () => { _road = "quiet"; Narrate("the quiet row. even the crows whisper."); }),
            ("THE SCREAMING ROW", "every grave open.\n\na grade-2 host —\nand an EXTRA pick\nif you walk out", "ROAD",
                () => { _road = "screaming"; Narrate("the screaming row. they know you're coming."); }),
        };
        _scene = Scene.Pick;
    }

    /// <summary>One grim little choice a run, before the last pack (Loop Hero's campfire beat).</summary>
    private void RollEventCards()
    {
        _eventDone = true;
        var rnd = new Random(++_seed);
        int hurt = Math.Max(1, (_you.CurrentHp ?? TitheContent.UnitMaxHp(_you)) - 8);
        (string title, string sub, (string, string, string, Action)[] options)[] events =
        {
            ("THE OPEN OSSUARY", "a stone lid, already ajar. something glints.",
                new (string, string, string, Action)[]
                {
                    ("PRY IT WIDE", "+25 STONES\n\nand the lid takes\n8 of your blood", "EVENT",
                        () => { _runStones += 25; _you.CurrentHp = hurt; }),
                    ("SEAL IT", "+2 TOLLS\n\nthe dead sleep\neasier. so do you", "EVENT",
                        () => _tolls += 2),
                }),
            ("THE WELL OF WAX", "old candle-water, still warm. it smells of church.",
                new (string, string, string, Action)[]
                {
                    ("DRINK DEEP", "FULL HEAL\n\nbut the wax takes\n2 tolls to swallow", "EVENT",
                        () => { _you.CurrentHp = null; _tolls = Math.Max(0, _tolls - 2); }),
                    ("PASS BY", "+8 STONES\n\nscraped from\nthe rim", "EVENT",
                        () => _runStones += 8),
                }),
            ("THE LOOSE ROPE", "a bell-rope, frayed, swaying. it wants pulling.",
                new (string, string, string, Action)[]
                {
                    ("PULL IT", "+5 TOLLS\n\nbut the last pack\nwakes a grade angrier", "EVENT",
                        () => { _tolls += 5; _ropePulled = true; }),
                    ("LET IT SWAY", "+1 TOLL\n\nrestraint is\nits own coin", "EVENT",
                        () => _tolls += 1),
                }),
        };
        var ev = events[rnd.Next(events.Length)];
        _pickTitle = ev.title; _pickSub = ev.sub; _pickInk = Mono.Dim;
        _cards = ev.options.Select(o => (o.Item1, o.Item2, o.Item3, o.Item4)).ToList();
        _scene = Scene.Pick;
    }

    // ================= FEEL HELPERS ====================================================

    private void Splatter(Vector2 at, int drops)
    {
        for (int i = 0; i < drops; i++)
            _blood.Add((at + new Vector2(_rng.Next(-26, 27), _rng.Next(-18, 19)), _rng.Next(1000)));
        if (_blood.Count > 500) _blood.RemoveRange(0, _blood.Count - 500);
    }

    private void Float(string t, Color c, Vector2 p) => _floats.Add((t, c, p, _time));
    private void Narrate(string line) { _narration = line; _narrationUntil = _time + 3.2f; }
    private void Banner(string t, Color c) { _banner = t; _bannerInk = c; _bannerUntil = _time + 1.1f; }

    // ================= GEOMETRY ========================================================

    private static Vector2 Center(CellCoord c) =>
        new(OX + (c.X - c.Y) * (TW / 2f), OY + (c.X + c.Y) * (TH / 2f));
    private static CellCoord? CellAt(Point m)
    {
        float lx = (m.X - OX) / (TW / 2f), ly = (m.Y - OY) / (TH / 2f);
        int x = (int)MathF.Floor((lx + ly) / 2f + 0.5f), y = (int)MathF.Floor((ly - lx) / 2f + 0.5f);
        if (x < 0 || y < 0 || x >= Cols || y >= Rows) return null;
        return new CellCoord(x, y);
    }

    private void DiamondOutline(Vector2 c, float th, Color col)
    {
        var t = c + new Vector2(0, -TH / 2f); var r = c + new Vector2(TW / 2f, 0);
        var b = c + new Vector2(0, TH / 2f); var l = c + new Vector2(-TW / 2f, 0);
        _prim.Line(_sb, t, r, th, col); _prim.Line(_sb, r, b, th, col);
        _prim.Line(_sb, b, l, th, col); _prim.Line(_sb, l, t, th, col);
    }
    private Point MP => new(_mouse.X, _mouse.Y);
    private bool Pressed(Keys k) => _keys.IsKeyDown(k) && _prevKeys.IsKeyUp(k);
    private bool Clicked() => _mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;

    private static Rectangle WellRect(int i) => new(400 + i * 52, H - 92, 48, 48);
    private static Rectangle EndTurnRect => new(W - 190, H - 86, 130, 30);
    private static Rectangle DepartRect => new(W / 2 - 90, 476, 180, 44);
    private static Rectangle HireRect => new(W / 2 - 170, 556, 340, 30);
    private Rectangle CardRect(int i) => new(W / 2 - (_cards.Count * 240 - 40) / 2 + i * 240, 240, 200, 260);

    // ================= DRAW ============================================================

    protected override void Draw(GameTime gt)
    {
        GraphicsDevice.Clear(Mono.Bg);
        var shakeOff = _shake > 0
            ? new Vector2(_rng.Next(-(int)_shake, (int)_shake + 1) * 0.5f, _rng.Next(-(int)_shake, (int)_shake + 1) * 0.5f)
            : Vector2.Zero;
        _sb.Begin(samplerState: SamplerState.PointClamp,
            transformMatrix: Matrix.CreateTranslation(shakeOff.X, shakeOff.Y, 0));

        switch (_scene)
        {
            case Scene.City: DrawCity(); break;
            case Scene.Fight: DrawFight(); break;
            case Scene.Pick: DrawPick(); break;
            case Scene.End: DrawEnd(); break;
        }

        if (_time < _narrationUntil && _scene != Scene.City)
            _font.DrawCentered(_sb, _narration.ToUpperInvariant(), W / 2, 62, 2,
                Mono.Ink * Math.Min(1f, (_narrationUntil - _time) / 0.6f));

        _sb.End();
        base.Draw(gt);
    }

    private void DrawCity()
    {
        _font.DrawCentered(_sb, "THE GAUNTLET", W / 2, 170, 6, Mono.Ink);
        _font.DrawCentered(_sb, "OF THE BELL", W / 2, 230, 3, Mono.Dim);
        _font.DrawCentered(_sb, $"BANKED: {_banked} ESSENCE STONES", W / 2, 320, 2, Mono.Ink);
        _font.DrawCentered(_sb, $"YOU — {_you.ClassId.ToUpperInvariant()}, LEVEL {_you.Level}", W / 2, 352, 1, Mono.Dim);
        // The road ahead, spelled out Loop Hero style: three graves, then HIM.
        for (int i = 0; i < 4; i++)
        {
            var p = new Vector2(W / 2 - 54 + i * 36, 380);
            if (i > 0) _prim.Line(_sb, p - new Vector2(26, 0), p - new Vector2(10, 0), 1f, Mono.Faint);
            _prim.DiscAt(_sb, p, i == 3 ? 4 : 3, i == 3 ? Mono.Danger : Mono.Dim);
        }
        _font.DrawCentered(_sb, "three packs stand between you and the sexton.", W / 2, 394, 1, Mono.Dim);
        _font.DrawCentered(_sb, "the bell grants twenty tolls. every turn you take is one.", W / 2, 408, 1, Mono.Faint);
        for (int i = 0; i < ClassIds.Length; i++)
        {
            var r = ClassRect(i);
            bool sel = _you.ClassId == ClassIds[i];
            bool ch = r.Contains(MP);
            Mono.Slot(_sb, _prim, r, hover: ch, selected: sel);
            _font.DrawCentered(_sb, ClassIds[i].ToUpperInvariant(), r.Center.X, r.Y + 9, 1,
                sel ? Mono.Ink : Mono.Dim);
        }
        // What the chosen hand actually does — the kit in one whispered line.
        _font.DrawCentered(_sb, _you.ClassId switch
        {
            "archer" => "LOOSED ARROW free, from afar · LONG SHOT: +4 at range 6+",
            "bulwark" => "SHOVE free — the coastline is a weapon · RAGE BELOW: +30% when bloodied",
            _ => "SPARK free · OVERCHANNEL: unspent mana burns hotter",
        }, W / 2, 462, 1, Mono.Dim);
        bool hov = DepartRect.Contains(MP);
        Mono.Button(_sb, _prim, DepartRect, hover: hov);
        _font.DrawCentered(_sb, "DEPART", DepartRect.Center.X, DepartRect.Y + 15, 2, Mono.ButtonInk(hov));
        _font.DrawCentered(_sb, "(SPACE)", W / 2, 530, 1, Mono.Faint);

        // The Post: one hire, paid in banked stones, dead when he's dead.
        if (_mate != null)
            _font.DrawCentered(_sb, $"THE SELLSWORD RIDES WITH YOU — {_mate.ClassId.ToUpperInvariant()}",
                W / 2, HireRect.Y + 10, 1, Mono.Ally);
        else if (_banked >= MateCost)
        {
            bool hh = HireRect.Contains(MP);
            Mono.Button(_sb, _prim, HireRect, hover: hh);
            _font.DrawCentered(_sb, $"HIRE THE SELLSWORD ({MateClassId().ToUpperInvariant()}) — {MateCost} ST",
                HireRect.Center.X, HireRect.Y + 10, 1, Mono.ButtonInk(hh));
        }
        else
            _font.DrawCentered(_sb, $"the post wants {MateCost} banked stones for a sellsword.",
                W / 2, HireRect.Y + 10, 1, Mono.Faint);
    }

    private void DrawFight()
    {
        // The board: a ragged iso island — void is the night, stones cluster like graves.
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
            {
                var cell = new CellCoord(x, y);
                if (_engine.Field.TileAt(cell) == TileKind.Void) continue;
                var cc = Center(cell);
                _prim.DiamondAt(_sb, cc, (x + y) % 2 == 0 ? Mono.Floor : Mono.FloorAlt);
                DiamondOutline(cc, 1f, Mono.Seam * 0.55f);
            }
        foreach (var e in _embers)
        {
            float fl = 0.55f + 0.25f * MathF.Sin(_time * 6f + e.X * 3 + e.Y);
            _prim.DiamondAt(_sb, Center(e), Mono.Element(Element.Fire) * (0.20f * fl));
            _prim.DiscAt(_sb, Center(e) + new Vector2(14, -10), 3, Mono.Element(Element.Fire) * fl);
        }
        foreach (var (p, seed) in _blood)
        {
            var rr = new Random(seed);
            for (int i = 0; i < 3; i++)
                _prim.FillRect(_sb, new Rectangle((int)p.X + rr.Next(-8, 9), (int)p.Y + rr.Next(-4, 5),
                    rr.Next(2, 5), rr.Next(1, 3)), Mono.Danger * 0.55f);
        }

        bool myTurn = _engine.Outcome == FightOutcome.Ongoing && _engine.Current == _avatar;

        // Piloting overlays: green ground, blue reach, in the old color law.
        if (myTurn && _selected < 0)
            foreach (var c in _moveRange.Keys)
                _prim.DiamondAt(_sb, Center(c), Mono.Walk * 0.32f);
        if (myTurn && _selected >= 0 && _selected < _avatar.Spells.Count)
            foreach (var c in _engine.CastableCells(_avatar, _avatar.Spells[_selected]))
                _prim.DiamondAt(_sb, Center(c), Mono.Cast * 0.32f);
        if (CellAt(MP) is { } hc && _engine.Field.InBounds(hc)
            && _engine.Field.TileAt(hc) != TileKind.Void)
        {
            DiamondOutline(Center(hc), 2f, Mono.Ink * 0.7f);
            // The promise (Mewgenics reads its numbers out loud): an armed attack over a
            // target shows what it would do — more when you've found their back.
            if (myTurn && _selected >= 0 && _selected < _avatar.Spells.Count
                && _engine.FighterAt(hc) is { Team: Team.Enemy } prey)
            {
                var arm = _avatar.Spells[_selected];
                bool back = CombatEngine.IsBackstab(_avatar.Pos, prey);
                // An illegal shot's promise is drawn faint — numbers you can't have yet.
                bool legal = _engine.CanCast(_avatar, arm, hc, out _);
                if (_engine.EstimateDamage(_avatar, arm, hc) is { } est)
                {
                    int lo = back ? est.min + est.min / 4 : est.min;
                    int hi = back ? est.max + est.max / 4 : est.max;
                    _font.DrawCentered(_sb, back ? $"{lo}-{hi} FROM BEHIND" : $"{lo}-{hi}",
                        (int)Center(hc).X, (int)Center(hc).Y - 42, 1,
                        !legal ? Mono.Faint : back ? Gold : Mono.Ink);
                }
                else if (back)
                    _font.DrawCentered(_sb, "FROM BEHIND +25%", (int)Center(hc).X, (int)Center(hc).Y - 42, 1,
                        legal ? Gold : Mono.Faint);
            }
        }

        // Entities: stones, corpses and fighters share ONE depth-sorted pass, so a
        // sprite behind a rock cluster is properly buried by it.
        var pass = new List<(int depth, Action draw)>();
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
            {
                var cell = new CellCoord(x, y);
                var kind = _engine.Field.TileAt(cell);
                var cc = Center(cell);
                if (kind == TileKind.Rock)
                    pass.Add(((x + y) * 4 + 1, () =>
                    {
                        _prim.BlockAt(_sb, cc, new Color(30, 30, 29), new Color(17, 17, 16), new Color(23, 23, 22));
                        var rock = _sprites.GetSheet("onebit_rock", "idle", "se");
                        if (rock != null) SpriteDraw.Feet(_sb, rock, cc + new Vector2(0, -4), Mono.Ink, 26, 0);
                    }));
                else if (kind == TileKind.Spikes)
                    pass.Add(((x + y) * 4 + 1, () =>
                    {
                        // an open grave's worth of teeth: three crooked bone spurs
                        for (int s = 0; s < 3; s++)
                        {
                            var b = cc + new Vector2(-13 + s * 13, 7 - s % 2 * 8);
                            _prim.Line(_sb, b, b + new Vector2(s % 2 == 0 ? 2 : -2, -10), 2f, Mono.Ink * 0.85f);
                            _prim.FillRect(_sb, new Rectangle((int)b.X + (s % 2 == 0 ? 2 : -4), (int)b.Y - 12, 2, 2), Mono.Ink);
                        }
                    }));
            }
        foreach (var (f, at) in _corpses)
        {
            var cc = Center(at);
            pass.Add(((at.X + at.Y) * 4, () =>
            {
                _prim.HaloAt(_sb, cc + new Vector2(0, 6), Mono.Danger * 0.45f);
                DrawSprite(f.Archetype, cc + new Vector2(0, 4), Mono.Faint, 32);
            }));
        }
        foreach (var f in _engine.Fighters.Where(f => f.IsAlive))
        {
            var ff = f; var cc = Center(f.Pos);
            pass.Add(((f.Pos.X + f.Pos.Y) * 4 + 2, () =>
            {
                bool current = _engine.Outcome == FightOutcome.Ongoing && ff == _engine.Current;
                _prim.HaloAt(_sb, cc + new Vector2(0, 5),
                    (ff.Team == Team.Player ? Mono.Ally : Mono.Danger) * (current ? 1f : 0.6f));
                // Transformations are WORN (Isaac's law: every pickup shows): extra halo rings.
                if (ff == _avatar)
                {
                    int ring = 0;
                    foreach (var theme in new[] { "MARROW", "BONE", "BELL" })
                        if (Transformed(theme))
                            _prim.HaloAt(_sb, cc + new Vector2(0, 5 - ++ring * 3),
                                ThemeInk(theme) * (0.4f + 0.15f * MathF.Sin(_time * 3f + ring)));
                }
                // The facing tick: which way they look is which way you flank.
                var fo = new Vector2((ff.Facing.X - ff.Facing.Y) * (TW / 4f), (ff.Facing.X + ff.Facing.Y) * (TH / 4f));
                _prim.Line(_sb, cc + new Vector2(0, 5) + fo * 0.55f, cc + new Vector2(0, 5) + fo * 0.95f,
                    2f, Mono.Ink * 0.55f);
                // Champions (grade 3) wear the danger color and stand a head taller.
                bool champ = ff.Team == Team.Enemy && ff.Level >= 3 && ff.Archetype != "sexton";
                DrawSprite(ff.Archetype, cc,
                    ff.Archetype == "sexton" || champ ? Mono.Danger : Mono.Ink,
                    ff.Archetype == "sexton" ? 62 : champ ? 56 : 46);
                _font.DrawCentered(_sb, ff.Hp.ToString(), (int)cc.X, (int)cc.Y + TH / 2 - 4, 1,
                    ff.Hp * 4 <= ff.MaxHp ? Mono.Danger : Mono.Ink);
                // A sliver of life under the number — the state of the fight at a squint.
                float hf = ff.Hp / (float)ff.MaxHp;
                _prim.FillRect(_sb, new Rectangle((int)cc.X - 11, (int)cc.Y + TH / 2 + 6, 22, 2), Mono.Faint);
                _prim.FillRect(_sb, new Rectangle((int)cc.X - 11, (int)cc.Y + TH / 2 + 6, (int)(22 * hf), 2),
                    ff.Hp * 4 <= ff.MaxHp ? Mono.Danger : ff.Team == Team.Player ? Mono.Ally : Mono.Ink);
            }));
        }
        foreach (var (_, draw) in pass.OrderBy(p => p.depth)) draw();

        foreach (var (from, to, _) in _smears)
            _prim.Line(_sb, from, to, 5f, Mono.Ink * 0.5f);

        const float FloatLife = 1.1f;
        for (int i = _floats.Count - 1; i >= 0; i--)
        {
            var (t, c, p, born) = _floats[i];
            float age = _time - born;
            if (age > FloatLife) { _floats.RemoveAt(i); continue; }
            _font.DrawCentered(_sb, t, (int)p.X, (int)(p.Y - 24 - age * 30), 2, c * (1f - age / FloatLife));
        }

        // Crawl's law: the dark leans in from the rim, and the candle is never quite steady.
        _sb.Draw(_vignette, Vector2.Zero, Color.White * (0.9f + 0.1f * MathF.Sin(_time * 5.3f)));
        // The ritual's stage: HIS fight is lit by half the candles.
        if (_bossFight) _sb.Draw(_vignette, Vector2.Zero, Color.White * 0.55f);

        // Turn banner — one loud breath, then gone.
        if (_time < _bannerUntil)
        {
            float a = Math.Min(1f, (_bannerUntil - _time) / 0.35f);
            _font.DrawCentered(_sb, _banner, W / 2, 148, 3, _bannerInk * a);
        }

        DrawFightHud(myTurn);
    }

    private void DrawFightHud(bool myTurn)
    {
        // The bell, top center — the only clock that matters. Under a minute it flickers.
        float frac = Math.Clamp(_tolls / (float)TollStart, 0f, 1f);
        bool urgent = !_sextonNow && _tolls <= 5;
        Mono.Bar(_sb, _prim, new Rectangle(W / 2 - 150, 8, 300, 10), frac,
            frac > 0.25f ? Mono.Ink : Mono.Danger);
        _font.DrawCentered(_sb, (_sextonNow ? "THE BELL HAS RUNG — " : $"{_tolls} TOLLS — ") + FightLabel(), W / 2, 24, 1,
            _sextonNow ? Mono.Danger
            : urgent ? Color.Lerp(Mono.Dim, Mono.Danger, 0.5f + 0.5f * MathF.Sin(_time * 6f))
            : Mono.Dim);

        // The road (Loop Hero keeps the whole loop in view): three graves, then HIM.
        for (int i = 0; i < 4; i++)
        {
            var p = new Vector2(W / 2 - 54 + i * 36, 44);
            if (i > 0) _prim.Line(_sb, p - new Vector2(26, 0), p - new Vector2(10, 0), 1f, Mono.Faint);
            bool here = i == Math.Min(_fightIndex, 3);
            var ink = i == 3 ? Mono.Danger : Mono.Ink;
            if (here) _prim.DiscAt(_sb, p, 5f + MathF.Sin(_time * 4f), ink);
            else _prim.DiscAt(_sb, p, i < _fightIndex ? 3 : 2, i < _fightIndex ? Mono.Dim : i == 3 ? Mono.Danger * 0.5f : Mono.Faint);
        }

        _font.Draw(_sb, $"{_runStones} st", 16, 12, 2, Mono.Ink);

        // Whoever the cursor rests on gets a name — know your dead before you make them.
        if (CellAt(MP) is { } tc && _engine.FighterAt(tc) is { } tf)
            _font.Draw(_sb,
                $"{tf.Name.ToUpperInvariant()}{(tf.Team == Team.Enemy && tf.Level >= 3 ? " · CHAMPION" : tf.Team == Team.Enemy && tf.Level == 2 ? " · GRADE 2" : "")}  {tf.Hp}/{tf.MaxHp}",
                16, 40, 1, tf.Team == Team.Enemy ? Mono.Danger : Mono.Ally);

        // Turn order, top right.
        int ty = 10;
        foreach (var f in _engine.Fighters.Where(f => f.IsAlive))
        {
            bool cur = _engine.Outcome == FightOutcome.Ongoing && f == _engine.Current;
            _font.Draw(_sb, (cur ? "> " : "  ") + f.Name.ToUpperInvariant(), W - 200, ty, 1,
                cur ? Mono.Ink : f.Team == Team.Player ? Mono.Ally : Mono.Danger);
            ty += 14;
        }

        // The band: mana pips (blue), move pips (green), the wells, END TURN.
        _prim.FillRect(_sb, new Rectangle(0, H - 110, W, 110), Mono.Panel);
        _prim.FillRect(_sb, new Rectangle(0, H - 110, W, 1), Mono.Dim);
        _font.Draw(_sb, _avatar.Name.ToUpperInvariant() + $"  ·  {_avatar.Hp}/{_avatar.MaxHp} HP", 20, H - 100, 1,
            _avatar.Hp * 4 <= _avatar.MaxHp ? Mono.Danger : Mono.Ink);
        _font.Draw(_sb, "MANA", 20, H - 78, 1, Mono.Ap);
        for (int i = 0; i < _avatar.BaseAp; i++)
            _prim.DiscAt(_sb, new Vector2(70 + i * 16, H - 73), 6,
                i < _avatar.CurrentAp ? Mono.Ap : Mono.Faint);
        _font.Draw(_sb, "MOVE", 20, H - 52, 1, Mono.Mp);
        for (int i = 0; i < _avatar.BaseMp; i++)
            _prim.DiscAt(_sb, new Vector2(70 + i * 16, H - 47), 6,
                i < _avatar.CurrentMp ? Mono.Mp : Mono.Faint);
        // Isaac wears its essences on its sleeve: one row, colored by theme; transformations
        // announce themselves first. A crowded late-run row compacts into theme counts.
        int ex = 20;
        foreach (var theme in new[] { "MARROW", "BONE", "BELL" })
            if (Transformed(theme))
            {
                string form = theme switch { "MARROW" => "THE BLIGHTED", "BONE" => "THE REVENANT", _ => "THE TITHED" };
                _font.Draw(_sb, form, ex, H - 26, 1, ThemeInk(theme));
                ex += _font.Measure(form, 1) + 18;
            }
        if (_essences.Count <= 6)
            foreach (var e in _essences)
            {
                string t = "* " + e;
                _font.Draw(_sb, t, ex, H - 26, 1, ThemeInk(ThemeOf(e)) * 0.8f);
                ex += _font.Measure(t, 1) + 14;
            }
        else
            foreach (var theme in new[] { "MARROW", "BONE", "BELL" })
            {
                int n = ThemeCount(theme);
                if (n == 0) continue;
                string t = $"{theme} {n}";
                _font.Draw(_sb, t, ex, H - 26, 1, ThemeInk(theme) * 0.8f);
                ex += _font.Measure(t, 1) + 16;
            }

        // The wardrobe, Loop Hero style: four slots, one number each, families at a glance.
        string[] slots = { "BLADE", "PLATE", "BOOTS", "CHARM" };
        _font.Draw(_sb, "GEAR", 810, H - 104, 1, Mono.Faint);
        for (int i = 0; i < slots.Length; i++)
        {
            var r = new Rectangle(810 + i * 44, H - 92, 40, 40);
            var worn = _gear.GetValueOrDefault(slots[i]);
            Mono.Slot(_sb, _prim, r, hover: r.Contains(MP));
            _font.DrawCentered(_sb, slots[i] == "BOOTS" ? "S" : slots[i][..1], r.Center.X, r.Y + 8, 2,
                worn != null ? Mono.Ink : Mono.Faint);
            _font.DrawCentered(_sb, worn != null ? $"+{worn.Power}" : "-", r.Center.X, r.Bottom + 4, 1,
                worn != null ? Mono.Dim : Mono.Faint);
            if (r.Contains(MP) && worn != null)
            {
                int wdt = Math.Max(120, _font.Measure(worn.Name, 1) + 20);
                var tip = new Rectangle(Math.Min(r.X, W - wdt - 8), r.Y - 40, wdt, 30);
                Mono.Frame(_sb, _prim, tip, emphasis: true);
                _font.Draw(_sb, worn.Name, tip.X + 10, tip.Y + 6, 1, Mono.Ink);
                _font.Draw(_sb, worn.Family + " SET", tip.X + 10, tip.Y + 18, 1, Mono.Dim);
            }
        }
        foreach (var fam in new[] { "GRAVE", "EMBER", "BONE" })
            if (FamilyCount(fam) >= 3)
            { _font.Draw(_sb, fam + " SET WHOLE", 810, H - 38, 1, Mono.Cast); break; }

        var spells = _avatar.Spells;
        int hoveredWell = -1;
        for (int i = 0; i < spells.Count && i < 7; i++)
        {
            var r = WellRect(i);
            var sp = spells[i];
            bool used = !_avatar.HasCastsLeft(sp) || _avatar.IsOnCooldown(sp, _engine.Round);
            bool canPay = !used && sp.ApCost <= _avatar.CurrentAp;
            if (r.Contains(MP)) hoveredWell = i;
            Mono.Slot(_sb, _prim, r, hover: r.Contains(MP), selected: _selected == i);
            string? key = TitheContent.SkillKeyById(sp.Id);
            var tint = canPay ? SpellInk(sp) : Mono.Faint;
            if (key == null || !DrawIcon("icon_spell_" + key, r, tint))
            {
                if (sp.Id >= 950) { if (!DrawIcon("icon_slot_weapon", r, tint)) _font.DrawCentered(_sb, "S", r.Center.X, r.Y + 16, 2, tint); }
                else _font.DrawCentered(_sb, sp.Name[..1], r.Center.X, r.Y + 16, 2, tint);
            }
            // Spent reads as spent, not as unaffordable: USED is quiet, a short purse is loud.
            _font.DrawCentered(_sb, used ? "USED" : sp.ApCost == 0 ? "FREE" : sp.ApCost.ToString(),
                r.Center.X, r.Bottom + 4, 1, used ? Mono.Faint : canPay ? Mono.Dim : Mono.Danger);
            _font.DrawCentered(_sb, (i + 1).ToString(), r.X + 6, r.Y + 2, 1, Mono.Faint);
        }
        if (hoveredWell >= 0) DrawSpellTooltip(spells[hoveredWell], WellRect(hoveredWell));

        if (myTurn)
        {
            bool hov = EndTurnRect.Contains(MP);
            Mono.Button(_sb, _prim, EndTurnRect, hover: hov);
            _font.DrawCentered(_sb, "END TURN", EndTurnRect.Center.X, EndTurnRect.Y + 11, 1, Mono.ButtonInk(hov));
            _font.Draw(_sb, "1-6 ARM · CLICK MOVE/CAST · ENTER ENDS", W - 330, H - 40, 1, Mono.Faint);
            // A drawn weapon stays drawn (a failed cast keeps its arm) — say so out loud.
            if (_selected >= 0 && _selected < spells.Count)
                _font.Draw(_sb, $"ARMED: {spells[_selected].Name.ToUpperInvariant()} · ESC CLEARS",
                    400, H - 124, 1, Mono.Cast);
        }
        else if (_engine.Outcome == FightOutcome.Ongoing)
            _font.Draw(_sb, "THE DEAD MOVE...", W - 190, H - 40, 1, Mono.Faint);
    }

    private void DrawPick()
    {
        _font.DrawCentered(_sb, _pickTitle, W / 2, 120, 4, _pickInk);
        _font.DrawCentered(_sb, _pickSub, W / 2, 168, 1, Mono.Dim);
        _font.DrawCentered(_sb, $"{_runStones} st carried  ·  {_tolls} tolls on the bell  ·  next: {FightLabel()}",
            W / 2, 190, 1, Mono.Faint);
        for (int i = 0; i < _cards.Count; i++)
        {
            var r0 = CardRect(i);
            bool hov = r0.Contains(MP);
            // Hovered cards lift out of the dirt (Loop Hero's hand feel).
            var r = hov ? new Rectangle(r0.X, r0.Y - 6, r0.Width, r0.Height) : r0;
            Mono.Frame(_sb, _prim, r, emphasis: hov);
            var (title, body, kind, _) = _cards[i];
            var kindInk = kind switch
            { "RUIN" => Gold, "ESSENCE" => Mono.Cast, "GEAR" => Mono.Ink, _ => Mono.Dim };
            _prim.FillRect(_sb, new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, 3), kindInk);
            _font.DrawCentered(_sb, kind == "RUIN" ? "WORD OF RUIN" : kind,
                r.Center.X, r.Y + 14, 1, kindInk);
            _font.DrawCentered(_sb, title, r.Center.X, r.Y + 36, 1, kind == "ESSENCE" ? Mono.Cast : Mono.Ink);
            int ly = r.Y + 78;
            foreach (var line in body.Split('\n'))
            {
                var ink = line.Contains(" OF 3") || line == "forever" ? Mono.Cast
                    : line.Contains(" SET") ? Mono.Dim : Mono.Dim;
                _font.DrawCentered(_sb, line, r.Center.X, ly, 1, ink); ly += 16;
            }
            var take = new Rectangle(r.Center.X - 46, r.Bottom - 38, 92, 24);
            Mono.Button(_sb, _prim, take, hover: hov);
            _font.DrawCentered(_sb, $"TAKE ({i + 1})", take.Center.X, take.Y + 8, 1, Mono.ButtonInk(hov));
        }
    }

    private void DrawEnd()
    {
        _font.DrawCentered(_sb, _runWon ? "THE SEXTON FALLS" : "THE GROUND TAKES YOU", W / 2, 240, 5,
            _runWon ? Mono.Ink : Mono.Danger);
        _font.DrawCentered(_sb, _runWon
                ? $"the bell is yours. +{_runStones} stones banked."
                : $"half your stones sink with you. +{_runStones / 2} banked.",
            W / 2, 320, 2, Mono.Dim);
        if (!_runWon) _font.DrawCentered(_sb, "a new leader answers the bell.", W / 2, 352, 1, Mono.Faint);
        // The ledger, read aloud over the grave.
        _font.DrawCentered(_sb,
            $"the earth fed: {_kills}   ·   taken by the void: {_falls}   ·   level {_you.Level}",
            W / 2, 384, 1, Mono.Dim);
        _font.DrawCentered(_sb, "PRESS SPACE", W / 2, 430, 1, Mono.Ink);
    }

    // ----- sprite/icon helpers (SpriteBank chain, fallback letters) --------------------

    private static readonly Dictionary<string, string> SpriteOf = new()
    {
        ["archer"] = "archer", ["bulwark"] = "hero", ["cannon"] = "cannon",
        ["barrow_husk"] = "husk", ["gravehound"] = "hound", ["marrow_spitter"] = "spitter",
        ["grave_mite"] = "mite", ["bone_piper"] = "piper", ["tomb_wraith"] = "wraith",
        ["grave_ghoul"] = "ghoul", ["crypt_warden"] = "warden", ["sexton"] = "sexton",
        ["cairn_brute"] = "warden",
    };

    private void DrawSprite(string archetype, Vector2 cellCenter, Color tint, float h)
    {
        var name = SpriteOf.GetValueOrDefault(archetype, "husk");
        var sheet = _sprites.GetSheet(name, "idle", "se");
        var feet = cellCenter + new Vector2(0, TH / 4f + 2);
        if (sheet != null)
            SpriteDraw.Feet(_sb, sheet, feet, tint, h, 0);
        else
        {
            _prim.DiscAt(_sb, cellCenter, 14, tint * 0.85f);
            _font.DrawCentered(_sb, archetype[..1].ToUpperInvariant(), (int)cellCenter.X, (int)cellCenter.Y - 4, 1, Mono.Bg);
        }
    }

    private bool DrawIcon(string name, Rectangle r, Color tint)
    {
        var sheet = _sprites.GetSheet(name, "idle", "se");
        if (sheet == null) return false;
        int k = Math.Max(1, (Math.Min(r.Width, r.Height) - 8) / 16);
        int s = 16 * k;
        _sb.Draw(sheet.Texture, new Rectangle(r.Center.X - s / 2, r.Center.Y - s / 2, s, s), sheet.Frame(0), tint);
        return true;
    }

    private static Color SpellInk(SpellDef s)
    {
        var dmg = s.Effects.FirstOrDefault(e => e.Kind is EffectKind.Damage or EffectKind.Lifesteal);
        if (dmg != null) return Mono.Element(dmg.Element);
        return s.Effects.Any(e => e.Kind == EffectKind.Heal) ? Mono.Heal : Mono.Ink;
    }

    // ----- the tooltip: everything a well knows, told over it on hover ----------------

    private void DrawSpellTooltip(SpellDef sp, Rectangle well)
    {
        var lines = new List<(string t, Color c)>
        {
            (sp.Name.ToUpperInvariant(), SpellInk(sp)),
            ((sp.ApCost == 0 ? "FREE" : $"{sp.ApCost} MANA")
             + (sp.MaxCastsPerTurn == 1 ? " · ONCE A TURN" : "")
             + (sp.Cooldown > 0 ? $" · REST {sp.Cooldown}" : ""), Mono.Ap),
            ($"RANGE {sp.MinRange}-{sp.MaxRange}"
             + (sp.LineOnly ? " · IN A LINE" : "")
             + (sp.RequiresLineOfSight ? "" : " · NEEDS NO SIGHT"), Mono.Dim),
        };
        string fx = EffectsLine(sp);
        if (fx.Length > 0) lines.Add((fx, Mono.Ink));
        foreach (var w in Wrap(sp.Description, 36)) lines.Add((w.ToLowerInvariant(), Mono.Dim));

        int wdt = Math.Max(150, lines.Max(l => _font.Measure(l.t, 1)) + 20);
        int hgt = lines.Count * 14 + 14;
        var r = new Rectangle(Math.Min(well.X, W - wdt - 8), well.Y - hgt - 8, wdt, hgt);
        Mono.Frame(_sb, _prim, r, emphasis: true);
        int y = r.Y + 8;
        foreach (var (t, c) in lines) { _font.Draw(_sb, t, r.X + 10, y, 1, c); y += 14; }
    }

    /// <summary>One readable clause per effect — the Loop Hero promise: numbers, no fine print.</summary>
    private static string EffectsLine(SpellDef s) => string.Join(" · ", s.Effects.Select(e => e.Kind switch
    {
        EffectKind.Damage => $"{e.Min}-{e.Max} {e.Element}".ToUpperInvariant(),
        EffectKind.Lifesteal => $"LEECH {e.Min}-{e.Max} {e.Element}".ToUpperInvariant(),
        EffectKind.Heal => $"HEAL {e.Min}-{e.Max}",
        EffectKind.Push => $"PUSH {e.Min}",
        EffectKind.Pull => $"PULL {e.Min}",
        EffectKind.StealAp => $"STEAL {e.Min} MANA",
        EffectKind.StealMp => $"STEAL {e.Min} MOVE",
        EffectKind.GrantAp => $"GRANT {e.Min} MANA",
        // Binary states (Rooted, Stabilized) have no magnitude worth printing.
        EffectKind.ApplyStatus => (e.Min > 0 ? $"{e.Status} {e.Min}, {e.Max} TURNS" : $"{e.Status}, {e.Max} TURNS").ToUpperInvariant(),
        EffectKind.Teleport => "LEAP THERE",
        EffectKind.Swap => "TRADE PLACES",
        EffectKind.SelfHpCost => $"BLOOD PRICE {e.Min}",
        EffectKind.Summon => "CALL A SERVANT",
        _ => "",
    }).Where(t => t.Length > 0));

    private static IEnumerable<string> Wrap(string text, int width)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width) { yield return line; line = word; }
            else line = line.Length == 0 ? word : line + " " + word;
        }
        if (line.Length > 0) yield return line;
    }
}

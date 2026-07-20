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
    private float _bell;                 // seconds until the Sexton comes for you
    private int _fightIndex;             // 0..2 packs, 3 = THE SEXTON
    private bool _runWon, _sextonNow;
    private CampaignUnit _you = NewYou();
    private readonly List<string> _essences = new();
    private int _bonusHp, _bonusDmg, _bonusMove, _bonusRegen;
    private const float BellStart = 300f;

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
    private string _narration = ""; private float _narrationUntil;
    private bool _firstBlood;
    private readonly Random _rng = new();

    // ----- the pick ------------------------------------------------------------------
    private List<(string title, string body, Action apply)> _cards = new();

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
    }

    // ================= RUN FLOW ========================================================

    private void StartRun()
    {
        _runStones = 0; _bell = BellStart; _fightIndex = 0; _sextonNow = false; _runWon = false;
        _essences.Clear(); _bonusHp = _bonusDmg = _bonusMove = _bonusRegen = 0;
        _you.CurrentHp = null;
        StartFight();
    }

    private void StartFight()
    {
        _blood.Clear(); _smears.Clear(); _floats.Clear(); _corpses.Clear();
        _manaCarry.Clear(); _selected = -1; _turnOwner = ""; _resolved = false; _firstBlood = false;

        string[][] waves =
        {
            new[] { "barrow_husk" },
            new[] { "barrow_husk", "gravehound" },
            new[] { "marrow_spitter", "barrow_husk", "barrow_husk" },
            new[] { "sexton", "barrow_husk" },
        };
        bool boss = _sextonNow || _fightIndex >= 3;
        var comp = waves[boss ? 3 : _fightIndex];

        // A ragged island with clustered stones — regenerated until every grave can
        // be reached from the crew's ground (no sealed pockets, ever).
        var rnd = new Random(++_seed);
        Battlefield field;
        CellCoord aSpawn; var mobSpawns = new List<CellCoord>();
        for (int attempt = 0; ; attempt++)
        {
            field = BuildBoard(rnd);
            var taken = new HashSet<CellCoord>();
            aSpawn = Spawn(field, left: true, 0, taken);
            mobSpawns.Clear();
            for (int i = 0; i < comp.Length; i++) mobSpawns.Add(Spawn(field, left: false, i, taken));
            bool ok = mobSpawns.All(mc => Pathfinding.FindPath(field, aSpawn, mc,
                _ => false, allowOccupiedGoal: true) != null);
            if (ok || attempt > 24) break;
        }

        _embers.Clear();
        for (int i = 0; i < 24 && _embers.Count < 5; i++)
        {
            var c = new CellCoord(2 + rnd.Next(Cols - 4), rnd.Next(Rows));
            if (field.IsWalkable(c) && c != aSpawn && !mobSpawns.Contains(c)) _embers.Add(c);
        }

        _avatar = Bless(TitheContent.MakeCrewMember(_you, aSpawn));
        var fighters = new List<Fighter> { _avatar };
        for (int i = 0; i < comp.Length; i++)
            fighters.Add(TitheContent.MakeMob(comp[i], $"mob_{_fightIndex}_{i}", mobSpawns[i]));

        _engine = new CombatEngine(field, fighters, new SystemRng(_seed));
        _engine.Emitted += OnCombatEvent;
        _engine.Start();
        _scene = Scene.Fight;
        _sfx.SetAmbient("wind", 0.12f);
        Narrate(boss ? "the bell falls silent. HE is here." : $"the dead notice you. ({FightLabel()})");
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

    /// <summary>The avatar carries the run's picks: gear numbers, essences, scars.</summary>
    private Fighter Bless(Fighter f)
    {
        var g = new Fighter
        {
            Id = f.Id, Name = f.Name, Team = f.Team, PlayerControlled = true,
            Archetype = f.Archetype, Policy = f.Policy, Passive = f.Passive,
            PreferredRangeMin = f.PreferredRangeMin, PreferredRangeMax = f.PreferredRangeMax,
            Level = f.Level, MaxHp = f.MaxHp + _bonusHp,
            Hp = Math.Min(f.Hp + _bonusHp, f.MaxHp + _bonusHp),
            BaseAp = f.BaseAp, BaseMp = f.BaseMp + _bonusMove,
            Strength = f.Strength, Intelligence = f.Intelligence + _bonusDmg,
            Chance = f.Chance, Agility = f.Agility,
            Power = f.Power, Wisdom = f.Wisdom, Initiative = f.Initiative,
            Pos = f.Pos,
            Spells = f.Spells.Concat(new[] { Strike() }).ToArray(),
        };
        foreach (var (k, v) in f.Resistances) g.Resistances[k] = v;
        if (_essences.Contains("WARDEN'S HIDE"))
            g.Statuses.Add(new StatusEffect(StatusKind.Shield, 3, 99));
        return g;
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
                // The mana trickle replaces the AP purse: carry what you saved, +2 a turn.
                ts.Fighter.CurrentAp = Math.Min(ts.Fighter.BaseAp,
                    _manaCarry.GetValueOrDefault(ts.Fighter.Id, 3)
                    + 2 + (ts.Fighter == _avatar ? _bonusRegen : 0));
                if (_embers.Contains(ts.Fighter.Pos) && ts.Fighter.IsAlive && ts.Fighter.Hp > 2)
                {
                    ts.Fighter.Hp = Math.Max(1, ts.Fighter.Hp - 2);
                    Splatter(Center(ts.Fighter.Pos), 3);
                    Float("-2", Mono.Element(Element.Fire), Center(ts.Fighter.Pos));
                    _sfx.Play("hit_fire", 0.5f);
                }
                if (ts.Fighter == _avatar) _sfx.Play("yourturn", 0.55f, jitter: false);
                break;

            case DamageDealt d:
                _freeze = Math.Max(_freeze, d.RemainingHp <= 0 ? 0.16f : 0.07f);
                _shake = Math.Max(_shake, Math.Min(10f, 2f + d.Amount * 0.25f));
                Splatter(Center(d.At), 4 + Math.Min(8, d.Amount / 3));
                Float($"-{d.Amount}", Mono.Danger, Center(d.At));
                if (_engine.Current != null && _engine.Current.Pos != d.At)
                    _smears.Add((Center(_engine.Current.Pos), Center(d.At), 0.11f));
                _sfx.Play("hit_" + d.Element.ToString().ToLowerInvariant(), 0.8f);
                if (!_firstBlood) { _firstBlood = true; Narrate("first blood feeds the ground."); }
                break;

            case HealApplied h:
                Float($"+{h.Amount}", Mono.Heal, Center(h.At));
                _sfx.Play("heal", 0.6f);
                break;

            case FighterDied fd:
                _freeze = Math.Max(_freeze, 0.2f); _shake = Math.Max(_shake, 12f);
                Splatter(Center(fd.At), 16);
                _corpses.Add((fd.Fighter, fd.At));
                _sfx.Play("death", 0.85f);
                if (fd.Fighter.Team == Team.Enemy)
                {
                    int pay = TitheContent.MobStonesOf(fd.Fighter)
                              + (_essences.Contains("GRASP'S COIN") ? 1 : 0);
                    _runStones += pay;
                    Float($"+{pay} st", Mono.Ink, Center(fd.At) + new Vector2(0, -20));
                    if (_essences.Contains("TOLL KEEPER")) _bell += 8f;
                    Narrate(fd.Fighter.Archetype == "sexton"
                        ? "the gravedigger digs his own."
                        : "another mouth for the earth.");
                }
                else Narrate("the ground remembers your name.");
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
        _prevKeys = _keys; _keys = Keyboard.GetState();
        _prevMouse = _mouse; _mouse = Mouse.GetState();
        if (Pressed(Keys.M)) _sfx.Muted = !_sfx.Muted;

        if (_freeze > 0) { _freeze -= dt; base.Update(gt); return; }   // hit-stop: the world holds its breath
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

    private void UpdateCity()
    {
        _sfx.SetAmbient("dirge", 0.09f);
        if (Clicked())
            for (int i = 0; i < ClassIds.Length; i++)
                if (ClassRect(i).Contains(MP) && _you.ClassId != ClassIds[i])
                { _you = NewYou(ClassIds[i]); _sfx.Play("click"); return; }
        if (Pressed(Keys.Space) || Pressed(Keys.Enter) || (Clicked() && DepartRect.Contains(MP)))
        { _sfx.Play("bell", 0.7f, jitter: false); StartRun(); }
    }

    private void UpdateFight(float dt)
    {
        _bell -= dt;
        if (_bell <= 0 && !_sextonNow && _fightIndex < 3)
        { _sextonNow = true; _bell = 0; }   // he arrives after this fight, wherever you are

        if (_engine.Outcome != FightOutcome.Ongoing)
        {
            if (!_resolved)
            {
                _resolved = true; _endPause = 1.1f;
                _sfx.Play(_engine.Outcome == FightOutcome.Victory ? "victory" : "defeat", 0.8f, jitter: false);
                if (_engine.Outcome == FightOutcome.Victory)
                {
                    _you.CurrentHp = Math.Max(1, _avatar.Hp);
                    int before = _you.Level;
                    _you.GainXp(45 + 30 * Math.Min(_fightIndex, 3));
                    if (_you.Level > before)
                    { Narrate($"you grow harder. LEVEL {_you.Level} — a new word of ruin."); _sfx.Play("levelup", 0.8f, jitter: false); }
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
                else { _fightIndex++; RollCards(); _scene = Scene.Pick; }
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
                StartFight();
                return;
            }
    }

    private void UpdateEnd()
    {
        if (Pressed(Keys.Space) || Pressed(Keys.Enter) || Clicked())
        {
            if (!_runWon) _you = NewYou(_you.ClassId); // the fallen leader is buried; kin answer
            _scene = Scene.City;
        }
    }

    // ================= THE PICK ========================================================

    private void RollCards()
    {
        var pool = new List<(string, string, Action)>
        {
            ("BLADE OATH", "+4 DAMAGE\n\none number,\nno fine print", () => _bonusDmg += 4),
            ("GRAVE PLATE", "+8 MAX HP\n\nthe dirt holds\nyou tighter", () => _bonusHp += 8),
            ("QUIET STEP", "+1 MOVE\n\nthe dead hear\nnothing", () => _bonusMove += 1),
            ("HOUR THIEF", "+1 MANA A TURN\n\nstolen seconds,\nspent as fire", () => _bonusRegen += 1),
            ("MEND", "FULL HEAL NOW\n\nthe bread is\nstale. eat.", () => _you.CurrentHp = null),
            ("COIN OF THE GRASP", "+25 STONES NOW\n\nit hums when\nyou hold it", () => _runStones += 25),
            ("OIL FOR THE BELL", "+45 BELL SECONDS\n\nhe waits.\nbarely.", () => _bell += 45f),
        };
        var essencePool = new List<(string, string, Action)>();
        void Ess(string name, string body)
        { if (!_essences.Contains(name)) essencePool.Add((name, body + "\n\nESSENCE — forever", () => { _essences.Add(name); _sfx.Play("chime", 0.7f, jitter: false); })); }
        Ess("WARDEN'S HIDE", "SHIELD 3 AT\nEVERY FIGHT'S START");
        Ess("GRASP'S COIN", "+1 STONE ON\nEVERY KILL");
        Ess("TOLL KEEPER", "+8 BELL SECONDS\nON EVERY KILL");

        var rnd = new Random(++_seed);
        _cards = new();
        if (essencePool.Count > 0) _cards.Add(essencePool[rnd.Next(essencePool.Count)]);
        while (_cards.Count < 3)
        {
            var c = pool[rnd.Next(pool.Count)];
            if (!_cards.Contains(c)) _cards.Add(c);
        }
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
    private static Rectangle CardRect(int i) => new(W / 2 - 340 + i * 240, 240, 200, 260);

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
            _font.DrawCentered(_sb, _narration.ToUpperInvariant(), W / 2, 44, 2,
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
        _font.DrawCentered(_sb, "three packs stand between you and the sexton.", W / 2, 388, 1, Mono.Dim);
        _font.DrawCentered(_sb, "the bell gives you five minutes. he keeps the change.", W / 2, 404, 1, Mono.Faint);
        for (int i = 0; i < ClassIds.Length; i++)
        {
            var r = ClassRect(i);
            bool sel = _you.ClassId == ClassIds[i];
            bool ch = r.Contains(MP);
            Mono.Slot(_sb, _prim, r, hover: ch, selected: sel);
            _font.DrawCentered(_sb, ClassIds[i].ToUpperInvariant(), r.Center.X, r.Y + 9, 1,
                sel ? Mono.Ink : Mono.Dim);
        }
        bool hov = DepartRect.Contains(MP);
        Mono.Button(_sb, _prim, DepartRect, hover: hov);
        _font.DrawCentered(_sb, "DEPART", DepartRect.Center.X, DepartRect.Y + 15, 2, Mono.ButtonInk(hov));
        _font.DrawCentered(_sb, "(SPACE)", W / 2, 530, 1, Mono.Faint);
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
            DiamondOutline(Center(hc), 2f, Mono.Ink * 0.7f);

        // Entities: stones, corpses and fighters share ONE depth-sorted pass, so a
        // sprite behind a rock cluster is properly buried by it.
        var pass = new List<(int depth, Action draw)>();
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
            {
                var cell = new CellCoord(x, y);
                if (_engine.Field.TileAt(cell) != TileKind.Rock) continue;
                var cc = Center(cell);
                pass.Add(((x + y) * 4 + 1, () =>
                {
                    _prim.BlockAt(_sb, cc, new Color(30, 30, 29), new Color(17, 17, 16), new Color(23, 23, 22));
                    var rock = _sprites.GetSheet("onebit_rock", "idle", "se");
                    if (rock != null) SpriteDraw.Feet(_sb, rock, cc + new Vector2(0, -4), Mono.Ink, 26, 0);
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
                DrawSprite(ff.Archetype, cc,
                    ff.Archetype == "sexton" ? Mono.Danger : Mono.Ink, ff.Archetype == "sexton" ? 62 : 46);
                _font.DrawCentered(_sb, ff.Hp.ToString(), (int)cc.X, (int)cc.Y + TH / 2 - 4, 1,
                    ff.Hp * 4 <= ff.MaxHp ? Mono.Danger : Mono.Ink);
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

        DrawFightHud(myTurn);
    }

    private void DrawFightHud(bool myTurn)
    {
        // The bell, top center — the only clock that matters.
        float frac = Math.Clamp(_bell / BellStart, 0f, 1f);
        Mono.Bar(_sb, _prim, new Rectangle(W / 2 - 150, 8, 300, 10), frac,
            frac > 0.25f ? Mono.Ink : Mono.Danger);
        _font.DrawCentered(_sb, $"{(int)MathF.Ceiling(Math.Max(0, _bell))}S — {FightLabel()}", W / 2, 24, 1,
            _sextonNow ? Mono.Danger : Mono.Dim);
        _font.Draw(_sb, $"{_runStones} st", 16, 12, 2, Mono.Ink);

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
        foreach (var (e, i) in _essences.Select((e, i) => (e, i)))
            _font.Draw(_sb, "* " + e, 20, H - 28 + i * 0, 1, Mono.Cast);

        var spells = _avatar.Spells;
        for (int i = 0; i < spells.Count && i < 7; i++)
        {
            var r = WellRect(i);
            var sp = spells[i];
            bool canPay = sp.ApCost <= _avatar.CurrentAp && _avatar.HasCastsLeft(sp);
            Mono.Slot(_sb, _prim, r, hover: r.Contains(MP), selected: _selected == i);
            string? key = TitheContent.SkillKeyById(sp.Id);
            var tint = canPay ? SpellInk(sp) : Mono.Faint;
            if (key == null || !DrawIcon("icon_spell_" + key, r, tint))
            {
                if (sp.Id >= 950) { if (!DrawIcon("icon_slot_weapon", r, tint)) _font.DrawCentered(_sb, "S", r.Center.X, r.Y + 16, 2, tint); }
                else _font.DrawCentered(_sb, sp.Name[..1], r.Center.X, r.Y + 16, 2, tint);
            }
            _font.DrawCentered(_sb, sp.ApCost == 0 ? "FREE" : sp.ApCost.ToString(),
                r.Center.X, r.Bottom + 4, 1, canPay ? Mono.Dim : Mono.Danger);
            _font.DrawCentered(_sb, (i + 1).ToString(), r.X + 6, r.Y + 2, 1, Mono.Faint);
        }

        if (myTurn)
        {
            bool hov = EndTurnRect.Contains(MP);
            Mono.Button(_sb, _prim, EndTurnRect, hover: hov);
            _font.DrawCentered(_sb, "END TURN", EndTurnRect.Center.X, EndTurnRect.Y + 11, 1, Mono.ButtonInk(hov));
            _font.Draw(_sb, "1-6 ARM · CLICK MOVE/CAST · ENTER ENDS", W - 330, H - 40, 1, Mono.Faint);
        }
        else if (_engine.Outcome == FightOutcome.Ongoing)
            _font.Draw(_sb, "THE DEAD MOVE...", W - 190, H - 40, 1, Mono.Faint);
    }

    private void DrawPick()
    {
        _font.DrawCentered(_sb, "THE SPOILS", W / 2, 120, 4, Mono.Ink);
        _font.DrawCentered(_sb, "take ONE. the rest sink into the dirt.", W / 2, 168, 1, Mono.Dim);
        _font.DrawCentered(_sb, $"{_runStones} st carried  ·  {(int)_bell}s on the bell  ·  next: {FightLabel()}",
            W / 2, 190, 1, Mono.Faint);
        for (int i = 0; i < _cards.Count; i++)
        {
            var r = CardRect(i);
            bool hov = r.Contains(MP);
            Mono.Frame(_sb, _prim, r, emphasis: hov);
            var (title, body, _) = _cards[i];
            bool isEss = body.Contains("ESSENCE");
            _font.DrawCentered(_sb, title, r.Center.X, r.Y + 26, 1, isEss ? Mono.Cast : Mono.Ink);
            int ly = r.Y + 70;
            foreach (var line in body.Split('\n'))
            { _font.DrawCentered(_sb, line, r.Center.X, ly, 1, line.Contains("ESSENCE") ? Mono.Cast : Mono.Dim); ly += 16; }
            _font.DrawCentered(_sb, $"({i + 1})", r.Center.X, r.Bottom - 24, 1, Mono.Faint);
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
        _font.DrawCentered(_sb, "PRESS SPACE", W / 2, 430, 1, Mono.Ink);
    }

    // ----- sprite/icon helpers (SpriteBank chain, fallback letters) --------------------

    private static readonly Dictionary<string, string> SpriteOf = new()
    {
        ["archer"] = "archer", ["bulwark"] = "hero", ["cannon"] = "cannon",
        ["barrow_husk"] = "husk", ["gravehound"] = "hound", ["marrow_spitter"] = "spitter",
        ["grave_mite"] = "mite", ["bone_piper"] = "piper", ["tomb_wraith"] = "wraith",
        ["grave_ghoul"] = "ghoul", ["crypt_warden"] = "warden", ["sexton"] = "sexton",
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
}

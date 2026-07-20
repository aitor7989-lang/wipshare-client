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
/// DofusSlice.Core engine. The run's RULES live in RunCore.cs (shared with the
/// balance sim); this class is the hands and eyes: input, motion, paint, sound.
/// g10 THE MOTION: bodies walk their paths, weapons swing, arrows fly, ranges
/// paint on hover, enemies open for inspection, SPACE lets the bell play itself —
/// and the AP/MP purse is back (the owner's call: it was more fun).
/// </summary>
public sealed class GauntletGame : Game, IRunFx
{
    // ----- shell ---------------------------------------------------------------------
    private const int W = 1280, H = 760;
    private const int Cols = RunRules.Cols, Rows = RunRules.Rows;
    private const int TW = 80, TH = 40;   // the iso diamond footprint
    // The island sits a touch lower now that bodies stand ~1.5 tiles tall — heads and
    // overhead bars must clear the road strip, and the dead space above the band shrinks.
    private const int OX = W / 2 - (Cols - Rows) * TW / 4, OY = 118;
    private const int BandTop = H - 120;

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

    // ----- the meta (survives the night) -----------------------------------------------
    private int _banked;                 // stones safe at home, across runs
    private CampaignUnit _you = NewYou();
    private CampaignUnit? _mate;         // the hired Sellsword — rides every run until the day he dies
    private readonly List<string> _learned = new();   // words learned ahead of your years (die with you)

    private static CampaignUnit NewYou(string classId = "cannon") =>
        new() { Id = "avatar", ClassId = classId, Name = "You", IsAvatar = true };

    // ----- the run (all mechanics live in RunState — the sim reads the same book) -------
    private RunState _st = new();
    private bool _runWon;

    // ----- the fight -----------------------------------------------------------------
    private CombatEngine _engine = null!;
    private Fighter _avatar = null!;
    private int _seed = Environment.TickCount;
    private int _selected = -1;          // armed spell index into _avatar.Spells
    private string _turnOwner = "";
    private string _inspectId = "";      // clicked enemy under the glass
    private float _aiTimer, _endPause;
    private bool _aiActed, _resolved, _autoPlay;
    private Dictionary<CellCoord, int> _moveRange = new();

    // ----- the motion layer (g10): the model is instant, the body takes its time -------
    private abstract record Anim;
    private sealed record WalkStep(string Id, List<Vector2> Pts, float Speed, string State) : Anim;
    private sealed record LungeStep(string Id, CellCoord Target) : Anim;
    private sealed record ProjStep(string CasterId, CellCoord From, CellCoord To, Element E) : Anim;
    private sealed record FxStep(Action Run) : Anim;

    private readonly Queue<Anim> _anims = new();
    private Anim? _anim;                 // the step playing now
    private float _animT;                // seconds into the step
    private readonly Dictionary<string, Vector2> _visPos = new();
    private readonly Dictionary<string, bool> _visFlip = new();
    private readonly Dictionary<string, float> _attackUntil = new();
    private readonly Dictionary<string, (float until, Vector2 dir)> _hurt = new();  // flash + recoil
    private bool AnimBusy => _anim != null || _anims.Count > 0;

    // g12: the world renders at HALF resolution and upscales with point sampling —
    // the Crawl/Powerhoof law: spend pixels on motion, not on rendering. UI stays native.
    private RenderTarget2D _worldRT = null!;

    // ----- the feel (the Crawl layer) -------------------------------------------------
    private float _freeze, _shake;
    private readonly List<(Vector2 p, int seed)> _blood = new();
    private readonly List<(Vector2 from, Vector2 to, float ttl)> _smears = new();
    // g11 feel pass: sparks fly, impacts ring, walks kick dust, embers breathe.
    private readonly List<(Vector2 p, Vector2 v, float born, float ttl, Color c, int size)> _sparks = new();
    private readonly List<(Vector2 p, float born, float dur, Color c)> _rings = new();
    private float _dustClock;
    private readonly List<(string t, Color c, Vector2 p, float born)> _floats = new();
    private readonly List<(Fighter f, CellCoord at)> _corpses = new();
    private string _narration = ""; private float _narrationUntil;
    private string _banner = ""; private float _bannerUntil; private Color _bannerInk;
    private bool _firstBlood, _firstSpike;
    private Texture2D _vignette = null!;                 // Crawl's candle-dark, baked once
    private static readonly Color Gold = new(232, 192, 88);   // the backstab's one glint of color
    private readonly Random _rng = new();

    // ----- the pick ------------------------------------------------------------------
    private List<PickCard> _cards = new();
    private string _pickTitle = "", _pickSub = "";
    private Color _pickInk = Mono.Ink;
    private bool _shopping;              // the trader's stall: multi-buy, leave when done

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
        _worldRT = new RenderTarget2D(GraphicsDevice, W / 2, H / 2);
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
            {
                banked = _banked, classId = _you.ClassId, level = _you.Level, xp = _you.Xp,
                mate = _mate?.ClassId ?? "",
                learned = string.Join(",", _learned),
                ranks = string.Join(",", _you.SpellRanks.Select(kv => kv.Key + ":" + kv.Value)),
            }));
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
            // Newer fields read tolerantly, so an old save still opens the gate.
            if (r.TryGetProperty("learned", out var lp))
                _learned.AddRange((lp.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));
            if (r.TryGetProperty("ranks", out var rp))
                foreach (var pair in (rp.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split(':');
                    if (kv.Length == 2 && int.TryParse(kv[1], out int rank)) _you.SpellRanks[kv[0]] = rank;
                }
        }
        catch { /* an unreadable save is a fresh start, not a crash */ }
    }

    // ================= RUN FLOW ========================================================

    private void StartRun()
    {
        _st = new RunState { Learned = _learned };   // the covenant resets; the words remain
        _st.Road = RunRules.BuildRoad(new Random(++_seed));
        _runWon = false;
        _you.CurrentHp = null;
        if (_mate != null) _mate.CurrentHp = null;   // the city rests everyone
        EnterRoom();
    }

    /// <summary>The road is dealt, not chosen (g11): walk into whatever the next room is.</summary>
    private void EnterRoom()
    {
        switch (_st.SextonNow ? RoomKind.Boss : _st.RoomNow)
        {
            case RoomKind.Shop: RollShop(); break;
            case RoomKind.Event: RollEvent(); break;
            case RoomKind.Mystery: RollMystery(); break;
            default: StartFight(); break;
        }
    }

    private void NextRoom()
    {
        if (_st.SextonNow) { StartFight(); return; }   // the bell rang: HE is the next room, wherever you are
        _st.Room++;
        EnterRoom();
    }

    private void StartFight()
    {
        _blood.Clear(); _smears.Clear(); _floats.Clear(); _corpses.Clear();
        _sparks.Clear(); _rings.Clear();
        _anims.Clear(); _anim = null; _visPos.Clear(); _visFlip.Clear(); _attackUntil.Clear(); _hurt.Clear();
        _selected = -1; _turnOwner = ""; _inspectId = ""; _resolved = false;
        _firstBlood = false; _firstSpike = false;

        var (engine, avatar) = RunRules.CreateFight(_st, _you, _mate, ref _seed);
        _engine = engine; _avatar = avatar;
        _engine.Emitted += e =>
        {
            RunRules.HandleMechanics(_engine, e, _st, _avatar, this);
            OnPresentation(e);
        };
        _engine.Start();
        foreach (var f in _engine.Fighters) _visPos[f.Id] = Center(f.Pos);

        _scene = Scene.Fight;
        _sfx.SetAmbient(_st.BossFight ? "dirge" : "wind", 0.12f);
        Narrate(_st.BossFight ? "the bell falls silent. HE is here." : $"the dead notice you. ({RunRules.FightLabel(_st)})");
        if (_st.BossFight) { Banner("HE IS HERE", Mono.Danger); _sfx.Play("bell", 0.8f, jitter: false); }
    }

    // ================= THE FX BOOK (IRunFx: mechanics call, the body answers) ==========

    /// <summary>Play a feel-beat in sequence: if the body is still walking out earlier
    /// history, the beat waits its turn in the queue instead of firing over it.</summary>
    private void Present(Action a)
    {
        if (AnimBusy) _anims.Enqueue(new FxStep(a));
        else a();
    }

    public void EmberBurn(Fighter f, CellCoord at) => Present(() =>
    {
        Splatter(Center(at), 3);
        Sparks(Center(at), 6, Mono.Element(Element.Fire), 60f, rise: 40f);
        Float("-2", Mono.Element(Element.Fire), Center(at));
        _sfx.Play("hit_fire", 0.5f);
    });

    public void Afflicted(Fighter f, bool burn) => Present(() =>
        Float(burn ? "burns" : "rot", burn ? Mono.Element(Element.Fire) : Mono.Heal,
            Center(f.Pos) + new Vector2(burn ? -18 : 18, -6)));

    public void StonesPaid(int amount, CellCoord at) => Present(() =>
        Float($"+{amount} st", Mono.Ink, Center(at) + new Vector2(0, -20)));

    public void FedByMite(Fighter avatar, int amount) => Present(() =>
        Float($"+{amount}", Mono.Heal, Center(avatar.Pos)));

    public void TollsLow(int tolls) => _sfx.Play("bell", 0.35f, jitter: false);

    public void SextonComes()
    {
        _sfx.Play("bell", 0.8f, jitter: false);
        Narrate("the last toll dies away. HE is coming.");
    }

    public void RitualTurns(List<CellCoord> grown) => Present(() =>
    {
        foreach (var c in grown) Splatter(Center(c), 6);
        Banner("THE RITUAL TURNS", Mono.Danger);
        Narrate("the ground answers him. the graves open.");
        _freeze = Math.Max(_freeze, 0.25f); _shake = Math.Max(_shake, 12f);
        _sfx.Play("bell", 0.8f, jitter: false);
    });

    public void Transformed(string theme, string form)
    {
        Banner(form, ThemeInk(theme));
        Narrate(theme switch
        {
            "MARROW" => "the rot takes root in you. you are THE BLIGHTED.",
            "BONE" => "half-dead already — the ground has no claim. you are THE REVENANT.",
            _ => "the bell tolls for you now. you are THE TITHED.",
        });
        _sfx.Play("levelup", 0.85f, jitter: false);
    }

    void IRunFx.Narrate(string line) => Narrate(line);

    // ================= PRESENTATION EVENTS (motion + gore, no rules) ===================

    private void OnPresentation(CombatEvent e)
    {
        switch (e)
        {
            case TurnStarted ts:
                if (ts.Fighter == _avatar)
                {
                    Present(() => { _sfx.Play("yourturn", 0.55f, jitter: false); Banner("YOUR TURN", Mono.Ink); });
                }
                else if (ts.Fighter.Archetype == "sexton")
                    Present(() => Banner("HE MOVES", Mono.Danger));
                break;

            case FighterMoved fm:
            {
                // The walk: the body follows the model's path at a readable pace.
                var pts = new List<Vector2>();
                foreach (var c in fm.Path) pts.Add(Center(c));
                _anims.Enqueue(new WalkStep(fm.Fighter.Id, pts, 145f, "walk"));
                break;
            }

            case FighterPushed p:
            {
                var pts = new List<Vector2>();
                foreach (var c in p.Path) pts.Add(Center(c));
                if (pts.Count > 0) _anims.Enqueue(new WalkStep(p.Fighter.Id, pts, 430f, "idle"));
                var slamAt = p.Path.Count > 0 ? Center(p.Path[^1]) : Center(p.Fighter.Pos);
                if (p.CollisionDamage > 0) Present(() =>
                { _shake = Math.Max(_shake, 6f); Sparks(slamAt + new Vector2(0, -8), 8, Mono.Ink, 90f); });
                break;
            }

            case FighterTeleported tp:
                Present(() => _visPos[tp.Fighter.Id] = Center(tp.To));
                break;

            case SpellCast sc:
            {
                _anims.Enqueue(new LungeStep(sc.Caster.Id, sc.Target));
                // Anything thrown beyond arm's reach flies: arrow, spark, stone.
                if (sc.Caster.Pos.DistanceTo(sc.Target) > 1)
                {
                    var dmg = sc.Spell.Effects.FirstOrDefault(x => x.Kind is EffectKind.Damage or EffectKind.Lifesteal);
                    _anims.Enqueue(new ProjStep(sc.Caster.Id, sc.Caster.Pos, sc.Target, dmg?.Element ?? Element.Neutral));
                }
                break;
            }

            case DamageDealt d:
            {
                var attacker = _engine.Outcome == FightOutcome.Ongoing ? _engine.Current : null;
                Present(() =>
                {
                    // Sleep frames by the fighting-game clock: ~90ms a hit, ~160ms a
                    // backstab, kills get the long silence in FighterDied below.
                    _freeze = Math.Max(_freeze, d.RemainingHp <= 0 ? 0.26f : d.Backstab ? 0.16f : 0.09f);
                    _shake = Math.Max(_shake, Math.Min(10f, 2f + d.Amount * 0.25f));
                    if (d.Target.IsAlive || d.RemainingHp <= 0)
                    {
                        var hdir = attacker != null && attacker.Pos != d.At
                            ? Center(d.At) - VisPos(attacker) : Vector2.Zero;
                        if (hdir.LengthSquared() > 1) hdir.Normalize();
                        _hurt[d.Target.Id] = (_time + 0.15f, hdir);
                    }
                    Splatter(Center(d.At), 4 + Math.Min(8, d.Amount / 3));
                    Sparks(Center(d.At) + new Vector2(0, -12), 5 + Math.Min(8, d.Amount / 3),
                        d.Element == Element.Neutral ? Mono.Ink : Mono.Element(d.Element));
                    Ring(Center(d.At), d.Backstab ? Gold : d.Element == Element.Neutral ? Mono.Ink : Mono.Element(d.Element));
                    Float(d.Backstab ? $"-{d.Amount}!" : $"-{d.Amount}", d.Backstab ? Gold : Mono.Danger, Center(d.At));
                    if (attacker != null && attacker.Pos != d.At)
                        _smears.Add((VisPos(attacker), Center(d.At), 0.11f));
                    _sfx.Play("hit_" + d.Element.ToString().ToLowerInvariant(), 0.8f);
                    if (!_firstBlood) { _firstBlood = true; Narrate("first blood feeds the ground."); }
                    if (!_firstSpike && _engine.Field.TileAt(d.At) == TileKind.Spikes && d.Element == Element.Neutral)
                    { _firstSpike = true; Narrate("the bones underfoot are hungry."); }
                });
                break;
            }

            case HealApplied h:
                Present(() =>
                {
                    Float($"+{h.Amount}", Mono.Heal, Center(h.At));
                    Sparks(Center(h.At) + new Vector2(0, -16), 6, Mono.Heal, 30f, rise: 46f, ttl: 0.7f);
                    _sfx.Play("heal", 0.6f);
                });
                break;

            case FighterFell ff:
                Present(() =>
                {
                    // The void takes them whole: no blood, no corpse — just the long quiet.
                    _freeze = Math.Max(_freeze, 0.3f); _shake = Math.Max(_shake, 14f);
                    Float("GONE", Mono.Danger, Center(ff.At));
                    _sfx.Play("crush", 0.9f);
                    Narrate(ff.Fighter == _avatar
                        ? "the dark has a floor. you never find it."
                        : "the island sheds its dead.");
                });
                break;

            case FighterDied fd:
            {
                bool fell = _st.Fallen.Contains(fd.Fighter.Id);
                if (!fell) _corpses.Add((fd.Fighter, fd.At));   // the body claims its ground at once
                Present(() =>
                {
                    _freeze = Math.Max(_freeze, 0.2f); _shake = Math.Max(_shake, 12f);
                    if (!fell)
                    {
                        Splatter(Center(fd.At), 16);
                        Sparks(Center(fd.At) + new Vector2(0, -10), 16, Mono.Danger, speed: 110f, ttl: 0.6f);
                        Ring(Center(fd.At), Mono.Danger, 0.45f);
                        _sfx.Play("death", 0.85f);
                    }
                    if (fd.Fighter.Team == Team.Enemy)
                    {
                        if (!fell) Narrate(fd.Fighter.Archetype == "sexton"
                            ? "the gravedigger digs his own."
                            : "another mouth for the earth.");
                    }
                    else if (!fell) Narrate(fd.Fighter == _avatar
                        ? "the ground remembers your name."
                        : "the sellsword's contract ends here.");
                });
                break;
            }
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

    // ----- the motion pump -------------------------------------------------------------

    private void UpdateAnims(float dt)
    {
        for (int guard = 0; guard < 64 && dt > 0 && _freeze <= 0; guard++)
        {
            if (_anim == null)
            {
                if (_anims.Count == 0) return;
                _anim = _anims.Dequeue(); _animT = 0;
                if (_anim is LungeStep lg)
                {
                    _attackUntil[lg.Id] = _time + 0.34f;
                    // Muzzle flash: a spit of motes off the weapon, toward the mark.
                    if (_engine.Fighters.FirstOrDefault(x => x.Id == lg.Id) is { } cf)
                    {
                        var from = VisPos(cf) + new Vector2(0, -14);
                        var dirv = Center(lg.Target) - from;
                        if (dirv.LengthSquared() > 1) dirv.Normalize();
                        for (int i = 0; i < 5; i++)
                            _sparks.Add((from + dirv * 12,
                                dirv * (90 + _rng.Next(60)) + new Vector2(_rng.Next(-20, 21), _rng.Next(-20, 21)),
                                _time, 0.3f, Mono.Ink, 1));
                    }
                }
                if (_anim is FxStep fx) { fx.Run(); _anim = null; continue; }
            }
            _animT += dt; dt = 0;
            switch (_anim)
            {
                case WalkStep w:
                {
                    float total = 0;
                    var start = _visPos.GetValueOrDefault(w.Id, w.Pts.Count > 0 ? w.Pts[0] : Vector2.Zero);
                    var pts = new List<Vector2> { start };
                    pts.AddRange(w.Pts.Where(p => (p - start).LengthSquared() > 1));
                    for (int i = 1; i < pts.Count; i++) total += Vector2.Distance(pts[i - 1], pts[i]);
                    float travelled = _animT * w.Speed;
                    if (total <= 0 || travelled >= total)
                    { if (pts.Count > 0) _visPos[w.Id] = pts[^1]; _anim = null; break; }
                    float acc = 0;
                    for (int i = 1; i < pts.Count; i++)
                    {
                        float seg = Vector2.Distance(pts[i - 1], pts[i]);
                        if (acc + seg >= travelled)
                        {
                            float t = seg <= 0 ? 1 : (travelled - acc) / seg;
                            // A little hop per cell (walks only — the shoved slide flat).
                            float hop = w.State == "walk" ? MathF.Sin(t * MathF.PI) * 3f : 0f;
                            _visPos[w.Id] = Vector2.Lerp(pts[i - 1], pts[i], t) - new Vector2(0, hop);
                            float dx = pts[i].X - pts[i - 1].X;
                            if (MathF.Abs(dx) > 1) _visFlip[w.Id] = dx < 0;
                            break;
                        }
                        acc += seg;
                    }
                    break;
                }
                case LungeStep l:
                {
                    // Anticipation → strike → hit frame → settle (the Slynyrd rhythm:
                    // lean back 2-3 beats, hit in 1-2, recover slow). Weight lives in
                    // the windup and the held frame, not in the travel.
                    const float dur = 0.34f;
                    var f = _engine.Fighters.FirstOrDefault(x => x.Id == l.Id);
                    if (f == null || _animT >= dur)
                    { if (f != null) _visPos[l.Id] = Center(f.Pos); _anim = null; break; }
                    var home = Center(f.Pos);
                    var dir = Center(l.Target) - home;
                    if (dir.LengthSquared() > 1)
                    {
                        dir.Normalize();
                        float t = _animT / dur;
                        float ext = t < 0.30f ? -6f * (t / 0.30f)                     // windup: lean away
                            : t < 0.50f ? -6f + 28f * ((t - 0.30f) / 0.20f)           // strike: snap in
                            : t < 0.72f ? 22f                                          // hit frame: hold
                            : 22f * (1f - (t - 0.72f) / 0.28f);                        // settle home
                        _visPos[l.Id] = home + dir * ext;
                        if (MathF.Abs(dir.X) > 0.2f) _visFlip[l.Id] = dir.X < 0;
                    }
                    break;
                }
                case ProjStep p:
                {
                    float dist = Vector2.Distance(Center(p.From), Center(p.To));
                    float dur = MathF.Max(0.1f, dist / 640f);
                    if (_animT >= dur) _anim = null;
                    break;
                }
            }
        }
    }

    private Vector2 VisPos(Fighter f) => _visPos.GetValueOrDefault(f.Id, Center(f.Pos));

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
                { _you = NewYou(ClassIds[i]); _learned.Clear(); _sfx.Play("click"); return; }   // the hired sword stays hired
            if (_mate == null && _banked >= RunRules.MateCost && HireRect.Contains(MP))
            {
                _banked -= RunRules.MateCost;
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
        UpdateAnims(dt);

        // The feel layer breathes every frame: motes drift and settle, rings die,
        // embers spit, walking feet kick dust.
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var s = _sparks[i];
            if (_time - s.born > s.ttl) { _sparks.RemoveAt(i); continue; }
            s.p += s.v * dt; s.v += new Vector2(0, 90f * dt);
            _sparks[i] = s;
        }
        _rings.RemoveAll(r => _time - r.born > r.dur);
        foreach (var e in _st.Embers)
            if (_rng.NextDouble() < dt * 1.1)
                _sparks.Add((Center(e) + new Vector2(_rng.Next(-14, 15), 0),
                    new Vector2(_rng.Next(-6, 7), -26 - _rng.Next(18)), _time, 0.7f,
                    Mono.Element(Element.Fire) * 0.8f, 1));
        if (_anim is WalkStep ws && (_dustClock += dt) > 0.11f)
        {
            _dustClock = 0;
            if (_visPos.TryGetValue(ws.Id, out var wp))
                _sparks.Add((wp + new Vector2(_rng.Next(-6, 7), TH / 4f),
                    new Vector2(_rng.Next(-10, 11), -8), _time, 0.35f, Mono.Dim * 0.6f, 1));
        }

        if (_engine.Outcome != FightOutcome.Ongoing)
        {
            if (AnimBusy) return;   // let the last blow land before the verdict
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
                    _you.GainXp(RunRules.XpForFight(_st.FightsWon));
                    if (_you.Level > before)
                    {
                        _st.PendingLevels += _you.Level - before;   // each ding owes a draft of three
                        Narrate($"you grow harder. LEVEL {_you.Level} — a new word of ruin.");
                        _sfx.Play("levelup", 0.8f, jitter: false);
                    }
                }
            }
            _endPause -= dt;
            if (_endPause <= 0)
            {
                if (_engine.Outcome != FightOutcome.Victory)
                { _banked += _st.RunStones / 2; _runWon = false; _scene = Scene.End; _sfx.SetAmbient(null); }
                else if (_st.BossFight)
                { _banked += _st.RunStones; _runWon = true; _scene = Scene.End; _sfx.SetAmbient(null); }
                else
                {
                    _st.FightsWon++;
                    RollSpoils(); _scene = Scene.Pick;
                }
            }
            return;
        }

        var cur = _engine.Current;
        if (cur.Id != _turnOwner)
        { _turnOwner = cur.Id; _aiTimer = 0.5f; _aiActed = false; }   // an armed spell STAYS armed across turns

        bool myTurn = cur == _avatar;
        _moveRange = myTurn && !AnimBusy ? _engine.MovementRange(cur) : new();

        // Arming and inspecting are always allowed — reading the fight is free, even
        // on the dead's turn. Casting and walking wait for yours.
        var spells = _avatar.Spells;
        for (int i = 0; i < spells.Count && i < 8; i++)
            if (Pressed(Keys.D1 + i)) { _selected = _selected == i ? -1 : i; _sfx.Play("click", 0.4f); }
        if (Pressed(Keys.Escape)) { _selected = -1; _inspectId = ""; }
        if (Pressed(Keys.Space)) { _autoPlay = !_autoPlay; _sfx.Play("click", 0.5f); }
        if (RightClicked()) { _selected = -1; _inspectId = ""; }

        if (!myTurn || _autoPlay)
        {
            if (myTurn && Pressed(Keys.Enter)) { _autoPlay = false; _engine.EndTurn(); return; }
            if (!AnimBusy)   // the brain waits for the body to finish its last thought
            {
                _aiTimer -= dt;
                if (_aiTimer <= 0 && !_aiActed)
                { _aiActed = true; Policy.TakeTurn(_engine, cur); _aiTimer = 0.4f; }
                else if (_aiTimer <= 0 && _aiActed && _engine.Outcome == FightOutcome.Ongoing)
                { _engine.EndTurn(); return; }
            }
            // Wells and inspection still answer the mouse below.
        }
        else if (Pressed(Keys.Enter)) { _engine.EndTurn(); return; }

        if (!Clicked()) return;
        var m = MP;
        for (int i = 0; i < spells.Count && i < 8; i++)
            if (WellRect(i).Contains(m)) { _selected = _selected == i ? -1 : i; _sfx.Play("click", 0.4f); return; }
        if (myTurn && !_autoPlay && EndTurnRect.Contains(m)) { _engine.EndTurn(); return; }
        if (m.Y >= BandTop) return;   // the band eats its own clicks

        var cell = CellAt(m);
        bool pilots = myTurn && !_autoPlay;
        if (_selected >= 0 && _selected < spells.Count)
        {
            var sp = spells[_selected];
            if (pilots && cell is { } tc && _engine.CanCast(_avatar, sp, tc, out _))
            {
                _engine.TryCast(_avatar, sp, tc);
                if (!_engine.CanCast(_avatar, sp, tc, out _)) _selected = -1;
            }
            else _selected = -1;   // a click past the blade sheathes it (owner: Dofus law)
        }
        else if (cell is { } c && _engine.FighterAt(c) is { Team: Team.Enemy } foe)
            _inspectId = _inspectId == foe.Id ? "" : foe.Id;   // open the enemy for reading
        else if (pilots && cell is { } mc && _moveRange.ContainsKey(mc))
        { _inspectId = ""; _engine.TryMove(_avatar, mc); }
        else _inspectId = "";
    }

    private void UpdatePick()
    {
        // The trader's stall: buy any number you can pay for, then leave on your feet.
        if (_shopping)
        {
            if (Pressed(Keys.Enter) || Pressed(Keys.Space)
                || (Clicked() && LeaveRect.Contains(MP)))
            { _shopping = false; _sfx.Play("click", 0.5f); Advance(); return; }
            for (int i = 0; i < _cards.Count; i++)
                if ((Clicked() && CardRect(i).Contains(MP)) || Pressed(Keys.D1 + i))
                {
                    var card = _cards[i];
                    if (card.Price > _st.RunStones) { Narrate("the trader eyes your thin purse."); return; }
                    _st.RunStones -= card.Price;
                    _sfx.Play("coin", 0.85f);
                    card.Apply();
                    _cards.RemoveAt(i);
                    if (_cards.Count == 0) { _shopping = false; Advance(); }
                    return;
                }
            return;
        }

        for (int i = 0; i < _cards.Count; i++)
            if ((Clicked() && CardRect(i).Contains(MP)) || Pressed(Keys.D1 + i))
            {
                _sfx.Play(_cards[i].Kind == "ESSENCE" ? "chime" : "coin", 0.75f);
                _cards[i].Apply();
                Advance();
                return;
            }
    }

    /// <summary>Between rooms, one screen at a time: level drafts first, then the road walks on.</summary>
    private void Advance()
    {
        if (_st.PendingLevels > 0) { _st.PendingLevels--; RollLevel(); return; }
        NextRoom();
    }

    private void UpdateEnd()
    {
        if (Pressed(Keys.Space) || Pressed(Keys.Enter) || Clicked())
        {
            if (!_runWon) { _you = NewYou(_you.ClassId); _learned.Clear(); } // the fallen leader is buried, words and all
            SaveMeta();
            _scene = Scene.City;
        }
    }

    // ================= THE PICK (hands rolled by RunRules, dressed here) ===============

    private void RollSpoils()
    {
        _pickTitle = "THE SPOILS"; _pickSub = "take ONE. the rest sink into the dirt."; _pickInk = Mono.Ink;
        _cards = RunRules.RollSpoils(_st, _you, new Random(++_seed), this);
        _scene = Scene.Pick;
    }

    private void RollLevel()
    {
        _pickTitle = $"A WORD OF RUIN — LEVEL {_you.Level}"; _pickSub = "you grow harder. learn ONE."; _pickInk = Gold;
        _cards = RunRules.RollLevelCards(_st, _you, new Random(++_seed));
        _scene = Scene.Pick;
    }

    private void RollEvent()
    {
        var (title, sub, cards) = RunRules.RollEventCards(_st, _you, new Random(++_seed));
        _pickTitle = title; _pickSub = sub; _pickInk = Mono.Dim;
        _cards = cards;
        _scene = Scene.Pick;
    }

    private void RollMystery()
    {
        var (title, sub, cards) = RunRules.RollMysteryCards(_st, _you, new Random(++_seed), this);
        _pickTitle = title; _pickSub = sub; _pickInk = Mono.Cast;
        _cards = cards;
        _scene = Scene.Pick;
    }

    private void RollShop()
    {
        _pickTitle = "THE TRADER"; _pickSub = "a mercenary grocer on a cart of bones. stones spend here.";
        _pickInk = Gold;
        _cards = RunRules.RollShop(_st, _you, new Random(++_seed), this);
        _shopping = true;
        _scene = Scene.Pick;
        _sfx.Play("coin", 0.6f);
    }

    // ================= FEEL HELPERS ====================================================

    private void Splatter(Vector2 at, int drops)
    {
        for (int i = 0; i < drops; i++)
            _blood.Add((at + new Vector2(_rng.Next(-26, 27), _rng.Next(-18, 19)), _rng.Next(1000)));
        if (_blood.Count > 500) _blood.RemoveRange(0, _blood.Count - 500);
    }

    /// <summary>A handful of 1-bit motes thrown from a point — the whole particle budget.</summary>
    private void Sparks(Vector2 at, int n, Color c, float speed = 70f, float rise = 26f, float ttl = 0.45f)
    {
        for (int i = 0; i < n; i++)
        {
            float a = (float)(_rng.NextDouble() * Math.PI * 2);
            float s = speed * (0.4f + (float)_rng.NextDouble() * 0.9f);
            _sparks.Add((at, new Vector2(MathF.Cos(a) * s, MathF.Sin(a) * s * 0.55f - rise),
                _time, ttl * (0.6f + (float)_rng.NextDouble() * 0.8f), c, _rng.Next(2) + 1));
        }
        if (_sparks.Count > 400) _sparks.RemoveRange(0, _sparks.Count - 400);
    }

    private void Ring(Vector2 at, Color c, float dur = 0.32f) => _rings.Add((at, _time, dur, c));

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
    private bool RightClicked() => _mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;

    // The band, Dofus-shaped: vitals in the middle, the wells on the right, END TURN centred.
    private static Rectangle PlateRect => new(340, BandTop + 8, 400, 78);
    private static Rectangle WellRect(int i) => new(806 + i % 8 * 54, BandTop + 10 + i / 8 * 54, 48, 48);
    private static Rectangle EndTurnRect => new(W / 2 - 88, H - 34, 176, 26);
    private static Rectangle DepartRect => new(W / 2 - 90, 476, 180, 44);
    private static Rectangle HireRect => new(W / 2 - 170, 556, 340, 30);
    private Rectangle CardRect(int i) => new(W / 2 - (_cards.Count * 240 - 40) / 2 + i * 240, 240, 200, 260);
    private static Rectangle LeaveRect => new(W / 2 - 90, 540, 180, 30);

    // ================= DRAW ============================================================

    protected override void Draw(GameTime gt)
    {
        if (_scene == Scene.Fight) { DrawFightFrame(); base.Draw(gt); return; }

        GraphicsDevice.Clear(Mono.Bg);
        _sb.Begin(samplerState: SamplerState.PointClamp);
        switch (_scene)
        {
            case Scene.City: DrawCity(); break;
            case Scene.Pick: DrawPick(); break;
            case Scene.End: DrawEnd(); break;
        }
        if (_time < _narrationUntil && _scene != Scene.City)
            _font.DrawCentered(_sb, _narration.ToUpperInvariant(), W / 2, 62, 2,
                Mono.Ink * Math.Min(1f, (_narrationUntil - _time) / 0.6f));
        _sb.End();
        base.Draw(gt);
    }

    /// <summary>The fight frame, Powerhoof-shaped (g12): the WORLD renders into a half-
    /// resolution target and integer-upscales with point sampling — chunky pixels, chunky
    /// 2px-stepped screenshake — while every word of text and the whole band stay native
    /// and crisp on top. Detail goes to motion, never to rendering.</summary>
    private void DrawFightFrame()
    {
        bool myTurn = _engine.Outcome == FightOutcome.Ongoing && _engine.Current == _avatar;
        bool pilots = myTurn && !_autoPlay;

        GraphicsDevice.SetRenderTarget(_worldRT);
        GraphicsDevice.Clear(Mono.Bg);
        _sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: Matrix.CreateScale(0.5f));
        DrawFight(myTurn, pilots);
        _sb.End();
        GraphicsDevice.SetRenderTarget(null);

        GraphicsDevice.Clear(Mono.Bg);
        var shakeOff = _shake > 0
            ? new Vector2(_rng.Next(-(int)_shake, (int)_shake + 1) / 2 * 2,
                          _rng.Next(-(int)_shake, (int)_shake + 1) / 2 * 2)
            : Vector2.Zero;
        _sb.Begin(samplerState: SamplerState.PointClamp);
        _sb.Draw(_worldRT, new Rectangle((int)shakeOff.X, (int)shakeOff.Y, W, H), Color.White);
        _sb.End();

        // World-anchored TEXT (floats, previews, hover counts, banners) rides the shake
        // but renders at native resolution — blocky world, legible numbers.
        _sb.Begin(samplerState: SamplerState.PointClamp,
            transformMatrix: Matrix.CreateTranslation(shakeOff.X, shakeOff.Y, 0));
        DrawWorldText(myTurn, pilots);
        _sb.End();

        _sb.Begin(samplerState: SamplerState.PointClamp);
        DrawFightHud(myTurn, pilots);
        var inspected = _inspectId == ""
            ? null : _engine.Fighters.FirstOrDefault(f => f.IsAlive && f.Id == _inspectId);
        if (inspected != null) DrawInspector(inspected);
        if (_time < _narrationUntil)
            _font.DrawCentered(_sb, _narration.ToUpperInvariant(), W / 2, 62, 2,
                Mono.Ink * Math.Min(1f, (_narrationUntil - _time) / 0.6f));
        _sb.End();
    }

    /// <summary>Everything written over the world at native resolution.</summary>
    private void DrawWorldText(bool myTurn, bool pilots)
    {
        var focus = FocusSpell(out _);
        var hovCell = CellAt(MP);

        if (pilots && focus == null && !AnimBusy && hovCell is { } dest && _moveRange.ContainsKey(dest))
            _font.DrawCentered(_sb, $"{_moveRange[dest]} MP", (int)Center(dest).X, (int)Center(dest).Y - 34, 1, Mono.Mp);

        if (focus != null && hovCell is { } hc && _engine.FighterAt(hc) is { Team: Team.Enemy } prey)
        {
            bool back = CombatEngine.IsBackstab(_avatar.Pos, prey);
            bool legal = myTurn && _engine.CanCast(_avatar, focus, hc, out _);
            if (_engine.EstimateDamage(_avatar, focus, hc) is { } est)
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

        // The exact count appears over a hovered body's bar — never uninvited.
        foreach (var f in _engine.Fighters.Where(f => f.IsAlive))
            if (hovCell == f.Pos)
            {
                var cc = VisPos(f);
                bool champ = f.Team == Team.Enemy && f.Level >= 3 && f.Archetype != "sexton";
                var (_, figH) = SizeOf(f.Archetype);
                int barY = (int)(cc.Y + TH / 4f + 2 - figH * (champ ? 1.15f : 1f)) - 8;
                _font.DrawCentered(_sb, $"{f.Hp}/{f.MaxHp}", (int)cc.X, barY - 11, 1,
                    f.Hp * 4 <= f.MaxHp ? Mono.Danger : Mono.Ink);
            }

        const float FloatLife = 1.1f;
        for (int i = _floats.Count - 1; i >= 0; i--)
        {
            var (t, c, p, born) = _floats[i];
            float age = _time - born;
            if (age > FloatLife) { _floats.RemoveAt(i); continue; }
            _font.DrawCentered(_sb, t, (int)p.X, (int)(p.Y - 24 - age * 30), 2, c * (1f - age / FloatLife));
        }

        // Turn banner — one loud breath, then gone.
        if (_time < _bannerUntil)
        {
            float a = Math.Min(1f, (_bannerUntil - _time) / 0.35f);
            _font.DrawCentered(_sb, _banner, W / 2, 148, 3, _bannerInk * a);
        }
    }

    private void DrawCity()
    {
        _font.DrawCentered(_sb, "THE GAUNTLET", W / 2, 170, 6, Mono.Ink);
        _font.DrawCentered(_sb, "OF THE BELL", W / 2, 230, 3, Mono.Dim);
        _font.DrawCentered(_sb, $"BANKED: {_banked} ESSENCE STONES", W / 2, 320, 2, Mono.Ink);
        _font.DrawCentered(_sb, $"YOU — {_you.ClassId.ToUpperInvariant()}, LEVEL {_you.Level}", W / 2, 352, 1, Mono.Dim);
        // The road ahead, spelled out Loop Hero style: a whole row of rooms, then HIM.
        for (int i = 0; i < 11; i++)
        {
            var p = new Vector2(W / 2 - 130 + i * 26, 380);
            if (i > 0) _prim.Line(_sb, p - new Vector2(19, 0), p - new Vector2(7, 0), 1f, Mono.Faint);
            _prim.DiscAt(_sb, p, i == 10 ? 4 : 3, i == 10 ? Mono.Danger : Mono.Dim);
        }
        _font.DrawCentered(_sb, "a road of eleven rooms is dealt: fights, traders, stranger things — then the sexton.", W / 2, 394, 1, Mono.Dim);
        _font.DrawCentered(_sb, "the bell grants forty-five tolls. every fighting turn you take is one.", W / 2, 408, 1, Mono.Faint);
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
            "archer" => "LOOSED ARROW 3 AP, from afar · LONG SHOT: +4 at range 6+",
            "bulwark" => "SHOVE 3 AP — the coastline is a weapon · RAGE BELOW: +30% when bloodied",
            _ => "SPARK 3 AP · OVERCHANNEL: unspent AP burns hotter",
        }, W / 2, 462, 1, Mono.Dim);
        bool hov = DepartRect.Contains(MP);
        Mono.Button(_sb, _prim, DepartRect, hover: hov);
        _font.DrawCentered(_sb, "DEPART", DepartRect.Center.X, DepartRect.Y + 15, 2, Mono.ButtonInk(hov));
        _font.DrawCentered(_sb, "(SPACE)", W / 2, 530, 1, Mono.Faint);

        // The Post: one hire, paid in banked stones, dead when he's dead.
        if (_mate != null)
            _font.DrawCentered(_sb, $"THE SELLSWORD RIDES WITH YOU — {_mate.ClassId.ToUpperInvariant()}",
                W / 2, HireRect.Y + 10, 1, Mono.Ally);
        else if (_banked >= RunRules.MateCost)
        {
            bool hh = HireRect.Contains(MP);
            Mono.Button(_sb, _prim, HireRect, hover: hh);
            _font.DrawCentered(_sb, $"HIRE THE SELLSWORD ({MateClassId().ToUpperInvariant()}) — {RunRules.MateCost} ST",
                HireRect.Center.X, HireRect.Y + 10, 1, Mono.ButtonInk(hh));
        }
        else
            _font.DrawCentered(_sb, $"the post wants {RunRules.MateCost} banked stones for a sellsword.",
                W / 2, HireRect.Y + 10, 1, Mono.Faint);
    }

    /// <summary>The spell under the player's attention right now: a hovered well wins,
    /// else the armed one — this is what the board paints ranges for.</summary>
    private SpellDef? FocusSpell(out bool hoveredOnly)
    {
        hoveredOnly = false;
        int hov = HoveredWell();
        if (hov >= 0) { hoveredOnly = _selected != hov; return _avatar.Spells[hov]; }
        if (_selected >= 0 && _selected < _avatar.Spells.Count) return _avatar.Spells[_selected];
        return null;
    }

    private int HoveredWell()
    {
        if (_scene != Scene.Fight) return -1;
        for (int i = 0; i < _avatar.Spells.Count && i < 8; i++)
            if (WellRect(i).Contains(MP)) return i;
        return -1;
    }

    private void DrawFight(bool myTurn, bool pilots)
    {
        // The board: a ragged iso island — void is the night, stones cluster like graves.
        // Every coast edge gets a brighter rim: the coastline KILLS here, so the eye must
        // find it without counting cells (and the island stops looking like torn paper).
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
            {
                var cell = new CellCoord(x, y);
                if (_engine.Field.TileAt(cell) == TileKind.Void) continue;
                var cc = Center(cell);
                _prim.DiamondAt(_sb, cc, (x + y) % 2 == 0 ? Mono.Floor : Mono.FloorAlt);
                DiamondOutline(cc, 2f, Mono.Seam * 0.7f);   // 2 logical px = 1 chunky pixel

                bool VoidAt(int dx, int dy)
                {
                    var n = new CellCoord(x + dx, y + dy);
                    return !_engine.Field.InBounds(n) || _engine.Field.TileAt(n) == TileKind.Void;
                }
                var t = cc + new Vector2(0, -TH / 2f); var r = cc + new Vector2(TW / 2f, 0);
                var b = cc + new Vector2(0, TH / 2f); var l = cc + new Vector2(-TW / 2f, 0);
                var rim = Mono.Dim * 0.85f;
                if (VoidAt(1, 0)) _prim.Line(_sb, r, b, 2f, rim);    // SE coast
                if (VoidAt(0, 1)) _prim.Line(_sb, b, l, 2f, rim);    // SW coast
                if (VoidAt(-1, 0)) _prim.Line(_sb, l, t, 2f, rim);   // NW coast
                if (VoidAt(0, -1)) _prim.Line(_sb, t, r, 2f, rim);   // NE coast
            }
        foreach (var e in _st.Embers)
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

        // ----- the reading layer: ranges, paths, threats — Dofus does this well ---------
        var focus = FocusSpell(out _);
        var hovCell = CellAt(MP);

        if (pilots && focus == null && !AnimBusy)
        {
            foreach (var c in _moveRange.Keys)
                _prim.DiamondAt(_sb, Center(c), Mono.Walk * 0.32f);
            // The promise of the walk: hover a green cell and the path draws itself.
            if (hovCell is { } dest && _moveRange.ContainsKey(dest))
            {
                var path = Pathfinding.FindPath(_engine.Field, _avatar.Pos, dest,
                    c => c != _avatar.Pos && _engine.IsOccupied(c),
                    stepDanger: _avatar.HazardImmune ? null : _engine.DangerAt);
                if (path != null)
                    foreach (var c in path.Where(c => c != _avatar.Pos))
                        _prim.DiscAt(_sb, Center(c), c == dest ? 6 : 4, Mono.Walk * (c == dest ? 0.95f : 0.7f));
            }
        }

        if (focus != null)
        {
            // Geometric reach first (dim): what the spell COULD touch from where you stand —
            // painted even off-turn, even with nothing legal to hit. Then the live targets, bright.
            foreach (var c in _engine.SpellReachCells(_avatar, focus))
                _prim.DiamondAt(_sb, Center(c), Mono.Cast * 0.16f);
            if (myTurn)
                foreach (var c in _engine.CastableCells(_avatar, focus))
                    _prim.DiamondAt(_sb, Center(c), Mono.Cast * 0.34f);
        }

        // The inspected enemy wears its whole threat on the ground, in the danger color.
        var inspected = _inspectId == "" ? null
            : _engine.Fighters.FirstOrDefault(f => f.IsAlive && f.Id == _inspectId);
        if (inspected != null)
        {
            var threat = new HashSet<CellCoord>();
            foreach (var sp in inspected.Spells.Where(s =>
                         s.Effects.Any(x => x.Kind is EffectKind.Damage or EffectKind.Lifesteal)))
                foreach (var c in _engine.SpellReachCells(inspected, sp)) threat.Add(c);
            foreach (var c in threat)
                _prim.DiamondAt(_sb, Center(c), Mono.Danger * 0.18f);
            DiamondOutline(VisPos(inspected), 2f, Mono.Danger);
        }

        if (hovCell is { } hc && _engine.Field.InBounds(hc)
            && _engine.Field.TileAt(hc) != TileKind.Void)
            DiamondOutline(Center(hc), 2f, Mono.Ink * 0.7f);
        // (The damage-preview NUMBERS moved to DrawWorldText — text stays native-crisp.)

        // Entities: stones, corpses and fighters share ONE depth-sorted pass, so a
        // sprite behind a rock cluster is properly buried by it. Depth follows the
        // VISUAL position — a body mid-walk sorts where it stands, not where it will.
        int DepthAt(Vector2 v) => (int)((v.Y - OY) / (TH / 2f) * 4f);
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
                        if (rock != null) SpriteDraw.Feet(_sb, rock, cc + new Vector2(0, -4), Mono.Ink, 30, 0);
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
                DrawSprite(f, cc + new Vector2(0, 4), Mono.Faint, SizeOf(f.Archetype).frame * 0.72f, "die");
            }));
        }
        foreach (var f in _engine.Fighters.Where(f => f.IsAlive))
        {
            var ff = f; var cc = VisPos(f);
            pass.Add((DepthAt(cc) + 2, () =>
            {
                bool current = _engine.Outcome == FightOutcome.Ongoing && ff == _engine.Current;
                _prim.HaloAt(_sb, cc + new Vector2(0, 5),
                    (ff.Team == Team.Player ? Mono.Ally : Mono.Danger) * (current ? 1f : 0.6f));
                // Transformations are WORN (Isaac's law: every pickup shows): extra halo rings.
                if (ff == _avatar)
                {
                    int ring = 0;
                    foreach (var theme in new[] { "MARROW", "BONE", "BELL" })
                        if (_st.Transformed(theme))
                            _prim.HaloAt(_sb, cc + new Vector2(0, 5 - ++ring * 3),
                                ThemeInk(theme) * (0.4f + 0.15f * MathF.Sin(_time * 3f + ring)));
                }
                // The facing tick: which way they look is which way you flank.
                var fo = new Vector2((ff.Facing.X - ff.Facing.Y) * (TW / 4f), (ff.Facing.X + ff.Facing.Y) * (TH / 4f));
                _prim.Line(_sb, cc + new Vector2(0, 5) + fo * 0.55f, cc + new Vector2(0, 5) + fo * 0.95f,
                    2f, Mono.Ink * 0.55f);
                // Champions (grade 3) wear the danger color and stand a head taller.
                bool champ = ff.Team == Team.Enemy && ff.Level >= 3 && ff.Archetype != "sexton";
                var (frameH, figH) = SizeOf(ff.Archetype);
                float scale = champ ? 1.15f : 1f;
                float sh = frameH * scale;
                // The hit frame (Vlambeer's law, SF's clock): for ~100ms the victim flashes
                // inverted, recoils 6px off the blow and trembles — pain you can SEE.
                var drawAt = cc;
                var tint = ff.Archetype == "sexton" || champ ? Mono.Danger : Mono.Ink;
                if (_hurt.TryGetValue(ff.Id, out var hh) && hh.until > _time)
                {
                    float rem = (hh.until - _time) / 0.15f;
                    drawAt += hh.dir * 6f * rem + new Vector2(_rng.Next(-2, 3), 0);
                    if (hh.until - _time > 0.05f) tint = tint == Mono.Danger ? Mono.Ink : Mono.Danger;
                }
                DrawSprite(ff, drawAt, tint, sh, StateOf(ff));
                // The life bar rides ABOVE the head (owner's law) — numbers only on hover
                // (drawn native in DrawWorldText).
                float hf = Math.Clamp(ff.Hp / (float)ff.MaxHp, 0f, 1f);
                int barY = (int)(cc.Y + TH / 4f + 2 - figH * scale) - 8;
                _prim.FillRect(_sb, new Rectangle((int)cc.X - 14, barY, 28, 4), Mono.Faint);
                _prim.FillRect(_sb, new Rectangle((int)cc.X - 14, barY, (int)(28 * hf), 4),
                    ff.Hp * 4 <= ff.MaxHp ? Mono.Danger : ff.Team == Team.Player ? Mono.Ally : Mono.Ink);
            }));
        }
        foreach (var (_, draw) in pass.OrderBy(p => p.depth)) draw();

        // The flying thing, over everyone's heads.
        if (_anim is ProjStep proj)
        {
            var from = Center(proj.From) + new Vector2(0, -18);
            var to = Center(proj.To) + new Vector2(0, -12);
            float dur = MathF.Max(0.1f, Vector2.Distance(from, to) / 640f);
            float t = Math.Clamp(_animT / dur, 0f, 1f);
            var at = Vector2.Lerp(from, to, t) + new Vector2(0, -MathF.Sin(t * MathF.PI) * 14f);
            var ink = Mono.Element(proj.E);
            var sheet = _sprites.GetSheet(ProjArt(proj.E), "idle", "se");
            if (sheet != null)
            {
                int fr = (int)(_time * 16) % sheet.FrameCount;
                var srcW = sheet.FrameWidth; int s = 2;   // 32px missiles beside 44px bodies
                bool flip = to.X < from.X;
                _sb.Draw(sheet.Texture,
                    new Rectangle((int)at.X - srcW * s / 2, (int)at.Y - sheet.FrameHeight * s / 2, srcW * s, sheet.FrameHeight * s),
                    sheet.Frame(fr), ink, 0f, Vector2.Zero,
                    flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
            }
            else
            {
                _prim.Line(_sb, Vector2.Lerp(from, to, Math.Max(0, t - 0.08f)), at, 3f, ink * 0.6f);
                _prim.DiscAt(_sb, at, 4, ink);
            }
        }

        foreach (var (from, to, _) in _smears)
            _prim.Line(_sb, from, to, 5f, Mono.Ink * 0.5f);

        // The feel layer on top of the bodies: motes, then the impact rings.
        foreach (var s in _sparks)
        {
            float a = 1f - (_time - s.born) / s.ttl;
            _prim.FillRect(_sb, new Rectangle((int)s.p.X, (int)s.p.Y, (s.size + 1) * 2, (s.size + 1) * 2),
                s.c * Math.Clamp(a, 0f, 1f));
        }
        foreach (var rg in _rings)
        {
            float t = Math.Clamp((_time - rg.born) / rg.dur, 0f, 1f);
            float rad = 8f + t * 22f;
            var col = rg.c * (1f - t);
            Vector2 P(int k)
            {
                float ang = MathF.PI * 2 * k / 8f;
                return rg.p + new Vector2(MathF.Cos(ang) * rad, MathF.Sin(ang) * rad * 0.5f);
            }
            for (int k = 0; k < 8; k++) _prim.Line(_sb, P(k), P(k + 1), 2f, col);
        }

        // Crawl's law: the dark leans in from the rim, and the candle is never quite steady.
        _sb.Draw(_vignette, Vector2.Zero, Color.White * (0.9f + 0.1f * MathF.Sin(_time * 5.3f)));
        // The ritual's stage: HIS fight is lit by half the candles.
        if (_st.BossFight) _sb.Draw(_vignette, Vector2.Zero, Color.White * 0.55f);
    }

    /// <summary>What the body is doing right now, for the sprite sheets.</summary>
    private string StateOf(Fighter f)
    {
        if (_attackUntil.TryGetValue(f.Id, out float until) && until > _time) return "attack";
        if (_anim is WalkStep w && w.Id == f.Id && w.State == "walk") return "walk";
        return "idle";
    }

    private static string ProjArt(Element e) => e switch
    {
        Element.Fire => "proj_fire",
        Element.Air => "proj_arrow",
        Element.Earth => "proj_rock",
        _ => "proj_bolt",
    };

    // ================= THE BAND (Dofus-shaped, repurposed from old TITHE) ==============

    private void DrawFightHud(bool myTurn, bool pilots)
    {
        // The bell, top center — the only clock that matters.
        float frac = Math.Clamp(_st.Tolls / (float)RunRules.TollStart, 0f, 1f);
        bool urgent = !_st.SextonNow && _st.Tolls <= 5;
        Mono.Bar(_sb, _prim, new Rectangle(W / 2 - 150, 8, 300, 10), frac,
            frac > 0.25f ? Mono.Ink : Mono.Danger);
        _font.DrawCentered(_sb, (_st.SextonNow ? "THE BELL HAS RUNG — " : $"{_st.Tolls} TOLLS — ") + RunRules.FightLabel(_st), W / 2, 24, 1,
            _st.SextonNow ? Mono.Danger
            : urgent ? Color.Lerp(Mono.Dim, Mono.Danger, 0.5f + 0.5f * MathF.Sin(_time * 6f))
            : Mono.Dim);

        // The road (Loop Hero keeps the whole loop in view): every room, then HIM.
        DrawRoadStrip(48);

        _font.Draw(_sb, $"{_st.RunStones} st", 16, 12, 2, Mono.Ink);

        // Whoever the cursor rests on gets a name — and an enemy tells you what it
        // could take out of you (hover reads; click opens the full ledger).
        if (CellAt(MP) is { } tc && _engine.FighterAt(tc) is { } tf)
        {
            _font.Draw(_sb,
                $"{tf.Name.ToUpperInvariant()}{(tf.Team == Team.Enemy && tf.Level >= 3 ? " · CHAMPION" : tf.Team == Team.Enemy && tf.Level == 2 ? " · GRADE 2" : "")}  {tf.Hp}/{tf.MaxHp}",
                16, 40, 1, tf.Team == Team.Enemy ? Mono.Danger : Mono.Ally);
            if (tf.Team == Team.Enemy && BiggestBlow(tf) is { } blow)
                _font.Draw(_sb, $"COULD HIT YOU FOR {blow.lo}-{blow.hi} · CLICK TO INSPECT", 16, 54, 1, Mono.Dim);
        }

        // Turn order, top right.
        int ty = 10;
        foreach (var f in _engine.Fighters.Where(f => f.IsAlive))
        {
            bool cur = _engine.Outcome == FightOutcome.Ongoing && f == _engine.Current;
            _font.Draw(_sb, (cur ? "> " : "  ") + f.Name.ToUpperInvariant(), W - 200, ty, 1,
                cur ? Mono.Ink : f.Team == Team.Player ? Mono.Ally : Mono.Danger);
            ty += 14;
        }

        // ----- the band itself --------------------------------------------------------
        _prim.FillRect(_sb, new Rectangle(0, BandTop, W, H - BandTop), Mono.Panel);
        _prim.FillRect(_sb, new Rectangle(0, BandTop, W, 1), Mono.Dim);

        // LEFT: the wardrobe and the sleeve of essences.
        string[] slots = { "BLADE", "PLATE", "BOOTS", "CHARM" };
        _font.Draw(_sb, "GEAR", 12, BandTop + 6, 1, Mono.Faint);
        for (int i = 0; i < slots.Length; i++)
        {
            var r = new Rectangle(12 + i * 44, BandTop + 18, 40, 40);
            var worn = _st.Gear.GetValueOrDefault(slots[i]);
            Mono.Slot(_sb, _prim, r, hover: r.Contains(MP));
            _font.DrawCentered(_sb, slots[i] == "BOOTS" ? "S" : slots[i][..1], r.Center.X, r.Y + 8, 2,
                worn != null ? Mono.Ink : Mono.Faint);
            _font.DrawCentered(_sb, worn != null ? $"+{worn.Power}" : "-", r.Center.X, r.Bottom + 3, 1,
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
            if (_st.FamilyCount(fam) >= 3)
            { _font.Draw(_sb, fam + " SET WHOLE", 196, BandTop + 6, 1, Mono.Cast); break; }

        // Isaac wears its essences on its sleeve: one row, colored by theme; transformations
        // announce themselves first. A crowded late-run row compacts into theme counts.
        int ex = 12; int ey = BandTop + 78;
        foreach (var theme in new[] { "MARROW", "BONE", "BELL" })
            if (_st.Transformed(theme))
            {
                string form = RunRules.FormOf(theme);
                _font.Draw(_sb, form, ex, ey, 1, ThemeInk(theme));
                ex += _font.Measure(form, 1) + 14;
            }
        if (_st.Essences.Count <= 4)
            foreach (var e in _st.Essences)
            {
                string t = "* " + e;
                if (ex + _font.Measure(t, 1) > 330) { ex = 12; ey += 13; if (ey > H - 14) break; }
                _font.Draw(_sb, t, ex, ey, 1, ThemeInk(RunRules.ThemeOf(e)) * 0.8f);
                ex += _font.Measure(t, 1) + 12;
            }
        else
            foreach (var theme in new[] { "MARROW", "BONE", "BELL" })
            {
                int n = _st.ThemeCount(theme);
                if (n == 0) continue;
                string t = $"{theme} {n}";
                _font.Draw(_sb, t, ex, ey, 1, ThemeInk(theme) * 0.8f);
                ex += _font.Measure(t, 1) + 14;
            }

        // CENTER: the Dofus plate — the leader's vitals where the eyes rest.
        var plate = PlateRect;
        _prim.FillRect(_sb, plate, new Color(14, 14, 14));
        _prim.StrokeRect(_sb, plate, 1, Mono.Dim);
        _font.Draw(_sb, _avatar.Name.ToUpperInvariant() + $" · LVL {_you.Level}", plate.X + 12, plate.Y + 6, 1, Mono.Ink);
        if (_autoPlay)
            _font.Draw(_sb, "AUTO", plate.Right - 44, plate.Y + 6, 1, Gold);

        // The health bar IS the centerpiece (the owner's law: it was missing).
        var hpBar = new Rectangle(plate.X + 12, plate.Y + 22, plate.Width - 24, 16);
        float hpf = Math.Clamp(_avatar.MaxHp <= 0 ? 0f : (float)Math.Max(0, _avatar.Hp) / _avatar.MaxHp, 0f, 1f);
        _prim.FillRect(_sb, hpBar, Mono.Faint * 0.6f);
        _prim.FillRect(_sb, new Rectangle(hpBar.X, hpBar.Y, (int)(hpBar.Width * hpf), hpBar.Height),
            hpf <= 0.25f ? Mono.Danger : Mono.Hp);
        _prim.StrokeRect(_sb, hpBar, 1, Mono.Dim);
        _font.DrawCentered(_sb, $"{_avatar.Hp} / {_avatar.MaxHp}", hpBar.Center.X, hpBar.Y + 4, 1, Mono.Ink);

        // AP blue, MP green — the Dofus vitals law, centred under the heart.
        _font.Draw(_sb, "AP", plate.X + 12, plate.Y + 50, 1, Mono.Ap);
        for (int i = 0; i < Math.Min(12, _avatar.BaseAp); i++)
            _prim.DiscAt(_sb, new Vector2(plate.X + 38 + i * 15, plate.Y + 55), 6,
                i < _avatar.CurrentAp ? Mono.Ap : Mono.Faint);
        int mpX = plate.X + plate.Width / 2 + 28;
        _font.Draw(_sb, "MP", mpX, plate.Y + 50, 1, Mono.Mp);
        for (int i = 0; i < Math.Min(8, _avatar.BaseMp); i++)
            _prim.DiscAt(_sb, new Vector2(mpX + 26 + i * 15, plate.Y + 55), 6,
                i < _avatar.CurrentMp ? Mono.Mp : Mono.Faint);

        // END TURN, dead centre beneath the plate — where the thumb lives in Dofus.
        bool hov = EndTurnRect.Contains(MP);
        if (pilots)
        {
            Mono.Button(_sb, _prim, EndTurnRect, hover: hov);
            _font.DrawCentered(_sb, "END TURN (ENTER)", EndTurnRect.Center.X, EndTurnRect.Y + 9, 1, Mono.ButtonInk(hov));
        }
        else
        {
            _prim.StrokeRect(_sb, EndTurnRect, 1, Mono.Faint);
            _font.DrawCentered(_sb, myTurn ? "AUTO — SPACE RESUMES" : "THE DEAD MOVE...",
                EndTurnRect.Center.X, EndTurnRect.Y + 9, 1, Mono.Faint);
        }

        // RIGHT: the wells — every word you know, the weapon among them, Dofus-style.
        var spells = _avatar.Spells;
        int hoveredWell = -1;
        for (int i = 0; i < spells.Count && i < 8; i++)
        {
            var r = WellRect(i);
            var sp = spells[i];
            bool used = !_avatar.HasCastsLeft(sp) || _avatar.IsOnCooldown(sp, _engine.Round);
            bool canPay = !used && sp.ApCost <= _avatar.CurrentAp;
            if (r.Contains(MP)) hoveredWell = i;
            Mono.Slot(_sb, _prim, r, hover: r.Contains(MP), selected: _selected == i);
            string? key = TitheContent.SkillKeyById(sp.Id);
            var tint = canPay || !myTurn ? SpellInk(sp) : Mono.Faint;
            if (key == null || !DrawIcon("icon_spell_" + key, r, tint))
            {
                if (sp.Id >= 950) { if (!DrawIcon("icon_slot_weapon", r, tint)) _font.DrawCentered(_sb, "W", r.Center.X, r.Y + 16, 2, tint); }
                else _font.DrawCentered(_sb, sp.Name[..1], r.Center.X, r.Y + 16, 2, tint);
            }
            // Spent reads as spent, not as unaffordable: USED is quiet, a short purse is loud.
            _font.DrawCentered(_sb, used && myTurn ? "USED" : sp.ApCost == 0 ? "FREE" : $"{sp.ApCost} AP",
                r.Center.X, r.Bottom + 3, 1, used && myTurn ? Mono.Faint : canPay || !myTurn ? Mono.Dim : Mono.Danger);
            _font.DrawCentered(_sb, (i + 1).ToString(), r.X + 6, r.Y + 2, 1, Mono.Faint);
        }
        if (hoveredWell >= 0) DrawSpellTooltip(spells[hoveredWell], WellRect(hoveredWell));

        // The whisper of controls, one line, out of the way.
        _font.DrawCentered(_sb,
            pilots ? "1-8 ARM · CLICK CAST/MOVE · CLICK AWAY CANCELS · SPACE AUTO · ENTER ENDS"
            : "HOVER WELLS FOR RANGE · CLICK AN ENEMY TO INSPECT · SPACE AUTO",
            W / 2, H - 13, 1, Mono.Faint * 0.9f);
        // A drawn weapon stays drawn (a failed cast keeps its arm) — say so out loud.
        if (_selected >= 0 && _selected < spells.Count)
            _font.DrawCentered(_sb, $"ARMED: {spells[_selected].Name.ToUpperInvariant()} · ESC OR CLICK AWAY CLEARS",
                W / 2, BandTop - 14, 1, Mono.Cast);
    }

    /// <summary>The enemy's heaviest single blow, estimated against YOUR hide.</summary>
    private (int lo, int hi)? BiggestBlow(Fighter foe)
    {
        (int lo, int hi)? best = null;
        foreach (var sp in foe.Spells)
            if (_engine.EstimateDamage(foe, sp, _avatar.Pos) is { } est
                && (best == null || est.max > best.Value.hi))
                best = (est.min, est.max);
        return best;
    }

    /// <summary>The clicked enemy under glass: its words, their prices, their reach —
    /// and what each would take out of you. Dofus taught us: knowing is half the fight.</summary>
    private void DrawInspector(Fighter foe)
    {
        var lines = new List<(string t, Color c)>
        {
            ($"{foe.Name.ToUpperInvariant()}{(foe.Level >= 3 ? " · CHAMPION" : foe.Level == 2 ? " · GRADE 2" : "")}", Mono.Danger),
            ($"{foe.Hp}/{foe.MaxHp} HP · {foe.BaseAp} AP · {foe.BaseMp} MP", Mono.Dim),
            ("", Mono.Dim),
        };
        foreach (var sp in foe.Spells.Take(4))
        {
            lines.Add((sp.Name.ToUpperInvariant(), Mono.Ink));
            lines.Add(($"  {(sp.ApCost == 0 ? "FREE" : $"{sp.ApCost} AP")} · RANGE {sp.MinRange}-{sp.MaxRange}"
                + (sp.Cooldown > 0 ? $" · REST {sp.Cooldown}" : ""), Mono.Dim));
            string fx = GauntletText.EffectsLine(sp);
            if (fx.Length > 0) lines.Add(($"  {fx}", Mono.Dim));
            if (_engine.EstimateDamage(foe, sp, _avatar.Pos) is { } est)
                lines.Add(($"  VS YOU: {est.min}-{est.max}", Mono.Danger));
        }
        lines.Add(("", Mono.Dim));
        lines.Add(("its reach burns red on the ground", Mono.Faint));

        int wdt = Math.Max(230, lines.Max(l => _font.Measure(l.t, 1)) + 24);
        int hgt = lines.Count * 13 + 16;
        var r = new Rectangle(W - wdt - 12, Math.Max(150, BandTop - hgt - 10), wdt, hgt);
        Mono.Frame(_sb, _prim, r, emphasis: true);
        int y = r.Y + 8;
        foreach (var (t, c) in lines) { _font.Draw(_sb, t, r.X + 12, y, 1, c); y += 13; }
    }

    private void DrawPick()
    {
        DrawRoadStrip(30);
        _font.DrawCentered(_sb, _pickTitle, W / 2, 120, 4, _pickInk);
        _font.DrawCentered(_sb, _pickSub, W / 2, 168, 1, Mono.Dim);
        string next = _st.Room + 1 < _st.Road.Count ? RunRules.RoomWord(_st.Road[_st.Room + 1]) : "THE SEXTON";
        _font.DrawCentered(_sb, $"{_st.RunStones} st carried  ·  {_st.Tolls} tolls on the bell  ·  ahead: {next}",
            W / 2, 190, 1, Mono.Faint);
        for (int i = 0; i < _cards.Count; i++)
        {
            var r0 = CardRect(i);
            bool hov = r0.Contains(MP);
            var card = _cards[i];
            bool payable = !_shopping || card.Price <= _st.RunStones;
            // Hovered cards lift out of the dirt (Loop Hero's hand feel).
            var r = hov && payable ? new Rectangle(r0.X, r0.Y - 6, r0.Width, r0.Height) : r0;
            Mono.Frame(_sb, _prim, r, emphasis: hov && payable);
            var kindInk = card.Kind switch
            { "RUIN" => Gold, "ESSENCE" or "MYSTERY" => Mono.Cast, "SHOP" => Gold, "GEAR" => Mono.Ink, _ => Mono.Dim };
            if (!payable) kindInk = Mono.Faint;
            _prim.FillRect(_sb, new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, 3), kindInk);
            _font.DrawCentered(_sb, card.Kind == "RUIN" ? "WORD OF RUIN" : card.Kind == "SHOP" ? "FOR SALE" : card.Kind,
                r.Center.X, r.Y + 14, 1, kindInk);
            _font.DrawCentered(_sb, card.Title, r.Center.X, r.Y + 36, 1,
                !payable ? Mono.Faint : card.Kind is "ESSENCE" or "MYSTERY" ? Mono.Cast : Mono.Ink);
            int ly = r.Y + 78;
            foreach (var line in card.Body.Split('\n'))
            {
                var ink = !payable ? Mono.Faint
                    : line.Contains(" OF 3") || line == "forever" ? Mono.Cast
                    : line.Contains("YOUR element") ? Mono.Heal
                    : line.Contains("not your element") ? Mono.Danger
                    : Mono.Dim;
                _font.DrawCentered(_sb, line, r.Center.X, ly, 1, ink); ly += 16;
            }
            var take = new Rectangle(r.Center.X - 46, r.Bottom - 38, 92, 24);
            Mono.Button(_sb, _prim, take, hover: hov && payable, disabled: !payable);
            _font.DrawCentered(_sb,
                _shopping ? $"{card.Price} ST ({i + 1})" : $"TAKE ({i + 1})",
                take.Center.X, take.Y + 8, 1, Mono.ButtonInk(hov && payable, disabled: !payable));
        }
        if (_shopping)
        {
            bool hov = LeaveRect.Contains(MP);
            Mono.Button(_sb, _prim, LeaveRect, hover: hov);
            _font.DrawCentered(_sb, "LEAVE (ENTER)", LeaveRect.Center.X, LeaveRect.Y + 10, 1, Mono.ButtonInk(hov));
        }
    }

    /// <summary>The whole road in one glance, Loop Hero style: every room a mark, HIM at
    /// the end in the danger color. You can plan against it; you don't get to redraw it.</summary>
    private void DrawRoadStrip(int y)
    {
        if (_st.Road.Count == 0) return;
        int step = 26;
        int x0 = W / 2 - (_st.Road.Count - 1) * step / 2;
        for (int i = 0; i < _st.Road.Count; i++)
        {
            var p = new Vector2(x0 + i * step, y);
            if (i > 0) _prim.Line(_sb, p - new Vector2(step - 7, 0), p - new Vector2(7, 0), 1f, Mono.Faint);
            bool here = i == Math.Min(_st.Room, _st.Road.Count - 1);
            bool past = i < _st.Room;
            var kind = _st.Road[i];
            var ink = kind == RoomKind.Boss ? Mono.Danger : past ? Mono.Faint : here ? Mono.Ink : Mono.Dim;
            switch (kind)
            {
                case RoomKind.Boss:
                    _prim.DiscAt(_sb, p, here ? 6f + MathF.Sin(_time * 4f) : 5, ink * (past ? 0.5f : 1f));
                    break;
                case RoomKind.Shop:
                    _font.DrawCentered(_sb, "$", (int)p.X, (int)p.Y - 3, 1, ink);
                    break;
                case RoomKind.Event:
                    _font.DrawCentered(_sb, "E", (int)p.X, (int)p.Y - 3, 1, ink);
                    break;
                case RoomKind.Mystery:
                    _font.DrawCentered(_sb, "?", (int)p.X, (int)p.Y - 3, 1, here ? Mono.Cast : ink);
                    break;
                default:
                    _prim.DiscAt(_sb, p, here ? 4f + MathF.Sin(_time * 4f) * 0.8f : past ? 2 : 3, ink);
                    break;
            }
            if (here) DiamondOutlineSmall(p);
        }
    }

    private void DiamondOutlineSmall(Vector2 p)
    {
        _prim.Line(_sb, p + new Vector2(0, -9), p + new Vector2(9, 0), 1f, Mono.Ink * 0.7f);
        _prim.Line(_sb, p + new Vector2(9, 0), p + new Vector2(0, 9), 1f, Mono.Ink * 0.7f);
        _prim.Line(_sb, p + new Vector2(0, 9), p + new Vector2(-9, 0), 1f, Mono.Ink * 0.7f);
        _prim.Line(_sb, p + new Vector2(-9, 0), p + new Vector2(0, -9), 1f, Mono.Ink * 0.7f);
    }

    private void DrawEnd()
    {
        _font.DrawCentered(_sb, _runWon ? "THE SEXTON FALLS" : "THE GROUND TAKES YOU", W / 2, 240, 5,
            _runWon ? Mono.Ink : Mono.Danger);
        _font.DrawCentered(_sb, _runWon
                ? $"the bell is yours. +{_st.RunStones} stones banked."
                : $"half your stones sink with you. +{_st.RunStones / 2} banked.",
            W / 2, 320, 2, Mono.Dim);
        if (!_runWon) _font.DrawCentered(_sb, "a new leader answers the bell.", W / 2, 352, 1, Mono.Faint);
        // The ledger, read aloud over the grave.
        _font.DrawCentered(_sb,
            $"the earth fed: {_st.Kills}   ·   taken by the void: {_st.Falls}   ·   level {_you.Level}",
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
        ["cairn_brute"] = "brute",
    };

    /// <summary>The size law (frame height drawn, figure height inside it). The animated
    /// sheets keep attack-swing headroom above the body (figure fills ~2/3 of the frame);
    /// the single-frame mobs are near-tight. One table so a mite reads SMALL, a husk
    /// man-sized, a champion a head taller, and the Sexton towers — instead of every
    /// body rendering at whatever its file's padding happened to be.</summary>
    private static (float frame, float fig) SizeOf(string archetype) => archetype switch
    {
        "grave_mite" => (34, 30),
        "gravehound" => (42, 39),
        "marrow_spitter" => (46, 35),
        "tomb_wraith" => (46, 40),
        "bone_piper" => (48, 45),
        "barrow_husk" => (50, 47),
        "grave_ghoul" => (70, 47),
        "crypt_warden" => (72, 48),
        "cairn_brute" => (74, 49),
        "sexton" => (96, 64),
        _ => (66, 44),   // the heroes
    };

    private void DrawSprite(Fighter f, Vector2 cellCenter, Color tint, float h, string state)
    {
        var name = SpriteOf.GetValueOrDefault(f.Archetype, "husk");
        // Facing: the walk sets it stride by stride; at rest the engine's Facing rules.
        bool flip = _visFlip.TryGetValue(f.Id, out bool vf) ? vf : f.Facing.X - f.Facing.Y < 0;
        var sheet = _sprites.GetSheet(name, state, flip ? "sw" : "se")
                    ?? _sprites.GetSheet(name, "idle", flip ? "sw" : "se");
        var feet = cellCenter + new Vector2(0, TH / 4f + 2);
        if (sheet != null)
        {
            int frame = state switch
            {
                "walk" => (int)(_time * 9) % sheet.FrameCount,
                "attack" => Math.Clamp((int)((1f - Math.Max(0f,
                    _attackUntil.GetValueOrDefault(f.Id) - _time) / 0.34f) * sheet.FrameCount),
                    0, sheet.FrameCount - 1),
                "die" => sheet.FrameCount - 1,
                _ => (int)(_time * 3.2f + f.Id.Length * 1.7f) % sheet.FrameCount,
            };
            SpriteDraw.Feet(_sb, sheet, feet, tint, h, frame);
        }
        else
        {
            _prim.DiscAt(_sb, cellCenter, 14, tint * 0.85f);
            _font.DrawCentered(_sb, f.Archetype[..1].ToUpperInvariant(), (int)cellCenter.X, (int)cellCenter.Y - 4, 1, Mono.Bg);
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
            ((sp.ApCost == 0 ? "FREE" : $"{sp.ApCost} AP")
             + (sp.MaxCastsPerTurn == 1 ? " · ONCE A TURN" : sp.MaxCastsPerTurn == 2 ? " · TWICE A TURN" : "")
             + (sp.Cooldown > 0 ? $" · REST {sp.Cooldown}" : ""), Mono.Ap),
            ($"RANGE {sp.MinRange}-{sp.MaxRange}"
             + (sp.LineOnly ? " · IN A LINE" : "")
             + (sp.RequiresLineOfSight ? "" : " · NEEDS NO SIGHT"), Mono.Dim),
        };
        string fx = GauntletText.EffectsLine(sp);
        if (fx.Length > 0) lines.Add((fx, Mono.Ink));
        foreach (var w in Wrap(sp.Description, 36)) lines.Add((w.ToLowerInvariant(), Mono.Dim));

        int wdt = Math.Max(150, lines.Max(l => _font.Measure(l.t, 1)) + 20);
        int hgt = lines.Count * 14 + 14;
        var r = new Rectangle(Math.Min(well.X, W - wdt - 8), well.Y - hgt - 8, wdt, hgt);
        Mono.Frame(_sb, _prim, r, emphasis: true);
        int y = r.Y + 8;
        foreach (var (t, c) in lines) { _font.Draw(_sb, t, r.X + 10, y, 1, c); y += 14; }
    }

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

using DofusSlice.Core.Combat;
using DofusSlice.Core.Grid;
using DofusSlice.Core.Spells;
using DofusSlice.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DofusSlice.Game.Animation;

/// <summary>
/// Presentation-only layer that turns <see cref="CombatEvent"/>s into timed animations:
/// tokens slide along their path, casters lunge, hits flash with floating damage numbers,
/// and the fallen fade out. Blocking animations play one at a time from a queue so the fight
/// reads as a sequence; while any are playing the game reports <see cref="IsBusy"/> and holds
/// input and turn flow. The engine's model is already final — this just replays how it got there.
/// </summary>
public sealed class BattleAnimator
{
    private readonly IsoProjector _proj;
    private readonly Queue<IAnim> _queue = new();
    private readonly List<Overlay> _overlays = new();
    private readonly List<Corpse> _corpses = new();
    private readonly Dictionary<string, float> _displayHp = new();
    // The REPLAY health: the bar eases toward this, and it only moves when a blow's HitAnim
    // actually lands (NudgeDisplayHp) — never toward the engine's already-final HP. So a ranged
    // hit drains the bar when the shot arrives, not the instant the engine resolved it.
    private readonly Dictionary<string, float> _hpTarget = new();
    private readonly Dictionary<string, Vector2> _displayPos = new();
    private readonly List<(CellCoord cell, Color color)> _telegraphCells = new();
    private readonly HashSet<string> _pendingDeaths = new();

    /// <summary>Cells the current telegraph wants highlighted this frame (movement green,
    /// spell-range blue, target red) — the game draws them under the units, 1.29-style.</summary>
    public IReadOnlyList<(CellCoord cell, Color color)> TelegraphCells => _telegraphCells;
    internal void AddTelegraphCell(CellCoord c, Color col) => _telegraphCells.Add((c, col));
    internal CellCoord DisplayCellOf(string id) =>
        _displayPos.TryGetValue(id, out var v) ? _proj.ScreenToCell(v) : default;
    internal IsoProjector Projector => _proj;
    private readonly Dictionary<string, float> _flash = new();
    private readonly Dictionary<string, Pose> _poses = new();
    private readonly Dictionary<string, Facing4> _facing = new();

    private const float FlashDuration = 0.3f;

    public BattleAnimator(IsoProjector proj) => _proj = proj;

    /// <summary>Sound hook: anims call this at their visual beats (hit lands, cast starts…).
    /// The game wires it to the SoundBank; null means silence, never a crash.</summary>
    public Action<string, float>? Sfx { get; set; }

    /// <summary>Optional resolver so corpses use the same sheet, pixel height AND tint the
    /// renderer drew the fighter with (tithe mode maps archetypes onto tinted shared sheets —
    /// an untinted corpse reads as a different creature). Null = defaults.</summary>
    public Func<Fighter, (string sprite, float heightPx, Color tint)>? CorpseSpriteOf { get; set; }

    /// <summary>True while a blocking animation or death fade is still playing. Use this to hold
    /// the END of a fight (the outcome/loot screens must wait for the last blow to land).</summary>
    public bool IsBusy => _queue.Count > 0 || _corpses.Any(c => !c.Done);

    /// <summary>
    /// True while an animation the player must not act through is playing — the queue ONLY.
    /// Deliberately excludes corpse hold+fade: a corpse is decoration, and gating input on it
    /// meant killing something bought the longest input lockout in the game (~2s) as the reward
    /// for the best thing you can do. Turn flow and input gate on this; outcome screens on IsBusy.
    /// </summary>
    public bool BlocksInput => _queue.Count > 0;

    /// <summary>Is this fighter the one the PLAYER is piloting right now? Intent telegraphs exist
    /// to explain an AI's plan; replaying them for your own clicks just adds latency to your turn.
    /// Null (unwired) means "treat everyone as AI" — the old behavior.</summary>
    public Func<string, bool>? IsPiloted { get; set; }

    private bool Piloted(string id) => IsPiloted?.Invoke(id) ?? false;

    /// <summary>Does this status help its bearer? Decides whether the cue reads as relief or harm.</summary>
    private static bool Beneficial(StatusKind k) => k is StatusKind.Shield or StatusKind.Regen
        or StatusKind.DamageBuff or StatusKind.DefenseBuff or StatusKind.RangeBuff or StatusKind.Stabilized;

    /// <summary>One short word per status for the float — the pip letter is too terse to teach.</summary>
    private static string StatusWord(StatusKind k) => k switch
    {
        StatusKind.Poison => "POISON",
        StatusKind.Regen => "REGEN",
        StatusKind.Shield => "SHIELD",
        StatusKind.DefenseBuff => "ARMOR",
        StatusKind.Vulnerable => "EXPOSED",
        StatusKind.DamageBuff => "EMPOWERED",
        StatusKind.DamageDebuff => "WEAKENED",
        StatusKind.RangeBuff => "REACH+",
        StatusKind.RangeDebuff => "LEASHED",
        StatusKind.MpDrain => "SLOWED",
        StatusKind.ApDrain => "DRAINED",
        StatusKind.Rooted => "ROOTED",
        StatusKind.Stabilized => "ANCHORED",
        StatusKind.Reflect => "REFLECT",
        _ => k.ToString().ToUpperInvariant(),
    };

    private static Color StatusInk(StatusKind k) => k switch
    {
        StatusKind.Poison => new Color(120, 200, 90),
        StatusKind.Shield or StatusKind.DefenseBuff => DofusSlice.Game.Rendering.Mono.Ap,
        StatusKind.Regen => DofusSlice.Game.Rendering.Mono.Heal,
        StatusKind.DamageBuff => new Color(232, 116, 60),
        StatusKind.Vulnerable or StatusKind.RangeDebuff => DofusSlice.Game.Rendering.Mono.Danger,
        StatusKind.MpDrain or StatusKind.ApDrain => new Color(170, 120, 210),
        _ => DofusSlice.Game.Rendering.Mono.Ink,
    };

    public void Reset(IEnumerable<Fighter> fighters)
    {
        _queue.Clear();
        _overlays.Clear();
        _corpses.Clear();
        _displayHp.Clear();
        _hpTarget.Clear();
        _flash.Clear();
        _poses.Clear();
        _facing.Clear();
        _pendingShake = 0f;
        _freeze = 0f;
        _lastCasterId = "";
        _displayPos.Clear();
        _pendingDeaths.Clear();
        foreach (var f in fighters)
        {
            _displayHp[f.Id] = f.Hp;
            _hpTarget[f.Id] = f.Hp;
            _displayPos[f.Id] = _proj.CellCenter(f.Pos);
        }
    }

    public void OnEvent(CombatEvent e)
    {
        switch (e)
        {
            case FighterMoved m:
                // You chose this path and watched the preview while choosing it — don't re-shade it.
                if (!Piloted(m.Fighter.Id))
                    _queue.Enqueue(new PathTelegraph(this, m.Path.ToArray(), 0.4f));
                _queue.Enqueue(new MoveAnim(m.Fighter.Id, ToPoints(m.Path), 0.15f, this));
                if (m.MpSpent > 0)   // the walk's price floats over the walker (UX pass)
                    _queue.Enqueue(new CostFloat(this, m.Fighter.Id, $"-{m.MpSpent} MP",
                        DofusSlice.Game.Rendering.Mono.MpInk));
                break;
            case FighterPushed p:
                _queue.Enqueue(new MoveAnim(p.Fighter.Id, ToPoints(p.Path), 0.07f, this, "zip"));
                break;
            case FighterTeleported t:
                _queue.Enqueue(new TeleportAnim(t.Fighter.Id, _proj.CellCenter(t.From), _proj.CellCenter(t.To), this));
                Sfx?.Invoke("zip", 0.7f);
                break;
            case SpellCast c:
                _lastCasterId = c.Caster.Id;   // so the struck fighter recoils away from this blow
                // A telegraph announces an AI's intent. For your own cast it is pure latency —
                // keep a short beat so the blow still reads as caused, then get out of the way.
                _queue.Enqueue(new CastTelegraph(this, c.Caster.Id, c.Spell, c.Target,
                    Piloted(c.Caster.Id) ? 0.12f : 0.85f,
                    // The caster's LIVE reach — range buffs/theft move it, and shading the
                    // authored range would paint cells the engine would refuse.
                    Math.Max(c.Spell.MinRange, c.Spell.MaxRange + c.Caster.RangeBonus)));
                if (c.Spell.ApCost > 0)   // the cast's price floats over the caster (UX pass)
                    _queue.Enqueue(new CostFloat(this, c.Caster.Id, $"-{c.Spell.ApCost} AP",
                        DofusSlice.Game.Rendering.Mono.ApInk));
                _queue.Enqueue(new CastAnim(c.Caster.Id, _proj.CellCenter(c.Target), c.Spell, this));
                // Anything cast from range visibly TRAVELS: an arrow-streak flies caster→target
                // before the hit lands (melee-range casts skip it — nothing to see).
                _queue.Enqueue(new ProjectileAnim(this, c.Caster.Id, c.Target, SpellColor(c.Spell)));
                break;
            case DamageDealt d:
                _queue.Enqueue(new HitAnim(d.Target.Id, _proj.CellCenter(d.At), d.Amount, this, d.Critical, d.Element,
                    RecoilDir(_proj.CellCenter(d.At))));
                break;
            case HealApplied h:
                _queue.Enqueue(new HitAnim(h.Target.Id, _proj.CellCenter(h.At), -h.Amount, this));
                break;
            case FighterDied dd:
                // Death replays IN SEQUENCE: the unit stays on the board (StillShown) until
                // the killing blow's animations have played, then the corpse takes over.
                _pendingDeaths.Add(dd.Fighter.Id);
                var (corpseSprite, corpseH, corpseTint) = CorpseSpriteOf?.Invoke(dd.Fighter)
                    ?? (SpriteName(dd.Fighter), 64f, Color.White);
                _queue.Enqueue(new DeathAnim(this, dd.Fighter.Id, _proj.CellCenter(dd.At),
                    ColorOf(dd.Fighter), corpseSprite, corpseH, corpseTint));
                break;
            case FighterSummoned s:
                _displayHp[s.Fighter.Id] = s.Fighter.Hp;   // seed the newcomer so its HP bar shows
                _hpTarget[s.Fighter.Id] = s.Fighter.Hp;
                _displayPos[s.Fighter.Id] = _proj.CellCenter(s.Fighter.Pos);
                _facing[s.Fighter.Id] = Facing4.Se;
                _overlays.Add(new ImpactFlash(_proj.CellCenter(s.Fighter.Pos) + new Vector2(0, -16),
                    DofusSlice.Game.Rendering.Mono.On
                        ? DofusSlice.Game.Rendering.Mono.Ink : new Color(150, 220, 180)));
                Sfx?.Invoke("summon", 0.7f);
                break;
            // A status landing or falling off was completely silent — roughly five times a fight
            // the rules changed under the player with no cue at all. Flash it in the status's own
            // colour and name it, so a poison or a shield reads the moment it happens.
            case StatusApplied sa:
                _overlays.Add(new ImpactFlash(_displayPos.TryGetValue(sa.Target.Id, out var sp)
                    ? sp + new Vector2(0, -16) : _proj.CellCenter(sa.Target.Pos), StatusInk(sa.Kind)));
                _overlays.Add(new FloatingText(
                    (_displayPos.TryGetValue(sa.Target.Id, out var sp2) ? sp2 : _proj.CellCenter(sa.Target.Pos))
                        + new Vector2(0, -40), StatusWord(sa.Kind), StatusInk(sa.Kind), 1));
                Sfx?.Invoke(Beneficial(sa.Kind) ? "chime" : "crush", 0.4f);
                break;
            case StatusExpired se:
                _overlays.Add(new FloatingText(
                    (_displayPos.TryGetValue(se.Target.Id, out var ep) ? ep : _proj.CellCenter(se.Target.Pos))
                        + new Vector2(0, -40), StatusWord(se.Kind) + " ENDS",
                    DofusSlice.Game.Rendering.Mono.Faint, 1));
                break;
            case TurnStarted ts:
                // The avatar's banner says what matters: it is YOUR turn (UX pass).
                bool yours = ts.Fighter is { PlayerControlled: true, IsMercenary: false, IsSummon: false };
                _queue.Enqueue(new TurnTelegraph(this, ts.Fighter.Id,
                    yours ? "YOUR TURN" : ts.Fighter.Name,
                    ts.Fighter.Team == Team.Player, yours ? 0.9f : 0.55f));
                break;
        }
    }

    public void Update(float dt, IReadOnlyList<Fighter> fighters)
    {
        if (_freeze > 0f) { _freeze -= dt; return; }   // hit-stop: hold the impact frame
        _telegraphCells.Clear();
        if (_queue.Count > 0)
        {
            var head = _queue.Peek();
            head.Update(dt);
            if (head.Done)
            {
                _queue.Dequeue();
                if (head.EndPos is { } end) _displayPos[end.id] = end.pos;
            }
        }
        else
        {
            // Nothing replaying: the ledger tracks the engine truth (placement drags, spawns).
            foreach (var f in fighters) _displayPos[f.Id] = _proj.CellCenter(f.Pos);
        }

        for (int i = _overlays.Count - 1; i >= 0; i--)
        {
            _overlays[i].Update(dt);
            if (_overlays[i].Done) _overlays.RemoveAt(i);
        }
        for (int i = _corpses.Count - 1; i >= 0; i--)
        {
            _corpses[i].Update(dt);
            if (_corpses[i].Done) _corpses.RemoveAt(i);
        }

        foreach (var id in _flash.Keys.ToList())
            _flash[id] = Math.Max(0f, _flash[id] - dt);

        // Ease each displayed HP toward its REPLAY target (set only when a blow lands), not toward
        // the engine's already-final HP — so a bar never drops before the hit is seen.
        foreach (var f in fighters)
        {
            float target = Math.Clamp(_hpTarget.TryGetValue(f.Id, out var tv) ? tv : f.Hp, 0f, f.MaxHp);
            _hpTarget[f.Id] = target;
            float cur = _displayHp.TryGetValue(f.Id, out var v) ? v : target;
            _displayHp[f.Id] = cur + (target - cur) * Math.Min(1f, dt * 12f);
        }

        UpdatePoses(dt, fighters);
    }

    /// <summary>
    /// Derive each live fighter's animation pose (state + facing) from what is currently
    /// playing, and advance a per-state clock the renderer turns into a frame index.
    /// </summary>
    private void UpdatePoses(float dt, IReadOnlyList<Fighter> fighters)
    {
        IAnim? head = _queue.Count > 0 ? _queue.Peek() : null;
        foreach (var f in fighters)
        {
            AnimState state;
            Facing4 dir = _facing.TryGetValue(f.Id, out var lf) ? lf : Facing4.Se;

            if (head?.ActorId == f.Id)
            {
                state = head.ActorState;
                dir = head.ActorFacing;
                _facing[f.Id] = dir;
            }
            else if (FlashAmount(f.Id) > 0.15f)
            {
                state = AnimState.Hurt;
            }
            else
            {
                state = AnimState.Idle;
            }

            var prev = _poses.TryGetValue(f.Id, out var p) ? p : new Pose(AnimState.Idle, dir, 0f);
            float clock = prev.State == state ? prev.Clock + dt : 0f;
            _poses[f.Id] = new Pose(state, dir, clock);
        }
    }

    public Pose PoseFor(Fighter f) =>
        _poses.TryGetValue(f.Id, out var p) ? p : new Pose(AnimState.Idle, Facing4.Se, 0f);

    public Facing4 LastFacing(string id) => _facing.TryGetValue(id, out var d) ? d : Facing4.Se;

    /// <summary>True while a fighter the engine already killed is still awaiting its death
    /// replay — the renderer keeps drawing it so nobody vanishes mid-exchange.</summary>
    public bool StillShown(string id) => _pendingDeaths.Contains(id);
    internal void ReleaseDeath(string id) => _pendingDeaths.Remove(id);
    internal void SpawnCorpse(Corpse c) => _corpses.Add(c);

    // ----- Queries used by the renderer ---------------------------------------------

    /// <summary>Tile-centre a fighter should render at (animated override or its logical cell).</summary>
    public Vector2 CenterFor(Fighter f)
    {
        if (_queue.Count > 0 && _queue.Peek().TryCenter(f.Id, out var c)) return c;
        if (_displayPos.TryGetValue(f.Id, out var p)) return p;   // replay truth, not engine truth
        return _proj.CellCenter(f.Pos);
    }

    public float DisplayHp(Fighter f) => _displayHp.TryGetValue(f.Id, out var v) ? v : f.Hp;

    public float FlashAmount(string id) =>
        _flash.TryGetValue(id, out var v) ? Math.Clamp(v / FlashDuration, 0f, 1f) : 0f;

    public void DrawEffects(SpriteBatch sb, Primitives prim, PixelFont font, SpriteBank sprites)
    {
        foreach (var c in _corpses) c.Draw(sb, prim, sprites);
        foreach (var o in _overlays) o.Draw(sb, prim, font);
    }

    // ----- Internals ----------------------------------------------------------------

    internal void AddOverlay(Overlay o) => _overlays.Add(o);
    internal void SetFlash(string id, float seconds) => _flash[id] = seconds;

    /// <summary>Move a fighter's REPLAY health when its blow lands (+amount = damage, -amount =
    /// heal). The bar eases to this, so it drains with the visible hit, not the engine's resolve.</summary>
    internal void NudgeDisplayHp(string id, int amount)
        => _hpTarget[id] = (_hpTarget.TryGetValue(id, out var t) ? t : 0f) - amount;

    private float _pendingShake;
    internal void RequestShake(float amp) => _pendingShake = Math.Max(_pendingShake, amp);
    /// <summary>Returns and clears the shake requested since the last call (synced to hits).</summary>
    public float ConsumeShake() { var s = _pendingShake; _pendingShake = 0f; return s; }

    // Hit-stop: a blow lands, and for a few frames the whole replay HOLDS on the impact — the
    // single missing juice primitive (map). Requested at the strike/kill beat; Update returns
    // early while it burns, freezing the queue, overlays, flash and poses on that frame. Runs on
    // the speed-scaled clock, so 2x/4x fast-forward shrinks it like every other beat.
    private float _freeze;
    internal void RequestFreeze(float seconds) => _freeze = Math.Max(_freeze, seconds);

    // The blow's origin, remembered from the last cast, so the struck fighter recoils AWAY from
    // it (hazards/reflects with no known source flinch straight up instead).
    private string _lastCasterId = "";
    private Vector2 RecoilDir(Vector2 victimAt)
    {
        if (_displayPos.TryGetValue(_lastCasterId, out var src) && src != victimAt)
        {
            var d = victimAt - src;
            if (d != Vector2.Zero) { d.Normalize(); return d; }
        }
        return new Vector2(0f, -1f);
    }

    private Vector2[] ToPoints(IReadOnlyList<CellCoord> path)
    {
        var pts = new Vector2[path.Count];
        for (int i = 0; i < path.Count; i++) pts[i] = _proj.CellCenter(path[i]);
        return pts;
    }

    private static Color ColorOf(Fighter f) =>
        f.PlayerControlled ? Palette.HeroColor : Palette.CreatureColor(f.Name);

    /// <summary>Sprite base name for a fighter (matches the renderer's lookup).</summary>
    internal static string SpriteName(Fighter f) =>
        f.PlayerControlled ? "iop" : f.Name.ToLowerInvariant();

    internal static Color SpellColor(SpellDef spell)
    {
        if (DofusSlice.Game.Rendering.Mono.On) return DofusSlice.Game.Rendering.Mono.Ink;
        if (spell.Effects.Any(e => e.Kind == EffectKind.Teleport)) return new Color(150, 210, 240);
        var dmg = spell.Effects.FirstOrDefault(e => e.Kind == EffectKind.Damage);
        return dmg != null
            ? DofusSlice.Game.Rendering.EwChrome.ElementColor(dmg.Element)
            : new Color(230, 220, 180);
    }
}

// ---- Animation pose model -----------------------------------------------------------

/// <summary>Which animation cycle a fighter is currently in. Die is handled by the corpse.</summary>
public enum AnimState { Idle, Walk, Cast, Hurt, Die }

/// <summary>The four isometric facings (matching the four grid-movement directions).</summary>
public enum Facing4 { Se, Sw, Ne, Nw }

public readonly record struct Pose(AnimState State, Facing4 Dir, float Clock);

public static class Facing
{
    public static string ToKey(this Facing4 d) => d switch
    {
        Facing4.Se => "se",
        Facing4.Sw => "sw",
        Facing4.Ne => "ne",
        _ => "nw",
    };

    /// <summary>Nearest of the four iso facings for a screen-space delta.</summary>
    public static Facing4 FromScreenDelta(Vector2 d)
    {
        bool down = d.Y >= 0f;
        bool right = d.X >= 0f;
        return down ? (right ? Facing4.Se : Facing4.Sw) : (right ? Facing4.Ne : Facing4.Nw);
    }
}

// ---- Blocking animations ------------------------------------------------------------

internal interface IAnim
{
    void Update(float dt);
    bool Done { get; }
    bool TryCenter(string id, out Vector2 center);

    /// <summary>The fighter this animation drives (for pose/facing), or null.</summary>
    string? ActorId => null;
    AnimState ActorState => AnimState.Idle;
    Facing4 ActorFacing => Facing4.Se;

    /// <summary>Where the driven fighter STANDS once this anim completes — the replay-position
    /// ledger applies it on dequeue so nobody ever pre-snaps to the engine's final state.</summary>
    (string id, Vector2 pos)? EndPos => null;
}

/// <summary>Slides a fighter's token along a path, one segment at a time.</summary>
internal sealed class MoveAnim : IAnim
{
    private readonly string _id;
    private readonly Vector2[] _pts;
    private readonly float _perSeg;
    private readonly BattleAnimator? _a;
    private readonly string _sound;
    private float _t;
    private int _stepsPlayed;   // one footfall PER CELL, not one for the whole walk

    public MoveAnim(string id, Vector2[] pts, float perSeg, BattleAnimator? a = null, string sound = "step")
    {
        _id = id;
        _pts = pts.Length > 0 ? pts : new[] { Vector2.Zero };
        _perSeg = perSeg;
        _a = a;
        _sound = sound;
    }

    private float Total => _perSeg * Math.Max(1, _pts.Length - 1);
    public bool Done => _t >= Total;
    public (string id, Vector2 pos)? EndPos => (_id, _pts[^1]);

    public void Update(float dt)
    {
        if (_t == 0f) { _a?.Sfx?.Invoke(_sound, 0.6f); _stepsPlayed = 1; }
        _t += dt;
        // A five-cell walk used to make ONE sound. Step as each cell is actually crossed, so a
        // long march reads as a march. (SoundBank rate-limits a repeated name, so this can't buzz.)
        int crossed = Math.Min(_pts.Length - 1, (int)(_t / Math.Max(0.0001f, _perSeg)) + 1);
        while (_stepsPlayed < crossed) { _stepsPlayed++; _a?.Sfx?.Invoke(_sound, 0.45f); }
    }

    public string? ActorId => _id;
    public AnimState ActorState => AnimState.Walk;
    public Facing4 ActorFacing
    {
        get
        {
            if (_pts.Length < 2) return Facing4.Se;
            float p = Math.Clamp(_t / _perSeg, 0f, _pts.Length - 1);
            int i = Math.Min((int)p, _pts.Length - 2);
            return DofusSlice.Game.Animation.Facing.FromScreenDelta(_pts[i + 1] - _pts[i]);
        }
    }

    public bool TryCenter(string id, out Vector2 center)
    {
        if (id != _id) { center = default; return false; }
        if (_pts.Length == 1) { center = _pts[0]; return true; }
        float p = Math.Clamp(_t / _perSeg, 0f, _pts.Length - 1);
        int i = Math.Min((int)p, _pts.Length - 2);
        center = Vector2.Lerp(_pts[i], _pts[i + 1], p - i);
        return true;
    }
}

/// <summary>A short lunge of the caster toward the target, spawning an impact flash.</summary>
internal sealed class CastAnim : IAnim
{
    private const float Dur = 0.34f;
    private readonly string _id;
    private readonly Vector2 _to;
    private Vector2 _from;
    private readonly Color _impact;
    private readonly BattleAnimator _a;
    private float _t;
    private bool _spawned;

    public CastAnim(string id, Vector2 to, SpellDef spell, BattleAnimator a)
    {
        _id = id; _to = to; _a = a;
        _impact = BattleAnimator.SpellColor(spell);
    }

    public bool Done => _t >= Dur;

    public string? ActorId => _id;
    public AnimState ActorState => AnimState.Cast;
    public Facing4 ActorFacing => Facing.FromScreenDelta(_to - _from);

    public void Update(float dt)
    {
        if (_t == 0f)
        {
            _from = _a.Projector.CellCenter(_a.DisplayCellOf(_id));  // lunge from where it STANDS
            _a.Sfx?.Invoke("cast", 0.6f);
        }
        if (!_spawned && _t >= Dur * 0.4f)
        {
            _spawned = true;
            // Melee-range casts flash at the point of contact; ranged ones leave the impact
            // to the projectile's arrival, so nothing lands before the shot does.
            if ((_to - _from).Length() < 70f)
                _a.AddOverlay(new ImpactFlash(_to + new Vector2(0, -16), _impact));
        }
        _t += dt;
    }

    public bool TryCenter(string id, out Vector2 center)
    {
        if (id != _id) { center = default; return false; }
        float k = MathF.Sin(MathF.Min(1f, _t / Dur) * MathF.PI); // 0 -> 1 -> 0
        var dir = _to - _from;
        if (dir != Vector2.Zero) dir.Normalize();
        center = _from + dir * (k * 14f);
        return true;
    }
}

/// <summary>Flashes the struck fighter and floats a damage/heal number over them.</summary>
internal sealed class HitAnim : IAnim
{
    private const float Dur = 0.3f;
    private const float RecoilDur = 0.16f;
    private readonly string _id;
    private readonly Vector2 _at;
    private readonly int _amount;
    private readonly BattleAnimator _a;
    private readonly bool _crit;
    private readonly Element _element;
    private readonly Vector2 _recoil;
    private float _t;
    private bool _spawned;

    public HitAnim(string id, Vector2 at, int amount, BattleAnimator a, bool crit = false,
        Element element = Element.Neutral, Vector2 recoilDir = default)
    {
        _id = id; _at = at; _amount = amount; _a = a; _crit = crit; _element = element; _recoil = recoilDir;
    }

    public bool Done => _t >= Dur;

    public void Update(float dt)
    {
        if (!_spawned)
        {
            _spawned = true;
            _a.SetFlash(_id, 0.3f);
            _a.NudgeDisplayHp(_id, _amount);   // the bar drains NOW, as the blow lands — not before
            if (_amount > 0)
            {
                _a.RequestShake(Math.Min(12f, 3f + _amount * 0.12f) * (_crit ? 1.5f : 1f));
                _a.RequestFreeze(_crit ? 0.09f : 0.05f);   // the blow lands with weight
            }
            bool heal = _amount < 0;
            _a.Sfx?.Invoke(heal ? "heal" : _crit ? "crit" : "hit_" + _element.ToString().ToLowerInvariant(),
                heal ? 0.7f : 0.85f);
            string text = (heal ? "+" : "-") + Math.Abs(_amount) + (_crit ? "!" : "");
            // 1.29 floats the number in the element's colour; crits go gold, heals green.
            // UX pass (designer's call): function speaks color — heals GREEN (+20),
            // damage RED (-20), crits bigger and red. Art stays 1-bit; numbers don't.
            var color = DofusSlice.Game.Rendering.Mono.On
                ? (heal ? DofusSlice.Game.Rendering.Mono.Heal : DofusSlice.Game.Rendering.Mono.Danger)
                : heal ? new Color(120, 220, 130)
                : _crit ? new Color(255, 210, 90)
                : DofusSlice.Game.Rendering.EwChrome.ElementColor(_element);
            int scale = _crit ? 3 : 2;
            _a.AddOverlay(new FloatingText(_at + new Vector2(0, -30), text, color, scale));
        }
        _t += dt;
    }

    // The struck fighter jerks away from the blow, then settles — a per-hit flinch the global
    // camera shake can't give. Damage only (heals don't knock), and only for the first ~0.16s.
    public bool TryCenter(string id, out Vector2 center)
    {
        if (id == _id && _amount > 0 && _t < RecoilDur)
        {
            float k = 1f - _t / RecoilDur;          // instant knock, quick settle
            center = _at + _recoil * (5f * k * (_crit ? 1.5f : 1f));
            return true;
        }
        center = default; return false;
    }
}

/// <summary>Brief blink: impact flashes at both the departure and arrival cells.</summary>
internal sealed class TeleportAnim : IAnim
{
    private const float Dur = 0.2f;
    private readonly BattleAnimator _a;
    private readonly Vector2 _from, _to;
    private float _t;
    private bool _spawned;

    private readonly string _id;

    public TeleportAnim(string id, Vector2 from, Vector2 to, BattleAnimator a)
    { _id = id; _from = from; _to = to; _a = a; }

    public bool Done => _t >= Dur;
    public (string id, Vector2 pos)? EndPos => (_id, _to);

    public void Update(float dt)
    {
        if (!_spawned)
        {
            _spawned = true;
            var col = DofusSlice.Game.Rendering.Mono.On
                ? DofusSlice.Game.Rendering.Mono.Ink : new Color(150, 210, 240);
            _a.AddOverlay(new ImpactFlash(_from + new Vector2(0, -16), col));
            _a.AddOverlay(new ImpactFlash(_to + new Vector2(0, -16), col));
        }
        _t += dt;
    }

    public bool TryCenter(string id, out Vector2 center) { center = default; return false; }
}

/// <summary>
/// The ranged read: a streak that flies from the caster to the target cell, blocking the
/// queue so the hit number only appears when the shot ARRIVES. Melee range = no projectile.
/// </summary>
internal sealed class ProjectileAnim : IAnim
{
    private readonly BattleAnimator _a;
    private readonly string _casterId;
    private readonly CellCoord _target;
    private readonly Color _color;
    private float _dur = -1f; // resolved on first update from the replay ledger
    private float _t;

    public ProjectileAnim(BattleAnimator a, string casterId, CellCoord target, Color color)
    { _a = a; _casterId = casterId; _target = target; _color = color; }

    public bool Done => _dur >= 0f && _t >= _dur;

    public void Update(float dt)
    {
        if (_dur < 0f)
        {
            var from = _a.Projector.CellCenter(_a.DisplayCellOf(_casterId)) + new Vector2(0, -22);
            var to = _a.Projector.CellCenter(_target) + new Vector2(0, -16);
            float dist = (to - from).Length();
            if (dist < 70f) { _dur = 0f; return; }     // adjacent: the lunge already reads
            _dur = Math.Clamp(dist / 900f, 0.12f, 0.45f);
            _a.AddOverlay(new ProjectileOverlay(from, to, _dur, _color));
            _a.Sfx?.Invoke("zip", 0.35f);
        }
        _t += dt;
    }

    public bool TryCenter(string id, out Vector2 center) { center = default; return false; }
}

/// <summary>
/// The queued death beat: only when this replays does the fallen unit leave the board and
/// hand over to its corpse fade — never before the blow that killed it has landed.
/// </summary>
internal sealed class DeathAnim : IAnim
{
    private const float Dur = 0.4f;
    private readonly BattleAnimator _a;
    private readonly string _id;
    private readonly Vector2 _at;
    private readonly Color _color;
    private readonly string _sprite;
    private readonly float _heightPx;
    private readonly Color _tint;
    private float _t;

    public DeathAnim(BattleAnimator a, string id, Vector2 at, Color color, string sprite,
        float heightPx, Color tint)
    { _a = a; _id = id; _at = at; _color = color; _sprite = sprite; _heightPx = heightPx; _tint = tint; }

    public bool Done => _t >= Dur;

    public void Update(float dt)
    {
        if (_t == 0f)
        {
            _a.ReleaseDeath(_id);
            _a.SpawnCorpse(new Corpse(_at, _color, _sprite, _a.LastFacing(_id), _heightPx, _tint));
            // A kill lands with weight: a beat of stillness + a camera thump scaled by how big the
            // fallen was (map: deaths previously carried no shake and no camera emphasis at all).
            _a.RequestShake(4f + _heightPx * 0.06f);
            _a.RequestFreeze(0.11f);
            _a.Sfx?.Invoke("death", 0.8f);
        }
        _t += dt;
    }

    public bool TryCenter(string id, out Vector2 center) { center = default; return false; }
}

// ---- Non-blocking overlays ----------------------------------------------------------

internal abstract class Overlay
{
    public abstract bool Done { get; }
    public abstract void Update(float dt);
    public abstract void Draw(SpriteBatch sb, Primitives prim, PixelFont font);
}

internal sealed class FloatingText : Overlay
{
    private const float Dur = 0.85f;
    private Vector2 _pos;
    private readonly string _text;
    private readonly Color _color;
    private readonly int _scale;
    private float _t;

    public FloatingText(Vector2 pos, string text, Color color, int scale = 2)
    {
        _pos = pos; _text = text; _color = color; _scale = scale;
    }
    public override bool Done => _t >= Dur;
    public override void Update(float dt) { _t += dt; _pos.Y -= dt * 34f; }

    public override void Draw(SpriteBatch sb, Primitives prim, PixelFont font)
    {
        float a = 1f - _t / Dur;
        font.DrawCentered(sb, _text, (int)_pos.X, (int)_pos.Y, _scale, _color * a);
    }
}

/// <summary>The flying shot itself: a bright head with a fading three-dot trail, on a low arc.</summary>
internal sealed class ProjectileOverlay : Overlay
{
    private readonly Vector2 _from, _to;
    private readonly float _dur;
    private readonly Color _color;
    private float _t;

    public ProjectileOverlay(Vector2 from, Vector2 to, float dur, Color color)
    { _from = from; _to = to; _dur = Math.Max(0.05f, dur); _color = color; }

    public override bool Done => _t >= _dur;
    public override void Update(float dt) => _t += dt;

    private Vector2 At(float p) =>
        Vector2.Lerp(_from, _to, p) + new Vector2(0, -14f * MathF.Sin(MathF.PI * p)); // the arc

    public override void Draw(SpriteBatch sb, Primitives prim, PixelFont font)
    {
        float p = Math.Clamp(_t / _dur, 0f, 1f);
        // The half-res mono world would shrink the shot to a speck — double it there.
        float m = DofusSlice.Game.Rendering.Mono.On ? 2f : 1f;
        for (int i = 3; i >= 1; i--)                       // trail, dimmer and smaller behind
        {
            float tp = Math.Clamp(p - i * 0.06f, 0f, 1f);
            prim.DiscAt(sb, At(tp), (3.5f - i * 0.8f) * m, _color * (0.5f - i * 0.12f));
        }
        prim.DiscAt(sb, At(p), 4f * m, Color.White * 0.9f);    // the bright head
        prim.DiscAt(sb, At(p), 2.5f * m, _color);
    }
}

internal sealed class ImpactFlash : Overlay
{
    private const float Dur = 0.3f;
    private readonly Vector2 _c;
    private readonly Color _color;
    private float _t;

    public ImpactFlash(Vector2 c, Color color) { _c = c; _color = color; }
    public override bool Done => _t >= Dur;
    public override void Update(float dt) => _t += dt;

    public override void Draw(SpriteBatch sb, Primitives prim, PixelFont font)
    {
        float p = _t / Dur;
        prim.DiscAt(sb, _c, 6f + p * 18f, _color * (1f - p));
    }
}

/// <summary>A fallen fighter: holds a beat at full, then shrinks and fades away.</summary>
internal sealed class Corpse
{
    private const float Hold = 0.85f, Fade = 0.55f; // long enough for a 10-frame die strip
    private readonly Vector2 _center;
    private readonly Color _color;
    private readonly string _name;
    private readonly Facing4 _facing;
    private readonly float _heightPx;
    private readonly Color _tint;
    private float _t;

    public Corpse(Vector2 center, Color color, string name, Facing4 facing,
        float heightPx = 64f, Color? tint = null)
    {
        _center = center; _color = color; _name = name; _facing = facing; _heightPx = heightPx;
        _tint = tint ?? Color.White;
    }

    public bool Done => _t >= Hold + Fade;
    public void Update(float dt) => _t += dt;

    public void Draw(SpriteBatch sb, Primitives prim, SpriteBank sprites)
    {
        float alpha = _t <= Hold ? 1f : 1f - (_t - Hold) / Fade;

        // Play a die strip once if the art exists; otherwise the procedural token fade.
        var sheet = sprites.GetSheet(_name, "die", _facing.ToKey());
        if (sheet != null && sheet.FrameCount > 1)
        {
            int frame = Math.Min((int)(_t * 10f), sheet.FrameCount - 1);
            SpriteDraw.Feet(sb, sheet, _center + new Vector2(0, 4), _tint * alpha, _heightPx, frame);
            return;
        }

        float scale = _t <= Hold ? 1f : 1f - 0.6f * ((_t - Hold) / Fade);
        var head = _center + new Vector2(0, -16);
        prim.DiscAt(sb, _center + new Vector2(0, 2), 13f * scale, new Color(0, 0, 0, 80) * alpha);
        prim.DiscAt(sb, head, 15f * scale, new Color(20, 20, 24) * alpha);
        prim.DiscAt(sb, head, 13f * scale, _color * alpha);
    }
}

/// <summary>A beat announcing whose turn begins: ring + name plate over the unit (1.29 read).</summary>
internal sealed class TurnTelegraph : IAnim
{
    private readonly BattleAnimator _a;
    private readonly string _id, _name;
    private readonly bool _player;
    private readonly float _dur;
    private float _t;

    public TurnTelegraph(BattleAnimator a, string id, string name, bool player, float dur)
    { _a = a; _id = id; _name = name; _player = player; _dur = dur; }

    public bool Done => _t >= _dur;
    public string? ActorId => _id;

    public void Update(float dt)
    {
        if (_t == 0f)
        {
            var pos = _a.Projector.CellCenter(_a.DisplayCellOf(_id));
            _a.AddOverlay(new BannerOverlay(pos + new Vector2(0, -52), _name.ToUpperInvariant(),
                DofusSlice.Game.Rendering.Mono.On
                    ? (_player ? DofusSlice.Game.Rendering.Mono.Ink : DofusSlice.Game.Rendering.Mono.Danger)
                    : _player ? new Color(120, 200, 120) : new Color(214, 110, 96), _dur));
        }
        var cell = _a.DisplayCellOf(_id);
        float pulse = 0.35f + 0.25f * MathF.Sin(_t * 9f);
        _a.AddTelegraphCell(cell, (DofusSlice.Game.Rendering.Mono.On
            ? (_player ? DofusSlice.Game.Rendering.Mono.Ink : DofusSlice.Game.Rendering.Mono.Danger)
            : _player ? new Color(120, 200, 120) : new Color(214, 110, 96)) * pulse);
        _t += dt;
    }

    public bool TryCenter(string id, out Vector2 center) { center = default; return false; }
}

/// <summary>The 1.29 movement read: the chosen path glows green before the walk plays.</summary>
/// <summary>A zero-duration queued beat that floats a resource cost ("-4 AP", "-3 MP")
/// over a unit exactly when its action replays — not when the engine resolved it.</summary>
internal sealed class CostFloat : IAnim
{
    private readonly BattleAnimator _a;
    private readonly string _id, _text;
    private readonly Color _color;
    private bool _fired;

    public CostFloat(BattleAnimator a, string id, string text, Color color)
    { _a = a; _id = id; _text = text; _color = color; }

    public bool Done => _fired;

    public void Update(float dt)
    {
        if (_fired) return;
        _fired = true;
        var pos = _a.Projector.CellCenter(_a.DisplayCellOf(_id));
        _a.AddOverlay(new FloatingText(pos + new Vector2(18, -46), _text, _color, 2));
    }

    public bool TryCenter(string id, out Vector2 center) { center = default; return false; }
}

internal sealed class PathTelegraph : IAnim
{
    private readonly BattleAnimator _a;
    private readonly CellCoord[] _path;
    private readonly float _dur;
    private float _t;

    public PathTelegraph(BattleAnimator a, CellCoord[] path, float dur) { _a = a; _path = path; _dur = dur; }

    public bool Done => _t >= _dur;

    public void Update(float dt)
    {
        // Cells light up one by one along the path, then hold — the plan, then the walk.
        int lit = Math.Min(_path.Length, 1 + (int)(_t / 0.05f));
        for (int i = 0; i < lit; i++)
            _a.AddTelegraphCell(_path[i], (DofusSlice.Game.Rendering.Mono.On
                ? DofusSlice.Game.Rendering.Mono.Ink : new Color(96, 190, 96)) * 0.45f);
        _t += dt;
    }

    public bool TryCenter(string id, out Vector2 center) { center = default; return false; }
}

/// <summary>
/// The spell read, exactly as the player asked: the caster announces the spell by name, its
/// range shades blue from where it stands, and the chosen target cell pulses red — THEN the
/// cast plays. Range is pure 1.29 diamond distance from the replay position.
/// </summary>
internal sealed class CastTelegraph : IAnim
{
    private readonly BattleAnimator _a;
    private readonly string _id;
    private readonly SpellDef _spell;
    private readonly CellCoord _target;
    private readonly float _dur;
    private readonly int _maxRange;   // EFFECTIVE reach (range buffs/debuffs applied), not the authored one
    private float _t;

    public CastTelegraph(BattleAnimator a, string id, SpellDef spell, CellCoord target, float dur,
        int maxRange)
    { _a = a; _id = id; _spell = spell; _target = target; _dur = dur; _maxRange = maxRange; }

    public bool Done => _t >= _dur;
    public string? ActorId => _id;

    public void Update(float dt)
    {
        if (_t == 0f)
        {
            var pos = _a.Projector.CellCenter(_a.DisplayCellOf(_id));
            _a.AddOverlay(new BannerOverlay(pos + new Vector2(0, -52), _spell.Name.ToUpperInvariant(),
                DofusSlice.Game.Rendering.Mono.On ? DofusSlice.Game.Rendering.Mono.Ink
                    : new Color(150, 190, 240), _dur));
            _a.Sfx?.Invoke("click", 0.5f);
        }
        var from = _a.DisplayCellOf(_id);
        for (int dx = -_maxRange; dx <= _maxRange; dx++)
            for (int dy = -_maxRange; dy <= _maxRange; dy++)
            {
                int d = Math.Abs(dx) + Math.Abs(dy);
                if (d < _spell.MinRange || d > _maxRange) continue;
                if (_spell.LineOnly && dx != 0 && dy != 0) continue;
                _a.AddTelegraphCell(new CellCoord(from.X + dx, from.Y + dy),
                    (DofusSlice.Game.Rendering.Mono.On ? DofusSlice.Game.Rendering.Mono.Dim
                        : new Color(90, 120, 220)) * 0.35f);
            }
        float pulse = 0.4f + 0.3f * MathF.Sin(_t * 10f);
        _a.AddTelegraphCell(_target, (DofusSlice.Game.Rendering.Mono.On
            ? DofusSlice.Game.Rendering.Mono.Danger : new Color(224, 60, 40)) * pulse);
        _t += dt;
    }

    public bool TryCenter(string id, out Vector2 center) { center = default; return false; }
}

/// <summary>A small floating name plate (turn and spell announcements).</summary>
internal sealed class BannerOverlay : Overlay
{
    private readonly Vector2 _pos;
    private readonly string _text;
    private readonly Color _color;
    private readonly float _dur;
    private float _t;

    public BannerOverlay(Vector2 pos, string text, Color color, float dur)
    { _pos = pos; _text = text; _color = color; _dur = dur; }

    public override bool Done => _t >= _dur;
    public override void Update(float dt) => _t += dt;

    public override void Draw(SpriteBatch sb, Primitives prim, PixelFont font)
    {
        float a = Math.Min(1f, Math.Min(_t / 0.1f, (_dur - _t) / 0.15f));
        // Mono renders the world at half res — scale-1 text would land on sub-pixels there.
        int ts = DofusSlice.Game.Rendering.Mono.On ? 2 : 1;
        int w = font.Measure(_text, ts) + 12;
        var r = new Rectangle((int)(_pos.X - w / 2f), (int)_pos.Y - 4, w, 6 + 10 * ts);
        prim.FillRect(sb, r, new Color(10, 11, 14) * (0.85f * a));
        prim.StrokeRect(sb, r, ts, _color * a);
        font.DrawCentered(sb, _text, (int)_pos.X, r.Y + 5, ts, Color.White * a);
    }
}

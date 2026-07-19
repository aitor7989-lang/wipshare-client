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

    /// <summary>Optional resolver so corpses use the same sheet AND pixel height the renderer
    /// drew the fighter with (tithe mode maps archetypes, not fighter names). Null = defaults.</summary>
    public Func<Fighter, (string sprite, float heightPx)>? CorpseSpriteOf { get; set; }

    /// <summary>True while a blocking animation or death fade is still playing.</summary>
    public bool IsBusy => _queue.Count > 0 || _corpses.Any(c => !c.Done);

    public void Reset(IEnumerable<Fighter> fighters)
    {
        _queue.Clear();
        _overlays.Clear();
        _corpses.Clear();
        _displayHp.Clear();
        _flash.Clear();
        _poses.Clear();
        _facing.Clear();
        _pendingShake = 0f;
        _displayPos.Clear();
        _pendingDeaths.Clear();
        foreach (var f in fighters)
        {
            _displayHp[f.Id] = f.Hp;
            _displayPos[f.Id] = _proj.CellCenter(f.Pos);
        }
    }

    public void OnEvent(CombatEvent e)
    {
        switch (e)
        {
            case FighterMoved m:
                _queue.Enqueue(new PathTelegraph(this, m.Path.ToArray(), 0.4f));
                _queue.Enqueue(new MoveAnim(m.Fighter.Id, ToPoints(m.Path), 0.15f, this));
                break;
            case FighterPushed p:
                _queue.Enqueue(new MoveAnim(p.Fighter.Id, ToPoints(p.Path), 0.07f, this, "zip"));
                break;
            case FighterTeleported t:
                _queue.Enqueue(new TeleportAnim(t.Fighter.Id, _proj.CellCenter(t.From), _proj.CellCenter(t.To), this));
                Sfx?.Invoke("zip", 0.7f);
                break;
            case SpellCast c:
                _queue.Enqueue(new CastTelegraph(this, c.Caster.Id, c.Spell, c.Target, 0.85f));
                _queue.Enqueue(new CastAnim(c.Caster.Id, _proj.CellCenter(c.Target), c.Spell, this));
                break;
            case DamageDealt d:
                _queue.Enqueue(new HitAnim(d.Target.Id, _proj.CellCenter(d.At), d.Amount, this, d.Critical, d.Element));
                break;
            case HealApplied h:
                _queue.Enqueue(new HitAnim(h.Target.Id, _proj.CellCenter(h.At), -h.Amount, this));
                break;
            case FighterDied dd:
                // Death replays IN SEQUENCE: the unit stays on the board (StillShown) until
                // the killing blow's animations have played, then the corpse takes over.
                _pendingDeaths.Add(dd.Fighter.Id);
                var (corpseSprite, corpseH) = CorpseSpriteOf?.Invoke(dd.Fighter)
                    ?? (SpriteName(dd.Fighter), 64f);
                _queue.Enqueue(new DeathAnim(this, dd.Fighter.Id, _proj.CellCenter(dd.At),
                    ColorOf(dd.Fighter), corpseSprite, corpseH));
                break;
            case FighterSummoned s:
                _displayHp[s.Fighter.Id] = s.Fighter.Hp;   // seed the newcomer so its HP bar shows
                _displayPos[s.Fighter.Id] = _proj.CellCenter(s.Fighter.Pos);
                _facing[s.Fighter.Id] = Facing4.Se;
                _overlays.Add(new ImpactFlash(_proj.CellCenter(s.Fighter.Pos) + new Vector2(0, -16),
                    new Color(150, 220, 180)));
                Sfx?.Invoke("summon", 0.7f);
                break;
            case TurnStarted ts:
                _queue.Enqueue(new TurnTelegraph(this, ts.Fighter.Id, ts.Fighter.Name,
                    ts.Fighter.Team == Team.Player, 0.55f));
                break;
        }
    }

    public void Update(float dt, IReadOnlyList<Fighter> fighters)
    {
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

        // Ease each displayed HP toward the true value so bars drain smoothly.
        foreach (var f in fighters)
        {
            float cur = _displayHp.TryGetValue(f.Id, out var v) ? v : f.Hp;
            _displayHp[f.Id] = cur + (f.Hp - cur) * Math.Min(1f, dt * 9f);
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

    private float _pendingShake;
    internal void RequestShake(float amp) => _pendingShake = Math.Max(_pendingShake, amp);
    /// <summary>Returns and clears the shake requested since the last call (synced to hits).</summary>
    public float ConsumeShake() { var s = _pendingShake; _pendingShake = 0f; return s; }

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
        if (_t == 0f) _a?.Sfx?.Invoke(_sound, 0.6f);
        _t += dt;
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
    private readonly string _id;
    private readonly Vector2 _at;
    private readonly int _amount;
    private readonly BattleAnimator _a;
    private readonly bool _crit;
    private readonly Element _element;
    private float _t;
    private bool _spawned;

    public HitAnim(string id, Vector2 at, int amount, BattleAnimator a, bool crit = false,
        Element element = Element.Neutral)
    {
        _id = id; _at = at; _amount = amount; _a = a; _crit = crit; _element = element;
    }

    public bool Done => _t >= Dur;

    public void Update(float dt)
    {
        if (!_spawned)
        {
            _spawned = true;
            _a.SetFlash(_id, 0.3f);
            if (_amount > 0) _a.RequestShake(Math.Min(12f, 3f + _amount * 0.12f) * (_crit ? 1.5f : 1f));
            bool heal = _amount < 0;
            _a.Sfx?.Invoke(heal ? "heal" : _crit ? "crit" : "hit_" + _element.ToString().ToLowerInvariant(),
                heal ? 0.7f : 0.85f);
            string text = (heal ? "+" : "-") + Math.Abs(_amount) + (_crit ? "!" : "");
            // 1.29 floats the number in the element's colour; crits go gold, heals green.
            var color = heal ? new Color(120, 220, 130)
                : _crit ? new Color(255, 210, 90)
                : DofusSlice.Game.Rendering.EwChrome.ElementColor(_element);
            int scale = _crit ? 3 : 2;
            _a.AddOverlay(new FloatingText(_at + new Vector2(0, -30), text, color, scale));
        }
        _t += dt;
    }

    public bool TryCenter(string id, out Vector2 center) { center = default; return false; }
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
            var col = new Color(150, 210, 240);
            _a.AddOverlay(new ImpactFlash(_from + new Vector2(0, -16), col));
            _a.AddOverlay(new ImpactFlash(_to + new Vector2(0, -16), col));
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
    private float _t;

    public DeathAnim(BattleAnimator a, string id, Vector2 at, Color color, string sprite, float heightPx)
    { _a = a; _id = id; _at = at; _color = color; _sprite = sprite; _heightPx = heightPx; }

    public bool Done => _t >= Dur;

    public void Update(float dt)
    {
        if (_t == 0f)
        {
            _a.ReleaseDeath(_id);
            _a.SpawnCorpse(new Corpse(_at, _color, _sprite, _a.LastFacing(_id), _heightPx));
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
    private float _t;

    public Corpse(Vector2 center, Color color, string name, Facing4 facing, float heightPx = 64f)
    {
        _center = center; _color = color; _name = name; _facing = facing; _heightPx = heightPx;
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
            SpriteDraw.Feet(sb, sheet, _center + new Vector2(0, 4), Color.White * alpha, _heightPx, frame);
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
                _player ? new Color(120, 200, 120) : new Color(214, 110, 96), _dur));
        }
        var cell = _a.DisplayCellOf(_id);
        float pulse = 0.35f + 0.25f * MathF.Sin(_t * 9f);
        _a.AddTelegraphCell(cell, (_player ? new Color(120, 200, 120) : new Color(214, 110, 96)) * pulse);
        _t += dt;
    }

    public bool TryCenter(string id, out Vector2 center) { center = default; return false; }
}

/// <summary>The 1.29 movement read: the chosen path glows green before the walk plays.</summary>
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
            _a.AddTelegraphCell(_path[i], new Color(96, 190, 96) * 0.45f);
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
    private float _t;

    public CastTelegraph(BattleAnimator a, string id, SpellDef spell, CellCoord target, float dur)
    { _a = a; _id = id; _spell = spell; _target = target; _dur = dur; }

    public bool Done => _t >= _dur;
    public string? ActorId => _id;

    public void Update(float dt)
    {
        if (_t == 0f)
        {
            var pos = _a.Projector.CellCenter(_a.DisplayCellOf(_id));
            _a.AddOverlay(new BannerOverlay(pos + new Vector2(0, -52), _spell.Name.ToUpperInvariant(),
                new Color(150, 190, 240), _dur));
            _a.Sfx?.Invoke("click", 0.5f);
        }
        var from = _a.DisplayCellOf(_id);
        for (int dx = -_spell.MaxRange; dx <= _spell.MaxRange; dx++)
            for (int dy = -_spell.MaxRange; dy <= _spell.MaxRange; dy++)
            {
                int d = Math.Abs(dx) + Math.Abs(dy);
                if (d < _spell.MinRange || d > _spell.MaxRange) continue;
                if (_spell.LineOnly && dx != 0 && dy != 0) continue;
                _a.AddTelegraphCell(new CellCoord(from.X + dx, from.Y + dy), new Color(90, 120, 220) * 0.35f);
            }
        float pulse = 0.4f + 0.3f * MathF.Sin(_t * 10f);
        _a.AddTelegraphCell(_target, new Color(224, 60, 40) * pulse);
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
        int w = font.Measure(_text, 1) + 12;
        var r = new Rectangle((int)(_pos.X - w / 2f), (int)_pos.Y - 4, w, 16);
        prim.FillRect(sb, r, new Color(10, 11, 14) * (0.85f * a));
        prim.StrokeRect(sb, r, 1, _color * a);
        font.DrawCentered(sb, _text, (int)_pos.X, r.Y + 5, 1, Color.White * a);
    }
}

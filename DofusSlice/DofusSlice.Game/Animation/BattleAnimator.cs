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
    private readonly Dictionary<string, float> _flash = new();
    private readonly Dictionary<string, Pose> _poses = new();
    private readonly Dictionary<string, Facing4> _facing = new();

    private const float FlashDuration = 0.3f;

    public BattleAnimator(IsoProjector proj) => _proj = proj;

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
        foreach (var f in fighters) _displayHp[f.Id] = f.Hp;
    }

    public void OnEvent(CombatEvent e)
    {
        switch (e)
        {
            case FighterMoved m:
                _queue.Enqueue(new MoveAnim(m.Fighter.Id, ToPoints(m.Path), 0.11f));
                break;
            case FighterPushed p:
                _queue.Enqueue(new MoveAnim(p.Fighter.Id, ToPoints(p.Path), 0.07f));
                break;
            case FighterTeleported t:
                _queue.Enqueue(new TeleportAnim(_proj.CellCenter(t.From), _proj.CellCenter(t.To), this));
                break;
            case SpellCast c:
                _queue.Enqueue(new CastAnim(c.Caster.Id, _proj.CellCenter(c.Caster.Pos),
                    _proj.CellCenter(c.Target), c.Spell, this));
                break;
            case DamageDealt d:
                _queue.Enqueue(new HitAnim(d.Target.Id, _proj.CellCenter(d.At), d.Amount, this, d.Critical, d.Element));
                break;
            case HealApplied h:
                _queue.Enqueue(new HitAnim(h.Target.Id, _proj.CellCenter(h.At), -h.Amount, this));
                break;
            case FighterDied dd:
                _corpses.Add(new Corpse(_proj.CellCenter(dd.At), ColorOf(dd.Fighter),
                    SpriteName(dd.Fighter), LastFacing(dd.Fighter.Id)));
                break;
            case FighterSummoned s:
                _displayHp[s.Fighter.Id] = s.Fighter.Hp;   // seed the newcomer so its HP bar shows
                _facing[s.Fighter.Id] = Facing4.Se;
                _overlays.Add(new ImpactFlash(_proj.CellCenter(s.Fighter.Pos) + new Vector2(0, -16),
                    new Color(150, 220, 180)));
                break;
            case TurnStarted:
                break;
        }
    }

    public void Update(float dt, IReadOnlyList<Fighter> fighters)
    {
        if (_queue.Count > 0)
        {
            var head = _queue.Peek();
            head.Update(dt);
            if (head.Done) _queue.Dequeue();
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

    // ----- Queries used by the renderer ---------------------------------------------

    /// <summary>Tile-centre a fighter should render at (animated override or its logical cell).</summary>
    public Vector2 CenterFor(Fighter f)
    {
        if (_queue.Count > 0 && _queue.Peek().TryCenter(f.Id, out var c)) return c;
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
        return dmg?.Element switch
        {
            Element.Fire => new Color(240, 120, 80),
            Element.Water => new Color(90, 160, 240),
            Element.Air => new Color(130, 220, 140),
            Element.Earth => new Color(190, 140, 90),
            _ => new Color(230, 220, 180),
        };
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
}

/// <summary>Slides a fighter's token along a path, one segment at a time.</summary>
internal sealed class MoveAnim : IAnim
{
    private readonly string _id;
    private readonly Vector2[] _pts;
    private readonly float _perSeg;
    private float _t;

    public MoveAnim(string id, Vector2[] pts, float perSeg)
    {
        _id = id;
        _pts = pts.Length > 0 ? pts : new[] { Vector2.Zero };
        _perSeg = perSeg;
    }

    private float Total => _perSeg * Math.Max(1, _pts.Length - 1);
    public bool Done => _t >= Total;
    public void Update(float dt) => _t += dt;

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
    private const float Dur = 0.28f;
    private readonly string _id;
    private readonly Vector2 _from, _to;
    private readonly Color _impact;
    private readonly BattleAnimator _a;
    private float _t;
    private bool _spawned;

    public CastAnim(string id, Vector2 from, Vector2 to, SpellDef spell, BattleAnimator a)
    {
        _id = id; _from = from; _to = to; _a = a;
        _impact = BattleAnimator.SpellColor(spell);
    }

    public bool Done => _t >= Dur;

    public string? ActorId => _id;
    public AnimState ActorState => AnimState.Cast;
    public Facing4 ActorFacing => Facing.FromScreenDelta(_to - _from);

    public void Update(float dt)
    {
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
            string text = (heal ? "+" : "-") + Math.Abs(_amount) + (_crit ? "!" : "");
            // 1.29 floats the number in the element's colour; crits go gold, heals green.
            var color = heal ? new Color(120, 220, 130)
                : _crit ? new Color(255, 210, 90)
                : _element switch
                {
                    Element.Fire => new Color(250, 120, 70),
                    Element.Water => new Color(100, 170, 250),
                    Element.Air => new Color(130, 225, 130),
                    Element.Earth => new Color(205, 150, 90),
                    _ => new Color(230, 224, 200),         // neutral grey-white
                };
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

    public TeleportAnim(Vector2 from, Vector2 to, BattleAnimator a) { _from = from; _to = to; _a = a; }

    public bool Done => _t >= Dur;

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
    private const float Hold = 0.25f, Fade = 0.4f;
    private readonly Vector2 _center;
    private readonly Color _color;
    private readonly string _name;
    private readonly Facing4 _facing;
    private float _t;

    public Corpse(Vector2 center, Color color, string name, Facing4 facing)
    {
        _center = center; _color = color; _name = name; _facing = facing;
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
            SpriteDraw.Feet(sb, sheet, _center + new Vector2(0, 4), Color.White * alpha, 64f, frame);
            return;
        }

        float scale = _t <= Hold ? 1f : 1f - 0.6f * ((_t - Hold) / Fade);
        var head = _center + new Vector2(0, -16);
        prim.DiscAt(sb, _center + new Vector2(0, 2), 13f * scale, new Color(0, 0, 0, 80) * alpha);
        prim.DiscAt(sb, head, 15f * scale, new Color(20, 20, 24) * alpha);
        prim.DiscAt(sb, head, 13f * scale, _color * alpha);
    }
}

using DofusSlice.Core.Grid;
using DofusSlice.Core.Spells;

namespace DofusSlice.Core.Combat;

public enum FightOutcome { Ongoing, Victory, Defeat }

/// <summary>
/// The authoritative turn-based combat state machine. It owns the fighters, the
/// initiative order and the round counter, and is the single place that validates and
/// applies movement and spell casts. The renderer and the AI both drive the fight only
/// through this class, so the rules live in exactly one spot.
/// </summary>
public sealed class CombatEngine
{
    public Battlefield Field { get; }
    private readonly List<Fighter> _order = new();
    private readonly IRng _rng;
    private int _pointer;

    public int Round { get; private set; }
    public IReadOnlyList<Fighter> Fighters => _order;
    public Fighter Current => _order[_pointer];

    /// <summary>Human-readable combat log; the renderer and the sim both read from it.</summary>
    public List<string> Log { get; } = new();
    public event Action<string>? Logged;

    public CombatEngine(Battlefield field, IEnumerable<Fighter> fighters, IRng rng)
    {
        Field = field;
        _rng = rng;
        _order.AddRange(fighters);
    }

    private void Emit(string message)
    {
        Log.Add(message);
        Logged?.Invoke(message);
    }

    public Fighter? FighterAt(CellCoord cell) => _order.FirstOrDefault(f => f.IsAlive && f.Pos == cell);
    public bool IsOccupied(CellCoord cell) => FighterAt(cell) != null;

    public void Start()
    {
        _order.Sort((a, b) => b.Initiative.CompareTo(a.Initiative));
        Round = 1;
        _pointer = 0;
        Current.BeginTurn();
        Emit($"=== Round 1 ===");
        Emit($"{Current.Name}'s turn (INI {Current.Initiative}).");
    }

    // ---- Movement -------------------------------------------------------------------

    /// <summary>Cells the given fighter can walk to this turn, mapped to their MP cost.</summary>
    public Dictionary<CellCoord, int> MovementRange(Fighter f) =>
        Pathfinding.ReachableCells(Field, f.Pos, f.CurrentMp, c => c != f.Pos && IsOccupied(c));

    public bool TryMove(Fighter f, CellCoord dest)
    {
        if (!f.IsAlive) return false;
        var range = MovementRange(f);
        if (!range.TryGetValue(dest, out int cost) || cost > f.CurrentMp) return false;

        f.Pos = dest;
        f.CurrentMp -= cost;
        Emit($"{f.Name} moves to {dest} (-{cost} MP, {f.CurrentMp} left).");
        return true;
    }

    // ---- Casting --------------------------------------------------------------------

    public bool CanCast(Fighter caster, SpellDef spell, CellCoord target, out string reason)
    {
        reason = "";
        if (!caster.IsAlive) { reason = "caster is dead"; return false; }
        if (caster.CurrentAp < spell.ApCost) { reason = "not enough AP"; return false; }
        if (!caster.HasCastsLeft(spell)) { reason = "cast limit reached this turn"; return false; }
        if (caster.IsOnCooldown(spell, Round)) { reason = "spell on cooldown"; return false; }
        if (!Field.InBounds(target)) { reason = "out of bounds"; return false; }

        int dist = caster.Pos.DistanceTo(target);
        if (dist < spell.MinRange || dist > spell.MaxRange) { reason = "out of range"; return false; }
        if (spell.LineOnly && !caster.Pos.IsAlignedWith(target)) { reason = "must be cast in a line"; return false; }
        if (spell.RequiresLineOfSight &&
            !LineOfSight.HasLineOfSight(Field, caster.Pos, target, c => c != target && IsOccupied(c)))
        {
            reason = "no line of sight";
            return false;
        }
        if (spell.NeedsTarget && FighterAt(target) == null) { reason = "no target on that cell"; return false; }
        if (spell.NeedsFreeCell && (!Field.IsWalkable(target) || IsOccupied(target)))
        {
            reason = "cell is not free";
            return false;
        }
        return true;
    }

    /// <summary>All cells the caster could legally target with the spell right now.</summary>
    public List<CellCoord> CastableCells(Fighter caster, SpellDef spell)
    {
        var cells = new List<CellCoord>();
        foreach (var cell in Field.AllCells())
            if (CanCast(caster, spell, cell, out _))
                cells.Add(cell);
        return cells;
    }

    /// <summary>
    /// Cells the spell can geometrically reach — range, line-of-sight and line-only, but
    /// ignoring whether a target/free cell is actually there. This is the "where can it go"
    /// area the UI paints, so a targeted spell still shows its full reach, not just the
    /// occupied cells that happen to be legal targets.
    /// </summary>
    public List<CellCoord> SpellReachCells(Fighter caster, SpellDef spell)
    {
        var cells = new List<CellCoord>();
        foreach (var cell in Field.AllCells())
        {
            int dist = caster.Pos.DistanceTo(cell);
            if (dist < spell.MinRange || dist > spell.MaxRange) continue;
            if (spell.LineOnly && !caster.Pos.IsAlignedWith(cell)) continue;
            if (spell.RequiresLineOfSight &&
                !LineOfSight.HasLineOfSight(Field, caster.Pos, cell, c => c != cell && IsOccupied(c)))
                continue;
            cells.Add(cell);
        }
        return cells;
    }

    public List<CellCoord> AreaCells(SpellDef spell, CellCoord impact) =>
        spell.Area.CellsAround(impact).Where(Field.InBounds).ToList();

    public bool TryCast(Fighter caster, SpellDef spell, CellCoord target)
    {
        if (!CanCast(caster, spell, target, out string reason))
        {
            Emit($"{caster.Name} cannot cast {spell.Name}: {reason}.");
            return false;
        }

        caster.CurrentAp -= spell.ApCost;
        caster.RecordCast(spell, Round);
        Emit($"{caster.Name} casts {spell.Name} at {target} (-{spell.ApCost} AP).");

        foreach (var effect in spell.Effects)
            ApplyEffect(caster, spell, effect, target);

        RemoveTheDead();
        return true;
    }

    private void ApplyEffect(Fighter caster, SpellDef spell, SpellEffect effect, CellCoord impact)
    {
        if (effect.Kind == EffectKind.Teleport)
        {
            if (Field.IsWalkable(impact) && !IsOccupied(impact))
            {
                caster.Pos = impact;
                Emit($"  {caster.Name} leaps to {impact}.");
            }
            return;
        }

        foreach (var cell in AreaCells(spell, impact))
        {
            var victim = FighterAt(cell);
            if (victim == null) continue;

            switch (effect.Kind)
            {
                case EffectKind.Damage:
                    ApplyDamage(caster, victim, effect);
                    break;
                case EffectKind.Heal:
                    if (victim.Team == caster.Team) ApplyHeal(caster, victim, effect);
                    break;
                case EffectKind.Push:
                    ApplyPush(caster, victim, effect.Min);
                    break;
            }
        }
    }

    private void ApplyDamage(Fighter caster, Fighter victim, SpellEffect effect)
    {
        int rolled = _rng.Roll(effect.Min, effect.Max);
        int boosted = rolled * (100 + caster.PrimaryStatFor(effect.Element)) / 100;
        int afterResist = boosted * (100 - victim.ResistanceFor(effect.Element)) / 100;
        int dmg = Math.Max(0, afterResist);
        victim.Hp = Math.Max(0, victim.Hp - dmg);
        Emit($"  {victim.Name} takes {dmg} {effect.Element} damage ({victim.Hp}/{victim.MaxHp} HP).");
        if (!victim.IsAlive) Emit($"  {victim.Name} is defeated!");
    }

    private void ApplyHeal(Fighter caster, Fighter victim, SpellEffect effect)
    {
        int rolled = _rng.Roll(effect.Min, effect.Max);
        int amount = rolled * (100 + caster.Intelligence) / 100;
        int before = victim.Hp;
        victim.Hp = Math.Min(victim.MaxHp, victim.Hp + amount);
        Emit($"  {victim.Name} recovers {victim.Hp - before} HP ({victim.Hp}/{victim.MaxHp}).");
    }

    private void ApplyPush(Fighter caster, Fighter victim, int cells)
    {
        if (victim.Pos == caster.Pos || cells <= 0) return;

        int dx = Math.Sign(victim.Pos.X - caster.Pos.X);
        int dy = Math.Sign(victim.Pos.Y - caster.Pos.Y);
        // Collapse to a single orthogonal axis (the dominant one) for a clean shove.
        if (dx != 0 && dy != 0)
        {
            if (Math.Abs(victim.Pos.X - caster.Pos.X) >= Math.Abs(victim.Pos.Y - caster.Pos.Y)) dy = 0;
            else dx = 0;
        }

        int moved = 0;
        for (int i = 0; i < cells; i++)
        {
            var next = victim.Pos.Offset(dx, dy);
            if (!Field.IsWalkable(next) || IsOccupied(next))
            {
                int collision = (cells - moved) * 5;
                victim.Hp = Math.Max(0, victim.Hp - collision);
                Emit($"  {victim.Name} slams into an obstacle for {collision} damage ({victim.Hp}/{victim.MaxHp} HP).");
                if (!victim.IsAlive) Emit($"  {victim.Name} is defeated!");
                return;
            }
            victim.Pos = next;
            moved++;
        }
        if (moved > 0) Emit($"  {victim.Name} is pushed {moved} cell(s) to {victim.Pos}.");
    }

    private void RemoveTheDead()
    {
        // Fighters stay in the order list (so the pointer math is stable) but are skipped
        // once dead; nothing else to do here beyond a hook for future death effects.
    }

    // ---- Turn flow ------------------------------------------------------------------

    public void EndTurn()
    {
        Emit($"{Current.Name} ends their turn.");
        if (Outcome != FightOutcome.Ongoing) return;

        for (int guard = 0; guard < _order.Count + 1; guard++)
        {
            _pointer++;
            if (_pointer >= _order.Count)
            {
                _pointer = 0;
                Round++;
                Emit($"=== Round {Round} ===");
            }
            if (Current.IsAlive) break;
        }

        Current.BeginTurn();
        Emit($"{Current.Name}'s turn.");
    }

    public FightOutcome Outcome
    {
        get
        {
            bool anyEnemy = _order.Any(f => f.Team == Team.Enemy && f.IsAlive);
            bool anyPlayer = _order.Any(f => f.Team == Team.Player && f.IsAlive);
            if (!anyPlayer) return FightOutcome.Defeat;
            if (!anyEnemy) return FightOutcome.Victory;
            return FightOutcome.Ongoing;
        }
    }
}

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

    /// <summary>The fight's RNG — reused after the fight for deterministic drop rolls.</summary>
    public IRng Rng => _rng;
    public IReadOnlyList<Fighter> Fighters => _order;
    public Fighter Current => _order[_pointer];

    /// <summary>Human-readable combat log; the renderer and the sim both read from it.</summary>
    public List<string> Log { get; } = new();
    public event Action<string>? Logged;

    /// <summary>Structured play-by-play for the animation layer (see <see cref="CombatEvent"/>).</summary>
    public event Action<CombatEvent>? Emitted;
    private void Raise(CombatEvent e) => Emitted?.Invoke(e);

    // Builds a summoned creature: (kind, team, cell, id) -> Fighter. Injected so the engine
    // stays decoupled from the content/bestiary layer.
    private readonly Func<string, Team, CellCoord, string, Fighter>? _summonFactory;
    private int _summonCounter;

    public CombatEngine(Battlefield field, IEnumerable<Fighter> fighters, IRng rng,
        Func<string, Team, CellCoord, string, Fighter>? summonFactory = null)
    {
        Field = field;
        _rng = rng;
        _summonFactory = summonFactory;
        _order.AddRange(fighters);
    }

    private void Emit(string message)
    {
        Log.Add(message);
        Logged?.Invoke(message);
    }

    public Fighter? FighterAt(CellCoord cell) => _order.FirstOrDefault(f => f.IsAlive && f.Pos == cell);
    public bool IsOccupied(CellCoord cell) => FighterAt(cell) != null;

    /// <summary>Add a summoned fighter to the fight; it acts when the turn order reaches it.</summary>
    public void Summon(Fighter fighter, Fighter? summoner = null)
    {
        // 1.29: a summon takes its turn immediately after its summoner; without a known summoner
        // it closes the round instead (still pointer-safe: insertion is always past the pointer).
        int at = summoner != null ? _order.IndexOf(summoner) : -1;
        if (at >= 0 && at >= _pointer) _order.Insert(at + 1, fighter);
        else _order.Add(fighter);
        Emit($"{fighter.Name} is summoned.");
        Raise(new FighterSummoned(fighter));
    }

    public void Start()
    {
        if (_order.Count == 0) throw new InvalidOperationException("CombatEngine has no fighters");

        // 1.29 turn order: the team holding the highest-initiative fighter leads, then turns
        // strictly alternate between the teams, each team consuming its own fighters in
        // descending initiative; the larger team's leftovers close the round. The weave is
        // computed once and stays fixed for the whole fight. Ties break by add order, so a
        // fight replays identically from the same setup (the event-log parity discipline).
        var indexed = _order.Select((f, i) => (f, i)).ToList();
        List<Fighter> TeamLine(Team t) => indexed.Where(x => x.f.Team == t)
            .OrderByDescending(x => x.f.Initiative).ThenBy(x => x.i).Select(x => x.f).ToList();
        var lead = indexed.OrderByDescending(x => x.f.Initiative).ThenBy(x => x.i).First().f.Team;
        var first = TeamLine(lead);
        var second = TeamLine(lead == Team.Player ? Team.Enemy : Team.Player);
        _order.Clear();
        for (int k = 0; k < Math.Max(first.Count, second.Count); k++)
        {
            if (k < first.Count) _order.Add(first[k]);
            if (k < second.Count) _order.Add(second[k]);
        }

        Round = 1;
        _pointer = 0;
        Emit($"=== Round 1 ===");
        BeginTurnFor(Current);
    }

    /// <summary>Refresh points, tick this fighter's statuses, and announce the turn.</summary>
    private void BeginTurnFor(Fighter f)
    {
        f.BeginTurn();
        TickTurnStart(f);
        Emit($"{f.Name}'s turn.");
        Raise(new TurnStarted(f, Round));
    }

    /// <summary>Start-of-turn status processing: poison damage and MP drain.</summary>
    private void TickTurnStart(Fighter f)
    {
        foreach (var s in f.Statuses.ToList())
        {
            if (s.Kind == StatusKind.Poison && f.IsAlive)
            {
                int dmg = Math.Max(0, s.Magnitude);
                var at = f.Pos;
                f.Hp = Math.Max(0, f.Hp - dmg);
                Emit($"  {f.Name} suffers {dmg} poison damage ({f.Hp}/{f.MaxHp} HP).");
                Raise(new DamageDealt(f, dmg, Element.Water, at, f.Hp));
                if (!f.IsAlive) { Emit($"  {f.Name} succumbs!"); Raise(new FighterDied(f, at)); }
            }
            else if (s.Kind == StatusKind.MpDrain)
            {
                f.CurrentMp = Math.Max(0, f.CurrentMp - s.Magnitude);
            }
        }
    }

    /// <summary>End-of-turn status processing: regeneration, then age and expire durations.</summary>
    private void TickTurnEnd(Fighter f)
    {
        foreach (var s in f.Statuses)
        {
            if (s.Kind == StatusKind.Regen && f.IsAlive && f.Hp < f.MaxHp)
            {
                int heal = Math.Min(s.Magnitude, f.MaxHp - f.Hp);
                f.Hp += heal;
                Emit($"  {f.Name} regenerates {heal} HP ({f.Hp}/{f.MaxHp}).");
                Raise(new HealApplied(f, heal, f.Pos, f.Hp));
            }
        }

        foreach (var s in f.Statuses) s.Remaining--;
        foreach (var s in f.Statuses.Where(s => s.Remaining <= 0).ToList())
        {
            f.Statuses.Remove(s);
            Raise(new StatusExpired(f, s.Kind));
        }
    }

    // ---- Movement -------------------------------------------------------------------

    /// <summary>The bible's flattened tackle (§3.4/§6.2): leaving an enemy's melee costs extra MP,
    /// deterministically — max(0, (best tackler AGI − mover AGI) / divisor). No rolls, no AP loss.</summary>
    public int TackleSurcharge(Fighter f)
    {
        var tacklers = TacklersOf(f);
        if (tacklers.Count == 0) return 0;
        return Math.Max(0, (tacklers.Max(t => t.Agility) - f.Agility) / TackleDivisor);
    }

    private const int TackleDivisor = 10; // the bible's placeholder divisor (§6.2)

    /// <summary>Cells the given fighter can walk to this turn, mapped to their TRUE MP cost —
    /// including the tackle surcharge on any destination that leaves the lockers' melee, so the
    /// AI policies and any UI reason about what the move really costs.</summary>
    public Dictionary<CellCoord, int> MovementRange(Fighter f)
    {
        if (f.IsRooted) return new Dictionary<CellCoord, int>();
        var raw = Pathfinding.ReachableCells(Field, f.Pos, f.CurrentMp, c => c != f.Pos && IsOccupied(c));
        int surcharge = TackleSurcharge(f);
        if (surcharge == 0) return raw;
        var tacklers = TacklersOf(f);
        var priced = new Dictionary<CellCoord, int>();
        foreach (var (cell, cost) in raw)
        {
            // Shuffling within a locker's melee pays nothing; breaking away pays the surcharge.
            int true_ = cost + (tacklers.Any(t => t.Pos.DistanceTo(cell) == 1) ? 0 : surcharge);
            if (true_ <= f.CurrentMp) priced[cell] = true_;
        }
        return priced;
    }

    /// <summary>Living enemies orthogonally adjacent to (locking) the given fighter.</summary>
    public List<Fighter> TacklersOf(Fighter f) =>
        _order.Where(e => e.IsAlive && e.Team != f.Team && e.Pos.DistanceTo(f.Pos) == 1).ToList();

    public bool IsLocked(Fighter f) => TacklersOf(f).Count > 0;

    public bool TryMove(Fighter f, CellCoord dest)
    {
        if (!f.IsAlive || f.IsRooted) return false;
        var range = MovementRange(f);
        if (!range.TryGetValue(dest, out int cost) || cost > f.CurrentMp) return false;

        bool paidLock = TackleSurcharge(f) > 0 && cost > 0; // the surcharge is folded into cost
        var path = Pathfinding.FindPath(Field, f.Pos, dest, c => c != f.Pos && IsOccupied(c))
                   ?? new List<CellCoord> { f.Pos, dest };
        f.Pos = dest;
        f.CurrentMp -= cost;
        Emit($"{f.Name} moves to {dest} (-{cost} MP, {f.CurrentMp} left)"
             + (paidLock ? " — pulled free of the lock." : "."));
        Raise(new FighterMoved(f, path, cost));
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

    /// <summary>
    /// Estimated (non-crit) damage range a spell would deal to whatever stands on the target
    /// cell, given the caster's stats/buffs and the victim's resistances — for a hover preview.
    /// Returns null if there's no damage effect or no fighter there.
    /// </summary>
    public (int min, int max)? EstimateDamage(Fighter caster, SpellDef spell, CellCoord target)
    {
        var victim = FighterAt(target);
        if (victim == null) return null;
        var dmg = spell.Effects.FirstOrDefault(e => e.Kind is EffectKind.Damage or EffectKind.Lifesteal);
        if (dmg == null) return null;
        return (ComputeDamage(caster, victim, dmg.Element, dmg.Min, false),
                ComputeDamage(caster, victim, dmg.Element, dmg.Max, false));
    }

    public List<CellCoord> AreaCells(SpellDef spell, CellCoord impact, CellCoord fromPos) =>
        spell.Area.CellsAround(impact, StepToward(fromPos, impact)).Where(Field.InBounds).ToList();

    private static CellCoord StepToward(CellCoord from, CellCoord to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        if (dx == 0 && dy == 0) return default;
        return Math.Abs(dx) >= Math.Abs(dy) ? new CellCoord(Math.Sign(dx), 0) : new CellCoord(0, Math.Sign(dy));
    }

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
        Raise(new SpellCast(caster, spell, target));

        // Critical failure: the spell fizzles (AP already spent).
        if (spell.CriticalFailureOneIn > 0 && _rng.Roll(1, spell.CriticalFailureOneIn) == 1)
        {
            Emit($"  {caster.Name}'s {spell.Name} critically fails!");
            Raise(new SpellFizzled(caster, spell));
            return true;
        }

        bool crit = spell.CriticalChanceOneIn > 0 && _rng.Roll(1, spell.CriticalChanceOneIn) == 1;
        if (crit) Emit("  Critical hit!");

        foreach (var effect in spell.Effects)
            ApplyEffect(caster, spell, effect, target, crit);

        RemoveTheDead();
        return true;
    }

    private void ApplyEffect(Fighter caster, SpellDef spell, SpellEffect effect, CellCoord impact, bool crit)
    {
        if (effect.Kind == EffectKind.Teleport)
        {
            if (Field.IsWalkable(impact) && !IsOccupied(impact))
            {
                var from = caster.Pos;
                caster.Pos = impact;
                Emit($"  {caster.Name} leaps to {impact}.");
                Raise(new FighterTeleported(caster, from, impact));
            }
            return;
        }

        if (effect.Kind == EffectKind.Swap)
        {
            var t = FighterAt(impact);
            if (t != null && t != caster) SwapPositions(caster, t);
            return;
        }

        if (effect.Kind == EffectKind.SelfHpCost)
        {
            // A sacrifice (Blood Pact): the caster pays HP up front. It wounds, never kills —
            // the pact takes blood, not the life (clamped to leave 1 HP).
            int cost = Math.Min(effect.Min, Math.Max(0, caster.Hp - 1));
            if (cost > 0)
            {
                caster.Hp -= cost;
                Emit($"  {caster.Name} pays {cost} HP in sacrifice ({caster.Hp}/{caster.MaxHp} HP).");
                Raise(new DamageDealt(caster, cost, Element.Neutral, caster.Pos, caster.Hp, false));
            }
            return;
        }

        if (effect.Kind == EffectKind.Summon)
        {
            if (_summonFactory != null && Field.IsWalkable(impact) && !IsOccupied(impact))
            {
                var ally = _summonFactory(effect.SummonKind, caster.Team, impact, $"summon_{_summonCounter++}");
                if (ally != null) Summon(ally, caster);
            }
            return;
        }

        foreach (var cell in AreaCells(spell, impact, caster.Pos))
        {
            var victim = FighterAt(cell);
            if (victim == null) continue;

            switch (effect.Kind)
            {
                case EffectKind.Damage:
                    ApplyDamage(caster, victim, effect, crit);
                    break;
                case EffectKind.Lifesteal:
                    ApplyLifesteal(caster, victim, effect, crit);
                    break;
                case EffectKind.Heal:
                    if (victim.Team == caster.Team) ApplyHeal(caster, victim, effect);
                    break;
                case EffectKind.Push:
                    ApplyShift(caster, victim, effect.Min, pull: false);
                    break;
                case EffectKind.Pull:
                    ApplyShift(caster, victim, effect.Min, pull: true);
                    break;
                case EffectKind.StealAp:
                    ApplySteal(caster, victim, effect.Min, ap: true);
                    break;
                case EffectKind.StealMp:
                    ApplySteal(caster, victim, effect.Min, ap: false);
                    break;
                case EffectKind.GrantAp:
                    if (victim.Team == caster.Team) ApplyGrantAp(caster, victim, effect.Min);
                    break;
                case EffectKind.ApplyStatus:
                    ApplyStatusEffect(victim, effect.Status, effect.Min, effect.Max);
                    break;
            }
        }
    }

    private void SwapPositions(Fighter a, Fighter b)
    {
        var from = a.Pos;
        a.Pos = b.Pos;
        b.Pos = from;
        Emit($"  {a.Name} swaps places with {b.Name}.");
        Raise(new FighterTeleported(a, from, a.Pos));
        Raise(new FighterTeleported(b, a.Pos, from));
    }

    private void ApplyLifesteal(Fighter caster, Fighter victim, SpellEffect effect, bool crit)
    {
        int rolled = _rng.Roll(effect.Min, effect.Max);
        int dmg = ComputeDamage(caster, victim, effect.Element, rolled, crit);
        var at = victim.Pos;
        victim.Hp = Math.Max(0, victim.Hp - dmg);
        Raise(new DamageDealt(victim, dmg, effect.Element, at, victim.Hp, crit));
        int healed = Math.Min(dmg / 2, caster.MaxHp - caster.Hp);
        caster.Hp += Math.Max(0, healed);
        Emit($"  {victim.Name} takes {dmg} {effect.Element} (life-stolen); {caster.Name} heals {healed}.");
        if (healed > 0) Raise(new HealApplied(caster, healed, caster.Pos, caster.Hp));
        if (!victim.IsAlive) { Emit($"  {victim.Name} is defeated!"); Raise(new FighterDied(victim, at)); }
    }

    private void ApplySteal(Fighter caster, Fighter victim, int amount, bool ap)
    {
        amount = Math.Max(0, amount);
        // Wisdom lets the victim dodge part of the theft.
        int resisted = victim.Wisdom > 0 ? _rng.Roll(0, Math.Max(0, victim.Wisdom / 20)) : 0;
        int want = Math.Max(0, amount - resisted);
        int taken = Math.Min(want, ap ? victim.CurrentAp : victim.CurrentMp);
        if (taken <= 0) return;
        if (ap) { victim.CurrentAp -= taken; caster.CurrentAp += taken; }
        else { victim.CurrentMp -= taken; caster.CurrentMp += taken; }
        Emit($"  {caster.Name} steals {taken} {(ap ? "AP" : "MP")} from {victim.Name}.");
    }

    private void ApplyGrantAp(Fighter caster, Fighter ally, int amount)
    {
        amount = Math.Max(0, amount);
        if (amount <= 0) return;
        ally.CurrentAp += amount;
        Emit($"  {caster.Name} grants {ally.Name} +{amount} AP.");
        Raise(new StatusApplied(ally, StatusKind.DamageBuff, 1)); // reuse the buff cue for a positive flash
    }

    private void ApplyStatusEffect(Fighter target, StatusKind kind, int magnitude, int turns)
    {
        if (kind == StatusKind.None || turns <= 0) return;
        magnitude = Math.Max(0, magnitude); // never let a status grant resources / heal via damage
        var existing = target.Statuses.FirstOrDefault(s => s.Kind == kind);
        if (existing != null)
        {
            existing.Magnitude = magnitude;
            existing.Remaining = Math.Max(existing.Remaining, turns);
        }
        else
        {
            target.Statuses.Add(new StatusEffect(kind, magnitude, turns));
        }
        Emit($"  {target.Name} gains {kind} ({turns} turns).");
        Raise(new StatusApplied(target, kind, turns));
    }

    /// <summary>
    /// Dofus-style damage pipeline: roll, scale by (primary stat + Power + %damage + buffs),
    /// add flat damage, apply the critical bonus, then reduce by %resistance, flat resistance
    /// and shields.
    /// </summary>
    private static int ComputeDamage(Fighter caster, Fighter victim, Element element, int rolled, bool crit)
    {
        int percent = 100 + caster.PrimaryStatFor(element) + caster.Power
                      + caster.DamagePercent + caster.DamageBuffPercent + PassivePercent(caster);
        int scaled = rolled * percent / 100 + caster.FlatDamage + PassiveFlat(caster, victim);
        if (crit) scaled += (int)MathF.Round(scaled * 0.5f);
        int afterPct = scaled * (100 - victim.ResistanceFor(element)) / 100;
        int dmg = afterPct - victim.FlatResistanceFor(element) - victim.ShieldAmount;
        return Math.Max(0, dmg);
    }

    // Class passives (TITHE) read at damage time, so they visibly change what you watch happen.
    private static int PassivePercent(Fighter caster) => caster.Passive switch
    {
        "rage_below" => caster.Hp * 2 <= caster.MaxHp ? 30 : 0, // +30% while bloodied (Bulwark)
        _ => 0,
    };

    private static int PassiveFlat(Fighter caster, Fighter victim) => caster.Passive switch
    {
        "long_shot" => caster.Pos.DistanceTo(victim.Pos) >= 6 ? 4 : 0, // reward the kiter (Archer)
        "overchannel" => caster.CurrentAp * 2,                          // unspent AP banks damage (Cannon)
        _ => 0,
    };

    private void ApplyDamage(Fighter caster, Fighter victim, SpellEffect effect, bool crit)
    {
        int rolled = _rng.Roll(effect.Min, effect.Max);
        int dmg = ComputeDamage(caster, victim, effect.Element, rolled, crit);
        var at = victim.Pos;
        victim.Hp = Math.Max(0, victim.Hp - dmg);
        Emit($"  {victim.Name} takes {dmg} {effect.Element} damage{(crit ? " (CRIT)" : "")} ({victim.Hp}/{victim.MaxHp} HP).");
        Raise(new DamageDealt(victim, dmg, effect.Element, at, victim.Hp, crit));
        if (!victim.IsAlive)
        {
            Emit($"  {victim.Name} is defeated!");
            Raise(new FighterDied(victim, at));
        }

        // Damage reflection (renvoi): a portion returns to the attacker.
        if (victim.ReflectPercent > 0 && caster != victim && caster.IsAlive)
        {
            int reflected = dmg * victim.ReflectPercent / 100;
            if (reflected > 0)
            {
                var cAt = caster.Pos;
                caster.Hp = Math.Max(0, caster.Hp - reflected);
                Emit($"  {caster.Name} takes {reflected} reflected damage ({caster.Hp}/{caster.MaxHp} HP).");
                Raise(new DamageDealt(caster, reflected, effect.Element, cAt, caster.Hp));
                if (!caster.IsAlive) { Emit($"  {caster.Name} is defeated!"); Raise(new FighterDied(caster, cAt)); }
            }
        }
    }

    private void ApplyHeal(Fighter caster, Fighter victim, SpellEffect effect)
    {
        int rolled = _rng.Roll(effect.Min, effect.Max);
        int amount = rolled * (100 + caster.Intelligence) / 100;
        int before = victim.Hp;
        victim.Hp = Math.Min(victim.MaxHp, victim.Hp + amount);
        Emit($"  {victim.Name} recovers {victim.Hp - before} HP ({victim.Hp}/{victim.MaxHp}).");
        Raise(new HealApplied(victim, victim.Hp - before, victim.Pos, victim.Hp));
    }

    /// <summary>Push (away) or pull (toward) the caster along one orthogonal axis.</summary>
    private void ApplyShift(Fighter caster, Fighter victim, int cells, bool pull)
    {
        if (victim.Pos == caster.Pos || cells <= 0) return;
        if (victim.IsStabilized)
        {
            Emit($"  {victim.Name} is stabilized and holds its ground.");
            return;
        }

        int sx = Math.Sign(victim.Pos.X - caster.Pos.X);
        int sy = Math.Sign(victim.Pos.Y - caster.Pos.Y);
        int dx = pull ? -sx : sx;
        int dy = pull ? -sy : sy;
        // Collapse to the dominant orthogonal axis for a clean shift.
        if (dx != 0 && dy != 0)
        {
            if (Math.Abs(victim.Pos.X - caster.Pos.X) >= Math.Abs(victim.Pos.Y - caster.Pos.Y)) dy = 0;
            else dx = 0;
        }

        var path = new List<CellCoord> { victim.Pos };
        int moved = 0;
        for (int i = 0; i < cells; i++)
        {
            var next = victim.Pos.Offset(dx, dy);
            if (!Field.IsWalkable(next) || IsOccupied(next))
            {
                int collision = pull ? 0 : (cells - moved) * 5; // only shoves deal collision damage
                var at = victim.Pos;
                if (collision > 0)
                {
                    victim.Hp = Math.Max(0, victim.Hp - collision);
                    Emit($"  {victim.Name} slams into an obstacle for {collision} damage ({victim.Hp}/{victim.MaxHp} HP).");
                }
                Raise(new FighterPushed(victim, path, collision));
                if (collision > 0) Raise(new DamageDealt(victim, collision, Element.Neutral, at, victim.Hp));
                if (!victim.IsAlive)
                {
                    Emit($"  {victim.Name} is defeated!");
                    Raise(new FighterDied(victim, at));
                }
                return;
            }
            victim.Pos = next;
            path.Add(next);
            moved++;
        }
        if (moved > 0)
        {
            Emit($"  {victim.Name} is {(pull ? "pulled" : "pushed")} {moved} cell(s) to {victim.Pos}.");
            Raise(new FighterPushed(victim, path, 0));
        }
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
        TickTurnEnd(Current); // regen, then age/expire the ending fighter's statuses

        // Advance to the next living fighter, beginning turns (which tick statuses) as we go.
        // A fighter that dies to poison at the start of its turn is skipped.
        for (int guard = 0; guard <= _order.Count * 2; guard++)
        {
            if (Outcome != FightOutcome.Ongoing) return;

            _pointer++;
            if (_pointer >= _order.Count)
            {
                _pointer = 0;
                Round++;
                Emit($"=== Round {Round} ===");
            }
            if (!Current.IsAlive) continue;

            BeginTurnFor(Current);
            if (Current.IsAlive) return; // survived its start-of-turn ticks
        }
    }

    public FightOutcome Outcome
    {
        get
        {
            bool anyEnemy = _order.Any(f => f.Team == Team.Enemy && f.IsAlive);
            // You lose when your hero falls — allied summons don't keep the fight alive.
            bool heroAlive = _order.Any(f => f.PlayerControlled && f.IsAlive);
            if (!heroAlive) return FightOutcome.Defeat;
            if (!anyEnemy) return FightOutcome.Victory;
            return FightOutcome.Ongoing;
        }
    }
}

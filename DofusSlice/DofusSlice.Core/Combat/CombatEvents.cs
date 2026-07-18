using DofusSlice.Core.Grid;
using DofusSlice.Core.Spells;

namespace DofusSlice.Core.Combat;

/// <summary>
/// Structured, immutable records of what happened in the fight — a play-by-play the engine
/// raises alongside its text log. The renderer turns these into animations; because each
/// event snapshots the data it needs (paths, amounts, resulting HP), the animation layer can
/// replay them over time even though the model has already advanced to its final state.
/// The rules never depend on anyone listening, so the engine stays pure and testable.
/// </summary>
public abstract record CombatEvent;

public sealed record TurnStarted(Fighter Fighter, int Round) : CombatEvent;

public sealed record FighterMoved(Fighter Fighter, IReadOnlyList<CellCoord> Path, int MpSpent) : CombatEvent;

public sealed record SpellCast(Fighter Caster, SpellDef Spell, CellCoord Target) : CombatEvent;

public sealed record FighterTeleported(Fighter Fighter, CellCoord From, CellCoord To) : CombatEvent;

public sealed record FighterPushed(Fighter Fighter, IReadOnlyList<CellCoord> Path, int CollisionDamage) : CombatEvent;

public sealed record DamageDealt(Fighter Target, int Amount, Element Element, CellCoord At, int RemainingHp, bool Critical = false) : CombatEvent;

public sealed record SpellFizzled(Fighter Caster, SpellDef Spell) : CombatEvent;

public sealed record HealApplied(Fighter Target, int Amount, CellCoord At, int RemainingHp) : CombatEvent;

public sealed record FighterDied(Fighter Fighter, CellCoord At) : CombatEvent;

public sealed record FighterSummoned(Fighter Fighter) : CombatEvent;

public sealed record StatusApplied(Fighter Target, StatusKind Kind, int Turns) : CombatEvent;

public sealed record StatusExpired(Fighter Target, StatusKind Kind) : CombatEvent;

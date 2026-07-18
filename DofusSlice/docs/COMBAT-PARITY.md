# Combat parity with Dofus

Where our combat now stands against Dofus (Retro/1.29-era) mechanics. Everything under
"Implemented" is verified by the deterministic `sim effects` self-test (9/9) and the fight
sim; "Still missing" is honest scope not yet built.

## Implemented

**Economy & turn flow**
- AP (PA) / MP (PM), initiative turn order, 30-second turn timer with auto-end.
- Statuses tick at **turn start** (poison, MP drain) *and* **turn end** (regen, then expire).

**Damage pipeline** (`ComputeDamage`)
- `roll · (100 + primaryStat + Power + %damage + damageBuff)/100 + flatDamage`, then the
  critical bonus, then `·(100 − %resist)/100`, then `− flatResist − shield`.
- 5 elements, each scaling off its characteristic (Str/Int/Cha/Agi) + Power.

**Critical hits & failures**
- Per-spell crit rate (1-in-N) with a damage bonus; per-spell critical failure that fizzles
  the cast. Crits render as a larger gold number with a stronger screen-shake.

**Positioning**
- **Tackle / lock**: leaving an adjacent enemy's melee costs AP/MP via an Agility contest.
- **Rooted** (can't move) and **Stabilized** (can't be pushed/pulled) states.
- **Push**, **Pull**, and position **Swap**; push collision damage into obstacles.

**Targeting / AoE**
- Range (min/max), line-of-sight, line-only casting, per-turn cast caps, cooldowns.
- AoE shapes: single, circle, cross, and caster-directional **line** and **cone**.

**Effect library**
- Damage, Heal, **Lifesteal**, **AP/MP steal** (with Wisdom dodge), **damage Reflect**,
  **Regen**, DamageBuff, Shield, Poison, MP drain, Rooted, Stabilized, Teleport.

**Characteristics**
- Vitality (HP), Strength/Intelligence/Chance/Agility, Power, %damage, flat damage,
  Wisdom (AP/MP-steal resistance), % and flat elemental resistance.

**AI**
- Greedy mob brain: nearest target, best affordable reaching spell, else path into range.

## Still missing (honest scope)

**Bigger systems**
- **Summons** — creatures added mid-fight that take their own turn (needs a dynamic fighter
  list + initiative insertion; the engine currently fixes the roster at fight start).
- **Weapons** as a system distinct from spells (own AP cost / damage / crit).
- **Pre-fight placement phase** (position on starting cells; team placement).
- **Spell levels/variants** (a spell's stats change by level 1–6).

**Mechanics depth**
- **Glyphs / traps / portals** — persistent ground effects that trigger on step / end of turn.
- **Flee** to end a fight (needs map-border exit cells).
- **Tombstones / resurrection** (dead players become graves).
- **Exact Dofus line-of-sight** — ours is Bresenham centre-to-centre; Dofus uses a specific
  cell-corner algorithm with known quirks.
- **AP/MP-loss dodge** is a simplified Wisdom roll rather than the full Dofus probability.
- Effect breadth still short of Dofus's full catalogue (invisibility, invulnerability,
  carry/throw, state-conditional effects, per-AP scaling, erosion, etc.).

**AI**
- No kiting for ranged mobs, AoE avoidance, cover use, or target prioritization.

## Verify

```bash
dotnet run --project DofusSlice.Sim effects   # 9/9 mechanics self-test
dotnet run --project DofusSlice.Sim           # full auto-played fight log
```

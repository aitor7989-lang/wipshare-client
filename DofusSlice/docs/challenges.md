# Fight challenges — design + port mapping

Source: the Dofus challenge list (supplied by the owner from tofus.fr). This file is the
**engineering spec**, not a copy of that page: it keeps only the challenges that fit TITHE's
3-unit party and states each as a rule our engine can actually evaluate.

## How the system works (Dofus)

- Before the fight the leader is offered a **choice between two** randomly drawn challenges.
  Ordinary fights offer one challenge; **dungeon fights offer two**.
- The condition binds the **whole party**, not one unit.
- Success **multiplies the fight's reward**; each player picks whether the bonus lands on
  **XP or drops**. Bonus size is not flat — it scales with the fight (notably with monster
  count minus one).
- A challenge is only offered if it is **achievable**: spell-dependent ones require someone to
  actually hold that spell, and specific bosses suppress specific challenges.

## Port mapping for TITHE

TITHE has a placement beat to choose in, a `CombatEvent` stream to judge against, and
`TitheResolution.Resolve` to pay out — so most of these need **no engine change**.

### Tier 1 — evaluable off events we already raise

| Challenge | Rule (as we'd implement it) | Reads |
|---|---|---|
| Untouched | the crew loses no HP all fight | `DamageDealt` (player team) |
| Survivor | every crew member is alive at the end | `TitheResolution` |
| Incurable | the crew regains no HP | `HealApplied` |
| Statue | end each turn on the cell you began it | `TurnStarted` + `FighterMoved` |
| Zombie | spend at most 1 MP per turn | `FighterMoved.MpSpent` |
| Nomad | spend **all** MP each turn, and never get tackled | `FighterMoved` + tackle surcharge |
| Eager | end your turn with 0 AP left | `TurnStarted` (AP on end) |
| Timid | never end a turn adjacent to an enemy | positions at turn end |
| Bold | always end a turn adjacent to an enemy | positions at turn end |
| Clingy / Hermit | always / never end a turn adjacent to an ally | positions at turn end |
| Elemental | every attack uses one and the same element | `SpellCast` + effect element |
| Thrifty | each spell used at most once in the whole fight | `SpellCast` |
| Versatile | each spell used at most once per turn | `SpellCast` (we already track casts/turn) |
| Orderly / Cruel | kill enemies in descending / ascending level order | `FighterDied` |
| Marked / Reprieve | kill a named enemy first / last | `FighterDied` |
| Focus | finish a target before attacking a different one | `DamageDealt` + `FighterDied` |
| Elitist | concentrate every attack on one enemy until it dies | `DamageDealt` |
| Blitzkrieg | a damaged enemy must die before its next turn | `DamageDealt` + `TurnStarted` |
| Duel | only one crew member may ever attack a given enemy | `DamageDealt` |
| Share | every crew member lands at least one kill | `FighterDied` |
| Two for one | whoever kills must kill exactly two that turn | `FighterDied` |
| Hitman | kill in a designated order, re-designated on each death | `FighterDied` |

### Tier 2 — maps onto systems added this session

These land exactly on the drain/theft statuses added in the buffs pass:

| Challenge | Rule | Reads |
|---|---|---|
| Move along | never remove MP from an enemy | `MpDrain` / `StealMp` |
| Time flies | never remove AP from an enemy | `ApDrain` / `StealAp` |
| Out of sight | never remove range from an enemy | `RangeDebuff` / `StealRange` |
| Selfless | never be healed during your own turn | `HealApplied` + turn owner |

### Tier 3 — needs a feature we don't have yet

| Challenge | Blocked on |
|---|---|
| Mystic (spells only) | weapon attacks |
| Barbarian (weapon damage every turn) | weapon attacks |
| Clean hands (kill with no direct damage) | glyphs/traps + poison as a kill source |

### Not porting

Class-summon challenges (Arakne / Chaferfu / Cawotte), the Roulette one, the gender-based
pair, and the "mule" one — they depend on Dofus classes, spells or party shapes TITHE
doesn't have.

## Implementation plan

1. **`ChallengesJson`** table beside the other tables (every rule in this codebase lives in data).
   Each row: id, name, the rule text shown to the player, and a `kind` the evaluator switches on.
2. **Offer** during `UpdateTithePlacement` — two drawn, pick one; crypt rooms draw two like a
   dungeon. Filter the draw by feasibility exactly as Dofus does (never offer Timid *and* Bold,
   never offer a rule the party structurally cannot meet).
3. **Evaluate** in a `ChallengeWatch` subscribed in `WireEngine`, holding a small state machine
   per active challenge. It must **fail loudly the instant it breaks** — telling the player at the
   loot screen is useless.
4. **Pay out** in `TitheResolution.Resolve` as a multiplier scaled by pack size, with a
   stones-vs-XP toggle chosen before FIGHT.

## Why this is worth building

The audit found the crypt's six rooms replay identically every dive — the single biggest
replayability problem in the game. Challenges make the same room a different *puzzle* each time,
they cost almost nothing (data + one event subscriber), and they give the crypt a reason to be
re-entered other than loot.

# WANDERER — design brief

_Status: **design only**, nothing built. Rev 2, after an adversarial design panel (3 research
lenses + 3 critics + synthesis). Rev 1's own text is quoted where the panel refuted it, because
the refutations are more useful than the claims were._

> **Retitled deliberately.** Rev 1 called this "the next version". It is not. WANDERER ships as a
> **second mode in the same solution**, with TITHE runnable as a control and a kill date set.
> Rev 1 never stated why the shipping game is failing, which made it a licence to delete a working
> game before its replacement existed. The missing diagnosis, for the record: **TITHE made the same
> core bet — watched, not piloted — and never tested it**, because crew, city, placement and
> class-picker scaffolding meant nobody could tell whether the bet or the scaffolding was the
> problem. WANDERER is the right experiment precisely because it strips down to the bet. That
> argues for a controlled second mode, not a rewrite.

---

## 0. The pitch

A **solo** warrior walks into the deeps and never turns around. The path **generates ahead of him
as he steps onto it** and **collapses behind him** — that is the fog of war, and it makes every
choice irreversible. He moves **automatically**, spending all his MP every turn. You are not his
hands. You are the voice in his ear: you read what the torchlight shows, you tell him where to go
and what to avoid, and when the floor lights up with an ambush telegraph you get **one turn** to
decide how he meets it.

---

## 1. The turn

1. **ADVANCE** — the hero spends MP toward his waypoint. **Telegraphed spawns from last turn land
   HERE, after his movement.**
2. **REVEAL** — the frontier moves with him; ground appears at the edge of torchlight, and ground
   behind the collapse line ceases to exist.
3. **TRIGGER** — cells he crossed are checked. Plates, ambush markers, glyph wards.
4. **TELEGRAPH** — anything that fired paints **cells and consequence** — expected damage and ring
   size, not just "danger here".
5. **COMMAND** — the player's window. The only place the player acts.
6. **RESOLVE** — combat, glyphs, hazards. Torch burns.

> **Rev 1's worst bug, and it was structural.** Rev 1 resolved spawns in step 6 — *before* the
> hero's next ADVANCE. So choosing PUSH meant the ring materialised on top of you and only *then*
> did you move. §3 defines Push as "try to simply not be there when it fires", which requires
> movement inside the resolution the loop didn't have. Under the literal spec **you were
> surrounded whether you pushed or braced**, so Brace strictly dominated — the exact line §4
> claimed to have solved. The centrepiece beat was broken by its own step numbering. Moving spawns
> to ADVANCE of turn N+1 is a one-line fix and makes Push a genuine race.

---

## 2. Doctrine

Two orthogonal standing orders, always both set.

**GAIT** governs MP — and sight, which is what keeps it alive:
- `PROBE` — MP−1, **+2 sight**
- `MARCH` — full MP, +0 sight
- `SPRINT` — MP+1, **−2 sight**, and caps **total** AP spend at 2 this turn
- `ANCHOR` — 0 MP. This is the Brace.

**TEMPER** governs AP:
- `STRIKE` — highest-damage line at the priority target
- `WARD` — defensive/utility first
- `CULL` — strike only what dies this turn, otherwise **do not draw**; a non-lethal hit on a
  full-HP enemy **advances its telegraph by one turn**
- `BREAK` — control, push, disengage

Presets: Aggressive `MARCH+STRIKE` · Cautious `PROBE+WARD` · Flee `SPRINT+BREAK` · Hunt `MARCH+CULL`.

> **Why sight modifiers exist.** Without them SPRINT is correct on every non-combat turn — the
> torch burns per *turn*, not per tile, so SPRINT covers more depth per unit light, and its cost
> ("can't cast above 2 AP") is meaningless when there's nothing to cast at. That's a dead axis for
> ~75% of the game. ±2 not ±1: at Bright, +1 is +12.5% and worthless. Also: the AP clause had to
> become a *total* cap, because a per-ability cap made the 2-AP no-LoS sling the only weapon usable
> while fleeing, and sling+SPRINT+BREAK kited everything at the cheapest light rate in the game.
> The sling loses no-LoS for the same reason — it was quietly deleting the corridor-reading fantasy
> the whole pitch rests on.
>
> **CULL referenced a mechanic that doesn't exist.** "Bank the AP" — there is no AP carryover
> anywhere in the codebase; the block refreshes per turn. Rewritten above with the telegraph-delay
> rule that actually gives it teeth.

### VOICE

- Regen **+1**/turn, cap **4**. Carries over between stretches.
- **Waypoints, taboos and marks are FREE.** Navigation is a chore, not a decision.
- Brace / Push / Charge: **1** (atomic macros — the cheap correct answer must always be affordable).
- Manual per-axis doctrine change: **2**. Override: **4**.

> **Rev 1's Voice was a cooldown you never notice**, and its one justifying example refuted itself:
> *"if you burned Voice on a greedy mark on turn 39, you have nothing left on turn 41"* — a mark
> cost 1, so turn 39 is 3→2, turn 40 regens to 3, turn 41 you're full. Income exactly equalled the
> modal action's cost, so you were at cap at every crisis by construction. "Doesn't carry between
> stretches" also inverted the intent: unspent Voice at cap is pure waste, so dumping it on
> marginal marks was strictly correct — fidgeting, the opposite of the restraint it claimed to buy.

---

## 3. The telegraph, and the three answers

**Being ambushed is not the failure state — it is the content.** Nothing may let a cautious player
detect and defuse triggers.

- **BRACE** (`ANCHOR`) — meet them ready, but surrounded.
- **PUSH** (`SPRINT`) — race: you move, *then* they arrive. Whether they reach you depends on
  distance and constriction. The ground ahead is ungenerated, so a second trigger can close both
  rings at once.
- **CHARGE** — stand on the emergence cells before they populate: take damage and **displace** the
  spawn instead of eating a full ring.

The prep turn is **fully open** — Brace/Push/Charge are priced favourites, not the only options.

> **Vindicated, not invented.** Into the Breach shipped this exact telegraph — every attack shown a
> turn ahead, exact tile, exact damage, never lying — *and* shipped Charge-onto-the-emergence-tile
> as its most celebrated verb. The concept took no damage from the panel. It only needed the
> turn-order fix to become the thing it described.

### Glyph wards

| Glyph | Counters | Forces |
|---|---|---|
| **Root** — no teleport/jump | mobility essences | Charge or Brace |
| **Drain** — MP cut | running | Brace or Charge |
| **Seal** — essences off N turns | buffing | Push or Charge |

Seal is the load-bearing one: with only Root and Drain, the deeps close doors until Brace is all
that's left.

---

## 4. Light — the clock, the risk dial, **and how loud you are**

- Torch = **15 turns**, carry two = 30 turns against a ~40-turn floor. **You cannot light a whole
  floor.** Darkness is a place you visit, not a failure state.
- `ANCHOR` burns **2**. Always-Brace across ~15 telegraphs costs a full torch — half the run clock.
- Torch swap on burnout is **automatic, never a player choice**; spares cost a pack slot.
- **Ring size scales with light**: Bright sight 8 / ring 6 · Dim 5 / 4 · Dark 2 / 2.

> **Two Rev 1 errors, one of them mine at the arithmetic level.** (a) 30 turns × 2 against a
> ~40-turn floor is a **50% surplus** — a resource you have more than enough of prices nothing, so
> every "light disciplines X" argument downstream was void. One ANCHOR turn cost 1.67% of the run.
> The instinct that hesitation is paid in blindness was right *in kind and off by an order of
> magnitude in degree*. (b) Three tiers of a monotone scalar **is not a choice** — Bright strictly
> dominates and Dim/Dark are decay states, which reproduces the Darkest Dungeon failure Rev 1 cited
> while claiming to fix it. DD's pitch-black at least *paid*. The fix is the missing non-monotonic
> axis: **darkness buys fewer enemies at the price of blindness.** Two independent panel lenses
> converged on this.

---

## 5. The generator

Two layers: a **segment grammar** (Markov chain over archetypes, each carrying a **Constriction**
number that generator, ambush placer, AI and sim all read) for macro; **Wang tiles** for micro.
One integer: `Frontier == depth(hero) + Sight`. Rows beyond it do not exist — advancing the
frontier *is* generation *and is* the reveal. No hidden pre-generated buffer, because that creates
two competing truths about what exists.

WFC's usual infinite-world weakness — independent chunks conflicting at seams — is its *best* case
here: one advancing frontier means exactly one seam, and the deleted tail stops constraints
propagating backwards. **This reasoning survived the panel intact. It is cut from the slice on
cost, not merit** (§8).

---

## 6. Weapons, essences, stats

### The weapon IS the basic attack — survived, and got stronger

Fists are simply the worst weapon, not a permanent fixture. Every weapon authors the whole attack:
AP, min/max range, shape, element, damage band, one rider. Fists' rider is **+1 MP on kill** — Rev 1
said +1 *AP*, which at 6 AP turned the designated worst weapon into a six-kill-per-turn cleave
engine, optimal against exactly the ambush ring the design is built around.

**Ship no use-based weapon proficiency, ever.** Rev 1 worried about grinding and was right for the
wrong reason. The killer objection isn't grind, it's that **proficiency manufactures switching
cost** — and with two slots, free swaps and accumulated proficiency, the correct play is to *never
adopt the bow*, which destroys the one claim this design leans hardest on. Weapons scale off
characteristics instead: **your stat investment is your proficiency** — permanent, non-grindable,
and it makes every found weapon a genuine carry-or-leave test of whether it fits your build.

(Rev 1 also claimed the one-way path makes use-grinding structurally impossible. Half wrong: three
vectors survive it — engage-everything routing, intra-combat whiff-farming, and the reverse-grind
of refusing to swap. Only the classic spatial form is blocked.)

### Essences are carved, not dropped — mechanism intact, rates bent

Bearers **15%** of spawns (was 8%), visibly marked before contact, each with a stated harvest
condition. **A failed harvest leaves a SHARD**; N shards carve a lesser essence.

Conditions must be **weapon-agnostic and player-controllable**, keyed to doctrine and clock:
*felled while the only enemy within 3 cells* · *felled the turn after it telegraphs* · *felled with
no Voice spent this stretch* · *felled while torch is Dim or darker*.

> Rev 1's conditions were weapon-gated — "felled from 5+ cells" is free with a war bow and
> impossible in melee, roughly a **4× essence gap between builds**. That replaces a loot dice-roll
> with a dice-roll on the weapon table. And 8% × condition × carve cost plausibly put the *median
> run below one essence* — Diablo 3's launch failure, where the identity-defining drop is so rare
> the median session has no identity. Monster Hunter part-breaks are the good precedent, but MH
> lets you re-fight the monster; a one-way descent gives you one attempt.
>
> The mechanism itself took no hits — even the hostile playtester called it the best idea in the
> document, because it makes the survival-optimal doctrine and the condition-optimal doctrine
> *diverge*. That divergence is the strongest single answer to the screensaver charge.

### Stats — six, each with two jobs

**Strength · Intelligence · Chance · Agility · Resolve · Perception.** Every primary carries a
damage face *and* a second non-damage job, and that pairing — not class, not level-up abilities —
is the whole specialisation engine. Strength → Earth damage + pack slots. Agility → Air + whether
PUSH actually disengages. Intelligence → Fire + heal/shield value in a game with no town restore.
Chance → Water + carve cost. Resolve → whether Drain/Seal/Root land at full duration, i.e. whether
the deeps can delete your escape verb. Perception → how legible the ground ahead is.

**Cut Power** — verified at `CombatEngine.cs:664` it sits in the same additive bucket as the
primary stat, so +10 Power and +10 Strength are byte-identical to the pipeline. It cannot express a
choice, and being element-agnostic it actively erodes the four primaries as an identity system.
**Cut Range as investable** — it lets you buy your way into being an archer, negating "the bow is
what made you an archer". **Cut Critical as investable** — a random 1.5× corrupts both the
telegraph's honesty and the single-turn harvest conditions.

**Perception is information-QUALITY, never quantity**: ±1 radius within a tier, and it *never*
reveals ambush markers. Its payload is reading Constriction ahead and reading a bearer's harvest
condition before contact. Otherwise it's win-more (information is the whole game), it slowly
violates §3, and it collapses the light tiers — a high-Perception hero at Dim sees what a low one
sees at Bright.

### Inventory

2 weapon slots + 4 pack slots, of which torches occupy 2 at baseline. Every pickup is literally
*"is this worth 15 turns of light?"* Strength grants pack slots.

---

## 7. Codebase: keep / rebuild / throw

**Keep:** `CombatEngine`'s **resolver** — TryCast, TryMove, statuses, tackle, ForecastShift —
**behind a new turn driver**. `Policy.cs` and `MobBrain.cs`, a 568-line autobattler brain whose own
header says its policies are "deliberately legible so a spectator can narrate the fight" — the
closest existing thing to GAIT×TEMPER, and absent from Rev 1's lists entirely. `SpellDef` (it
already carries ApCost, MinRange/MaxRange, LineOnly, RequiresLineOfSight, Area, Effects — so
"the weapon IS the basic attack" is a data table, not a new system). `CrtPass`, `Mono`, 4:3.

**Throw:** crew, city, placement phase, class picker, the bell, the loop.

### Where Rev 1's engineering claims were wrong

- **"CombatEngine unchanged" is false at turn one.** `CombatEngine.cs:834` returns
  `FightOutcome.Victory` whenever no living `Team.Enemy` fighter exists, and `EndTurn()` at `:818`
  hard-returns unless `Ongoing`. WANDERER's *normal* state — a solo hero walking an empty corridor —
  is a **finished, won fight**: the turn pump stops and every Round-keyed cooldown freezes. Also
  the initiative weave is built once at `Start()`, `RemoveTheDead()` is a no-op so `_order` only
  grows, `FighterAt` is a LINQ scan called per-cell-per-turn, and `ReplaceFighter` throws once
  `Round > 0`. **Fix: one engine per ambush, not one per run** — which dissolves all of it at once,
  and puts weapon swaps between engines, i.e. out of combat, exactly as §6 requires.
- **The ring-buffer warning was backwards.** `Battlefield.Index` is private and all access goes
  through the public surface, so an origin-window with monotone Y wraps the storage index
  *invisibly* and no coordinate ever changes value. The memmove Rev 1 recommended is the option
  that actually forces rebasing `Fighter.Pos`. The caches Rev 1 warned about don't exist yet.
- **AsciiPass cannot be promoted to primary renderer,** and "a rune on the floor is literally a
  glyph" is precisely what it structurally cannot do. It is a **luma-density post-effect**: glyph
  choice is a function of brightness only (so you cannot draw `@` for the hero and `#` for a wall),
  its grid is derived from screen pixels with no correspondence to `CellCoord`, and its 11-entry
  ramp contains no letters or runes. If glyphs are native output the right architecture is a
  cols×rows buffer of (glyph, fg, bg) filled by iterating cells and choosing glyphs *semantically*
  from TileKind. ~40 lines of AsciiPass survive that — the atlas and the one-quad-per-cell draw
  loop, whose performance reasoning is correct.
- **The Sim's *code* is not the asset Rev 1 claimed.** `CampaignSim`, `TitheSim` and `HeroAuto` all
  die with the campaign (HeroAuto is hardcoded to the Iop kit). What survives is the **discipline
  and the harness pattern** — and the fact that `DofusSlice.Sim` references only `Core`, which has
  no MonoGame dependency at all. Headless is genuinely available; the reusable line count is ~0.

---

## 8. The slice

### Step zero — costs zero new code, do it first

**Shrink TITHE's crew to one unit and cap the player to one command window per turn. Play it for
twenty minutes.** If watched-plus-one-window isn't engaging inside a game that already exists and
already renders, stop here. This tests the shared bet without building anything.

### The one question

> **"Does the player's input change the outcome often enough, and legibly enough, to be worth
> showing up for?"**

Not "do the three answers win equally". Balance is a knob; this is a kill switch.

> Rev 1's slice was **both too big and unable to fail**. Too big: it built the generator, the most
> expensive item in it, which cannot change the answer to its own question since Constriction is a
> scalar the sim can just set. Unable to fail: "do Brace/Push/Charge each win often enough" while
> sweeping four knobs will *always* find a region where three win rates land within a few points —
> a tuning exercise in a falsification costume.

### Built (headless, no renderer)

`Descent` type owning frontier/torch/doctrine/triggers (~150) · the six-phase outer loop (~150) ·
**one CombatEngine per ambush** · a hand-authored bank of ~24-row corridors with Constriction as a
parameter, **no generator** (~50) · visibility state, which does not exist today — TileKind has no
"unseen" member — reusing `LineOfSight.HasLineOfSight` (~100) · GAIT×TEMPER with the revised Voice
economy and a policy honouring all 16 pairs, the real cost and real risk (~400–600) · **two enemy
archetypes with opposed geometry** — a ranged ring that punishes Brace, a melee ring that punishes
Charge (one archetype guarantees an uninterpretable negative, since situational variance *is* the
anti-dominance mechanism) · one weapon as a SpellDef (~50) · `WandererSim` harness (~200).

**~1,100–1,300 lines.** The generator is the line that got cut.

### Measured

1. **Input-sensitivity rate — the verdict metric.** For every COMMAND window, re-run it under each
   available answer and compare outcome distributions. What fraction of windows is the player's
   input outcome-changing? **Below ~40% this is a screensaver with a command prompt bolted on**,
   however well the three answers balance.
2. **Confusion matrix** over (constriction, ring size, HP%, torch tier). Pass: each answer correct
   in 25–40% of telegraph states, none correct in more than ~70% of any state class. Sweep for
   strategies that **never lose** as hard as for ones that always lose — this project already
   shipped a greedy heuristic that wiped 100% of the time and banked nothing.
3. **The legibility test, zero engine work.** Static mockups of ten telegraph states — five the sim
   calls unambiguous, five marginal. Show a person each for five seconds. Can they name the right
   answer? Equal win rates are noise if the player can't tell which state they're in. This also
   forces the ASCII decision early: an 11-entry brightness ramp is a poor substrate for "which
   cells are telegraphed, and where will he be standing".

**Out:** generator, essences, glyphs, loot, weapon variety, rolling window, coordinate rebasing,
all rendering.

---

## 9. The riskiest assumption, still unproven after all of that

**That per-window engagement scales to session-length engagement — that the 200th COMMAND window is
still worth showing up for.**

Everything the slice measures is local. Input-sensitivity is per window; the confusion matrix is a
distribution over window states; the legibility test shows one frozen moment. All three can come
back green while sitting through a thirty-minute descent of the identical six-phase loop is
something a player stops attending to — the failure isn't that any turn is bad, it's that the
*sequence* stops surprising. **Loop Hero is the exact precedent and the exact warning**: players ran
it at 2–4× and looked away, and no per-turn analysis would have caught it.

The slice deliberately makes this *worse* to stay a clean test: cutting essences, glyphs, loot,
weapon variety and the generator removes the five systems whose whole job is making hour two differ
from hour one. And the design's own answer to session variety — that the dungeon becomes
"specifically hostile to the identity you built" — is the one claim no panelist could evaluate,
because it depends on essences and glyphs existing.

**So a green slice authorises a second slice, not production.** That one is a full descent with
essences and glyphs in, played by a human end to end, measured on whether they leave the room.
Until someone sits through thirty unbroken minutes, the central bet — that watching a hero you
cannot pilot holds a whole session — stays exactly as unproven as it is today. Which is the bet
TITHE already made, and never tested.

---

## 10. Still open

- One continuous descent, or floors with a breath between them?
- Does the carve cost torch, HP, or Voice?
- Does the hero get a say — morale/wounds that make him refuse an order?
- What replaces `PixIso`/`SpriteBank`/`BattleAnimator`? In a game you don't pilot, animation is the
  entire feedback channel, and Rev 1 never decided their fate.

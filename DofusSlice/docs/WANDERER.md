# WANDERER — design brief for the next version

_Status: **design only**. Nothing here is built. This is the output of a brainstorming session
plus a partial design-panel run; it exists so the thinking survives the session that produced it._

---

## 0. The pitch

A **solo** warrior walks into the deeps and never turns around. The path **generates ahead of him
as he steps onto it** and **collapses behind him** — that is the fog of war, and it means every
choice is irreversible. He moves **automatically**, spending all his MP every turn. You are not
his hands. You are the voice in his ear: you look at what the torchlight shows, you tell him
where to go and what to avoid, and when the floor lights up with an ambush telegraph you get
**exactly one turn** to decide how he meets it.

This is not Loop Hero. There is no circuit and no base. It is one continuous descent.

---

## 1. The turn

The whole game is this sequence, repeated:

1. **ADVANCE** — the hero spends all MP toward his current waypoint. No player input.
2. **REVEAL** — the frontier moves with him; new ground comes into existence at the edge of
   torchlight. Ground behind the collapse line ceases to exist.
3. **TRIGGER** — the cells he crossed are checked. Pressure plates, ambush markers, glyph wards.
4. **TELEGRAPH** — anything that fired paints its intent on the grid: *enemies materialise on
   these cells next turn.* This is the promise the game makes and always keeps.
5. **COMMAND** — the player's window. Spend Voice: change doctrine, drop a waypoint, mark a
   target, override. This is the only place the player acts.
6. **RESOLVE** — combat, spawns, glyphs, hazards all execute. Torch burns 1.

The design lives in step 5 being *one* window, in step 4 being *honest*, and in step 2 being
*irreversible*.

---

## 2. Doctrine — how you command a hero who won't take orders

A single stance list is too thin: *"sprint through the corridor but do not engage"* cannot be
said with one word. So doctrine is **two orthogonal standing orders**, always both set.

**GAIT** — governs MP:
- `MARCH` — spend all MP. The default.
- `PROBE` — spend MP−1, end on the best cover cell, +1 sight.
- `SPRINT` — spend MP+1, but cannot cast anything above 2 AP this turn.
- `ANCHOR` — spend 0 MP. This is the Brace.

**TEMPER** — governs AP:
- `STRIKE` — highest-damage line at the priority target.
- `WARD` — defensive and utility first; attack only with leftover AP.
- `CULL` — spend AP only on targets killable this turn, otherwise bank it.
- `BREAK` — control, push, disengage; deny enemy AP/MP and buy distance.

Ship the four plain words as **presets** over the pair — Aggressive = `MARCH+STRIKE`, Cautious =
`PROBE+WARD`, Flee = `SPRINT+BREAK`, Hunt = `MARCH+CULL` — with the axes exposed underneath.
Four words on the on-ramp, sixteen meaningful cells for someone who wants them.

### VOICE — the command currency

Free directives are weightless; the player would retune every turn and no choice would cost
anything. So:

- Voice caps at **3**, gains **+1** at the start of each COMMAND phase.
- Change either doctrine slot: **1**. Drop a waypoint / taboo / mark: **1**. Override: **3**.
- It does not carry between stretches.

The tension this creates is the right one: *intervene now, or keep the ability to intervene at
the crisis you can already see two turns out.* It is also what makes a death legibly yours —
if you burned Voice on a greedy mark on turn 39, you have nothing left on turn 41.

---

## 3. The telegraph, and the three answers

When an ambush telegraphs, the player has one turn and three real answers. **Being ambushed is
not the failure state — it is the content.** Nothing in the design should let a cautious player
detect and defuse triggers, because that deletes the core beat.

- **BRACE** (`ANCHOR`) — stop, buff, guard. You meet them ready, but you meet them surrounded.
- **PUSH** (`SPRINT`) — try to simply not be there when it fires. But the ground ahead is
  ungenerated: a second trigger can fire while you are mid-flight and close both rings on you at
  once, or there is simply something already standing there and you arrive unbuffed with a pack
  on your heels.
- **CHARGE** (`MARCH+STRIKE` into the marked cells) — stand on the ambush before it populates.
  Take it on your terms, deep inside it.

Which is correct depends on the situation — ring size, distance, what is behind you, whether the
corridor ahead is survivable at current HP. **The stances are not balanced against each other;
the situations vary.** That is what stops one doctrine from dominating.

### Glyph wards — the deeps answer your build

Deeper floors pre-place glyphs (Feca-style) that each **counter one escape verb**:

| Glyph | Counters | Forces |
|---|---|---|
| **Root** — cannot teleport/jump out | mobility essences | Charge or Brace |
| **Drain** — MP reduced while standing on it | running | Brace or Charge |
| **Seal** — essences disabled for N turns | buffing | Push or Charge |

The third one matters most: with only Root and Drain, Brace becomes correct every time on deep
floors. Every answer needs a counter or the deeps just close doors until one remains.

The dungeon is therefore not "harder deeper" by numbers — it is **specifically hostile to the
identity you happened to build**, which is a far better curve for a classless hero.

Glyphs are also nearly free to render: in a 1-bit ASCII grid a rune on the floor is literally a
glyph, generated the same way as the empty-slot quatrefoil.

---

## 4. Light is the clock and the risk dial

The torch burns **1 per turn**. It is a consumable. This does more work than it looks like:

- **Brace costs a turn, therefore Brace costs light.** Hesitation is paid for in future
  blindness. This self-corrects the "always Brace" degenerate line without any extra rule.
- **Light is information, and information is the whole game** when Push is a blind gamble.
  Gear/essences that extend sight compete for slots against raw damage — a genuinely hard build
  question rather than a stat-stick comparison.
- It replaces the bell as the run clock, and it is diegetic.

**Three tiers, not a gradient**: Bright (8) / Dim (5) / Dark (2).

> **Negative precedent, take it seriously.** Darkest Dungeon's light meter is this exact idea and
> its documented flaw is that only the darkest tier paid enough to justify its risk — every
> intermediate level was a waypoint players skipped. Dim must be a destination, not a stage.

Starting guess: ~30 turns per torch. But the number that matters is **torches per segment**, not
turns per torch. At ~4 turns/segment and ~10 segments/floor, 30 turns ≈ ¾ of a floor, so you carry
two. **Do not hand-tune this — sweep it in the Sim.**

---

## 5. The generator

The sliding window makes this a *much* easier problem than normal dungeon generation. Whole-map
algorithms (BSP, cellular automata) are the wrong family: they are global, and there is no global
map — only a ~24-tile window that advances and destroys itself.

Notably, WFC's usual weakness for infinite worlds — that it can only solve cells adjacent to
solved ones, so independent chunks conflict at their seams — **is not a weakness here.** A single
advancing frontier is its best case: there is exactly one seam, always, and the tail is deleted so
constraints cannot propagate backwards forever.

**Two layers:**

- **Macro — a segment grammar.** A weighted Markov chain picks the next archetype: tight corridor,
  chamber, fork, gallery, collapse. *The game design lives here*, because the ambush concept needs
  **controlled** terrain — pure drunkard's-walk cannot promise a chokepoint when the design needs
  one, and if it can't, the whole Brace/Push/Charge tension fires at random. Each segment carries a
  **Constriction number** that the generator, the ambush placer, the AI and the Sim all read from
  the same field.
- **Micro — Wang tiles** (cheap, edge-matched) filling segment interiors, upgradeable to WFC if it
  reads as repetitive.

Generate ~16 ahead (double the sight radius) so the frontier is never a visible seam; free the tail
a few tiles past the light.

**One integer.** `Frontier == depth(hero) + Sight`. Rows beyond it *do not exist* — no tiles, no
triggers, no entities. Advancing the frontier **is** generation **and is** the reveal, in one call.
The alternative (a hidden pre-generated buffer behind the fog) creates two competing truths — what
exists vs. what is shown — and a whole bug class where a telegraph lands on a cell the player
cannot see.

---

## 6. Weapons, essences, stats

### The weapon IS the basic attack

Delete "punch" as a permanent fixture — **fists are simply the worst weapon**. Every weapon
authors the whole basic attack: AP cost, min/max range, shape, element, damage band, and one rider.
Sketch: fists (2 AP, rng 1, weak, +1 AP refund on kill) · short sword (3 AP, rng 1) · sabre (3 AP,
cleaves 2 in a line) · mace (4 AP, big, push 1) · spear (3 AP, rng 2) · short bow (3 AP, rng 2–5
LoS, −2 dmg at rng 1) · war bow (4 AP, rng 3–7, cannot fire at rng ≤2) · sling (2 AP, rng 2–4, no
LoS) · censer (3 AP, rng 1–3, fire + poison).

This is **load-bearing**, not flavour: it is what lets essences be brutally rare *without* an empty
early game. Picking up a bow at minute 8 rewrites how the hero fights, which corridors are lethal,
and which doctrine is correct. The owner's own example — *"as an archer you know that corridor is
dangerous"* — only means anything if **the bow is what made you an archer**.

Weapons drop ~1 per 8 encounters (~10× the essence rate). Two carried, swap free out of combat.

### Essences are CARVED, not dropped

Essences never appear in a loot roll. Three gates:

1. ~8% of spawned enemies are **essence bearers**, visibly marked *before* contact.
2. Each bearer states a **harvest condition** it must die under — felled in a single turn; felled
   before it acts; felled from 5+ cells; felled with no enemy adjacent to you.
3. The carve itself costs something (proposed: torch/time, which ties it back to the clock).

This turns ability acquisition into **a puzzle you can see coming and can fail**, rather than a dice
roll — which is the only way "very rare" stays interesting instead of feeling stingy.

### Stats

Keep the existing Dofus-style characteristic block (already implemented). Kill any stat that does
not change a decision. Add **Perception** as the sight/light stat, since sight is now a currency.

### Inventory

No town, no stash, no backtracking — so inventory is a pure **carry-or-leave** problem. Small slot
count, and taking something means dropping something. Tense and simple, and it needs no new UI
metaphor.

---

## 7. What we keep and what we throw away

**Keep:**
- `CombatEngine` — the tactical resolver, unchanged.
- `DofusSlice.Sim` — becomes the balance oracle **on day one** rather than an afterthought.
- The whole render stack: `Mono`, `CrtPass` (curve, bloom, scanlines), 4:3, and `AsciiPass`, which
  is promoted from an F6 toggle to **the** renderer.
- Content tables: mobs, gear families, essence definitions, statuses.

**Throw away:** the crew/party, the city and its NPCs, the placement phase, the class picker, the
bell (replaced by the torch), the loop/circuit structure.

### Known technical obstacles (verified against the code, not guessed)

- `CombatEngine.Field` is a **get-only** property — the field cannot be swapped once an engine
  exists. The rolling window must therefore mutate one long-lived `Battlefield` in place.
- **A ring buffer would silently break the engine.** `CellCoord.DistanceTo` is plain Manhattan and
  `Battlefield.Orthogonal` is bounds-clipped, so range checks, LoS and pathfinding would all lie
  across a wrapped seam. Instead: memmove the rows down once per turn and rewrite the top — a few
  hundred cells is free, and every existing system keeps working byte-for-byte.
- Scrolling must **rebase every stored coordinate** outside `Fighter.Pos`: trigger cells, telegraph
  anchors, player marks, AI target caches.
- Weapons currently carry **no** damage/AP/range at all, so §6 fills a known hole rather than
  adding a system.

---

## 8. The vertical slice — what proves or kills this

Build the smallest thing that answers **one measurable question**, before any content:

> With an auto-moving hero, a bounded torch, and telegraphed ambushes — do Brace / Push / Charge
> each win often enough to be worth choosing, or does one dominate?

Slice contents: a rolling corridor with the segment grammar (tight/open/fork only), one enemy
archetype, one ambush trigger, the telegraph, the three answers, the torch burning down. No
essences, no glyphs, no loot.

Then point the **Sim** at it and sweep: sight radius, torch length, constriction frequency, ring
size. We can learn whether this design is a game *in simulation*, before building a single screen
of content. Not doing that is the exact mistake this project has already made once.

---

## 9. Open questions

- How blind should Push be? Fully unknown ground makes deaths feel arbitrary; terrain that signals
  risk (tight ⇒ chains more often) means you are reading odds. **Recommendation: legible terrain.**
- Is the prep turn strictly Brace/Push/Charge, or fully open with those as common cases?
- Does the carve cost torch, HP, or a Voice bank?
- One continuous descent, or floors with a breath between them?
- Does the hero have any say — a morale/wound system that makes him refuse an order?

# Engine gap analysis — what our 2D tactical slice is missing

A technical audit of the DofusSlice engine against what a mature 2D isometric tactical RPG
(Dofus-style) needs, grounded in a look at how the open-source Dofus 1.29 emulators and
common 2D-engine techniques work. Each item is tagged **[P1]** (core, biggest impact),
**[P2]** (fidelity/feel), or **[P3]** (engineering/robustness).

> On the open-source Dofus repos: the community emulators (Araknemu, Ancestra, Jumbo, gofus,
> GDCore…) and the `ArakneUtils` algorithm library are useful to **study** for formulas,
> protocol and the map/cell model. Reusing their *code* is subject to each repo's license
> (mostly GPL — copying verbatim would make our code GPL too). Their **art/assets are not
> included** and remain Ankama's copyright. We port ideas, not their art.

## Baseline — what we already have

Iso grid + obstacles, A* pathfinding + geodesic distance field, Bresenham line-of-sight,
initiative turns with AP/MP, spells (range / LoS / line / AoE / cooldown / cast-limits),
elemental damage with resist, push+collision, teleport, greedy mob AI, a structured
combat-event stream, an animation layer (slide / lunge / impact flash / floating numbers /
hit flash / death fade / eased HP), a 30 s turn timer, turn-order timeline, and an external
sprite pipeline with feet-anchored depth sorting. The **Core is deterministic** (seeded RNG),
which is a strong foundation for replays/netcode.

## Sprite sorting (the specific ask) — where we stand

Our renderer sorts rocks + fighters in one pass by **feet (base-cell) screen-Y**, painter's
order. Key insight: in a 2:1 iso projection `feetY = originY + (x + y) * tileH/2`, so
**sorting by feet-Y is identical to the canonical `(x + y)` iso depth order.** For our
current single-cell, similar-size sprites, that is the *correct* algorithm — not a hack.

What still breaks and needs work:

- **[P2] Overhead layers.** Some map objects must always draw *above* characters (roof tops,
  tree canopy, high walls). Depth-sorting can't express "always on top" — these need an
  explicit layer drawn after entities. Real Dofus maps carry object layers flagged for this.
- **[P2] Multi-cell / oversized sprites.** A sprite that visually covers several cells (a big
  monster, a 2-wide prop) can overlap neighbours ambiguously; anchor-Y sorting is no longer
  sufficient. The robust fixes are a **topological "is-behind" sort** (nodes = sprites, edges
  = occlusion pairs) or splitting the sprite across cells. Topological sort is also required
  if sprites have **semi-transparency** (the depth buffer can't handle that). See sources.
- **[P3] Tie-breaking.** Equal feet-Y currently orders rocks-before-fighters arbitrarily;
  formalize as an explicit per-cell sub-layer (floor < floor-deco < entities < overhead).

## [P1] The core 2D-game pieces

- **Frame animation / spritesheets.** ✅ **Done.** Directional strips
  (`{name}_{state}_{dir}_{frames}.png`) with idle / walk / cast / hurt / die cycles, driven by
  an animation state machine that reads the combat-event stream (`FighterMoved` → walk,
  `SpellCast` → cast, hit → hurt, death → die). 32-bit RGBA is premultiplied on load; missing
  facings mirror from SE/NE; everything falls back to the procedural placeholders.
- **Directional facing.** ✅ **Done.** 4-way iso facing derived from movement/cast direction.
- **Camera.** ✅ **Done.** `Camera2D` follows the active fighter, wheel-zooms (0.6x-2.2x),
  clamps to the map's world bounds, and screen-shakes on hits (synced via the animator).
  Rendering is split into a camera-transformed world pass and a screen-space HUD pass.
- **Data-driven maps + richer cell model.** ✅ **Done.** Cells now carry a `TileKind`
  (grass/grass2/dirt/path/rock/void) that derives walkability + line-of-sight; maps are
  JSON ASCII-grids with a spawn legend (`MapData` + `MapLoader`), the game loads
  `maps/incarnam.json`, and the renderer draws per tile-kind (grass/dirt sprites, void pits).
  Still open: ground elevation and object/overhead layers.
- **Status-effect / buff system.** ✅ **Done.** Timed statuses (DamageBuff / Shield / Poison /
  MpDrain) tick at each fighter's turn start (poison damage, MP drain, then age/expire),
  modify outgoing/incoming damage, and render as per-fighter pips. Iop's "Power" self-buff and
  the Gobball's poison headbutt exercise it. Still open: summons and a turn-*end* queue.

## [P2] Combat fidelity & game feel

- **Hover feedback.** Dofus previews the **path + MP cost** when you hover a move cell, and an
  **estimated-damage** range when you hover a target with a spell selected. We show neither.
- **Spell/target tooltips** and right-click unit info.
- **Critical hits & failures**, and a **higher-fidelity damage formula** (flat + % resist,
  damage bonuses, crit bonus, Dofus rounding) — ours is a simplified single term.
- **More AoE shapes** (lines, cones/rectangles, rings) and **pull / swap** in addition to push.
- **Summons** — combat assumes a fixed fighter list; Dofus adds entities mid-fight.
- **Sound** — no SFX or music; per-action SFX alone is a big feel upgrade.
- **Pre-fight placement phase** — position your fighter on starting cells before combat.

## [P3] Engineering & robustness

- **Fixed-timestep accumulator + render interpolation** (the Gaffer pattern). Low priority for
  a turn-based game with no physics, but it's the clean base for deterministic **replays**.
- **Replays / save states.** We're well-positioned: seeded RNG + the event stream already
  capture a fight; persisting `seed + inputs` would reproduce it exactly.
- **Unit tests** for pathfinding, line-of-sight and the damage formula (only the end-to-end
  sim exists today).
- **Smarter AI** — threat/target scoring, kiting for ranged mobs, using LoS and cover, and not
  stepping into its own AoE.
- **Content as data** — spells/bestiary are hardcoded in C#; move them to JSON for hot-iteration.

## Suggested order

1. ~~Spritesheet animation + directional facing~~ ✅ done.
2. ~~Camera (follow + clamp + shake)~~ ✅ done.
3. ~~Data-driven maps + richer cell model + a loader~~ ✅ done.
4. ~~Status-effect system (turn-tick durations)~~ ✅ done.
5. Hover previews (MP cost + estimated damage) ✅ done; crits ✅ done.
6. **Next:** sound; overhead map layers; summons; pre-fight placement.

All of P1 is implemented, plus much of the P2 combat depth (see `COMBAT-PARITY.md`) and a
hardening pass. Remaining work is the rest of P2 (sound, overhead layers) and P3 (summons,
placement phase, smarter mob AI).

## Sources

- Isometric depth sorting (topological vs z-buffer, semi-transparency):
  <https://mazebert.com/forum/news/isometric-depth-sorting--id775/>,
  <https://gamedev.net/forums/topic/470599-isometric-depth-sorting/>
- Fixed timestep & determinism: <https://gafferongames.com/post/fix_your_timestep/>
- Dofus 1.29 open-source algorithm library (map/pathfinding/LoS/encoding):
  <https://github.com/Arakne/ArakneUtils>, <https://github.com/Arakne/Araknemu>
- Dofus line-of-sight (centre-to-centre, entities block): <https://dofuswiki.fandom.com/wiki/Line_of_Sight>
- Tactics-RPG state-machine architecture: <https://theliquidfire.com/2015/06/01/tactics-rpg-state-machine/>

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
- **Camera.** The map is pinned to the screen. We need a camera: follow the active fighter,
  pan/zoom, clamp to map bounds, and **screen-shake** on impact for feel.
- **Data-driven maps + richer cell model.** We hardcode one rectangular encounter. A real
  Dofus cell carries more than walkable/LoS: a **line-of-sight flag, a movement flag, ground
  level/elevation, and object-layer references**. We want (a) a cell model with those fields
  and (b) a map **loader** (Tiled TMX/JSON, or a decoded cell-data array) so maps are content,
  not code. This is also the seam to import your own Incarnam-style maps.
- **Status-effect / buff system.** Effects are instant only. Dofus combat is built on timed
  states: buffs/debuffs, shields, damage-over-time, AP/MP steal/reduction, with **turn-start
  and turn-end ticks**. The engine already notes a hook for an end-of-turn queue — this is it.

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
2. **Camera** (follow + clamp + shake).
3. **Data-driven maps** + richer cell model + a loader.
4. **Status-effect system** (turn-tick durations).
5. Hover path/MP + damage preview; crits; sound.

## Sources

- Isometric depth sorting (topological vs z-buffer, semi-transparency):
  <https://mazebert.com/forum/news/isometric-depth-sorting--id775/>,
  <https://gamedev.net/forums/topic/470599-isometric-depth-sorting/>
- Fixed timestep & determinism: <https://gafferongames.com/post/fix_your_timestep/>
- Dofus 1.29 open-source algorithm library (map/pathfinding/LoS/encoding):
  <https://github.com/Arakne/ArakneUtils>, <https://github.com/Arakne/Araknemu>
- Dofus line-of-sight (centre-to-centre, entities block): <https://dofuswiki.fandom.com/wiki/Line_of_Sight>
- Tactics-RPG state-machine architecture: <https://theliquidfire.com/2015/06/01/tactics-rpg-state-machine/>

# Tileset composition rules (derived from the pack's reference map)

The 8-bit sheet ships with an example map (34×49 tile-index grid). These rules are mined from
that grid — every rule cites the reference rows that establish it. The renderer follows them
exactly; deliberate deviations are listed at the end. Tile indices refer to the 8-column sheet
(index = row·8 + col); the named constants live in `Tid` (`TileSet.cs`).

## 0. Sheet mechanics

- 8×8 px tiles, 8 columns. Sprite transparency = pure magenta `#FF00FF`, keyed at load.
- The atlas is repacked with 1px extruded gutters; **only integer on-screen pixel scales**
  (cell 32 = 4×; sprites at ×1.25 → 40px, ×2 → 64px etc. keep whole-pixel texels).

## 1. The wall sandwich (reference rows 1–2, 15–17, 30–31, 45–47)

Every enclosed space is bounded by a wall drawn as **three horizontal layers**:

```
band   1  2  2  2  2  3     the wall's top surface (bright), corner-capped
face   5 13 12 13 13  5     the wall's front, decorated at intervals
floor  .  .  .  .  .  .
band   1  2  2  2  2  3     the south wall's top surface
face   5 13 13 12 13  5     the south face, seen from outside
skirt 46 46 46 46 46 46     dark base strip (reference rows 17/47)
```

- **North wall** (above the playfield): band row, then face row.
- **South wall** (below): band row, then face row, then skirt row.
- **Sides**: single vertical columns of the family's side tile, meeting the bands at corners.
- **Face composition** (reference row 2: `5,12,12,12,13,12,13,13,14,...,5`): the base face tile
  dominates; the darker edge face sits beside the corners only; decor forms a small CLUSTER near
  one end plus sparse repeats — a cycle pattern reads mechanical and is wrong.
- **Depth (the 3D read)**: light band on top, dark face directly below, floor below that. Two
  reinforcing cues from the reference: props stand ON the north band (shelves/jars along the
  wall top, every ~6 tiles in the City), and the west wall shades the single floor column beside
  it with VARIED shade tiles (16/17/26 mixed down the column — reference rows 3–8 — never one
  tile stacked into a stripe).

Per family:

| layer | City (purple room, ref rows 1–17) | Yard/Crypt (dungeon, ref rows 30–47) |
|---|---|---|
| band | 2 (corners 1 / 3) | 105 (corners 84 / 85) |
| face | 5·13·14 cycle, decor 12 (bottles) | 111·112·113·112 cycle, decor 114 (torch) |
| side | 3 | 94 |
| skirt | 46 | 106 (sparse 107) |
| outer void | 7 sprinkled SE (ref: right column + below) | 91 / 94 |

## 2. Floors (reference: every room interior)

- **One plain base tile floods the room.** City 15; Crypt 25 (smooth worked stone). The Yard is
  the outdoor exception: its base mixes 82 / 83 **per cell** (dead, uneven earth).
- **Variants come in clumps, never lone tiles**: the reference's moss/stripe patches span 2+
  adjacent tiles (rows 32–44 grow green 88/89/90 patches inside dark floor). Implemented by
  hashing the 2×2 block (`x/2, y/2`), so a variant always appears as a patch.
  - City floors are UNIFORM 15 — the reference's apparent variation indoors is wall-adjacent
    shading (16/17/26 by the west wall) and furniture, never random floor sprinkles. 13/14 are
    face tiles and must not appear on floors.
  - Yard clumps: mossy 88 / 89 / 97, ~1 block in 3; bramble 90 as a RARE accent (~1 block in 8).
  - Crypt clumps: 82 / 83 / 86 / 93, ~1 block in 4.
- **Rugs are rectangles anchored to landmarks, never scattered** (reference: the striped mat
  and check rugs form solid rectangles inside the top room): 2×2 gold-stripe 20 before the
  Tithe-Keeper, 2×2 lattice 47 before the Temple, 2×2 blue-weave 52 before the Hiring Post.
- `Water` renders 92 with sparse 98 glints.

## 3. Obstacles & props

- Rock cells: **tombstone arch 110** in the Yard (pale `#CECEDA` tint); **stone lump 44** in the
  Crypt; the City map has none.
- Tree cells: **evergreen 45**, feet-anchored, ×1.25.
- The Lychgate and the Crypt door: **arch 110 at ×2 with torches 114** flanking (reference
  embeds torches in wall faces; gates get them as standing props).
- Services: Tithe-Keeper = chest 71, Temple = statue 12, Hiring Post = figure 76.

## 4. Sprites (reference: figures inside rooms; sidebar sprite table)

- All sprites are **feet-anchored** — they stand on their cell's bottom edge and grow upward.
- Idle pairs animate at 2 fps: figures 74/76/78/80(+1), bird 63, ghost 65, spider 101.
- Crew = figures A/B/C; mobs = figure D, crab 100, spider 101, mite 67, bird 63; the Sexton
  is figure D at ×2 (a looming silhouette, reference-style black).
- The survivor is a figure plus the pack's own **"?" bubble 69** beside its head.

## 5. Deviations (deliberate, for gameplay legibility or space)

1. **Team pads**: colored ground pads under units (class color vs enemy red). The reference has
   no pads; watched combat needs team/class readability at a glance, and HP bars need an anchor.
2. **Skirt + shadow economy**: the south skirt row renders, but the outer SE `7`-shadow ring is
   reduced to the right-hand column only — the HUD panel already crops the bottom.
3. **Cell highlights** (hover, movement range, placement) are translucent squares over the
   tiles — pure gameplay UI with no reference equivalent.
4. **No drop-shadow discs under props** — the pack's sprites cast no shadows; only units keep
   their team pads (rule 1).

## 6. Findings from the 1:1 reference recreation

The reference was rebuilt pixel-identically (0 differing pixels of 426,496) from three layers,
which is the proof behind every rule above and adds two discoveries:

1. **Tile data alone = 97.3%.** The example map uses `flipX` on 71 tiles and one rotation —
   mirroring is part of the pack's vocabulary (not yet used by the game renderer).
2. **The dither shadow layer (+2.3%)**: light comes from the NW. Interior north and west wall
   edges (and hand-picked interior blocks) cast a shadow ramp onto the floor, colour `#1E2431`:
   a SOLID 1px line against the wall, then two dithered lines fading out —
   `########` / `.#.#.#.#` / `#.#.#.#.` (and the same rotated for west walls). Application in
   the reference is contextual (interior edges only; exterior south faces never cast), so the
   game applies it along the playfield's north and west edges.
3. **The sprite layer (+0.35%)** is hand-placed: single keyed tiles plus a few multi-tile
   composites (a 2-tile robot, a 2×2 mushroom whirl). Sprites stand mid-cell or perch ON wall
   faces/bands — free placement, not grid law.

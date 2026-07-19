# Chamber8x8 composition rules (mined from the pack's example screenshots)

The skin uses adamzbuub's **Chamber8x8** pack (https://adamzbuub.itch.io/chamber8x8): an 8×8-px
wall autotile (4×4 sheet), a floor autotile (16×4 sheet), individually named props, and a
4-frame animated knight. The art itself is **local-only and never committed** — the repo keeps
its procedural fallback.

Every rule below was derived empirically from the author's two example screenshots: both were
reduced to native resolution (they are 10× recordings; grid origins (2,4) and (4,5)), then every
8×8 cell was masked-matched against the pack's tiles until the whole map decoded. The rules cite
that evidence.

## 0. Sheet mechanics

- 8×8-px tiles. The wall sheet is 4 columns (index = row·4+col), the floor sheet 16 columns.
- Real alpha transparency (no magenta keying needed; keying stays on harmlessly).
- Atlases are repacked with 1-px extruded gutters; **only integer on-screen scales** (cell 32 = 4×).
- Scale detection note: both examples pass a NEAREST roundtrip at 5× AND 10× — the 10× is real;
  a 5× reduction still passes because every native pixel covers an even number of recording
  pixels. Detailed-tile matching (0 exact hits at 5×, full decode at 10×) settles it.

## 1. Walls — a ring autotile, one tile thick

The wall sheet is a ring-plus-fill blob. Decoded roles (col,row):

| tile | role | evidence |
|---|---|---|
| (0,0) (1,0) (2,0) | NW corner, N edge, NE corner | every room's top row |
| (0,1) (2,1) | W edge, E edge | every room's side columns |
| (0,2) (1,2) (2,2) | SW corner, S edge, SE corner | every room's bottom row; (1,2) is pixel-identical to (1,0) |
| (0,3) (1,3) | concave (inner) corners W/E | stepped outlines: `W02 W03` / `F59 W13 W22` turns |
| (3,y), (1,1) | empty | unused |
| (2,3) | fill / plain cap | runs inside 2-thick walls and T-junctions (ex2 rows 5–6) |

- **Rooms are outlined by a single-tile wall run** — the ring tiles, corner-capped. The cap is
  pale stone; its **dark south lip is baked into the tile** (there is no separate face row and
  no separate shadow layer — see §3).
- **Two-thick walls** (a wall seen from both sides, ex2's room divider): north row = fill (2,3),
  south row = N-edge (1,0) so the lip faces the lower room.
- **Stepped outlines**: convex turns use the ring corners, concave turns the (0,3)/(1,3) inner
  corners — the examples' alcove bumps prove both.

### Wall furniture (all embedded IN the wall line)

- **Chest alcoves**: a chest prop replaces a wall cell's look (chest over the dark interior),
  flanked by wall tiles; gold piles often on the floor beside. Ex1's north wall bumps out one
  row to pocket three chests; ex2 embeds them straight into runs.
- **Doors**: `DoorUp` in horizontal runs, `DoorSide` in vertical runs — they sit ON a wall cell
  where a corridor passes (ex2 has two DoorSide at the same x, rows 3 and 8).
- **Pillars** (8×16): stand at wall corners/junctions, feet on the adjacent floor cell, upper
  half overlapping the band (ex1: three; ex2: two — all at corners or junction points).
- **Spiderwebs** (16×8): hang UNDER a band's south edge, sparse (ex1: one under the south wall).

## 2. Floors — an edge autotile that carries all the shading

The floor sheet is four 4×4 groups. Decoded (index = row·16+col):

- **41** — the plain grey paving that floods every interior. **26** — solid dark: the void
  outside rooms (and pits).
- **Group 2 (cols 8–11) = the wall-adjacent edge ring.** Observed neighbour masks are unanimous:
  8=NW corner, 9/10=N edge, 11=NE, 24/40=W edge, 27/43=E edge, 56=SW, 57/58=S edge, 59=SE.
  The dark edging + brown weathering on these tiles IS the wall-base shading — place them on
  every floor cell that touches a wall, dark side toward the wall. Pairs (9/10, 24/40, 27/43,
  57/58) are variants; mix them to break repetition.
- **Group 1 inner 2×2 = diagonal nubs**: 21=wall-NW-only, 22=NE, 37=SW, 38=SE — for floor cells
  that touch a wall only diagonally (observed 4–5× each, exclusively on those masks).
- **Group 3 inner 2×2 = interior detail**: 29/30/45/46 sprinkled on open floor with no wall
  constraint, roughly 1 cell in 12. Never clumped, unlike the old Scut moss rule.
- Free debris props (bones, wood scraps, the key) are scattered on open floor at **sub-tile
  offsets** — the examples place them off-grid deliberately (bones at (5.12,2.12), (13.88,9.88)…).

## 3. Depth & light — everything is baked

The Scut skin needed a hand-dithered shadow layer; Chamber does not. The 1:1 decode left zero
unexplained shading: the wall tiles carry their own south lip, the floor edge tiles carry the
wall-base darkening, the knight and props have baked outlines. **The renderer draws no
procedural shadows in this skin.**

## 4. The knight & sprites

- `Knight_Idle/Walk/Use`: 4-frame 8×8 strips, feet-anchored, small baked drop shadow.
- The examples free-place the knight (it moves off-grid); our units stay cell-anchored and
  animate Idle at ~3 fps, mirrored to face their enemy's side.
- The pack has no monster art: crew are knights tinted by class; the dead are the same knight
  in rust/bone tints (a dark mirror of the living — fits TITHE), the Sexton at ×2.

## 5. Game mapping (deliberate deviations for TITHE)

1. **Obstacles**: rocks = `Pillar` (8×16), trees/clutter = `Barril`/bones — the pack has no
   rocks or trees; pillars and barrels are its blocker vocabulary.
2. **Services** (City): Tithe-Keeper = red-brown chest with gold piles beside (the pack's own
   chest-and-gold pairing), Temple = `Crown` on a metal pedestal, Hiring Post = `Barril` + wood.
3. **Families by tint**: one stone family shipped, so City is neutral, the Graveyard is tinted
   mossy-grey, the Crypt cold blue. Props keep their colour (gold must pop).
4. **Gates**: the Lychgate and Crypt door are `DoorUp` embedded in the wall run, pillars flanking.
5. **Team pads, HP bars, square cell highlights**: gameplay UI kept verbatim from the Scut skin
   (watched combat needs the readability; the pack has no equivalent).
6. **Pits**: `Water`/`Void` cells render as floor-holes of 26 — the examples' rooms float in
   that same darkness.

## 7. The 45° iso mode (the Dofus projection)

The square grid was retired for the classic 2:1 diamond view. Rules of the projection:

- **Ground**: 64×32 screen diamonds baked at load (`IsoFloorBank`) — the paving art is tiled
  axis-aligned across a 32×16 native diamond then point-doubled, exactly how 1.29 clips a flat
  ground texture to a cell. The light/dark cell checker is a tint on alternating parity.
  *Worn* diamonds (built only from the sheet's four detail tiles — the edge tiles streak) get
  likelier toward the map edge: 45% on the rim ring, 22% one ring in, 7% elsewhere.
- **No walls**: a Dofus map floats in the dark. The playfield is hemmed by a scenery ring one
  cell outside — a dimmed worn shelf under ~2/3-density prop clutter that clusters (a second
  scrap besides a first), families keeping their tables (city: barrels/chests/wood/gold;
  dungeon: bones/barrels/webs' cousins). **Never a pillar on the extreme corners.** The south
  run draws after units so it overlaps them; everything else draws before.
- **One pixel density everywhere**: `ChamberSet.PxScale = 2` — characters, props, floors and
  UI all put 2 screen px per texel, integer only. Landmark props (gates, services) may double
  to 4 but never fractional; nothing is ever scaled 1.25× again.
- **Characters over knights**: units wear the animation packs (hero/soldier/orc/slime strips
  baked by `tools/bake_assets.py` into SpriteBank's `{name}_{state}_se_{n}` convention,
  union-cropped so feet sit on the strip bottom). Facing mirrors for SW/NW; crew keep class
  pads, the dead keep red pads; the sexton is the orc at double scale.
- **UI**: cream 9-slice panels, cards and the green button from the pixel UI pack; the
  blackletter dungeon font only on standalone titles (GOLD, scene names, NPC headers) — it is
  too ornate for running text, which stays PixelFont. Dark ink on cream, never light.

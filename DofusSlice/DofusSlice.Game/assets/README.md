# Sprite assets (local only)

Drop PNG files here and the game uses them automatically instead of the procedural
placeholders. **Nothing in this folder is committed** (see `.gitignore`) — keep any
third-party/datamined art strictly local, since it may be copyrighted.

Use **32-bit RGBA PNGs** (transparent background). Art is premultiplied on load, so
soft/anti-aliased edges composite cleanly with no dark halo. Fighter sprites are drawn
anchored at the **feet** (bottom-centre sits on the cell centre), so taller sprites stand up
out of their tile and depth-sort correctly against other fighters and obstacles.

## Animated fighters (directional strips)

Name a horizontal frame strip `{name}_{state}_{dir}_{frames}.png`:

- **name**: `iop`, `boar`, `gobball`, `piou`
- **state**: `idle`, `walk`, `cast`, `hurt`, `die`
- **dir**: `se`, `sw`, `ne`, `nw` (SW/NW auto-mirror from SE/NE if you omit them)
- **frames**: number of equal-width frames in the strip (omit for a single static frame)

Examples: `iop_walk_se_6.png` (6-frame SE walk), `iop_idle_se_2.png`, `iop_cast_se_4.png`,
`boar_idle_se_2.png`. Frames are laid out left-to-right, each `width / frames` wide.

**Resolution order** for any (name, state, dir): exact strip → mirrored SE/NE → `{name}_{state}_se`
→ `{name}_{state}` → `{name}.png` (static) → procedural placeholder. So you can ship as little
as one `iop.png` and grow toward a full directional set over time.

## Tiles

| File | Used for | Suggested size |
|------|----------|----------------|
| `tile_grass.png` | floor tile | 64x32 iso diamond |
| `tile_rock.png` | obstacle prop | 64x56 (extra height above the diamond) |

> Provide your own art, or art you are licensed to use. This project ships only
> procedural placeholders and never redistributes third-party assets.

## tileset.png — the 8-bit skin

Drop an 8×8-tile, 8-column tileset sheet here as `tileset.png` (e.g. Scut's "7DRL Tilemaps"
pack) and the whole game switches to a top-down 8-bit look: tiled City / Graveyard / Crypt
scenes, sprite crews and mobs, tombstones, the arch Lychgate. Sprite transparency is keyed on
pure magenta (#FF00FF). Without the file the game keeps its procedural iso look — the repo
always runs. Third-party sheets are **local only, never committed** (this folder is gitignored).

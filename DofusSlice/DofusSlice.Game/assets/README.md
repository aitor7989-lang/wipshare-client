# Sprite assets (local only)

Drop PNG files here and the game uses them automatically instead of the procedural
placeholders. **Nothing in this folder is committed** (see `.gitignore`) — keep any
third-party/datamined art strictly local, since it may be copyrighted.

Recognised filenames (all optional — any you omit fall back to procedural art):

| File | Used for | Suggested size / anchor |
|------|----------|-------------------------|
| `iop.png` | the player character | ~64x96, bottom-centre = feet |
| `boar.png` | Boar mob | ~64x64, bottom-centre = feet |
| `gobball.png` | Gobball mob | ~64x64 |
| `piou.png` | Piou mob | ~48x48 |
| `tile_grass.png` | floor tile | 64x32 iso diamond |
| `tile_rock.png` | obstacle tile | 64x48 (extra height above the diamond) |

Fighter sprites are drawn anchored at the **feet** (bottom-centre sits on the cell
centre), so taller sprites stand up out of their tile and depth-sort correctly against
other fighters and obstacles.

> Provide your own art, or art you are licensed to use. This project ships only
> procedural placeholders and never redistributes third-party assets.

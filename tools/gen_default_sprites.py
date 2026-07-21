#!/usr/bin/env python3
"""Generate ORIGINAL 1-bit silhouette sprites for THE GAUNTLET — now with real motion.

These are my own procedural silhouettes — NOT the licensed art pack — so they can be
committed to git and ship inside the binary. SpriteBank indexes assets-default/ first,
then the (gitignored, user-dropped) assets/ folder overrides them. A clean checkout / CI
release always renders readable, ANIMATED bodies; dropping the real art pack still wins.

Each fighter gets THREE distinct 4-frame strips, white-on-transparent, that the game
tints and scales:
  * idle   — a quiet breathing bob, arms at rest.
  * walk   — a scissoring gait (legs alternate, body leans into the stride). The game
             adds the vertical hop on the move tween, so the strip carries the LEGS.
  * attack — a wind-up → strike → follow-through swing. The game adds the positional
             lunge; the strip carries the WEAPON arc.

Frame semantics the game relies on (GauntletGame.DrawSprite):
  walk   : frame = (time*9) % count   -> a 4-beat loop, so f0..f3 must read as one stride.
  attack : frame = progress(0.34s) across 0..count-1 -> f0 coiled, f3 extended.
  idle   : slow cycle.

    python3 tools/gen_default_sprites.py
"""
import pathlib

from PIL import Image, ImageDraw

OUT = pathlib.Path(__file__).resolve().parent.parent / "Gauntlet" / "assets-default"
FW, FH, FRAMES = 24, 32, 4
W = (255, 255, 255, 255)
CX = FW // 2
BASE = FH - 3                      # the feet line


# ---- primitives ---------------------------------------------------------------------

def disc(d, cx, cy, r):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=W)


def limb(d, p0, p1, w=2):
    d.line([p0, p1], fill=W, width=w)


def sheet(draw_state_frame, state):
    """Assemble a 4-frame horizontal strip for one state."""
    img = Image.new("RGBA", (FW * FRAMES, FH), (0, 0, 0, 0))
    for f in range(FRAMES):
        cell = Image.new("RGBA", (FW, FH), (0, 0, 0, 0))
        draw_state_frame(ImageDraw.Draw(cell), state, f)
        img.paste(cell, (f * FW, 0))
    return img


# ---- pose tables (keyed to the 4 frames per state) ----------------------------------
# leg split (px each foot travels from centre), body lean at the shoulders, and a vertical
# nudge. Walk reads as a stride; attack coils back (f0,f1) then drives forward (f2,f3).
LEG   = {"idle": [0, 0, 0, 0],     "walk": [3, 0, -3, 0],   "attack": [1, 0, -2, -1]}
LEAN  = {"idle": [0, 0, 0, 0],     "walk": [2, 1, 2, 1],    "attack": [-2, -3, 4, 3]}
RISE  = {"idle": [0, -1, 0, 1],    "walk": [0, -1, 0, -1],  "attack": [0, 0, 0, 0]}
# near-hand offset from the shoulder (dx, dy), and weapon-tip offset from the hand.
HAND  = {"idle":   [(2, 6), (2, 5), (2, 6), (2, 7)],
         "walk":   [(3, 5), (1, 6), (-2, 5), (1, 6)],
         "attack": [(-2, -2), (-3, -4), (5, 1), (4, 3)]}
TIP   = {"idle":   [(3, 2), (3, 2), (3, 2), (3, 2)],
         "walk":   [(3, 2), (3, 3), (2, 3), (3, 3)],
         "attack": [(-3, -5), (-4, -6), (7, 0), (6, 3)]}   # windup kept inside the 32px frame


def biped(d, state, f, *, tall=15, head=4, shoulder=5, weapon="sword", hunch=0, robe=False):
    """A humanoid silhouette posed for the given state/frame. weapon in
    {sword, club, none}; robe swaps legs for a hem with peeking step-feet."""
    lean = LEAN[state][f] + hunch
    rise = RISE[state][f]
    hip = BASE - 5 + rise
    top = hip - tall                                # shoulder line
    tcx = CX + lean

    if robe:
        hem = [(CX - shoulder - 2, BASE), (CX + shoulder + 2, BASE),
               (tcx + shoulder - 1, top), (tcx - shoulder + 1, top)]
        d.polygon(hem, fill=W)
        # two little feet stepping under the hem (so a robe still reads as walking)
        step = LEG[state][f]
        disc(d, CX - 2 - max(0, step), BASE, 1)
        disc(d, CX + 2 + max(0, -step), BASE, 1)
    else:
        # tapered torso
        d.polygon([(tcx - shoulder, top), (tcx + shoulder, top),
                   (CX + shoulder - 2, hip), (CX - shoulder + 2, hip)], fill=W)
        # scissoring legs
        split = LEG[state][f]
        limb(d, (CX - 1, hip), (CX - 1 - split, BASE), 2)
        limb(d, (CX + 1, hip), (CX + 1 + split, BASE), 2)

    disc(d, tcx, top - head + 1, head)              # head

    if weapon != "none":
        sh = (tcx + shoulder - 1, top + 2)
        hdx, hdy = HAND[state][f]
        hand = (sh[0] + hdx, sh[1] + hdy)
        limb(d, sh, hand, 2)                        # near arm
        tdx, tdy = TIP[state][f]
        tip = (hand[0] + tdx, hand[1] + tdy)
        limb(d, hand, tip, 3 if weapon == "club" else 2)   # the blade / haft
        if weapon == "club":
            disc(d, tip[0], tip[1], 2)              # heavy head


# ---- family draws (each returns a draw(d, state, f)) ---------------------------------

def humanoid(d, s, f): biped(d, s, f, shoulder=5, tall=15, weapon="sword")
def husk(d, s, f):     biped(d, s, f, shoulder=5, tall=14, weapon="club")
def piper(d, s, f):    biped(d, s, f, shoulder=4, tall=15, weapon="none", hunch=1)
def big(d, s, f):      biped(d, s, f, shoulder=7, tall=14, head=4, weapon="club")
def ghoul(d, s, f):    biped(d, s, f, shoulder=4, tall=12, weapon="none", hunch=3)


def archer(d, s, f):
    biped(d, s, f, shoulder=4, tall=15, weapon="none")
    # a bow on the near side; the string draws back on the attack, flexes on the walk.
    tcx = CX + LEAN[s][f]
    top = (BASE - 5 + RISE[s][f]) - 15
    bx, by = tcx + 5, top + 3
    d.arc([bx - 1, by - 7, bx + 5, by + 7], 290, 70, fill=W, width=1)
    draw = {"attack": [2, 4, 0, 1], "walk": [1, 0, 1, 0], "idle": [0, 1, 0, 1]}[s][f]
    limb(d, (bx + 3, by - 6), (bx - draw, by), 1)
    limb(d, (bx + 3, by + 6), (bx - draw, by), 1)


def cannon(d, s, f):
    biped(d, s, f, shoulder=3, tall=14, head=4, weapon="none", robe=True)
    # a staff with a spark that swells as the cast lands.
    tcx = CX + LEAN[s][f]
    top = (BASE - 5 + RISE[s][f]) - 14
    sx, sy = tcx + 5, top
    limb(d, (sx, sy + 6), (sx, sy - 4), 1)
    spark = {"attack": [1, 2, 4, 3], "walk": [2, 2, 2, 2], "idle": [2, 3, 2, 2]}[s][f]
    disc(d, sx, sy - 5, spark)


def sexton(d, s, f):
    # the boss: tall hooded robe, a scythe that sweeps on the attack.
    lean = LEAN[s][f]
    rise = RISE[s][f]
    top = BASE - 18 + rise
    tcx = CX + lean
    d.polygon([(CX - 8, BASE), (CX + 8, BASE), (tcx + 4, top), (tcx - 4, top)], fill=W)
    disc(d, tcx, top - 2, 3)
    # scythe: a shaft + blade arc, angled back on wind-up, swept across on the strike.
    ang = {"attack": [(-3, 7), (-5, 5), (6, 8), (5, 9)],
           "walk":   [(6, 8), (7, 8), (6, 8), (7, 8)],
           "idle":   [(7, 7), (7, 6), (7, 7), (7, 8)]}[s][f]
    hx = tcx + ang[0]
    limb(d, (tcx + 4, top + 4), (hx, top - 6), 2)         # shaft
    d.arc([hx - 4, top - 10, hx + 6, top - 2], 180, 330, fill=W, width=2)  # blade (kept in-frame)


def hound(d, s, f):
    # a low four-legged runner: legs gallop on the walk, head lunges on the bite.
    rise = RISE[s][f] if s != "walk" else [0, -1, 0, -1][f]
    y = BASE - 6 + rise
    lunge = {"attack": [0, -1, 3, 2], "walk": [0, 0, 0, 0], "idle": [0, 0, 0, 0]}[s][f]
    d.rectangle([5, y, 18, y + 5], fill=W)                # body
    disc(d, 18 + lunge, y + 1 + (0 if s != "attack" else 1), 3)   # head thrusts to bite
    limb(d, (5, y + 1), (2, y - 1), 1)                    # tail
    gait = {"attack": [1, 1, -1, -1], "walk": [2, -2, 2, -2], "idle": [0, 0, 0, 0]}[s][f]
    for i, lx in enumerate((6, 9, 13, 16)):
        step = gait if i % 2 else -gait
        limb(d, (lx, y + 5), (lx + step, BASE), 1)


def mite(d, s, f):
    # a tiny round crawler: legs scuttle on the walk, a little chomp-hop on the attack.
    rise = {"attack": [0, -2, -1, 0], "walk": [0, -1, 0, -1], "idle": [0, -1, 0, 0]}[s][f]
    r = 4 + (1 if s == "attack" and f == 1 else 0)
    cy = BASE - 4 + rise
    disc(d, CX, cy - r, r)
    spread = {"attack": [1, 3, 2, 1], "walk": [2, 0, 2, 0], "idle": [1, 1, 1, 1]}[s][f]
    for dx in (-r, 0, r):
        limb(d, (CX + dx, cy - 1), (CX + dx + (spread if dx > 0 else -spread if dx < 0 else 0),
                                    cy + 2), 1)


def serpent(d, s, f):
    # spitter / wraith: a coiled column that sways on the walk and strikes forward on the
    # attack (the head lunges out and back).
    if s == "attack":
        head_dx = [-1, -2, 5, 3][f]
        sway = [0, 0, 2, 1][f]
    elif s == "walk":
        head_dx = [0, 0, 0, 0][f]
        sway = [-2, 0, 2, 0][f]
    else:
        head_dx = 0
        sway = [-1, 0, 1, 0][f]
    for i, y in enumerate(range(BASE, BASE - 16, -2)):
        off = sway * (i % 2)
        d.ellipse([CX - 3 + off, y - 2, CX + 3 + off, y + 1], fill=W)
    disc(d, CX + sway + head_dx, BASE - 17, 3)


MAP = {
    "hero": humanoid, "warden": big, "husk": husk, "piper": piper,
    "archer": archer, "cannon": cannon, "sexton": sexton,
    "ghoul": ghoul, "brute": big, "hound": hound,
    "mite": mite, "spitter": serpent, "wraith": serpent,
}


def rock():
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(3, 13), (2, 8), (6, 4), (11, 5), (14, 9), (13, 13)], fill=W)
    return img


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, fn in MAP.items():
        for state in ("idle", "walk", "attack"):
            sheet(fn, state).save(OUT / f"{name}_{state}_se_4.png")
    rock().save(OUT / "onebit_rock.png")
    print(f"wrote {len(list(OUT.glob('*.png')))} default sprites to {OUT}")


if __name__ == "__main__":
    main()

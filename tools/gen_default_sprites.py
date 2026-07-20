#!/usr/bin/env python3
"""Generate ORIGINAL 1-bit silhouette fallback sprites for THE GAUNTLET.

These are my own procedural silhouettes — NOT the licensed art pack — so they can
be committed to git and ship inside the binary. SpriteBank indexes assets-default/
first, then the (gitignored, user-dropped) assets/ folder overrides them. Result: a
clean checkout / CI release always renders readable bodies instead of the old
placeholder letter-balls; dropping the real art pack still upgrades everything.

Each sheet is a 4-frame horizontal idle strip, white-on-transparent, that the game
tints and scales. A gentle 1px vertical bob gives the silhouettes a little life.

    python3 tools/gen_default_sprites.py
"""
import math
import pathlib

from PIL import Image, ImageDraw

OUT = pathlib.Path(__file__).resolve().parent.parent / "Gauntlet" / "assets-default"
FW, FH, FRAMES = 24, 32, 4
W = (255, 255, 255, 255)


def sheet(draw_fn):
    img = Image.new("RGBA", (FW * FRAMES, FH), (0, 0, 0, 0))
    for f in range(FRAMES):
        cell = Image.new("RGBA", (FW, FH), (0, 0, 0, 0))
        d = ImageDraw.Draw(cell)
        bob = [0, -1, 0, 1][f]           # a quiet breathing bob
        draw_fn(d, bob, f)
        img.paste(cell, (f * FW, 0))
    return img


def disc(d, cx, cy, r):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=W)


def humanoid(d, bob, f, *, head=4, shoulder=5, tall=13, base=FH - 3):
    cx = FW // 2
    top = base - tall + bob
    disc(d, cx, top - head, head)                       # head
    d.polygon([(cx - shoulder, top), (cx + shoulder, top),
               (cx - shoulder + 1, base), (cx + shoulder - 1, base)], fill=W)  # tapered body
    d.rectangle([cx - shoulder - 1, top + 2, cx + shoulder + 1, top + 4], fill=W)  # shoulders


def archer(d, bob, f):
    humanoid(d, bob, f, shoulder=4, tall=13)
    cx = FW // 2
    top = FH - 3 - 13 + bob
    # a bow arc on the near side, string flexing with the frame
    flex = [0, 1, 0, 1][f]
    d.arc([cx + 3, top - 6, cx + 11, top + 8], 300, 60, fill=W, width=1)
    d.line([cx + 9 - flex, top - 4, cx + 9 - flex, top + 6], fill=W, width=1)


def cannon(d, bob, f):
    # robed caster: wide skirt, small head, a raised staff-spark
    cx = FW // 2
    base = FH - 3
    top = base - 14 + bob
    disc(d, cx, top - 4, 4)
    d.polygon([(cx - 3, top), (cx + 3, top), (cx + 7, base), (cx - 7, base)], fill=W)  # robe
    spark = [3, 4, 3, 2][f]
    disc(d, cx + 8, top - 2, 1)                          # staff head
    d.line([cx + 6, top + 2, cx + 8, top - 2], fill=W, width=1)
    disc(d, cx + 8, top - 2 - spark, 1)                 # rising spark


def robed_tall(d, bob, f):
    # the sexton: tall hooded figure, a scythe line
    cx = FW // 2
    base = FH - 2
    top = base - 20 + bob
    disc(d, cx, top - 3, 3)
    d.polygon([(cx - 4, top), (cx + 4, top), (cx + 8, base), (cx - 8, base)], fill=W)
    d.line([cx + 7, top - 6, cx + 7, base - 2], fill=W, width=1)   # scythe shaft
    d.arc([cx + 3, top - 9, cx + 11, top - 1], 180, 320, fill=W, width=1)


def hunched(d, bob, f):
    # ghoul: forward-leaning, long arms
    cx = FW // 2
    base = FH - 3
    top = base - 11 + bob
    disc(d, cx + 3, top - 2, 3)                          # head forward
    d.polygon([(cx - 4, top + 1), (cx + 4, top - 1), (cx + 5, base), (cx - 5, base)], fill=W)
    reach = [5, 6, 5, 4][f]
    d.line([cx + 3, top + 2, cx + 3 + reach, top + 6], fill=W, width=1)  # dangling arm


def big(d, bob, f):
    # brute / heavy: broad, low, thick arms
    humanoid(d, bob, f, head=4, shoulder=7, tall=12)


def quadruped(d, bob, f):
    # hound: low four-legged body
    base = FH - 3
    y = base - 6 + bob
    d.rectangle([5, y, 18, y + 5], fill=W)               # body
    disc(d, 18, y + 1, 3)                                # head
    step = [0, 1, 0, -1][f]
    for lx in (6, 9, 13, 16):
        d.line([lx, y + 5, lx + step, base], fill=W, width=1)  # legs
    d.line([5, y + 1, 2, y - 1], fill=W, width=1)        # tail


def blob(d, bob, f):
    # mite: tiny round crawler
    cx = FW // 2
    base = FH - 4
    r = 4 + [0, 1, 0, 0][f]
    disc(d, cx, base - r, r)
    for dx in (-r, 0, r):
        d.line([cx + dx, base - 1, cx + dx, base + 2], fill=W, width=1)  # little legs


def serpent(d, bob, f):
    # spitter / wraith: tall coiled figure
    cx = FW // 2
    base = FH - 3
    sway = [-1, 0, 1, 0][f]
    for i, y in enumerate(range(base, base - 16, -2)):
        d.ellipse([cx - 3 + sway * (i % 2), y - 2, cx + 3 + sway * (i % 2), y + 1], fill=W)
    disc(d, cx + sway, base - 17, 3)                     # raised head


MAP = {
    "hero": humanoid, "warden": big, "husk": humanoid, "piper": humanoid,
    "archer": archer, "cannon": cannon, "sexton": robed_tall,
    "ghoul": hunched, "brute": big, "hound": quadruped,
    "mite": blob, "spitter": serpent, "wraith": serpent,
}


def rock():
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(3, 13), (2, 8), (6, 4), (11, 5), (14, 9), (13, 13)], fill=W)
    return img


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, fn in MAP.items():
        s = sheet(lambda d, bob, f, fn=fn: fn(d, bob, f))
        # the same silhouette serves idle/walk/attack/die as a fallback strip
        for state in ("idle", "walk", "attack"):
            s.save(OUT / f"{name}_{state}_se_4.png")
    rock().save(OUT / "onebit_rock.png")
    print(f"wrote {len(list(OUT.glob('*.png')))} default sprites to {OUT}")


if __name__ == "__main__":
    main()

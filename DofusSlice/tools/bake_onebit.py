#!/usr/bin/env python3
"""Bake the ONE-BIT sprite set from the two local kits (never committed):

  * Hexany's Roguelike Tiles (CC0) — 16x16 creature/prop tiles. Each TITHE archetype
    gets its OWN single static PNG; the SpriteBank name chain ({name}_{state}_se_{n} ->
    ... -> {name}) lets one static frame serve every animation state.
  * 1-bit UI pack by Batuhan Karagol — heart/shield/star vitals icons for the HUD.

Tiles are re-inked to pure white-on-transparent (the engine tints with Mono.Ink /
Mono.Danger) and kept at NATIVE 16px (the renderer doubles -> 32px on screen, and the
half-res world pass makes each art pixel exactly one chunky screen block). UI icons are
normalized into identical 24x24 boxes and saved at 2x (48px) for integer HUD scaling.

Usage:
  python3 tools/bake_onebit.py --hexany <dir with Tilesheets/> --batuhan <dir> \
      --out DofusSlice.Game/assets
"""
import argparse
from pathlib import Path

from PIL import Image

# archetype sprite name -> (sheet, col, row) in the Hexany Tilesheets/Transparent set.
CREATURES = {
    "hero":    ("creatures", 5, 0),    # sword-and-shield warrior (bulwark + the avatar token)
    "archer":  ("creatures", 10, 0),   # figure with the bow at their side
    "cannon":  ("creatures", 15, 0),   # wizard, hat and staff — the ruin-bolt caster
    "husk":    ("creatures", 4, 7),    # mummy         (barrow_husk)
    "hound":   ("creatures", 3, 9),    # snarling dog  (gravehound)
    "spitter": ("creatures", 6, 9),    # rearing snake (marrow_spitter)
    "mite":    ("creatures", 7, 9),    # spider        (grave_mite)
    "piper":   ("creatures", 5, 7),    # robed figure with staff (bone_piper)
    "wraith":  ("creatures", 2, 7),    # ghost         (tomb_wraith)
    "ghoul":   ("creatures", 6, 3),    # skull-headed walker (grave_ghoul)
    "warden":  ("creatures", 10, 4),   # horned armoured brute (crypt_warden)
    "sexton":  ("creatures", 0, 7),    # hooded monk — the gravedigger boss
    "onebit_rock": ("general", 2, 2),  # rubble pile prop for obstacle tops
}
# Vitals icons: the Batuhan sheet's heart/shield carry a diagonal shine slash that reads
# "broken" at HUD size, so the three vitals shapes are pixelled here in the same language —
# solid, symmetric, on the same 1-bit grid. (The kit still drives the buttons/frames style.)
PIXEL_ICONS = {
    "onebit_heart": [
        "..###...###..",
        ".#####.#####.",
        "#############",
        "#############",
        "#############",
        ".###########.",
        "..#########..",
        "...#######...",
        "....#####....",
        ".....###.....",
        "......#......",
    ],
    "onebit_star": [
        "......#......",
        ".....###.....",
        ".....###.....",
        "....#####....",
        "#############",
        ".###########.",
        "..#########..",
        "...#######...",
        "...#######...",
        "..####.####..",
        "..###...###..",
        ".##.......##.",
    ],
    "onebit_shield": [
        "#############",
        "#############",
        "#############",
        "#############",
        ".###########.",
        ".###########.",
        "..#########..",
        "...#######...",
        "....#####....",
        ".....###.....",
        "......#......",
    ],
}


def load_sheet(hexany: Path, name: str) -> Image.Image:
    return Image.open(hexany / "Tilesheets" / "Transparent" / f"{name}_transparent.png").convert("RGBA")


def tile(sheet: Image.Image, c: int, r: int) -> Image.Image:
    return sheet.crop((c * 16, r * 16, c * 16 + 16, r * 16 + 16))


def reink_white(im: Image.Image) -> Image.Image:
    """Any visible pixel becomes pure white (engine tints); alpha is preserved."""
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a > 0:
                px[x, y] = (255, 255, 255, a)
    return im


def bake(im: Image.Image, out: Path, scale: int = 2) -> None:
    im = reink_white(im.convert("RGBA"))
    if scale != 1:
        im = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
    im.save(out)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--hexany", required=True, type=Path)
    ap.add_argument("--batuhan", required=True, type=Path)
    ap.add_argument("--out", required=True, type=Path)
    args = ap.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)

    sheets = {n: load_sheet(args.hexany, n) for n in ("creatures", "general", "items")}

    for name, (sheet, c, r) in CREATURES.items():
        bake(tile(sheets[sheet], c, r), args.out / f"{name}.png", scale=1)
        print(f"  {name}.png  <- {sheet} ({c},{r})")

    for name, rows in PIXEL_ICONS.items():
        w, h = len(rows[0]), len(rows)
        icon = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
        ox, oy = (16 - w) // 2, (16 - h) // 2
        px = icon.load()
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                if ch == "#":
                    px[ox + x, oy + y] = (255, 255, 255, 255)
        bake(icon, args.out / f"{name}.png", scale=3)   # 48x48, integer HUD pixels
        print(f"  {name}.png <- pixelled {w}x{h} in 16-box -> 48x48")

    print("done.")


if __name__ == "__main__":
    main()

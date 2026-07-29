#!/usr/bin/env python3
"""Bake the 1-bit 48px animation pack into Gauntlet SpriteBank strips.

The pack (owner-supplied, NEVER committed — see the art policy in
DofusSlice/docs/GAUNTLET-DESIGN.md) ships one 192x192 sheet per unit: a 4x4
grid of 48px frames, rows = idle / attack / walk / death, plus a 64x64
projectiles sheet of 16px frames. SpriteBank wants horizontal strips named
`{name}_{state}_{dir}_{frames}.png`, white-on-transparent so the game's tint
law colors them.

Usage:
    python3 tools/bake_gauntlet_anim.py --src <dir with *_48px.png> --out Gauntlet/assets
"""
import argparse
import pathlib

from PIL import Image

# pack sheet -> in-game sprite name (see SpriteOf in GauntletGame.cs)
UNITS = {
    "wizard_48px.png": "cannon",    # robed fire-caster
    "archer_48px.png": "archer",
    "warrior_48px.png": "hero",     # the bulwark
    "elderly_48px.png": "sexton",   # the old gravedigger himself
    "ghoul_48px.png": "ghoul",
    "berserk_48px.png": "brute",    # the cairn brute's axe
    "paladin_48px.png": "warden",   # the crypt warden's plate
}
STATES = ["idle", "attack", "walk", "die"]   # sheet rows, top to bottom

# projectiles sheet rows -> in-game projectile art (tinted by element)
PROJROWS = ["proj_fire", "proj_bolt", "proj_rock", "proj_arrow"]


def recolor(img: Image.Image, invert: bool) -> Image.Image:
    """1-bit normalize: every opaque pixel becomes pure white (or, inverted,
    dark bodies turn white and light accents turn black so ghoul eyes stay eyes)."""
    img = img.convert("RGBA")
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            if invert:
                lum = (r + g + b) / 3
                v = 255 if lum < 128 else 0
            else:
                v = 255
            px[x, y] = (v, v, v, 255)
    return img


def strip(sheet: Image.Image, row: int, cell: int, crop_top: int = 0) -> Image.Image:
    """One horizontal strip of 4 frames. crop_top trims dead headroom (the 48px unit
    frames hold a ~16px figure at the bottom; keeping the empty sky made every unit
    render a third of its intended size in-game). Bottom stays anchored."""
    h = cell - crop_top
    out = Image.new("RGBA", (cell * 4, h), (0, 0, 0, 0))
    for i in range(4):
        out.paste(sheet.crop((i * cell, row * cell + crop_top, (i + 1) * cell, (row + 1) * cell)), (i * cell, 0))
    return out


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--flip", action="store_true",
                    help="mirror every frame (use when the pack faces left but 'se' means right)")
    args = ap.parse_args()
    src, out = pathlib.Path(args.src), pathlib.Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    for fname, name in UNITS.items():
        path = src / fname
        if not path.exists():
            print(f"  !! missing {fname}, skipped")
            continue
        sheet = recolor(Image.open(path), invert=True)
        for row, state in enumerate(STATES):
            # All seven sheets keep their figures in the bottom 24px (attack swooshes
            # reach to y=25 at worst) — crop the sky, keep the swing room.
            s = strip(sheet, row, 48, crop_top=24)
            if args.flip:
                s = s.transpose(Image.FLIP_LEFT_RIGHT)
                # re-order frames so animation order survives the mirror
                fixed = Image.new("RGBA", s.size, (0, 0, 0, 0))
                for i in range(4):
                    fixed.paste(s.crop(((3 - i) * 48, 0, (4 - i) * 48, s.height)), (i * 48, 0))
                s = fixed
            s.save(out / f"{name}_{state}_se_4.png")
        print(f"  {fname} -> {name}_(idle|attack|walk|die)_se_4.png")

    proj = src / "projectiles_16px.png"
    if proj.exists():
        sheet = recolor(Image.open(proj), invert=False)
        for row, name in enumerate(PROJROWS):
            strip(sheet, row, 16).save(out / f"{name}_4.png")
        print(f"  projectiles_16px.png -> {', '.join(PROJROWS)}")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Bake local third-party art packs into the game's (gitignored) assets folder.

The repo never ships third-party art: you point this script at packs you own and it
writes game-ready PNGs into DofusSlice.Game/assets/, which is ignored by git. The game
falls back to procedural rendering when the files are absent.

Usage:
  python3 tools/bake_assets.py --chars2 <FreeCharactersAnimationsAssetPack dir> \
      --tiny <"Tiny RPG Character Asset Pack 01..." dir> \
      --ui <FreeBasicPixelArtUIforRPG PNG dir> --font <DungeonFont.ttf>

Any source may be omitted; only the parts you provide are baked.

What it produces:
  * Character strips  {name}_{state}_se_{frames}.png  (SpriteBank convention: right-facing,
    SW/NW are mirrored at load). Frames are union-cropped per character so the feet sit on
    the strip's bottom edge — SpriteDraw.Feet anchors there.
  * UI nine-slice pieces: ui_panel / ui_panel_titled / ui_card / ui_button / ui_button_down.
  * A bitmap font atlas ui_font.png + ui_font.json (glyph metrics), baked at 16 px.
"""
import argparse
import json
import os

from PIL import Image, ImageDraw, ImageFont

OUT = os.path.join(os.path.dirname(__file__), "..", "DofusSlice.Game", "assets")

# name -> (path template, {game state -> pack state}, frame size)
CHARS2 = {
    "hero": ("SpriteSheets(96x96)/Human_Soldier_Sword_Shield/With_Shadows/Human_Soldier_Sword_Shield_{s}-Sheet.png", 96),
    "slime": ("SpriteSheets(96x96)/Monster_Slime/With_Shadows/Monster_Slime_{s}-Sheet.png", 96),
}
CHARS2_STATES = {"idle": "Idle", "walk": "Walk", "cast": "Attack1", "hurt": "Hurt", "die": "Death"}

TINY = {
    "soldier": ("Characters(100x100 split)/Soldier/Soldier with shadows/Soldier_{s}.png", 100),
    "orc": ("Characters(100x100 split)/Orc/Orc with shadows/Orc_{s}.png", 100),
}
TINY_STATES = {"idle": "Idle", "walk": "Walk", "cast": "Attack01", "hurt": "Hurt", "die": "Death"}


def recolor_ranger(img):
    """Hue the hero's steel armour toward ranger green (low-saturation pixels only), so the
    archer stops wearing the sword-and-board guy's exact skin."""
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            mx, mn = max(r, g, b), min(r, g, b)
            sat = 0 if mx == 0 else (mx - mn) / mx
            if sat < 0.28:  # armour greys -> mossy green leathers
                px[x, y] = (int(r * 0.62), int(g * 0.88), int(b * 0.5), a)
    return img


def bake_character(name, root, template, states, frame, recolor=None):
    sheets = {}
    for game_state, pack_state in states.items():
        p = os.path.join(root, template.format(s=pack_state))
        if os.path.exists(p):
            sheets[game_state] = Image.open(p).convert("RGBA")
    if not sheets:
        print(f"  {name}: no sheets found, skipped")
        return

    # Union bbox across every frame of every state -> one crop box with a shared baseline,
    # so animations don't bob when the renderer anchors the strip bottom at the feet.
    x0 = y0 = 10_000
    x1 = y1 = -1
    for im in sheets.values():
        for i in range(im.width // frame):
            b = im.crop((i * frame, 0, (i + 1) * frame, frame)).getbbox()
            if b:
                x0 = min(x0, b[0]); y0 = min(y0, b[1])
                x1 = max(x1, b[2]); y1 = max(y1, b[3])
    w, h = x1 - x0, y1 - y0
    for game_state, im in sheets.items():
        n = im.width // frame
        strip = Image.new("RGBA", (w * n, h))
        for i in range(n):
            strip.paste(im.crop((i * frame + x0, y0, i * frame + x1, y1)), (i * w, 0))
        if recolor:
            strip = recolor(strip)
        strip.save(os.path.join(OUT, f"{name}_{game_state}_se_{n}.png"))
    print(f"  {name}: {len(sheets)} states, frame {w}x{h}")


def bake_ui(ui_png_dir):
    mt = Image.open(os.path.join(ui_png_dir, "Main_tiles.png")).convert("RGBA")
    bt = Image.open(os.path.join(ui_png_dir, "Buttons.png")).convert("RGBA")

    def crop_trim(src, box, out):
        c = src.crop(box)
        c = c.crop(c.getbbox())
        c.save(os.path.join(OUT, out))
        print(f"  {out}: {c.size}")

    crop_trim(mt, (209, 196, 256, 232), "ui_panel.png")          # plain cream panel
    crop_trim(mt, (5, 96, 43, 133), "ui_panel_titled.png")       # brown header + cream body
    crop_trim(mt, (11, 0, 37, 37), "ui_card.png")                # small titled card
    # The blank green button block stacks the normal and pressed variants; split at the
    # sparsest interior row (the shadow gap between the two).
    btn = bt.crop((3, 96, 45, 128))
    rows = [sum(1 for x in range(btn.width) if btn.getpixel((x, y))[3] > 0) for y in range(btn.height)]
    mid = rows.index(min(rows[8:-8]), 8)
    top, bottom = btn.crop((0, 0, btn.width, mid)), btn.crop((0, mid, btn.width, btn.height))
    top.crop(top.getbbox()).save(os.path.join(OUT, "ui_button.png"))
    bottom.crop(bottom.getbbox()).save(os.path.join(OUT, "ui_button_down.png"))
    print(f"  ui_button(+down): split at row {mid}")


def bake_font(ttf_path, size=16):
    font = ImageFont.truetype(ttf_path, size)
    chars = [chr(c) for c in range(32, 127)]
    pad = 1
    entries = []
    for ch in chars:
        b = font.getbbox(ch)
        w = max(1, b[2] - b[0])
        h = max(1, b[3] - b[1])
        adv = int(font.getlength(ch))
        entries.append((ch, b, w, h, adv))
    cols = 12
    cw = max(e[2] for e in entries) + pad * 2
    chh = max(e[3] for e in entries) + pad * 2
    rows = (len(entries) + cols - 1) // cols
    atlas = Image.new("RGBA", (cols * cw, rows * chh), (0, 0, 0, 0))
    d = ImageDraw.Draw(atlas)
    meta = {"size": size, "lineHeight": chh, "glyphs": {}}
    for i, (ch, b, w, h, adv) in enumerate(entries):
        gx, gy = i % cols * cw + pad, i // cols * chh + pad
        d.text((gx - b[0], gy - b[1]), ch, font=font, fill=(255, 255, 255, 255))
        meta["glyphs"][ch] = [gx, gy, w, h, b[0], b[1], adv]
    atlas.save(os.path.join(OUT, "ui_font.png"))
    with open(os.path.join(OUT, "ui_font.json"), "w") as f:
        json.dump(meta, f)
    print(f"  ui_font: {len(entries)} glyphs at {size}px, atlas {atlas.size}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--chars2", help="FreeCharactersAnimationsAssetPack directory")
    ap.add_argument("--tiny", help="Tiny RPG Character Asset Pack directory")
    ap.add_argument("--ui", help="Free Basic Pixel Art UI PNG directory")
    ap.add_argument("--font", help="path to a pixel TTF (e.g. DungeonFont.ttf)")
    a = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)

    if a.chars2:
        print("characters (FreeCharactersAnimations):")
        for name, (tpl, frame) in CHARS2.items():
            bake_character(name, a.chars2, tpl, CHARS2_STATES, frame)
        # The archer: the hero re-dressed in ranger greens (distinct silhouette colourway).
        bake_character("archer", a.chars2, CHARS2["hero"][0], CHARS2_STATES,
                       CHARS2["hero"][1], recolor=recolor_ranger)
    if a.tiny:
        print("characters (Tiny RPG):")
        for name, (tpl, frame) in TINY.items():
            bake_character(name, a.tiny, tpl, TINY_STATES, frame)
    if a.ui:
        print("ui:")
        bake_ui(a.ui)
    if a.font:
        print("font:")
        bake_font(a.font)


if __name__ == "__main__":
    main()

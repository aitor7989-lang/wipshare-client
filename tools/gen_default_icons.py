#!/usr/bin/env python3
"""Generate ORIGINAL 1-bit spell/slot icons for THE GAUNTLET's committed defaults.

My own procedural glyphs (not the icon pack) so they ship in git / the CI release and
the spell wells + gear slots read even with no user art dropped. The pack-baked icons
(tools/bake_icons.py, private) OVERRIDE these when the user drops assets/. White-on-
transparent 16x16, tinted by the game.

    python3 tools/gen_default_icons.py
"""
import pathlib

from PIL import Image, ImageDraw

OUT = pathlib.Path(__file__).resolve().parent.parent / "Gauntlet" / "assets-default"
W = (255, 255, 255, 255)


def canvas():
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def flame():
    img, d = canvas()
    d.polygon([(8, 1), (11, 6), (10, 9), (12, 8), (12, 12), (8, 15),
               (4, 12), (4, 8), (6, 9), (5, 6)], fill=W)
    return img


def comet():
    img, d = canvas()
    d.ellipse([9, 2, 14, 7], fill=W)
    d.line([2, 14, 9, 6], fill=W, width=2)
    d.line([4, 14, 10, 8], fill=W, width=1)
    return img


def blooddrop():
    img, d = canvas()
    d.polygon([(8, 2), (12, 10), (8, 14), (4, 10)], fill=W)
    return img


def bow():
    img, d = canvas()
    d.arc([3, 2, 12, 14, ], 300, 60, fill=W, width=2)
    d.line([4, 3, 4, 13], fill=W, width=1)       # string
    d.line([2, 8, 14, 8], fill=W, width=1)       # arrow
    d.polygon([(14, 8), (11, 6), (11, 10)], fill=W)
    return img


def arrow():
    img, d = canvas()
    d.line([2, 13, 13, 2], fill=W, width=2)
    d.polygon([(13, 2), (8, 3), (12, 7)], fill=W)   # head
    d.line([2, 13, 5, 12], fill=W, width=1)         # fletch
    d.line([2, 13, 3, 10], fill=W, width=1)
    return img


def target():
    img, d = canvas()
    d.ellipse([2, 2, 14, 14], outline=W, width=1)
    d.ellipse([5, 5, 11, 11], outline=W, width=1)
    d.rectangle([7, 7, 9, 9], fill=W)
    return img


def hammer():
    img, d = canvas()
    d.rectangle([3, 2, 12, 6], fill=W)      # head
    d.line([8, 6, 8, 14], fill=W, width=2)  # haft
    return img


def fist():
    img, d = canvas()
    d.rounded_rectangle([3, 6, 13, 14], radius=2, fill=W)
    for x in (5, 8, 11):
        d.line([x, 6, x, 4], fill=W, width=1)   # knuckles
    return img


def shield():
    img, d = canvas()
    d.polygon([(8, 2), (13, 4), (13, 9), (8, 14), (3, 9), (3, 4)], fill=W)
    return img


def dagger():
    img, d = canvas()
    d.polygon([(12, 2), (13, 3), (6, 11), (5, 10)], fill=W)  # blade
    d.line([3, 13, 6, 10], fill=W, width=2)                  # hilt
    d.line([4, 9, 8, 13], fill=W, width=1)                   # guard
    return img


def hook():
    img, d = canvas()
    d.line([8, 1, 8, 8], fill=W, width=2)              # shaft
    d.arc([4, 6, 12, 14], 0, 250, fill=W, width=2)     # hook curve
    return img


def sword():
    img, d = canvas()
    d.line([12, 2, 5, 11], fill=W, width=2)     # blade
    d.line([3, 11, 7, 13], fill=W, width=2)     # guard
    d.line([3, 13, 5, 15], fill=W, width=2)     # pommel
    return img


def breastplate():
    img, d = canvas()
    d.polygon([(4, 3), (12, 3), (13, 7), (8, 14), (3, 7)], fill=W)
    d.line([8, 4, 8, 12], fill=(0, 0, 0, 0), width=1)
    return img


def boots():
    img, d = canvas()
    d.polygon([(5, 2), (9, 2), (9, 10), (13, 10), (13, 14), (5, 14)], fill=W)
    return img


def amulet():
    img, d = canvas()
    d.arc([4, 2, 12, 10], 20, 160, fill=W, width=1)   # chain
    d.polygon([(8, 8), (11, 12), (8, 15), (5, 12)], fill=W)  # gem
    return img


ICONS = {
    "icon_spell_ruin_bolt": flame, "icon_spell_flashfire": flame,
    "icon_spell_blood_pact": blooddrop, "icon_spell_backblast": comet,
    "icon_spell_piercing_shot": bow, "icon_spell_crippling_arrow": arrow,
    "icon_spell_deadeye": target, "icon_spell_barbed_quill": arrow,
    "icon_spell_slam": hammer, "icon_spell_seize": fist,
    "icon_spell_bastion": shield, "icon_spell_blood_price": dagger,
    "icon_spell_attraction": hook,
    "icon_slot_weapon": sword,
    "icon_slot_blade": sword, "icon_slot_plate": breastplate,
    "icon_slot_boots": boots, "icon_slot_charm": amulet,
}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, fn in ICONS.items():
        fn().save(OUT / f"{name}.png")
    print(f"wrote {len(ICONS)} default icons to {OUT}")


if __name__ == "__main__":
    main()

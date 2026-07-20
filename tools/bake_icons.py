#!/usr/bin/env python3
"""Bake spell/slot icons from the 1-bit Pixel Icons pack into GAUNTLET icon PNGs.

The pack (owner-supplied) ships ~1500 tight-cropped two-tone 16px glyphs. This maps
each class skill + the weapon slot to a fitting glyph, flattens it to a WHITE-on-
transparent silhouette centred in 16x16 (so the game's tint law colours it in its
element like every other 1-bit icon), and writes icon_spell_<key>.png / icon_slot_*.

Third-party pack art — NEVER committed (no bundled license). Baked LOCALLY into the
user's private assets/ drop; the committed assets-default/ carries original icons.

    python3 tools/bake_icons.py --src <Sprites_Cropped dir> --out <assets dir>
"""
import argparse
import pathlib

from PIL import Image

# game icon name  ->  pack glyph (filename stem in Sprites_Cropped)
MAP = {
    # cannon (fire)
    "icon_spell_ruin_bolt":      "RPG_Spell_Skill_Magic_Fireball_Flame_Bolt",
    "icon_spell_flashfire":      "Weather_Wildfire_Flame_Element_Hot_Burn",
    "icon_spell_blood_pact":     "RPG_Buff_Enraged_Anger_Bloodlust_Taunt",
    "icon_spell_backblast":      "RPG_Spell_Skill_Meteorite_Strike_Comet",
    # archer (air)
    "icon_spell_piercing_shot":  "RPG_Item_Weapon_Bow_Ranged_Shooting",
    "icon_spell_crippling_arrow":"RPG_Item_Weapon_Crossbow_Bolt_Ranged_Pierce_Damage",
    "icon_spell_deadeye":        "RPG_Stat_Accuracy_Ranged_Target_Arrow",
    "icon_spell_barbed_quill":   "RPG_Skill_Flaming_Shot_Fire_Arrow",
    # bulwark (earth)
    "icon_spell_slam":           "RPG_Item_Weapon_Hammer_Mace_Crushing_Damage",
    "icon_spell_seize":          "Emoji_Hand_Fist_Cursor_Pan_Grab_Closed",
    "icon_spell_bastion":        "RPG_Item_Stat_Shield_Defense_Armor",
    "icon_spell_blood_price":    "RPG_Item_Weapon_Dagger_Knife",
    "icon_spell_attraction":     "RPG_Skill_Harpoon_Shot_Hooking_Pulling_Arrow",
    # the free class weapon (spark / loosed arrow / shove all share this slot)
    "icon_slot_weapon":          "RPG_Skill_Strike_Attack_Sword_Slash_Cleave",
    # gear slots (band wardrobe)
    "icon_slot_blade":           "RPG_Item_Weapon_Sword_Attack_Melee_Slashing_Damage",
    "icon_slot_plate":           "RPG_Item_Armor_Equipment_Slot_Chest_Breastplate_Body",
    "icon_slot_boots":           "RPG_Item_Armor_Equipment_Slot_Feet_Boots_Legplates",
    "icon_slot_charm":           "RPG_Item_Accessory_Trinket_Equipment_Slot_Necklace_Amulet_Talisman_Jewelry",
}


def silhouette(path: pathlib.Path) -> Image.Image:
    """Flatten a two-tone pack glyph to a white-on-transparent silhouette, centred 16x16."""
    src = Image.open(path).convert("RGBA")
    box = src.split()[3].getbbox() or (0, 0, src.width, src.height)
    src = src.crop(box)
    out = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    ox, oy = (16 - src.width) // 2, (16 - src.height) // 2
    sp = src.load()
    for y in range(src.height):
        for x in range(src.width):
            if sp[x, y][3] > 0 and 0 <= ox + x < 16 and 0 <= oy + y < 16:
                out.putpixel((ox + x, oy + y), (255, 255, 255, 255))
    return out


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True, help="the pack's Sprites_Cropped directory")
    ap.add_argument("--out", required=True, help="assets dir to write icon_*.png into")
    args = ap.parse_args()
    src, out = pathlib.Path(args.src), pathlib.Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    made = 0
    for name, stem in MAP.items():
        p = src / f"{stem}.png"
        if not p.exists():
            print(f"  !! {stem}.png missing, skipped {name}")
            continue
        silhouette(p).save(out / f"{name}.png")
        made += 1
    print(f"baked {made}/{len(MAP)} icons into {out}")


if __name__ == "__main__":
    main()

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
      [--icons <1bit_Pixel_Icons dir>] --out DofusSlice.Game/assets
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


# 1-bit Pixel Icons pack: named 16x16 two-tone glyphs (white fill + black outline, real
# alpha) -> baked at 2x as icon_*.png. Drawn tinted Ink: white -> ink, outline stays dark.
ICON_MAP = {
    # characteristics + vitals
    "icon_stat_vit": "RPG_Stat_HP_Health_Heart",
    "icon_stat_str": "RPG_Stat_Strength_Fist_Melee_Attack",
    "icon_stat_int": "RPG_Stat_Intelligence_Intellect_Brain_Wisdom_Thinking_IQ",
    "icon_stat_cha": "RPG_Stat_Luck_Four_Leaf_Clover",
    "icon_stat_agi": "RPG_Skill_Dash_Dodge_Movement_Speed_Run_Sprint",
    "icon_stat_wis": "RPG_Magic_Crystal_Ball_Clairvoyance_Omnipotence",
    "icon_stat_ap": "RPG_Stat_MP_Mana_Star",
    "icon_stat_mp": "RPG_Stat_Dexterity_Agility_Boots_Movement_Speed",
    "icon_stat_ini": "Boardgames_Chess_Clock_Timer",
    # equipment slots (items resolve through their slot)
    "icon_slot_weapon": "RPG_Item_Weapon_Sword_Attack_Melee_Slashing_Damage",
    "icon_slot_hat": "RPG_Item_Armor_Equipment_Slot_Head_Helmet",
    "icon_slot_cape": "RPG_Item_Accessory_Armor_Equipment_Slot_Cape_Cloak_Clothing",
    "icon_slot_amulet": "RPG_Item_Accessory_Trinket_Equipment_Slot_Necklace_Amulet_Talisman_Jewelry",
    "icon_slot_ring": "RPG_Item_Accessory_Trinket_Equipment_Slot_Finger_Ring_Jewelry",
    "icon_slot_belt": "RPG_Item_Accessory_Armor_Equipment_Slot_Waist_Belt",
    "icon_slot_boots": "RPG_Item_Armor_Equipment_Slot_Feet_Boots_Legplates",
    # crew spells
    "icon_spell_piercing_shot": "RPG_Item_Weapon_Arrow_Ranged_Pierce_Damage",
    "icon_spell_crippling_arrow": "RPG_Skill_Harpoon_Shot_Hooking_Pulling_Arrow",
    "icon_spell_deadeye": "RPG_Stat_Accuracy_Ranged_Target_Arrow",
    "icon_spell_slam": "Emoji_Hand_Fist_Cursor_Pan_Grab_Closed",
    "icon_spell_seize": "Software_Link_Chain_Shortcut_Combo",
    "icon_spell_bastion": "RPG_Item_Stat_Shield_Defense_Armor",
    "icon_spell_blood_price": "RPG_Skill_Teeth_Fangs_Bite_Beast_Vampire_Blood_Leech_Damage",
    "icon_spell_ruin_bolt": "Alchemy_Element_Fire",
    "icon_spell_flashfire": "RPG_Item_Weapon_Torch_Light_Flame_Fire",
    "icon_spell_blood_pact": "RPG_Buff_Enraged_Anger_Bloodlust_Taunt",
    "icon_spell_blink": "RPG_Buff_Blink_Teleport_Invisibility",
    # mob signatures (enemy hover cards + essence-taught spells)
    "icon_spell_husk_strike": "Boardgames_Card_Attack_Sword",
    "icon_spell_marrow_spit": "Misc_Poison_Venom_Skull_Drop_Death",
    "icon_spell_grave_bite": "Cosmetics_Lips_Mouth_Vampire_Fangs",
    "icon_spell_warden_ironhide": "RPG_Difficulty_4_Hard_Knightly_Kite_Heater_Shield",
    "icon_spell_mite_sap": "Weather_Water_Droplet_Liquid_Rain_Element_Big",
    "icon_spell_wraith_wail": "RPG_Creature_Archetypes_Ghost_Specter_Poltergeist",
    "icon_spell_ghoul_rend": "RPG_Skill_Claw_Scratch_Rake_Maul_Attack_Damage",
    "icon_spell_piper_gift": "Media_Musical_Instrument_Flute_1",
    "icon_spell_sexton_smash": "RPG_Item_Weapon_Hammer_Mace_Crushing_Damage",
    # UI chips
    "icon_ui_char": "Travel_Person_Player_Character_Single",
    "icon_ui_book": "Misc_Religion_Christianity_Bible_Book_Cross_Tome_Libram",
    "icon_ui_bag": "Travel_Backpack_Bag_Pouch_Small",
    "icon_ui_gold": "RPG_Coin_Gold_Currency_Money_GP",
    "icon_ui_bread": "Food_Bread_Loaf",
    "icon_ui_draught": "Alchemy_Potion_Vial_Bottle_Heart_Health_Life_Full",
    "icon_ui_essence": "Alchemy_Sulphur_Sulfur_Soul",
    "icon_ui_bell": "Travel_Bell_Alarm_Alert_Disaster",
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
    ap.add_argument("--icons", type=Path, default=None)
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

    if args.icons:
        n = 0
        for out_name, src in ICON_MAP.items():
            f = args.icons / "Sprites" / f"{src}.png"
            if not f.exists():
                print(f"  !! missing {src}"); continue
            im = Image.open(f).convert("RGBA")
            im = im.resize((im.width * 2, im.height * 2), Image.NEAREST)  # keep two-tone as authored
            im.save(args.out / f"{out_name}.png"); n += 1
        print(f"  {n} icons baked from the 1-bit Pixel Icons pack")

    print("done.")


if __name__ == "__main__":
    main()

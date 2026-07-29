#!/usr/bin/env python3
"""Bake the Dofus 'oldUI Remake' fan theme into the game's (gitignored) assets folder.

Point this at a checkout of https://github.com/aspette/dofus_themes and it copies the
window chrome, buttons, gauges, slots and stat icons the game knows how to wear into
DofusSlice.Game/assets/dofusui/, plus a manifest.json carrying each piece's nine-slice
margins (taken from the theme's own scale9Grid metadata). The repo never ships this art:
without the folder the game keeps its procedural Emberwick chrome.

Usage:
  python3 tools/bake_dofus_ui.py --theme "<dofus_themes>/themes/oldUI Remake/oldUIRemake"
"""
import argparse
import json
import os
import shutil

from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), "..", "DofusSlice.Game", "assets", "dofusui")

# out name -> (theme-relative path, (l, t, r, b) nine-slice margins or None for fixed/stretch)
PIECES = {
    # window chrome
    "panel_frame":   ("common/block_border.png",               (10, 10, 10, 12)),
    "popup_frame":   ("common/window_border_popup_bottom.png", (50, 40, 51, 46)),
    "sub_dark":      ("common/bg_small_border_dark.png",       (10, 10, 11, 10)),
    "sub_light":     ("common/bg_small_border_light.png",      (10, 10, 11, 10)),
    "float_bg":      ("common/floatingWindow_bg_full.png",     (14, 14, 10, 12)),
    # buttons
    "btn":           ("common/btn_green_normal.png",           (12, 0, 12, 0)),
    "btn_over":      ("common/btn_green_over.png",             (12, 0, 12, 0)),
    "btn_pressed":   ("common/btn_green_pressed.png",          (12, 0, 12, 0)),
    "btn_disabled":  ("common/btn_green_disabled.png",         (12, 0, 12, 0)),
    "btn_big":       ("common/btn_greenLargeBorder_normal.png",  (50, 0, 50, 0)),
    "btn_big_pressed": ("common/btn_greenLargeBorder_pressed.png", (50, 0, 50, 0)),
    "btn_close":     ("common/btn_close_normal.png",           None),
    "btn_close_over": ("common/btn_close_over.png",            None),
    # tabs
    "tab_on":        ("common/bg_tab_selected_normal.png",     (12, 27, 12, 8)),
    "tab_on_over":   ("common/bg_tab_selected_over.png",       (12, 27, 12, 8)),
    "tab_off":       ("common/bg_tab_disabled_normal.png",     (12, 27, 12, 8)),
    "tab_off_over":  ("common/bg_tab_disabled_over.png",       (12, 27, 12, 8)),
    # gauges (fills are segmented: crop from the left, never stretch)
    "gauge_track":   ("texture/hud/gauge_background.png",      (4, 2, 4, 2)),
    "gauge_red":     ("texture/hud/gauge_red.png",             None),
    "gauge_blue":    ("texture/hud/gauge_blue.png",            None),
    "gauge_timer":   ("texture/hud/gauge_yellow_timer.png",    None),
    # slots
    "slot64":        ("texture/slot/slot_dark_background_empty.png", (10, 10, 10, 10)),
    "slot_ring":     ("common/green_border_slot_normal.png",   (15, 15, 15, 15)),
    "slot40":        ("common/slot_inventory_normal.png",      (7, 7, 7, 7)),
    "slot40_over":   ("common/slot_inventory_over.png",        (7, 7, 7, 7)),
    "slot40_sel":    ("common/slot_inventory_selected.png",    (7, 7, 7, 7)),
    "spell_bg":      ("texture/hud/background_spell.png",      (7, 7, 7, 7)),
    # hud extras
    "hud_hp":        ("texture/hud/icon_hp_full.png",          None),
    "hud_ap":        ("texture/hud/icon_ap_full.png",          None),
    "hud_mp":        ("texture/hud/icon_mp_full.png",          None),
    "hp_round":      ("texture/hud/background_round_hp.png",   None),
    "ap_mp":         ("texture/hud/background_ap_mp.png",      None),
    "chat_input":    ("texture/hud/chat_input_background.png", (8, 8, 8, 8)),
    "scroll_thumb":  ("common/scrollbar_cursor_normal.png",    (3, 3, 3, 3)),
    # inventory dressing
    "lock":          ("common/lock_dark.png",                  None),
    "help":          ("common/btn_help_normal.png",            None),
    "help_over":     ("common/btn_help_over.png",              None),
    "turn_r":        ("common/btn_arrow_turn_character_normal.png", None),
    "slotsil_amulet": ("bitmap/tx_slot_collar.png",            None),
    "slotsil_hat":   ("bitmap/tx_slot_helmet.png",             None),
    "slotsil_cape":  ("bitmap/tx_slot_cape.png",               None),
    "slotsil_ring":  ("bitmap/tx_slot_ring.png",               None),
    "slotsil_belt":  ("bitmap/tx_slot_belt.png",               None),
    "slotsil_boots": ("bitmap/tx_slot_shoe.png",               None),
    "slotsil_weapon": ("bitmap/tx_slot_weapon.png",            None),
    "slotsil_shield": ("bitmap/tx_slot_shield.png",            None),
    "slotsil_pet":   ("bitmap/tx_slot_pet.png",                None),
    "slotsil_dofus": ("bitmap/tx_slot_dofus.png",              None),
    # NOTE: the theme's texture/windowIcon PNGs are fully transparent placeholders — the real
    # glyphs live in texture/btnIcon (verified by bbox), so the category icons come from there.
    "icon_cat_equip": ("texture/btnIcon/btnIcon_character.png", None),
    "icon_cat_useful": ("texture/btnIcon/btnIcon_chest.png",    None),
    "icon_cat_res":  ("texture/btnIcon/btnIcon_grid.png",       None),
    "icon_cat_quest": ("texture/btnIcon/btnIcon_exclamation_mark.png", None),
    "icon_cat_all":  ("texture/btnIcon/btnIcon_filter.png",     None),
    "icon_gear":     ("texture/btnIcon/btnIcon_gear.png",       None),
}

# The stat icons the kit screen shows (theme charac icon name -> our stat key).
CHARACS = {
    "vitality": "vit", "strength": "str", "intelligence": "int",
    "chance": "cha", "agility": "agi", "wisdom": "wis",
    "actionPoints": "ap", "movementPoints": "mp", "initiative": "ini",
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--theme", help="path to .../oldUI Remake/oldUIRemake")
    ap.add_argument("--spells", help="dir of spell_<key>.png / glyph_*.png icons (classic Dofus tiles)")
    a = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)

    manifest = {}
    manifest_path = os.path.join(OUT, "manifest.json")
    if os.path.exists(manifest_path):
        with open(manifest_path) as f:
            manifest = json.load(f)  # additive runs keep earlier pieces

    if a.spells:
        n = 0
        prefixes = ("spell_", "glyph_", "item_", "egg_", "lock")
        for fn in sorted(os.listdir(a.spells)):
            if not fn.endswith(".png") or not fn.startswith(prefixes):
                continue
            shutil.copyfile(os.path.join(a.spells, fn), os.path.join(OUT, fn))
            manifest[fn[:-4]] = dict(zip(("w", "h"), Image.open(os.path.join(a.spells, fn)).size))
            n += 1
        print(f"  spell/glyph icons: {n}")

    if not a.theme:
        with open(manifest_path, "w") as f:
            json.dump(manifest, f, indent=1)
        print(f"baked {len(manifest)} pieces -> {OUT}")
        return
    for name, (rel, margins) in PIECES.items():
        src = os.path.join(a.theme, rel)
        if not os.path.exists(src):
            print(f"  MISSING {rel}")
            continue
        shutil.copyfile(src, os.path.join(OUT, name + ".png"))
        w, h = Image.open(src).size
        manifest[name] = {"w": w, "h": h}
        if margins:
            manifest[name]["m"] = list(margins)
        print(f"  {name}: {rel} {w}x{h}")

    # The doll-rotate arrow points right; mirror a left twin.
    turn = os.path.join(a.theme, "common/btn_arrow_turn_character_normal.png")
    if os.path.exists(turn):
        img = Image.open(turn).convert("RGBA").transpose(Image.FLIP_LEFT_RIGHT)
        img.save(os.path.join(OUT, "turn_l.png"))
        manifest["turn_l"] = {"w": img.width, "h": img.height}
        print("  turn_l: mirrored")

    # The parchment window body: the theme's block_background is a flat fill — ship a small
    # tile instead of a 1280x900 bitmap.
    bg = os.path.join(a.theme, "common/block_background.png")
    if os.path.exists(bg):
        Image.open(bg).convert("RGBA").crop((16, 16, 80, 80)).save(os.path.join(OUT, "panel_fill.png"))
        manifest["panel_fill"] = {"w": 64, "h": 64, "tile": True}
        print("  panel_fill: 64x64 tile from block_background")

    # The window title strip: keep the silver left ornament + a stretchable tail.
    strip = os.path.join(a.theme, "common/window_border_top.png")
    if os.path.exists(strip):
        Image.open(strip).convert("RGBA").crop((0, 0, 512, 45)).save(os.path.join(OUT, "title_strip.png"))
        manifest["title_strip"] = {"w": 512, "h": 45, "m": [90, 0, 12, 0]}
        print("  title_strip: 512x45 crop from window_border_top")

    # Stat icons for the kit screen.
    ch_dir = os.path.join(a.theme, "texture", "Characteristics")
    if os.path.isdir(ch_dir):
        listing = {f.lower(): f for f in os.listdir(ch_dir)}
        got = 0
        for theme_name, key in CHARACS.items():
            hit = next((v for k, v in listing.items() if theme_name.lower() in k), None)
            if hit is None:
                continue
            shutil.copyfile(os.path.join(ch_dir, hit), os.path.join(OUT, f"charac_{key}.png"))
            manifest[f"charac_{key}"] = dict(zip(("w", "h"), Image.open(os.path.join(ch_dir, hit)).size))
            got += 1
        print(f"  charac icons: {got}/{len(CHARACS)}")

    with open(manifest_path, "w") as f:
        json.dump(manifest, f, indent=1)
    print(f"baked {len(manifest)} pieces -> {OUT}")


if __name__ == "__main__":
    main()

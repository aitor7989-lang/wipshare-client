#!/usr/bin/env python3
"""Generate ORIGINAL default art for the DofusSlice CAMPAIGN roster + spell icons.

The campaign's 1-bit renderer (SliceGame.PixActor) asks for unit sheets named
archer/hero/cannon/husk/hound/spitter/mite/piper/wraith/ghoul/warden/sexton and spell
icons named icon_spell_<key>. Only iop/boar/gobball/piou + tiles were ever committed to
DofusSlice.Game/assets-default/, so a clean CI release (no gitignored pack art) fell back
to placeholder pillars for every fighter and to bare letters for every spell — exactly
the "letter-ball" bug the Gauntlet already solved with committed originals.

This reuses the Gauntlet's own procedural silhouettes + icon glyphs (same names, license-
clean, mine) and writes them here under DofusSlice's state naming (idle/walk/cast + a
single-frame fallback so ANY state resolves). Anything a user drops into the gitignored
assets/ folder still overrides these.

    python3 DofusSlice/tools/gen_campaign_sprites.py
"""
import pathlib
import sys

from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parents[2]          # wipshare-client/
sys.path.insert(0, str(ROOT / "tools"))                      # the Gauntlet generators
import gen_default_sprites as G                               # noqa: E402  silhouettes
import gen_default_icons as I                                 # noqa: E402  icon glyphs

OUT = ROOT / "DofusSlice" / "DofusSlice.Game" / "assets-default"
OUT.mkdir(parents=True, exist_ok=True)

# PixActor's 1-bit archetype -> sprite names (SliceGame.cs). brute is a live crypt mob too.
NAMES = ["hero", "archer", "cannon", "husk", "hound", "spitter", "mite",
         "piper", "wraith", "ghoul", "warden", "brute", "sexton"]


def unit_sheets():
    for name in NAMES:
        fn = G.MAP[name]
        # idle/walk animate; cast reuses the attack swing. All at SE — SpriteBank mirrors
        # SE->SW / NE->NW and falls back to _se for any facing.
        G.sheet(fn, "idle").save(OUT / f"{name}_idle_se_4.png")
        G.sheet(fn, "walk").save(OUT / f"{name}_walk_se_4.png")
        G.sheet(fn, "attack").save(OUT / f"{name}_cast_se_4.png")
        # A single-frame fallback (idle frame 0): the Candidates chain ends at "{name}", so
        # hurt/die/any unhandled state resolves to this instead of a placeholder pillar.
        single = Image.new("RGBA", (G.FW, G.FH), (0, 0, 0, 0))
        fn(ImageDraw.Draw(single), "idle", 0)
        single.save(OUT / f"{name}.png")
    print(f"  units: {len(NAMES)} archetypes x (idle/walk/cast + single)")


# Every skill key the campaign can show in a spell well or the inspector -> a fitting glyph
# from the Gauntlet icon set (player kits already have exact icons; mob skills get a themed
# one so nothing ever falls back to a letter).
ICON_FOR = {
    "ruin_bolt": I.flame, "flashfire": I.flame, "blood_pact": I.blooddrop, "backblast": I.comet,
    "piercing_shot": I.bow, "crippling_arrow": I.arrow, "deadeye": I.target, "barbed_quill": I.arrow,
    "slam": I.hammer, "seize": I.fist, "bastion": I.shield, "blood_price": I.dagger,
    "husk_strike": I.hammer, "marrow_spit": I.arrow, "grave_bite": I.fist, "warden_ironhide": I.shield,
    "mite_sap": I.dagger, "wraith_wail": I.comet, "ghoul_rend": I.dagger, "piper_gift": I.amulet,
    "sexton_smash": I.hammer, "sexton_hook": I.hook, "marrow_rot": I.blooddrop, "brute_hurl": I.hammer,
}


def spell_icons():
    for key, glyph in ICON_FOR.items():
        glyph().save(OUT / f"icon_spell_{key}.png")
    print(f"  icons: {len(ICON_FOR)} spell glyphs")


def main():
    print(f"writing DofusSlice campaign defaults to {OUT}")
    unit_sheets()
    spell_icons()
    print(f"done — {len(list(OUT.glob('*.png')))} PNGs now in assets-default/")


if __name__ == "__main__":
    main()

# TITHE — UI Design Brief & Asset Pack Contract

**For: a Claude Design project that produces a UI skin the game can wear AS-IS.**
The engine already has a complete loader for this contract; if the pack follows the naming,
sizes and nine-slice rules below, it drops into the game with zero code changes.

---

## 1. What the game is

TITHE is a dark single-player dungeon-crawl RPG modeled on **Dofus 1.29**: isometric tactical
combat you *watch* (autobattler), a city hub, a graveyard overworld, and heavy loot/stat
management. Fixed canvas **1280 × 760**, play field above y=600, HUD band below. Pixel-art
characters (~2× density) on flat two-tone iso diamonds; the UI sits on top and defines the
game's whole personality.

**Art direction**: Dofus 1.29 "oldUI" — dark warm-grey windows `rgb(48,48,48)` bodies,
a single bright silver/white rail as the window border (the rail alone defines the window —
no secondary borders), glossy orange pill buttons, rounded-top tab domes, segmented candy
gauges, dark rounded item slots, gold accents `rgb(240,202,96)`, white ink text
`rgb(232,230,224)` with dim grey `rgb(164,158,148)`.

## 2. Hard engine constraints (why past attempts failed)

The renderer consumes **flat PNG pieces** only. It cannot use: vector art, CSS, gradients
other than baked-into-the-PNG, blur/shadows at runtime, fonts (text is drawn by the engine
in DejaVu Sans Bold 13px + 18px — design AROUND these two sizes, never deliver text baked
into images), or images sliced from screenshots of existing games (rights).

Every piece must be:
- an **individual PNG with real transparency** (straight alpha, not matte) — never one big
  collage sheet, never JPEG;
- **original art** (no captures of Dofus/other games; "in the style of" is the point);
- delivered at the **exact pixel size** listed below (the engine scales via nine-slice, not
  by resampling whole pieces);
- accompanied by its **scale9 margins** (left/top/right/bottom, px) when the piece stretches.
  Corners are never scaled; edges stretch along one axis; the centre stretches both.
- for stretch-critical art: put NOTHING ornamental in the stretch zones (they tile/stretch).

**States**: interactive pieces need `normal / over / pressed / disabled` variants where
listed. Selected-state pieces are separate.

**Gauges** are *cropped from the left*, never stretched — design them as full-width strips
whose left N% looks correct when cut at any point (segmented blocks work perfectly).

## 3. The asset pack contract (file names, sizes, slice margins)

Deliver PNGs with exactly these names. Sizes may vary ±20% if margins are updated to match;
names may not. `m=[L,T,R,B]` = scale9 margins.

### Window chrome
| name | size | slice | role |
|---|---|---|---|
| `popup_frame` | 119×124 | m=[50,40,51,46] | THE window frame: one bright rail + corner ornaments, transparent centre. The visible rail should be ~20px thick; nothing outside the rail. |
| `panel_fill` | 64×64 | tiles | window body texture (subtle, near-flat; engine tints it dark) |
| `sub_dark` | 46×40 | m=[10,10,11,10] | dark translucent inner panel / tooltip plate / pill row |
| `sub_light` | 46×40 | m=[10,10,11,10] | slightly more opaque variant (raised cards, dropdowns) |
| `float_bg` | 59×61 | m=[14,14,10,12] | HUD band plate (rounded bottom corners) |
| `title_strip` | 512×45 | m=[90,0,12,0] | ornamental header strip (left ornament + stretchable tail). Note: engine also draws flat header bands; this is optional flavor. |
| `chat_input` | 38×29 | m=[8,8,8,8] | dark input well (search, number wells) |

### Buttons & chips
| name | size | slice | states |
|---|---|---|---|
| `btn` | 95×32 | m=[12,0,12,0] | + `btn_over`, `btn_pressed`, `btn_disabled` — the glossy orange pill |
| `btn_big` | 302×93 | m=[50,0,50,0] | + `btn_big_pressed` — hero CTA (FIGHT!) with built-in shadow padding |
| `btn_close` | 30×25 | fixed | + `btn_close_over` — orange X chip |
| `help` | 32×32 | fixed | + `help_over` — the "?" chip |
| `scroll_thumb` | 10×10 | m=[3,3,3,3] | round orange thumb / tiny round chip |
| `tab_on` | 91×38 | m=[12,27,12,8] | + `tab_on_over`, `tab_off`, `tab_off_over` — rounded-top dome; ON = solid (dark text on it), OFF = outline (dim text) |

### Gauges (crop-from-left fills)
| name | size | role |
|---|---|---|
| `gauge_track` | 666×12, m=[4,2,4,2] | dark segmented track |
| `gauge_red` | 600×14 | HP fill |
| `gauge_blue` | 600×14 | XP/mana fill |
| `gauge_timer` | 660×13 | yellow/orange level & timer fill |

### Slots & inventory
| name | size | slice | role |
|---|---|---|---|
| `slot64` | 64×64 | m=[10,10,10,10] | large equip slot, translucent dark centre |
| `slot_ring` | 64×64 | m=[15,15,15,15] | selection/hover ring for slot64 |
| `slot40` | 40×40 | m=[7,7,7,7] | + `slot40_over` (translucent orange), `slot40_sel` (solid orange) — grid/spell cell |
| `slotsil_*` | 64×64 | fixed | ghost silhouettes drawn inside empty equip slots: `amulet, hat, cape, ring, belt, boots, weapon, shield, pet, dofus` |
| `lock` | 32×32 | fixed | padlock overlay (drawn centred on a dimmed cell) |

### HUD vitals & misc
| name | size | role |
|---|---|---|
| `hud_hp` | 101×85 | the big heart (engine writes cur/max inside it) |
| `hud_ap` | 51×49 | PA badge (blue/star family) |
| `hud_mp` | 49×49 | PM badge (green/leaf family) |
| `rot_l` / `rot_r` | ~68×111 (drawn at 12×18) | character rotate arrows |
| `turn_l` / `turn_r` | 36×33 | small curl arrows (alt) |
| `icon_cat_equip/useful/res/quest/all`, `icon_gear` | 32×32 | white glyphs (engine tints them) |
| `charac_vit/str/int/cha/agi/wis/ap/mp/ini` | ~20×20 | stat icons used beside every stat row |
| `spell_<key>.png` | 96×96 | spell tiles, keys: `piercing_shot, crippling_arrow, slam, bastion, ruin_bolt, flashfire, husk_strike, marrow_spit, grave_bite, warden_ironhide, mite_sap, wraith_wail, ghoul_rend, piper_gift, sexton_smash, seize, blood_pact, blink` |
| `item_<id>.png` | 60×60 | gear icons, ids: `adv_blade, adv_hat, adv_cape, adv_amulet, adv_ring, adv_belt, adv_boots, gravewalkers, pipers_whistle` |

Plus a `manifest.json`: `{ "<name>": { "w": W, "h": H, "m": [L,T,R,B]? , "tile": true? } }`.

## 4. The screens this skin dresses (design references, not new layouts needed)

The layouts exist in code; the skin re-dresses them. For mockups use canvas 1280×760.

1. **Combat band** (y 600–760): crew name + HP gauge rows left; big heart + PA/PM centre;
   actor's spell tiles right (46px wells, AP cost chip). Turn-order cards top-right
   (96×46, active = underlined). Fight log panel (344×250) with header strip.
2. **Spell card tooltip**: header band + icon + name, then effect lines (element-colored),
   meta line (AP · range · cooldown).
3. **Placement**: countdown number + big FIGHT! pill bottom-right; crew list bottom-left.
4. **Kit screen** (window 800×540): crew tabs, LVL + XP gauge, 6 stat rows with [+]
  spenders OUTSIDE the rows, SPELLS list rows (icon, name, RANK n/m, info line, RANK UP
  pill), EQUIPPED and STASH list rows with item icons, effective-stats footer.
5. **NPC shop windows** (620×336): title, list of wide pill buttons (label + price),
  esc-to-close footer.
6. **Fight report / loot window** (600×~380): title, gold + XP pool, per-unit XP rows with
   gauges, level-up call-outs, found items, PRESS SPACE footer.
7. **City / yard HUD**: gold + resources block, crew summary rows, bell timer gauge top,
   yard messages.
8. **Reference recreation**: characteristics + inventory windows as in the classic
   screenshot (portrait, Résumé/Détails tabs, PV/PA/PM pill rows, stat rows, points row,
   Ensembles/Sorts pills; equip doll with silhouettes + rotate arrows, category tabs,
   item grid with counts/locks/selection, search well, kamas row).

## 5. Layout rules the engine enforces (bake them into mockups)

- Content sits ≥ (rail thickness + 12px) inside every window edge; measure the rail from
  the art, don't assume.
- ONE body text size (13px) per panel + ONE heading size (18px); values right-aligned with
  ≥16px clearance from rounded edges; numbers must fit INSIDE their pills.
- Row rhythm: 26–30px pitch, icon (16px) + 34px text indent.
- Stack counts top-LEFT of cells; locks centred on a 45% dimmed cell.
- Selected tab = solid dome + dark text; unselected = outline + dim text.
- Buttons never touch a window rail (≥14px clearance).
- The HUD is a centred plate (~580px), not full-width; the level gauge matches its width.

## 6. Palette (current, for continuity)

| token | rgb |
|---|---|
| window body | 46,44,40 (α≈0.94) over game |
| canvas grey (HUD plate) | 48,48,48 |
| ink / ink-dim | 232,230,224 / 164,158,148 |
| gold accent | 240,202,96 |
| orange pill top→bottom | ~236,110,32 → 196,78,16 |
| header band | 30,28,26 → 16,15,14 + gold hairline 146,116,58 |
| element colors | fire 240,129,46 · water 82,167,232 · earth 189,138,60 · air 134,206,75 · neutral 169,127,214 |
| HP / AP / MP | 233,75,88 / 79,143,240 / 89,193,78 |

## 7. Acceptance test

A pack is "working" when: every file above exists at spec size with straight-alpha
transparency; nine-slice pieces look correct stretched to 2× width and 3× height (test it);
gauges look correct cropped at 10/50/90%; no text is baked into any piece; and dropping the
folder into `assets/dofusui/` + running `tools/bake_dofus_ui.py --spells <pack>` (or copying
directly with the manifest) reskins the game with no code edits.

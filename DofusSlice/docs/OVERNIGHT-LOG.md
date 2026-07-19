# TITHE — Autonomous work log & roadmap

Durable memory for long autonomous work stretches toward Dofus-1.29 parity. NOTE:
the self-scheduling trigger for a fully unattended overnight loop was **blocked by
the environment's permission classifier**, so continuation happens within active
sessions (or when the owner re-prompts) rather than on a timer. The container is
ephemeral — only pushed commits survive — so **every increment is committed and
pushed** to `claude/dofus-engine-vertical-slice-7ijlfm`, and this log records what
is done and what is next.

## Operating rules (read first, every wake-up)

1. Re-read this log and `git log --oneline -15` to see real state (context may have
   been summarized or the container recycled).
2. Pick the **next unchecked item** from the roadmap. Do ONE coherent increment.
3. Keep it **close to Dofus 1.29** (the reference) AND consistent with the TITHE
   prototype and the Slice Bible (`docs/TITHE_slice_bible.md`).
4. **Test before commit:** `dotnet build` all three projects (0 errors) AND run the
   headless sim (`effects` must stay 15/15; run the relevant `campaign …` mode).
5. Data-driven discipline: numbers live in `TitheTables.cs`, never in code.
6. Original code only. Never commit 32rogues art or uploaded `.tmx/.tsx`.
7. Commit with the standard co-author/session trailer, push, then tick the box here
   and commit the log update too.
8. If an item reveals a balance problem, note it under "M5 tuning" rather than
   rabbit-holing. Leave the tree building-clean at every commit.

## Reference commands

```
dotnet build DofusSlice.Core/DofusSlice.Core.csproj
dotnet build DofusSlice.Game/DofusSlice.Game.csproj
dotnet build DofusSlice.Sim/DofusSlice.Sim.csproj
dotnet run --project DofusSlice.Sim effects
dotnet run --project DofusSlice.Sim campaign progression
dotnet run --project DofusSlice.Sim campaign crypt 7
dotnet run --project DofusSlice.Sim campaign survey 40
```

## Roadmap (do in order; each is one tested, committed increment)

- [x] **1. Elements & the 4 elemental stats.** DONE. Classes themed (Bulwark→Earth,
      Archer→Air, Cannon→Fire), skills carry elements, mobs have elemental armor
      (Warden 35% Earth, Sexton 20% all) + the Spitter got Chance for its Water
      spit, gear carries Int/Chance/Power (all-element), StatsOf/factories/HUD/sims
      element-aware. Audit fixes folded in: Wisdom = +1% XP per point now real
      (TitheResolution), XP curve honestly relabeled as 1.29-SHAPED placeholder
      (real mined table + per-mob XP must land together, §9), GearWeight values all
      damage stats + Power. (Bible defers elements, but the owner asked to get as
      close to Dofus as possible — elements are core Dofus.)
- [x] **2. Essence system (Dofus spellbook, M4 core).** DONE. EssencesJson catalog
      maps each essence to its mob's signature skill (+ new "seize" root skill for
      the Husk's essence); CampaignUnit.EssenceSlots (2, campaign-permanent);
      Campaign.TeachEssence (consumption, no class check); combat kit = class
      skills + taught skills; Temple Sister UI action; sim city AI teaches avatar-
      first; HUD shows learned essences. Temple exclusives (Blood Pact, Blink) and
      paid essence removal still open — fold into item 6 (Temple services).
- [x] **3. Spell ranks.** DONE (ranks half): +1 spell point per level (1.29),
      `"ranks"` rows in SkillsJson as cumulative shape/economics overrides (Ruin
      Bolt II range 7, III costs 3 AP; Piercing Shot II min-range 3, III 2 AP;
      Slam II reach 2, III 3 AP), BuildSkill(key, rank) with stable engine ids,
      AutoSpendSpellPoints by class template (signature → essences) hooked into
      ApplyResult + CityPrep; hires arrive with level-1 banked points. Crit half
      DEFERRED ON PURPOSE: the bible excludes crits from the prototype and no
      TITHE skill crits — rewiring CriticalBonus now is dead code + risks the
      Dofus-slice tests. Revisit if crits enter the data.
      BALANCE NOTE: survey greedy went 100% wipes → 83% (avg 8.8 dives) — gear +
      essences + ranks give deep gambles a real chance. Cautious still 0% risk;
      M5 must add risk to cautious play, not nerf greedy hope.
- [x] **4. City equip screen (stash & kit).** DONE (screen half): E in the City
      opens STASH & KIT — equipped list (click to strip) vs stash list (click to
      equip), per-piece stat lines, and a live effective-block readout (HP, damage
      element, AGI/WIS/POW, +AP/+MP, set count) so every click's consequence is
      visible. Avatar-only re-gearing (§6.6.9). Campaign.Equip/Unequip exposed;
      manual ops proven headless (strip the Blade: Fire 137→116 → re-equip 137,
      set-tier recompute included). VISUAL PASS PENDING: Xvfb refused to start
      this session (exit 144) — screenshot the screen when the display env works.
      Manual characteristic spend (stat points UI) still open — fold into a later
      pass with the level-up notification (Bible §6.13).
- [x] **5. Second signature skill per class.** DONE: Bulwark Bastion (self-shield),
      Archer Crippling Arrow (2 AP, −1 MP filler; Piercing became the 4 AP big
      shot after cast-count telemetry proved a 3-AP main mathematically starved
      the filler), Cannon Flashfire (1-2 range burst + push, the dead-zone
      escape). No AI changes needed. WARNING found by survey: crew power made
      BOTH profiles riskless → fixed immediately by mob grades (below).
- [x] **8-partial. Mob grades (Dofus 1.29).** DONE early because item 5 broke the
      death spectrum: +30% HP / +15% stats / +25% XP+gold per grade, grade by
      depth in data (packs g1→g4, crypt g1→g3), mob Fighter.Level carries the
      grade. Measured result: greedy 43% wipes but HIGHER avg gold than cautious
      (2709 vs 2581) — Pillar 4's gamble finally exists. Still open for M5:
      cautious play has 0% risk (hunting packs that actually hunt — item 7 —
      is the intended fix), and the real Dofus 5-grade tables come with §9.
- [x] **6. Temple services + survivors & temperament (M4 social).** DONE in two
      commits. 6a: Temple exclusives (Blood Pact = SelfHpCost engine effect +
      self-economy AI; Blink = targetsGround skills + escape AI; both proven in
      effects tests, 17/17), the rotating shelf at painful prices, surgery
      (essence destroyed). 6b: survivors — 30% of dives spawn one on the yard
      (walk to hire, cheap, nature hidden), Temple VET row reveals Loyal/Grasping,
      and the Grasping exit fires when haul ≥100g with bell ≤75s (30% cut; toast
      in the game, ~ notes headless). Proof mode: `campaign survivors 60` →
      17 offered / 17 hired / 5 betrayals. Threshold tuned 120→100 because a
      full cautious clear banks 116g — the exact arithmetic is in the log.
      Open flourish for later: the huntable traitor chase.
- [x] **7. Hunting packs hunt + the Jumped tier.** DONE. Visual: "hunts" packs step
      toward the crew every 2s inside a 6-cell aggro radius (red "!" on the token
      when closing); adjacency = CATCH → a Jumped fight (scattered mid-map crew,
      no placement phase, "JUMPED" banner). Headless: after each fight, the ONE
      hunter nearest that fight's territory (reach within 10) rolls 4% to catch
      the crew jumped. Balance measured: CAUTIOUS 25% wipes / avg 1879g (finally
      has real risk — the hound near warden-way), GREEDY 85% / 1214g. Greedy
      looking dominated is a dumb-greedy stand-in artifact (it dives the g4
      warband at L1); M5 owns that dial — consider a "weaver" stand-in profile
      that scales depth with level before touching the data.
- [ ] **8. Balance pass (M5).** Give cautious play real risk; make the Crypt a fair
      big bet; tune income-per-dive. Fix the bimodal survive-forever / wipe-fast.
- [x] **9-partial. The behavior-rule uniques.** DONE: Gravewalkers (+1 MP boots)
      and the Piper's Whistle (+1 AP amulet) — set-less rows in ItemsJson; ~15%
      of successful gear rolls resolve to an unowned unique (RandomUnownedGear);
      GearWeight values AP/MP at 45 so auto-equip treats them as the screaming
      finds they are. Verified dropping AND worn in campaign runs (a seed-11
      avatar broke the panoply for Gravewalkers — the classic dilemma, live).
      Still open for 9: a second full set, more bestiary vs Chafer numbers.
- [x] **Polish pass** (user-requested). Fixed: mid-fight Grasping exit (a merc
      could "slip away" while their fighter still stood on the grid — new
      DiveSession.InFight gate; the fold remains the betrayal moment); stale
      "R = NEW FIGHT · B = SEXTON" hints shown during campaign fights where
      neither key works (now standalone-only, and the combat title says THE
      CRYPT inside the crypt); ambush fights report "THE AMBUSH IS BEATEN"
      instead of the generic pack title; crew-summary double-paren typo;
      `_cryptMsg` renamed `_yardMsg` (it carries survivor/betrayal toasts too).
      Checked-clean: no TODO markers, 0 compiler warnings ×3 projects, README
      progression table verified against live output, Wisdom-XP claim verified
      implemented (TitheResolution), HUD crew rows fit the 760px window.

## Progress journal (newest last)

- 2026-07-18: Baseline before overnight run. Done already this session: TITHE M2
  loop, roaming Graveyard, M3 multi-room Crypt, and Dofus stat block + leveling +
  Adventurer set (commit `4ddaedd`). Starting item 1 (elements).
- 2026-07-18 (later): Full audit mid-item-1 caught: set had no Int (Cannon's set
  damage gain had collapsed to ~13%), progression readout computed Ruin Bolt from
  STR while the spell was Fire, Spitter silently lost stat scaling, GearWeight
  ignored Int/Cha/Power, README overclaimed ("real 1.29 curve", Wisdom XP bonus
  that didn't exist). All fixed; Wisdom XP bonus implemented for real. Item 1
  committed. Verified: 3 builds clean, effects 15/15, progression (Fire 30→137,
  ~82% damage lift), crypt clears, survey bimodal-as-known. Next: item 2
  (essence system — consume to learn skills, 2 slots).
- M5 note: real 1.29 XP table must arrive TOGETHER with rescaled per-mob XP
  values (current mob XP is tiny; the real curve alone would stall leveling).
- 2026-07-18 (later still): Upgrades landed — real 1.29 XP table + Dofus-band mob
  XP (gold decoupled via mob "gold" column; pacing verified L3≈3 dives, L5 by 12)
  and the +1 MP Adventurer full-set bonus (ap/mp plumbed through gear). Then item
  2 (essences) done and verified: progression demo shows the kit growing (Ruin
  Bolt + Ironhide + Grave Bite), campaign log shows drops→teaching in the loop.
  Survey: greedy had its FIRST survivor (39/40 wipes) — gear + learned skills give
  deep gambles a sliver of hope; keep an eye on it in M5. Next: item 3 (crit
  wiring per-1.29-data + spell ranks) or item 4 (city equip screen).
- 2026-07-18 (batch close): six commits pushed this stretch — real-1.29 leveling
  + full-set MP (38f7bdf), essences (85cf7aa), spell ranks (d2221d5), equip
  screen (94b1253), second skills (5bf1a8a), mob grades (ad0f9bf). Balance
  spectrum in its best state yet (greedy 43% wipes / better avg gold). NEXT UP:
  item 7 — hunting packs that actually hunt (real-time aggro on the yard, the
  Jumped placement tier on catch). It doubles as the cautious-risk fix, so it
  advances items 7 AND the rest of 8. Then Temple services (6a: exclusives need
  either AI triggers for Blink/Blood Pact or a SelfHPCost effect — design note),
  survivors/temperament (6b), and the visual pass on the equip screen when Xvfb
  cooperates.

## Chamber8x8 skin (the second tileset)

The Scut skin is retired; the game now wears adamzbuub's **Chamber8x8** pack (local-only,
never committed). Method, same as before but sharper: the pack's two example screenshots were
decoded 100% — they are 10× recordings (a 5× reduction also roundtrips because every feature
is even-sized; detailed-tile matching settles the scale), grid origins (2,4)/(4,5). Every cell
masked-matched against the pack: the wall sheet is a ring autotile (+2 concave corners + fill),
the floor sheet an edge autotile whose dark sides carry all wall-base shading, props embed in
wall runs (chest alcoves, doors) or scatter off-grid (bones at (5.12, 2.12)), and nothing is
left over — no dither shadow layer this time, depth is baked into the tiles.

In game: `ChamberSet` (wall/floor atlases + named props + the 4-frame knight), floor edge
autotile per boundary adjacency, wall ring with corner pillars, City alcove chests, webs in
the dungeon families, tomb blocks/pillars/barrels as obstacles, services in pack vocabulary
(open chest + gold, crown, barrel), gates as doors with flanking pillars, and every unit is
the knight — crew silver, the dead in rust/bone tints, walk/use animations driven by the
battle poses. Full ruleset with per-tile evidence: docs/TILESET-RULES.md.

## The 45° turn (squares were boring)

The pixel skin now projects like Dofus: classic 2:1 diamonds, checkered ground baked from the
Chamber paving (axis-aligned texture clipped to the cell, exactly the 1.29 trick), no walls —
the map floats in the dark with a dimmed scenery shelf around it, prop clutter accumulating
against the rim (never a pillar on a corner). Units wear real animation packs now: the
FreeCharactersAnimations hero + slime and the Tiny RPG soldier + orc, baked into SpriteBank
strips by tools/bake_assets.py with feet-baseline union crops — idle/walk/cast/hurt/die per
archetype, mirrored facings, class pads intact. One density rule everywhere: 2 screen px per
texel, integer scales only (the "props feel scaled" fix). The UI wears the pixel pack: cream
9-slice panels and cards, the green button, and the blackletter dungeon font on titles baked
to a bitmap atlas. All of it stays local-only; the repo still runs procedural-clean.

## Tactical mode (the goal was 1.29's chessboard)

Matched to the two reference screenshots (Astrub + Minotoror tactical mode), palette sampled
from the pixels: the field is now flat two-tone tan diamonds with hairline seams floating in
black; void cells are holes; obstacles are slightly elevated tiles — the diamond raised 14 px
on two darker procedural faces, depth-sorted with the fighters. All field props and floor
textures are retired (Chamber art now only backs overworld tokens). No UI above heads: a red
halo under the crew, blue under the dead, brighter for the active unit; hovering any fighter
shows the 1.29 rollover plate (name + health number). Placement paints both teams' ground —
red yours, blue theirs. Everything here is procedural: the tactical look ships in the repo
with zero art files.

## QA runs + the hover pass

Three full scripted playthroughs (city -> trade -> stash -> yard -> fight at speed -> aftermath
-> back out) caught and fixed: the crypt label buried under mob huddles (z-order), and confirmed
the loop banks gold, auto-equips found gear and clears packs correctly. UX layer on top of
tactical mode: the rollover plate grew level, AP/MP and live status effects (POISON 6 (2T),
MP DRAIN, POWER +%); graveyard packs got a composition tooltip with grade and "HUNTS THE
LIVING" warnings; timeline cards show the fighter's plate on hover and ring its cell; services,
the Lychgate and the open Crypt advertise their click ("CLICK TO TRADE / DIVE / DESCEND") when
hovered. Floating damage numbers now wear their element's colour (Fire orange, Water blue, Air
green, Earth ochre, Neutral bone; crits stay gold, heals green) — another AUDIT-129 item down.

## Gum: a real UI editor on top of MonoGame

The game now hosts the Gum UI runtime (Gum.MonoGame nuget; MonoGame bumped to 3.8.5 to match).
A committed, generated project — ui/TitheHud.gumx — holds the watched-combat HUD (bottom band,
crew rows with HP bars, round/turn header) as an editable Gum screen: open it in the visual Gum
editor, drag things around, save, and the running game hot-reloads the change in seconds
(verified live: recolouring the panel mid-fight). The game binds data by element name every
frame, so layouts are free to change shape; missing file or missing names and the hand-drawn
HUD quietly returns. `dotnet run --project DofusSlice.Game -- --emit-gum` regenerates the stock
project via the same Gum data classes the editor uses, so the files are always schema-perfect.
One gotcha found on the way: this Gum's "Rectangle" standard is a stroke shape — filled panels
are "ColoredRectangle", which PopulateProjectWithDefaultStandards no longer adds by default.

## Sound, from nothing but math

The game speaks now, and ships no audio files: Audio/Synth.cs is a ~100-line chiptune synth
(oscillators with glides, noise, exponential envelopes, a soft-clip limiter) and SoundBank
renders all 19 sounds from recipes at startup — element-flavoured hits (fire saw-growl, water
plop, air zip, earth thud), the crit arpeggio, heals, a death knell, steps, casts, coin
chimes, victory/defeat stings, and THE BELL as four inharmonic partials of 220 Hz, tolling at
dive start and as the clock crosses 30 and 10 seconds. Ambient beds loop seamlessly (graveyard
wind from low-passed tremolo noise, a 55 Hz crypt drone with exact-cycle frequencies). Sounds
fire on the ANIMATION beats, not the engine events — a hit sounds when the number pops — via
an Sfx delegate on the BattleAnimator. Per-sound cooldowns keep 4x playback from machine-
gunning, M mutes, missing audio hardware disables the bank gracefully (verified headless),
and `--emit-wavs` writes every sound to disk for auditioning; the numbers were verified by
FFT (bell dominant at exactly 220 Hz, drone at 55, coin at 2093).

## The first playtest report (all of it)

The root visual bug is dead: the animator now keeps a replay-position ledger, so a fighter
STANDS where the replay says it stands — no more pre-snapping to the engine's final cell, no
more attacks landing before the walk, no more back-and-forth "teleporting" archers (that was
CenterFor falling through to engine truth). On top of the ledger, watched combat now
TELEGRAPHS: every turn opens with a name banner and a pulsing team ring, movement paints its
green path cell by cell before the walk, and every cast announces the spell by name, shades
its true range blue from the caster's replay cell and pulses the target red before the lunge
— the whole AI decision is legible, and the pacing slowed to match. The dead leave the turn
order (1.29), placement supports swapping two crew, the blackletter font is retired, and the
archer finally has its own skin (the hero re-baked into ranger greens).

Progression got its Dofus furniture: a cream end-of-fight window with per-unit XP shares and
live XP bars, level-up call-outs with their own sting, XP bars + banked-point nudges in the
city crew panel, and the kit screen grew crew tabs, an XP header and MANUAL characteristic
points — five per level, spent with + buttons (or auto-spend), the class contributing only
its level-1 base now. The sims auto-spend to stay honest. Variety: four fight arenas (sunken
yard, tomb rows, barrow circle) keyed per pack and per dive, two new mobs (tomb wraith, grave
ghoul) with skills and two new pack types fielding them.

## Emberwick lands (the user's Claude Design import)

Pulled the "Game UI system design" project through the design MCP (readme, tokens, the Combat
UI board) and implemented its combat kit in-engine, 100% procedurally — rounded slate panels
with the 2px ink outline and vertical gradients, sunken slot wells, amber/gold pill buttons,
and the glossy gem badges rasterized from implicit curves (heart = Vim/HP with a drain fill,
star = Spark/AP, diamond = Stride/MP). The combat band now shows the active unit's vitals
cluster Dofus-style, crew HP wells left, the actor's spells as element-tinted wells right; the
turn order wears slate cards with the amber active underline; the combat log became the
design's turn-chat (header strip, element-tinted lines); FIGHT! is the gold pill. Element
colors everywhere (floats, impacts, log) now come from the Emberwick tokens (ember/brook/
gale/loam/moon). The Gum band yields to Emberwick in combat and stays wired for other screens.

## Death replays in sequence + the archer finally gets a bow
- Playtest: "the enemy disappears before the attacker finishes the action." Root cause:
  `FighterDied` spawned the corpse fade at event time while the killing cast/hit were still
  queued, and the renderer only drew engine-alive fighters. Now death is a queued `DeathAnim`:
  the fallen unit keeps rendering (`BattleAnimator.StillShown`) — board, halo, turn-order
  card, no spoilers — until the blow lands on screen, then the corpse takes over and plays
  the character's actual die strip at the correct pixel height (`CorpseSpriteOf` hook).
- Playtest: "this is not an archer cus he has a sword." The Tiny RPG Soldier's Attack03 is a
  genuine bow shot (the pack even ships the arrow projectile), so the bake now builds the
  archer from it in ranger greens; the bulwark takes the sword-and-shield hero, and the
  cannon wears the hero re-forged in ember reds (`recolor_fire`). Three distinct silhouettes.

## Playtest batch 2: agency, legibility, generated ground
- One SIGNATURE spell per crew class (piercing shot / slam / ruin bolt) — essences add more.
  Spell ranks are now PLAYER-SPENT: the kit screen grew a SPELLS section with RANK UP buttons
  (auto-spend removed from the dive path; sims still auto-spend).
- Spell legibility: hover any kit well in combat for the full card (element damage, AP, range,
  cooldown, every effect); the kit screen prints the same line under each spell.
- The kit screen (E) now opens in the graveyard too — inventory, stats, ranks, mid-dive.
- Balance: archer 11-16 (was 13-18) and AGI 20; bulwark 100 HP at L1 (was 78); cannon 58 (was 42).
- Corpses now carry the fighter's tint (a tinted shared sheet without its tint read as a
  different creature — "the death anim is using old sprites").
- Bread made visible: the mend prints into the fight log ("HARD BREAD: bulwark +22 HP (1 left)")
  and the shop line says what bread does.
- SOLO START: a new campaign is just you; the Hiring Post is the first decision (160g covers
  two L1 hires). The city header says so.
- Ready phase has the 1.29 countdown (30s over the FIGHT! pill, red under 10, auto-starts).
- Ranged casts fly a PROJECTILE (arced streak, element-colored, queue-blocking so the hit
  lands when the shot does).
- PROPER GENERATED MAPS: TitheContent.GenerateArena — irregular ellipse-union silhouettes,
  clustered obstacles, flood-fill connectivity guarantee (west landing always reaches east
  ground; sealed pockets drown). Every fight and every yard uses them.
- THE YARD IS THREE MAPS: near/mid/deep, connected by glowing edge gates; packs sort by reach,
  the survivor wanders the mid yard, the Crypt waits in the deep one.
- Obstacles got art: stacked-canopy trees and rubble boulders on the raised blocks.

## The Dofus oldUI Remake skin, everywhere
- New local-only theme layer: tools/bake_dofus_ui.py copies the aspette/dofus_themes
  "oldUI Remake" chrome (using the theme's own scale9Grid margins) plus classic spell
  tiles + stat icons into assets/dofusui/ (gitignored, never committed). Rendering/DofusUi.cs
  loads the manifest and draws parchment windows, orange pill buttons, tab domes, segmented
  candy gauges, dark rounded slots and the silver title strip.
- ONE hook reskins everything: EwChrome.Theme routes Panel/Well/Pill/HeaderStrip through the
  theme, so the combat band, fight log, timeline cards, placement band and spell cards
  re-dress at once; UiPanelBg/UiButtonBg cover the fight report, NPC windows and every
  button; the kit screen wears the full parchment treatment with stat icons, tab domes,
  spell tiles and hairline list rows; crew HP + XP + the bell are candy gauges.
- Spell icons: 18 classic Dofus tiles mapped by theme (Knell for Sexton's Toll, Lamentations
  for Wraith Wail, Carrion for Ghoul Rend...) drawn in the combat kit wells (with AP chip),
  kit screen rows and spell cards. Sourced from the open-source DofusFashionistaVanced set;
  dofusroom.com itself is blocked by this environment's egress policy, so its mirror of the
  same icons couldn't be used directly.
- Everything keeps the procedural fallback: no assets folder -> Emberwick chrome unchanged.

## Region-by-region fidelity pass on the oldUI skin
- The windows were WRONG: real oldUI composes a near-black translucent body under the silver
  popup frame (bg at ~0.16 RGB), not parchment. DofusUi.Window now does exactly that, and all
  themed window text flipped to white ink (WinInk/WinInkDim/WinGold helpers) — kit screen,
  NPC shops and the fight report included.
- Real ITEMS, per the user's sources (dofusroom.com itself is egress-blocked; identical icons
  pulled from the open-source DofusFashionistaVanced set): 24 classic Dofus eggs fill the demo
  inventory grid, and the game's 9 gear pieces got their icons (Adventurer set, Black Crow's
  boots for the Gravewalkers, a Flute for the Piper's Whistle) — drawn in the kit screen's
  equipped/stash rows.
- Demo scene region fixes: equip slots wear the theme's tx_slot silhouettes; the doll got the
  real turn-character arrows; locked slots show the padlock; category tabs use real btnIcon
  glyphs (the theme's windowIcon PNGs are empty placeholders — bbox-verified); [+] spenders
  sit OUTSIDE the stat rows; help chips, page pip, gold kamas coin, wider XP strip; search
  and kamas rows tucked inside the frame rail.

## Second fidelity pass on the demo (user's region notes)
- Corners fixed: the ornament title strip distorts when squashed, so themed headers are now a
  flat near-black band with a gold hairline (EwChrome.HeaderStrip too), and the X/help chips
  sit INSIDE the frame corners.
- Real margins: window content starts 28px inside the silver rail; tabs at native proportions
  seated on a hairline; the left column re-metered so nothing collides.
- The bottom HUD is flat near-black with a hairline top edge (not the brown float panel) and
  gained the missing furniture: two chat lines + input + toolbar chips, pager arrows + page
  number by the bar, eight glyph option chips, a framed minimap with a diamond grid, and the
  full-width XP strip.
- Typography: DejaVu Sans Bold (freely redistributable) baked to the ui_font atlas at 13px —
  the rounded bold sans reads like Dofus 2's text. The demo draws everything with it.

## Demo pass 3 (user notes)
- The heart, PA and PM now use the theme's OWN sprites (icon_hp_full / icon_ap_full /
  icon_mp_full) instead of the Emberwick gems.
- Header bands moved fully inside the silver rails — the frame's border is never covered.
- The HUD is flat black; the chat corner and the minimap are deleted per the user's call;
  the yellow LEVEL strip runs flush along the very bottom, full width.
- The scene backdrop is black like the original's.

## Demo pass 4 (user notes + their sprite sheet)
- Unified typography: bake_assets now writes a TRUE 26px ui_font_big atlas alongside the 13px
  one; headings and big values draw from it (never integer-doubled), so all text carries the
  same rendering quality. UiFont learned named atlases.
- The character-rotate arrows are now the orange arrow pair from the user's uploaded UI sheet
  (keyed from the JPEG into the local theme assets), placed flanking the doll like the
  original layout.

## Demo pass 5: the grey
- The bottom bar was read as "flat black" — it now uses the EXACT canvas grey sampled from
  the user's sprite sheet, RGB(48,48,48) flat with a double hairline top edge, verified by
  sampling the rendered output. Backdrop lifted to the same neutral family (34,34,34).

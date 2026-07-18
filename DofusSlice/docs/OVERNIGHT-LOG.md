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
- [ ] **6. Temple, survivors & temperament (M4 social).** Vetting, essence removal,
      Grasping survivors that leave with loot when the haul is heavy and clock low.
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
- [ ] **9. Content & uniques.** Second set, the canonical +1 MP boots screaming
      find, more bestiary tuned toward Chafer-family numbers.

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

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
- [ ] **2. Essence system (Dofus spellbook, M4 core).** Essences already drop as
      string tags; make each map to a learnable skill. 2 permanent slots per unit;
      consuming in the city teaches the skill; a unit's combat spell list = class
      skill + learned essences. Wrong-class fits allowed and wasted (Bible §6.5).
- [ ] **3. Critical hits + spell ranks.** `SpellDef` already carries crit fields;
      wire `CriticalChanceOneIn`/`CriticalBonus` into `ComputeDamage` (it currently
      hardcodes +50%). Spell points on level-up buy ranks that change shape/economy.
- [ ] **4. City inventory & equip screen.** View the stash, equip/unequip per unit
      with stat deltas, manual characteristic spend for the avatar (Bible §6.13).
- [ ] **5. More skills per class + AI use.** Give each class/mob 2–3 skills with AI
      trigger conditions so fights read richer (closer to Dofus spell variety).
- [ ] **6. Temple, survivors & temperament (M4 social).** Vetting, essence removal,
      Grasping survivors that leave with loot when the haul is heavy and clock low.
- [ ] **7. Placement depth (Jumped tier) + aggro-catch.** Enemy-defined scattered
      spawn, aggro radii, "caught" starts.
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

# TITHE: The Vertical Slice Bible
### v1.0, Prototype and Handoff Edition (July 2026)
### This document is the single source of truth. It supersedes GDD v0.4 and Slice Bible v0.1 wherever they conflict. Sim Spec v0.1 remains valid as the future production-combat spec (see §2, Two Tracks).

**Audience:** Claude Code agents building the prototype, and the developer (solo, Lead Environment Artist, veteran 3ds Max / UE5 / C++ / .NET, deep Dofus 1.29 knowledge).

**The prime directive for every "how" question:** the answer is Dofus 1.29. How does a unit move in combat? Like Dofus. How does line of sight behave? Like Dofus. How is XP computed? Like Dofus. When this document is silent, mine the 1.29 emulator sources (§9) and imitate. Deviations from Dofus are explicit and listed (§3.4).

---

## 1. The Game in One Page

**TITHE** (working title) is a dark, single-player dungeon RPG. A gloomy city sits above a labyrinth. Diving is a taxed profession. The player owns one character, hires mortal mercenaries, and descends.

**The loop:** City (arrange, restock, hire, pay the tithe) → through the Lychgate into the labyrinth floor → explore, fight, loot, level → the floor's clock runs out and the labyrinth ejects everyone back to the city → arrange again → descend again, stronger, deeper. Campaign ends in one of two ways: you conquer the slice content, or every player-managed character dies and the campaign restarts from zero.

**Combat:** exact Dofus 1.29 grammar (grid, PA/PM, initiative turns, tactical spells), but **watched, not piloted**. All units, including the player's, act via AI policies. The player's skill lives around the fight: what to engage, when, with whom, placed where, carrying which build.

**Tone:** dark, depressing, dangerous, punishing. Moonlighter's structure with the joy inverted: a town that has stopped expecting people back.

**Prototype standard:** planes, cubes, and honest data. Pretty comes later.

### 1.1 Pillars (unchanged, from GDD)

1. **Information is the resource.** Knowing the map, the mobs, the drop tables, and who to trust is the real progression. No minimap, ever: route knowledge is a skill the player's hands learn.
2. **Watchable combat, real grammar.** Fights auto-resolve with full Dofus tactical rules, fully legible. If the player cannot see why a fight was lost, the system failed.
3. **Loot changes behavior.** Baseline gear can be stat-sets, Dofus-style. Standout items and essences must change what the player watches happen.
4. **Harsh, never arbitrary.** Ruin always traces to a choice: a mitigation skipped, unaffordable, or gambled against. Surprise requires ignorance, and ignorance is purchasable away.

### 1.2 Reference games

Dofus 1.29 / Incarnam (combat, leveling, items, mood of first danger), Surviving the Game as a Barbarian manhwa (floors, the clock, essence harvesting, expendable companions), Moonlighter (city-dungeon loop structure, inverted tone), Loop Hero (dread, indirect play), heroic-server Dofus (permadeath economy, drop chasing).

---

## 2. Two Tracks (read this before architecting)

**Track A, the Prototype (now).** Built in UE5 on purchased assets to reach fun fast: the Alex Quevillon Turn-Based Tactic template as the combat foundation, the Dyno Mega inventory and leveling asset for inventory/equipment UI and hooks. Blueprint-heavy, in-engine, no determinism guarantees. Goal: validate the loop and pass the Door Test (§10, M2).

**Track B, Production combat (later).** Sim Spec v0.1 stands: a pure C++ deterministic combat library with a console harness and Monte Carlo balance. Once Track A proves the game is fun, combat rules migrate into the sim, and the template becomes the reference implementation to test against.

**The discipline that makes both tracks possible: assets never own truth.** All rules and numbers live in data tables (§8). Marketplace assets are renderers and executors of that data. Any rule implemented "inside the template" must be expressible as a row, or it is implemented wrong.

---

## 3. Design Decisions, Current and Binding

### 3.1 Decided this pass (supersedes older docs)

1. **XP is Dofus's XP.** The flat one-kill-one-XP system is dead. Adopt the 1.29 experience curve and per-mob XP values, mined from emulator data (§9). Party XP is split Dofus-style (level-weighted; exact formula from mining). Wisdom grants bonus XP as in 1.29. The kill tally survives only as codex flavor.
2. **Skills have Dofus limit fields.** Casts per turn, casts per target, cooldown interval ("relance"), in addition to PA cost, range min/max, line of sight, linear flag, area. Few skills, simple skills, tactical limits.
3. **The Clock.** Every floor has a fixed real-time duration; deeper floors get longer clocks. At zero, the labyrinth ejects everyone to the city: loot kept, Wounded included, dungeon-interior progress reset. The clock never pauses, including inside the dungeon. There is no manual extraction, no recall item, no walking back up. Experienced players rush; lost players lose time to being lost (no minimap makes the clock and the map one system).
4. **Wounds and death, avatar model.**
   - Any unit reaching 0 HP while its side still wins the fight: dragged out **Wounded** (status: -1 PA and -1 PM) if it is player-managed.
   - **Mercenaries die permanently** at 0 HP, mid-fight, gone.
   - Losing a fight outright (all crew at 0): **campaign over if no player-managed units remain. Restart from zero.**
   - Wounded is cured only by an expensive potion (Physicker's Draught), usable outside combat. Wounded does not stack in the prototype (a second down refreshes it). All player-managed units Wounded at once = badly incapacitated but alive: hope survives any won fight.
5. **One character system, two flags.** Every unit (player, merc, survivor, even mobs where sensible) shares the same anatomy: level, XP, stats, equipment slots, two essence slots, skills. Two flags create the modes: `managed_by: player | auto_template` and `on_zero_hp: wounded_if_fight_won | permanent_death`. The avatar campaign is one player-managed unit. A future squad campaign (the multi-account Dofus fantasy) is three. Nothing may hardcode "the" player character: screens take a unit id; campaign-over reads "all player-managed units dead."
6. **Mercenaries auto-level but never re-gear.** They receive their XP share, auto-spend stats via class template, keep the kit they were hired with. With XP and items as scarce as intended, they stay relevant for hours and fade gradually because the player got richer, not because of a decay mechanic. Falling behind is emergent economics.
7. **City and labyrinth are separate scenes** connected by a portal (the **Lychgate**), Moonlighter-style. No streaming cleverness. City is clickable NPCs with dialog/shop windows, Dofus idiom.
8. **The slice is one floor: the Graveyard, containing one dungeon: the Crypt.** Details §5.
9. **Bosses are drop-table peaks, not keys.** Beating the Crypt is prestige and loot (heroic-style set pieces at elevated rates), never a door permit. Descent to floor 2 is risk-gated, not key-gated: in the full game the stairs are simply there. In the slice, the stairs down stand in the graveyard as set dressing, dark and open and non-functional: the whole game's promise in one doorway.
10. **Essences, hardened.** Two slots per character **for the entire campaign**. Learning is consumption. Removal exists but is **very expensive** (temple surgery; whether the removed essence is destroyed or refunded is open, lean destroyed). A wrong-class essence is a wasted slot: an archer skill on a tank is nonsense the game will let you commit to.
11. **Marketplace-first prototype.** Quevillon template = combat. Dyno Mega = inventory/leveling scaffolding. Everything else (exploration, dungeon flow, clock, meta) is custom but thin.

### 3.2 Standing decisions (from prior docs, still true)

Watched combat with a pre-fight placement phase. Click-to-move exploration with party follow and marching order. Aggro radii per mob type; combat never starts by mere room entry, only by initiating or being caught. Deterministic-style lock rule in spirit (flattened tacle). Pushback collision damage adopted. Damage formula shape: base × (100 + stat) / 100. PA/PM baseline 6/3. Fixed camera. Dark visual language; darkness as literal light, not a UI shader. Sparse text. Betrayal: survivors found below carry hidden temperament (Loyal / Grasping); Grasping steals the haul when it is heavy and the clock is low; temple vetting is the paid mitigation. Fiction is original IP; the manhwa and Dofus are structural references only.

### 3.3 Deferred (cut from prototype, listed so nobody builds them)

Traps and the light-economy (candles, peek fidelity tiers) beyond simple darkness. Affixed elites and their essences. Reinforcements mid-fight. Sigil seeding. Elements and the four elemental stats' damage channels. Procedural anything. Second floor. Squad mode UI. Instant-resolve. Contracts. The story (hint at most).

### 3.4 Explicit deviations from Dofus 1.29

No player piloting in combat (policies fight). No critical hits in prototype. Tacle/esquive flattened to a deterministic PM surcharge instead of a dodge roll. No character classes beyond our three archetypes. Single damage stat active until elements arrive. Everything else: imitate Dofus.

---

## 4. The Game Loop, Precisely

1. **City.** Pay tithe if due. Treat Wounded (potion purchase). Buy consumables (healing food for HP between fights, Draughts). Hire or replace mercenaries. Vet a survivor if hired below. Manage the stash and equip units. Talk to NPCs (flavor, hints).
2. **Lychgate.** Confirm crew (player + up to 2 mercs). Enter. The floor clock starts.
3. **Graveyard.** Open dark ground. Skeleton packs drift at the edge of visibility. Most are passive and farmable (click a pack to initiate, Dofus field-mob style). One or two types hunt with wide aggro radii. Farm XP and drops, find caches and the occasional survivor, learn the routes. The Crypt entrance sits deeper in.
4. **Crypt (optional, the dive's big bet).** Linear rooms: enter, mobs wait, initiate when ready; on victory the door behind seals and the next opens; three fight rooms escalating; then the boss room (guardian plus retinue); then the altar, which teleports the crew out. Clock keeps running throughout: attempting the Crypt with six minutes left is a gamble knowingly taken.
5. **The Bell.** Clock hits zero anywhere: everyone is ejected to the city with everything carried. Downed-but-won crew come out Wounded. Crypt progress resets.
6. **Repeat.** Deeper mastery, better gear, higher floors someday. Or the fight that ends everything, and a fresh campaign carrying only the player's knowledge.

---

## 5. The Slice, Concretely

**Scenes:** City (plane, cubes, 3 NPCs, the Lychgate). Graveyard (one handcrafted dark map, modest size, learnable by heart). Crypt (4 arenas: 3 rooms + boss, plus the altar).

**City NPCs (3):**
- **The Tithe-Keeper:** collects the tithe, buys drops, sells consumables (Hard Bread: restores HP outside combat; Physicker's Draught: cures Wounded, very expensive; prices in §8 tables).
- **The Temple Sister:** vets survivors (reveals temperament, fee), performs essence removal (very expensive), sells the two exclusive essences.
- **The Hiring Post:** rotating board of mercenaries with visible class, level, kit, and price.

**Crew:** player's avatar (chosen class, one starting essence drafted from three) plus up to two mercenaries.

**Classes (3), policy + signature + passive:**

| Class | Policy (what you watch) | Signature | Passive |
|---|---|---|---|
| Archer | Keep max distance, shoot highest-value target in range | Piercing Shot: 3 PA, range 4 to 8, LoS | Long Shot: +2 damage at range 6+ |
| Bulwark | Advance, body-block, hold locks | Slam: 4 PA, melee, push 1 cell | Rage Below: bonus damage under 50% HP |
| Cannon | Hold a safe sightline, nuke priority target | Ruin Bolt: 4 PA, heavy, range 3 to 6 | Overchannel: unspent PA banks bonus damage |

**Bestiary (graveyard skins of the locked archetypes; stats to be tuned against mined Incarnam/Chafer-family numbers):**

| Mob | Archetype | Aggro | Essence it drops (rare) |
|---|---|---|---|
| Barrow Husk | Slow melee, high lock | Near zero (farmable) | Seize: melee, target cannot leave adjacency next turn |
| Gravehound | Fast flanker, hunts | Very wide | Pounce: leap 2 cells ignoring lock, once per fight |
| Marrow Spitter | Fragile ranged | Narrow | Marrow Spit: ranged attack, 3 to 6, LoS |
| Crypt Warden | Armored blocker | Zero until approached | Ironhide: +lock and damage reduction, 1 turn |
| Grave Mites | Weak swarm, drains PM on hit | Medium | Sap: light damage, target loses 1 PM next turn |
| Bone Piper | Buffer, grants allies +PA | Narrow | Piper's Gift: grant ally +2 PA this turn |

**Boss: The Sexton.** The graveyard's keeper, large, slow, devastating, with a retinue (Wardens and Mites) in the biggest arena. Dofus Royal pattern: one big monster whose court does the tactical work. Drop-table peak: elevated set-piece rates, guaranteed essence roll. Exact kit designed in the combat drill; must showcase pushback, the swarm, and a reason the Cannon lives or dies by its lock.

**Essence catalog for the slice: the six above + two Temple exclusives:** Blood Pact (2 PA, sacrifice HP for +2 PA this turn), Blink (2 PA, teleport 2 cells, once per fight). Drop rates very low (baseline 3 to 8 percent, tune against mined Dofus drop tables); the Temple sells everything at painful prices per Pillar 4.

**Items:** Dofus 1.29 slot list, because mined item data will arrive in it: Weapon, Hat, Cape, Amulet, Ring x2, Belt, Boots. One full starter set in the graveyard pool (six pieces, Adventurer-set pattern: modest stats per piece, meaningful full-set stat bonus, mined values as reference), one or two behavior-rule uniques (a +1 PM boot equivalent stays the canonical screaming find), plus vendor consumables. Heroic-mode drop philosophy: set pieces drop from mobs and boss at rates worth chasing; mine heroic rates if findable, else tune.

**The Clock, slice values (placeholders):** Graveyard floor: 12 real-time minutes per dive. A veteran full Crypt run should be feasible in roughly 7 to 8 minutes leaving margin; a first-timer should fail the Crypt on time at least once. Tune in M5.

**Tithe (placeholder):** due every third return to the city, amount scaling with campaign progress; missing it triggers a forced contract dive (city sets floor and a quota) in the full game; in the slice, missing it simply escalates the debt ledger. Numbers in §8, tuned in M5.

---

## 6. Systems Specifications

Each system: rules, Dofus reference, data dependencies, and prototype implementation note (template, asset, or custom).

### 6.1 Unit and Character System (custom, foundational)

The universal anatomy: `unit_id, archetype/class_id, level, xp, stats{}, equipment[slots], essence_slots[2], skills[], statuses[], managed_by, on_zero_hp, temperament (hirelings only), hire_price, kit_template`. Mobs use the same struct with `managed_by: ai` and mob-grade stats. Everything downstream (inventory, leveling, combat, roster) consumes units by id. No singleton player.

### 6.2 Stats (Dofus block, prototype subset)

Data model carries the full 1.29 block: **Vitality, Wisdom, Strength, Intelligence, Chance, Agility**, plus Initiative. Prototype rules use: Vitality → HP; Wisdom → XP bonus percent (1.29 rule, exact ratio from mining); **Strength → the single active damage stat** (Int/Cha unused until elements); **Agility → lock and escape** (1.29 used agility for tacle/esquive; our flattened surcharge reads it: extra PM to leave adjacency = max(0, (enemy AGI − mover AGI) / 10), placeholder divisor); Initiative → turn order. Stat points per level and soft-cost tiers: copy the 1.29 characteristic point costs per class-agnostic table (mined). Auto-template spending for mercs: per-class ratio table (e.g., Bulwark 3 Vit : 1 Str : 1 Agi), data-driven.

### 6.3 XP and Leveling (Dofus verbatim)

Level curve: the 1.29 experience table, levels 1 to 30 mined, slice cap expected ~15 (Incarnam band; the curve was tuned for exactly this arc, inherit it). Mob XP: per-mob per-grade values, mined. Party split: 1.29 group rules (level-weighted shares, wisdom bonus per member), formula mined and copied. On level: characteristic points (mined count) + 1 spell point. Spell points buy ranks in signature or slotted essences; ranks change economics or shape, never just damage (rank tables authored in the combat drill).

### 6.4 Skill System

Skill schema (fields mined from 1.29 spell data to confirm names and semantics): `id, name, pa_cost, range_min, range_max, needs_los, linear, casts_per_turn, casts_per_target, cooldown_turns, area (point | cross), effects[], rank, trigger_rule (AI condition)`. Effects closed set for prototype: DealDamage(min,max), PushCells(n), GrantPA(n,target), DrainPM(n), ApplyStatus(id,dur), LeapTo(ignore_lock), SelfHPCost(n). Statuses: Seized, Ironhide, Sapped, WindGifted, **Wounded** (meta-persistent, -1 PA -1 PM). Each skill row includes its AI trigger so the autobattler knows when to use it; triggers from Sim Spec §7 carry over unchanged.

### 6.5 Essence System

Essences are inventory items dropped by their mob at low rates. Consuming (outside combat) teaches the skill to one chosen unit, filling an essence slot. Two slots per unit, campaign-permanent. Removal: Temple service, very expensive, essence lost (open: refund). Player weighs class fit: essences do not check class; bad fits are allowed and wasted. Temple sells all essences expensively (Pillar 4: gambling is the discount path, never the only path).

### 6.6 Combat System (Quevillon template as foundation)

Dofus 1.29 in every mechanic: square grid per arena with obstacle cells, LoS Dofus-style, initiative-ordered turns, PA/PM economy, movement via pathfinding with the lock surcharge, damage formula base × (100 + Strength)/100 minus flat DR, pushback with collision damage (4 per blocked cell placeholder, half to a blocking unit), skill limit fields enforced (per turn, per target, cooldown), turn cap 25 as stalemate guard. **All units act by AI policy; there is no player input during combat except watching and speed control.** Pre-fight placement: the placement screen shows the arena and enemy positions; the player places the crew in the start zone and commits (Prepared tier ships first; Jumped, with enemy-defined scattered cells, arrives with aggro-catch in M2). Fight end: XP split, drop rolls, Wounded assignment or merc death, loot screen.

Template mapping: grid, pathfinding, obstacles, turn manager, skill executor, AI-vs-AI auto-battle = use. Extend: skill schema fields the template lacks (verify cooldown and cast-limit support; add if missing), our damage formula, lock surcharge, pushback collision, the three class policies and six mob policies as custom AI controllers in the template's hooks, event log tap for future Track B parity testing.

### 6.7 Mob and Spawning System (custom)

Spawn tables per zone: mob type, pack size range, count, respawn rule (packs respawn between dives, not during). Wander: slow drift within a leash radius. Aggro: per-type radius; entering it means the pack initiates (Jumped). Player initiates by clicking a pack (Prepared). Packs are visible entities in the world carrying their composition (Dofus group idiom: you see roughly what you are clicking).

### 6.8 Dungeon System (custom, simple)

Room graph, strictly linear for the slice. Room states: sealed-behind, current, locked-ahead. Enter room → mobs wait (no aggro inside; every dungeon fight is chosen) → initiate → win → reappear in cleared room, previous door seals, next opens. Boss room, then altar: interact to teleport crew outside the Crypt entrance. Ejection or leaving resets the dungeon. Data: rooms reference arena layouts and fixed encounter comps.

### 6.9 The Clock (custom, trivial)

Per-floor duration in data. Runs during everything, including combat and dungeon. UI: a diegetic bell/hourglass, prominent. At zero: if a fight is in progress, it resolves, then ejection; otherwise immediate. Ejection: fade, city scene, loot intact, Wounded applied, dungeon reset, tithe check.

### 6.10 Inventory and Equipment (Dyno Mega as scaffolding)

One shared stash plus per-unit equipment (Dofus slots §5). Items defined in our data tables; the asset renders and manages UI and equip/unequip; an adapter maps our item rows to the asset's item defs. Set bonuses: computed from equipped-piece counts against the set table (2..6 piece tiers, mined pattern). The asset's built-in leveling module is used only as hooks; the XP curve and stat grants come from our tables, never its defaults.

### 6.11 Economy

One currency (name open; ledger idiom). Income: mob coin drops and vendoring items. Sinks: consumables (Hard Bread cheap-ish, Draught expensive by design), hires (price by level), vetting, essence purchases and removal, the tithe. The costs sheet lives in data (§8) and is the difficulty curve wearing a ledger; first-pass numbers are placeholders flagged for M5 tuning against actual income-per-dive telemetry.

### 6.12 Roster (custom)

Hiring Post: rotating list (size 3), each merc a full unit with class, level near the player's, template-generated kit, price. Survivors: low-chance spawn in graveyard or crypt rooms; hire cheap mid-dive; class and level visible, temperament hidden; Temple vetting reveals for a fee. Grasping behavior: when carried loot value crosses a threshold and remaining clock is under a limit, the Grasping merc leaves the party with a loot share and despawns toward the Lychgate (prototype: simple despawn plus ledger note; the huntable chase is a later flourish). Merc death: permanent; kit is recoverable as drops on the spot (open: or lost).

### 6.13 City, Controls, Camera, Interaction

Both scenes: click-to-move, party follows in marching order (order set in a simple UI, doubles as default placement fill). Fixed camera, isometric-leaning, per-scene framing. Interactions: click NPC → dialog/shop window (Dofus idiom); click pack → engage confirm; click Lychgate/altar/stairs → transition or flavor. Darkness: prototype-level global dark treatment in the graveyard and crypt (one fog/exposure setting); the full light economy is deferred.

### 6.14 UI (minimal set, no minimap)

Clock. Party bars (HP, Wounded marker). Placement screen. Combat readout (turn order strip, floating damage, speed toggle 1x/2x/4x). Inventory/equip (asset). NPC shop/dialog. Level-up notification and stat spend (player only). Loot/essence pickup toasts. Campaign-over screen. Codex is deferred except a bare drop-log page if cheap.

### 6.15 Save System

Single campaign slot, save in city only (prototype). Serialize: all units, stash, economy, tithe state, codex log, clock/floor config, RNG seeds where relevant.

---

## 7. What the Purchased Assets Must Prove (integration risks)

1. Template supports or can be extended with casts-per-turn, casts-per-target, cooldowns. If its skill model resists, wrap skills in our own executor and use the template only for grid, pathing, and turn flow.
2. Template AI hooks allow fully custom per-unit controllers (our policies), not just parameter tweaks. AI vs AI must run headless-ish (no required player input) to the end of a fight.
3. Template events are tappable into a log (needed for debugging now, Track B parity later).
4. Dyno Mega item and stat models can defer to external data tables without fighting its editor workflow.
5. Both assets tolerate units created at runtime from data (mercs, mobs), not hand-placed actors only.
Day-one spike task: verify all five before deep integration (M0 exit criterion).

---

## 8. Data Tables (the single source of truth)

Master data as JSON (repo-friendly, diffable), with a build step or import path into UE DataTables. Tables: `stats_curve` (xp per level, stat points, spell points), `classes` (policies, signatures, passives, merc auto-spend ratios, kit templates), `skills` (full schema §6.4), `essences` (source mob, drop rate, skill ref, price), `mobs` (per-grade stats, XP, aggro radius, policy, drop table ref), `spawn_tables` (zone, packs), `encounters` (crypt rooms, comps, arena refs), `arenas` (dims, obstacles, start zones), `items` (slot, stats, effects, value), `sets` (pieces, tier bonuses), `drop_tables`, `prices` (services, consumables, tithe schedule), `floors` (clock durations), `strings` (the ~30 lines of ambient text). Every numeric in this Bible marked "placeholder" lives here, never in code.

---

## 9. Dofus 1.29 Mining Plan (for Claude Code)

**Sources:** Araknemu (github.com/Arakne/Araknemu; modular, tested, cleanest to read; data extracted from lang files), Codebreak (github.com/Dyshay/codebreak; C#, full fight loop), SunDofus / AncestraRemake as fallbacks; community wikis (dofuswiki.fandom, dofusretrotools, solomonk.fr, retro.dofusbook.net) for cross-checking item and set values.

**Extract, with file-path citations into `/reference/`:**
1. `experience.csv`: level → cumulative XP, levels 1 to 30; plus characteristic points and spell points per level.
2. `xp_split.md`: the exact party XP division formula (level weighting, wisdom bonus application, group bonus if any).
3. `formulas.md`: damage calculation (base, stat scaling, flat bonuses, resistances), tacle/esquive (for curve reference; we flatten), initiative, pushback damage if present in 1.29 sources.
4. `spell_schema.md`: the real field list of a 1.29 spell/level entry (AP, range, castPerTurn, castPerTarget, relance/cooldown, LoS, linear, area, effects encoding), to finalize our skills schema.
5. `mobs_incarnam.csv` and `mobs_chafer_family.csv`: per-grade level, HP, PA, PM, initiative, stats, XP, and drop tables for the Incarnam roster and the skeleton (Chafer) family: our tuning reference for the slice bestiary.
6. `items_lvl1_15.csv` and `sets_lvl1_15.csv`: starter-band equipment and panoplies with per-piece stats and tier bonuses (Adventurer set as the canonical pattern), plus drop rates where present; note any heroic-mode drop modifiers if discoverable.
7. `characteristic_costs.md`: the stat-point cost tiers for raising characteristics.
**Rule:** mine math and data, never paste code (licenses; TITHE targets commercial release). Every extracted number lands in §8 tables with a source comment.

---

## 10. Build Plan (milestones with exit criteria)

**M0, Foundations.** Repo, data pipeline (JSON → UE), the five asset spike checks (§7), mining pass complete into `/reference/` and first-fill of §8 tables. *Exit: a mob row and a class row round-trip from JSON into the template and the inventory asset.*

**M1, One Fight.** One arena, placement screen (Prepared), our three class policies vs Husk and Spitter packs on template combat with the extended skill schema, XP split and drops on victory, Wounded/merc-death outcomes wired. *Exit: an AI-vs-AI fight a stranger can watch and narrate ("the archer kept range, the tank held the line") without being told the rules.*

**M2, The Graveyard Loop.** City scene with three NPCs and the Lychgate; graveyard with spawner, wandering packs, aggro-catch (Jumped), click-to-engage; the clock and ejection; tithe tick; Hard Bread and Draught functioning; save in city. *Exit: the **Door Test**, adapted: after an ejection, with cubes on a plane, do you immediately want to dive again?*

**M3, The Crypt.** Room chain with sealing doors, three escalating encounters, The Sexton and retinue, the altar out, clock pressure intact. Hiring Post and merc lifecycle complete. *Exit: a full crypt clear and a full crypt time-out both feel correct.*

**M4, The Long Game.** Essence drops, consumption, Temple services (vetting, removal, exclusives), survivors with temperament and the Grasping exit, the starter set with tier bonuses, campaign-over and restart flow. *Exit: a 3-hour campaign session produces at least one essence agony, one merc funeral, and one clock-forced retreat.*

**M5, Tuning.** Numbers pass against telemetry: income per dive, XP pacing vs the mined curve, clock durations, price sheet, drop rates. Itch-list burn-down. *Exit: the developer stops wanting to touch numbers and starts wanting to touch art.*

---

## 11. Claude Code Mission Brief

You are building Track A (§2) of TITHE from this document. Suggested agent split: **Miner** (§9, outputs `/reference/` and fills §8), **Data Engineer** (§8 pipeline and validation), **Combat Integrator** (§6.6 on the Quevillon template, plus §7 spikes), **Systems Builder** (§6.1 to 6.5, 6.9 to 6.12, 6.15), **World Builder** (§6.7, 6.8, 6.13, city/graveyard/crypt graybox). Sequence by §10. Prime directive §0 applies: when in doubt, do it like Dofus 1.29, and cite the emulator source you imitated. Prototype standard: cubes, planes, honest data, no art. Never hardcode a number that belongs in §8; never let a purchased asset own truth; never add player input to combat.

## 12. Open Questions (small, deliberately)

1. Title. 2. Essence removal: destroy or refund. 3. Wounded stacking (default: no). 4. Merc death kit: drops on the spot or lost. 5. Currency name. 6. Betrayal chase fight: later flourish or M4. 7. Campaign-over: any thin unlock besides knowledge (default: none, codex only).

# HANDOVER — `wipshare-client` games (DofusSlice · Gauntlet archived)

_Last updated: 2026-07-25 · branch `claude/dofus-engine-vertical-slice-7ijlfm` · latest work: "audit-driven improvement programme — 16 commits, see §8" · HEAD `911db47`_

---

## 0. TL;DR — where we are

- **Go-forward game: _DofusSlice_** (internally **TITHE**). **The Gauntlet is ARCHIVED**: it still builds, no longer auto-releases, no longer developed (`Gauntlet/ARCHIVED.md`).
- **A 15-agent audit drove this session's work.** It produced ~30 findings; 8 survived adversarial verification. Everything below §8 is the result. `sim effects` is now **40 tests** (was 26).
- **The campaign can finally be LOST and WON.** It previously could not fail at all — `Campaign.Over` reads "the avatar is gone" and nothing ever removed the avatar (0 wipes in 3,200 sim dives; `DrawGameOver` unreachable). Now: missing the tithe three times running ends the run, and **falling in the Crypt is final** (the yard still just wounds you). Felling the Sexton is banked and escalates every future Crypt.
- **Measured balance:** cautious play **15% wipes** over 40 dives (was 0%). Greedy play **100% wipes** — that sim heuristic banks literally nothing (ends on 0 stones), so it is a degenerate strategy rather than a tuning target. Flagged, not "fixed".
- **Four bugs that shipped** were fixed: placement rank-up did nothing to the fight you were in; the arena was non-deterministic across processes; placement cells came from the wrong map; and the campaign had **no critical hits at all** (all the crit juice was unreachable).
- **Known-open, verified as NOT done:** weapon attacks (items still carry no damage/AP/range), glyphs & traps, the enemy threat overlay, build divergence within a class (the ladder is still a fixed `Take(level)`), a title screen, and the **progression curve** — the Crypt chain still stalls at ~2/6 rooms because a level-3 party cannot beat grade-3 rooms. That is a POWER problem, not an attrition one: buffing the rest beat to 70% + wound-clearing barely moved it (18→20 of 72).
- **The sim's crypt runner now models the rest beat.** It didn't, so the audit's brutal "2 rooms in 60" figure measured a Crypt the player never actually fights.

---

## 1. The two games

| Game | Path | What it is | Download (public, no login) | Run |
|---|---|---|---|---|
| **DofusSlice** (TITHE) — _go-forward_ | `DofusSlice/DofusSlice.Game/` | Dark Dofus-1.29-flavored **campaign** dungeon crawl. Combat is **watched, not piloted** — the whole crew + enemy pack fight by AI policy; player skill = class/essence build, placement, engagement, speed control. | [`dofusslice-latest`](https://github.com/aitor7989-lang/wipshare-client/releases/download/dofusslice-latest/DofusSlice-windows.zip) | `DofusSlice.Game.exe` |
| **Gauntlet** — _archived_ | `Gauntlet/` | Tighter **roguelite**: one dealt road of ~11 rooms, a bell/toll clock, run-and-done. Built later on the same engine. **Archived this session** (`Gauntlet/ARCHIVED.md`): kept + buildable, no longer developed or auto-released. | [`gauntlet-latest`](https://github.com/aitor7989-lang/wipshare-client/releases/download/gauntlet-latest/Gauntlet-windows.zip) _(frozen snapshot)_ | `Gauntlet.exe` |

Both are self-contained Windows zips — unzip, run the `.exe`, no .NET install needed. The Gauntlet's download is a frozen snapshot (its CI no longer republishes it).

### DofusSlice campaign, in one screen each
- **Crew:** avatar (chosen class + one drafted essence) + up to two hired mercenaries. Merc death is permanent; a downed avatar comes out Wounded; a full wipe ends the campaign.
- **Scene flow** (`Scene` enum `Combat | City | Graveyard`, `SliceGame.cs:30`): City (Tithe-Keeper / Temple Sister / Hiring Post NPCs + Lychgate) → through the Lychgate → roam the Graveyard on a real-time floor clock → engage packs as watched fights → the Bell ejects you back to City with loot; repeat.
- **Combat:** initiative-ordered PA/PM turns, LoS/range/AoE spells, pushback + hazards, a placement phase, 1×/2×/4× playback speed. Rules in `DofusSlice.Core/Combat/CombatEngine.cs`; presentation only in `SliceGame.cs`.
- **Crypt:** level-3-gated linear sealing-door dungeon (Ossuary → Nave → Reliquary → Sexton's court → altar-out); clock + HP carry through. `DofusSlice.Core/Content/Tithe/DiveSession.cs`.
- **Inventory doll + slots** (`LeaderPanels.cs`, `DollSlots` L193): Dofus slot doll (Weapon/Hat/Cape/Amulet/2×Ring/Belt/Boots), click-to-equip **and** drag-and-drop.
- **Panels:** `C` = CARACTÉRISTIQUES sheet (`DrawCharacterWindow`, `LeaderPanels.cs:76`); `S` = SORTS spell book (`SpellWin`, L35).
- **Sets/essences:** Adventurer starter set (+1 MP full-set) + behaviour-rule uniques; essences drop from mobs, consumed out of combat to teach a skill into one of 2 permanent essence slots.
- **Audio:** fully **synthesized** chiptune SFX + looping ambient beds — no audio asset files. `Audio/Synth.cs`, `Audio/SoundBank.cs`.
- **UI:** Dofus "oldUI" theme (`Rendering/DofusUi.cs`), a Gum HUD skin (`Ui/GumHud.cs`, `ui/TitheHud.gumx`), Emberwick combat chrome, a baked pixel font. All procedural — no Ankama assets committed.

---

## 2. Build & run (local, Linux-friendly)

> **Do NOT run `dotnet build` at the repo root.** The root is a *different* project — a Windows-only WPF screen-capture app (`WipShare.Client.csproj`, `net8.0-windows…`). With no root `.sln`, a bare build resolves to it and fails on Linux with `NETSDK1100` **by design**. That failure is not a game problem.

**DofusSlice (the go-forward):**
```bash
cd DofusSlice
dotnet build DofusSlice.sln -c Release        # Core + Game + Sim; builds clean (0 errors)
dotnet run --project DofusSlice.Game -c Release   # → DofusSlice.Game.exe, the TITHE campaign
# one-off watched fights / seeds:
dotnet run --project DofusSlice.Game -- boss      # Sexton's court
dotnet run --project DofusSlice.Game -- pack      # a pack fight
dotnet run --project DofusSlice.Game -- 4         # start on RNG seed 4
dotnet run --project DofusSlice.Game dofus        # legacy PILOTED slice (not the campaign)
# convenience: DofusSlice/run.sh  or  run.bat
```
`DofusSlice.Sim` (third project in the solution) is a **headless balance/QA simulator**, not a shipped game.

**Gauntlet (archived — `Gauntlet/ARCHIVED.md`; still builds, no longer auto-released):**
```bash
cd Gauntlet
dotnet build Gauntlet.csproj -c Release           # references ..\DofusSlice\DofusSlice.Core
dotnet run -c Release                             # → Gauntlet.exe
dotnet run -c Release -- --sim 500 all            # headless balance ledger (cannon/archer/bulwark)
```

**Build health (verified):** `DofusSlice.sln` → 0 errors / 24 benign warnings (Gum obsolete alias + unreachable debug branches). `Gauntlet.csproj` → 0 errors / 1 benign warning (`CS7022`, the secondary `RunSim.Main` is ignored in favour of top-level `Program.cs`).

---

## 3. Repo layout & shared engine

```
wipshare-client/
├─ WipShare.Client.csproj        # UNRELATED WPF tray app (Windows-only) — ignore for games
├─ HANDOVER.md                   # this file
├─ DofusSlice/
│  ├─ DofusSlice.sln             # the ONLY .sln (Core + Game + Sim)
│  ├─ DofusSlice.Core/           # SHARED ENGINE + TITHE content tables (no deps)
│  ├─ DofusSlice.Game/           # the campaign game (TITHE) → DofusSlice.Game.exe
│  ├─ DofusSlice.Sim/            # headless balance simulator
│  ├─ docs/                      # design bible + audits + roadmaps (the real backlog — see §7b)
│  └─ tools/                     # DofusSlice asset scripts (Python + Pillow)
├─ Gauntlet/                     # roguelite → Gauntlet.csproj (in NO .sln); refs ..\DofusSlice\DofusSlice.Core
│  ├─ assets-default/            # committed original art (see §5)
│  └─ assets/                    # gitignored pack art (see §5)
├─ tools/                        # Gauntlet asset scripts
└─ .github/workflows/            # dofusslice-build.yml, gauntlet-build.yml
```

- **Shared engine = `DofusSlice.Core`.** Combat (`CombatEngine`), AI (`Policy`, `MobBrain`), and all TITHE content tables live here. Both games depend on it, so **an engine change ripples into both**.
- Gauntlet is standalone (`Gauntlet.csproj`), pulling the engine in via a cross-tree `ProjectReference` to `DofusSlice.Core`.

### AI brains (important)
- **`DofusSlice.Core/AI/Policy.cs`** — the real brain (per-unit `AiPolicy`: Bruiser / Flanker / Skirmisher / Artillery / Support). **Drives the DofusSlice TITHE campaign** (`SliceGame.cs:958`, `Policy.TakeTurn` in `UpdateWatchedTurn`) **AND** the Gauntlet mobs + autoplay + balance sim.
- **`DofusSlice.Core/AI/MobBrain.cs`** — a simpler legacy greedy brain. Only reached by the old **piloted** `dofus` mode (`SliceGame.cs:987`). Not used by the TITHE campaign or the Gauntlet.
- ⇒ **Any change to `Policy.cs` affects both games.** (This is why §7a matters for DofusSlice.)

---

## 4. Content & data

All classes, mobs, skills, items, sets, arenas, encounters are **embedded JSON string constants** in
`DofusSlice/DofusSlice.Core/Content/Tithe/TitheTables.cs` (`ClassesJson`, `MobsJson`, `SkillsJson`, `EssencesJson`, `ItemsJson`, arenas, prices…). Kept as strings so the games and the sim consume identical data with no file plumbing. **This file is in the shared `DofusSlice.Core`, so content edits change both games.** Key parsing/lookup lives in `TitheContent.cs` (`MakeMob`, `LoadSkills`, `HasSkill`, `ClassIds`, essence table).

---

## 5. Asset pipeline — the committed-vs-local art rule (CRITICAL, do not break)

**Third-party art packs are NEVER committed to git** (their licenses forbid redistribution). **Only original, procedurally-generated art is committed.** This is enforced by `.gitignore`.

- `Gauntlet/assets/.gitignore` = `*` (+ keep `.gitignore`). The only tracked file under `Gauntlet/assets/` is `.gitignore` itself. All pack-derived PNGs sitting there are **local-only**.
- `Gauntlet/assets-default/` = **58 committed original PNGs** — procedural silhouette animation strips (`{name}_{state}_se_4.png`) + procedural icons (`icon_spell_*`, `icon_slot_*`) + `onebit_rock.png`. License-clean; these ship in git and in the CI binary.
- **`SpriteBank` resolves default-first, then override** (`Gauntlet/Rendering/SpriteBank.cs:54-71`): it indexes `assets-default/` then `assets/`, and **later dir wins**, so any pack file dropped into `assets/` transparently overrides the committed default of the same name. Missing files fall back to procedural drawing. (The class XML-doc at `SpriteBank.cs:33-44` is stale — trust the constructor.)
- **DofusSlice has the same setup**: `DofusSlice.Game/assets-default/` (committed originals: iop/boar/gobball/piou + `tile_*`) and a gitignored `assets/`.

**Which script produces what:**

| Script | Output | Source | Commit? |
|---|---|---|---|
| `tools/gen_default_sprites.py` | `Gauntlet/assets-default/` | own procedural code | ✅ COMMIT (original) |
| `tools/gen_default_icons.py` | `Gauntlet/assets-default/` | own procedural code | ✅ COMMIT (original) |
| `tools/bake_icons.py` | `Gauntlet/assets/` | owner's 1-bit Pixel Icons pack | ❌ LOCAL ONLY |
| `tools/bake_gauntlet_anim.py` | `Gauntlet/assets/` | owner's 48px 1-bit anim pack | ❌ LOCAL ONLY |
| `DofusSlice/tools/gen_default_sprites.py` | `DofusSlice.Game/assets-default/` | own procedural code | ✅ COMMIT (original) |
| `DofusSlice/tools/bake_onebit.py`, `bake_assets.py`, `bake_dofus_ui.py` | `DofusSlice.Game/assets/` | Hexany CC0 / Batuhan UI / owner packs | ❌ LOCAL ONLY |

**Rule of thumb: `gen_*` → `assets-default/` (commit); `bake_*` → `assets/` (never commit).**
CI ships on the committed `assets-default/` fallbacks. To get the "real" pack look, run the `bake_*` scripts locally against the owner's private pack folders and deliver the resulting `assets/` zip out-of-band (never via git).

_(Stale-comment note: `Gauntlet.csproj:21` names `bake_onebit.py` as the Gauntlet override baker — wrong; that script bakes DofusSlice art. The Gauntlet bakers are `bake_icons.py` / `bake_gauntlet_anim.py`. Mechanism is correct; only the name is off.)_

---

## 6. CI & releases

Both workflows: `windows-latest`, `setup-dotnet 8.0.x`, `dotnet publish -c Release -r win-x64 --self-contained`, zip → upload artifact → `gh release create` (force-recreates the tag, `--prerelease`).

| Workflow | Builds | Trigger | Tag | Asset | Exe |
|---|---|---|---|---|---|
| `.github/workflows/dofusslice-build.yml` | `DofusSlice.Game.csproj` | push to the branch on `DofusSlice/**`, or manual | `dofusslice-latest` | `DofusSlice-windows.zip` | `DofusSlice.Game.exe` |
| `.github/workflows/gauntlet-build.yml` | `Gauntlet.csproj` | **`workflow_dispatch` only — ARCHIVED** (push trigger removed) | `gauntlet-latest` | `Gauntlet-windows.zip` | `Gauntlet.exe` |

- **The Gauntlet workflow is now manual-only.** Its push trigger (including the old `DofusSlice/DofusSlice.Core/**` path) was removed when the game was archived, so an engine change no longer rebuilds or republishes the Gauntlet. Run it by hand from the Actions tab if a fresh Gauntlet build is ever needed; the last-built `gauntlet-latest` release stays live as a frozen snapshot.
- A change under `DofusSlice/DofusSlice.Core/**` now triggers **only** the DofusSlice workflow.
- No CI for the root WPF client.
- **CI status was last confirmed GREEN for HEAD `4373803`.** This session's push (engine fixes) re-runs only the DofusSlice build.

---

## 7. Open work / next-up

### 7a. Shared-engine bug-hunt fixes — `Policy` / `ForecastShift` — ✅ APPLIED this session

An adversarial review had found real defects in this session's combat code. Because they live in the shared engine (`DofusSlice.Core`), they hit the DofusSlice campaign too — so all six are now **fixed** (commit _"Apply §7a AI/shove fixes + archive the Gauntlet"_), verified by a new `ForecastShift` regression test (`sim effects` → 26/26 pass) and by re-running the sims (DofusSlice **bit-identical**, no regression; Gauntlet bulwark 38.8%→46.0% as its shoves stopped being wasted). A post-fix verification pass then caught a **7th** defect (follow-up commit): `ForecastShift` couldn't see the game-side warm/BONE avatar ember exemption, so an over-counted ember could feed the spike pass a phantom kill on the warm avatar (Gauntlet only — DofusSlice has no embers/spikes). Fixed with a new engine flag `Fighter.SoftHazardImmune`, set by the Gauntlet on that avatar and honoured by the ember pass — the "honest forecast" invariant now truly holds. Each item below is written as _defect → fix as shipped_:

1. **HIGH — `ForecastShift` over-values embers as kills** (`DofusSlice.Core/Combat/CombatEngine.cs`, the new `ForecastShift`). It sums ember danger as flat stacking `+2/cell`, but embers are **non-lethal** (floor the victim at 1 HP, do nothing at ≤2 HP — see `Gauntlet/RunCore.cs` `TryEmberBurn`). So the AI predicts ember-drag "kills" that never land and wastes its setup. **Fix:** walk the victim's HP through the shove and apply **spikes** (lethal, respect `HazardImmune`) vs **soft hazards/embers** (`TileDanger` — never lethal, `Max(1, hp-n)`, skip at `hp<=2`, and note `TryEmberBurn` ignores `HazardImmune`) with their real rules; return actual HP lost.
2. **HIGH — `TryShove` preempts a better/lethal plain attack** (`Policy.cs:32`, scoring at `~300`). It fires on an absolute threshold (`bestScore >= 200`) *before* the policy switch and never compares against what a normal swing/`TryShootBest` would do — so a low-value wall-slam or hazard chip can steal the AP a lethal hit needed, or shove a killable enemy out of reach. **Fix:** only pre-empt when the shove **kills** (void, or `direct + fc.Damage >= hp`) or **strictly beats** the best plain attack this turn; never fire a chip-shove that displaces a currently-killable enemy.
3. **MED/HIGH — `TryShove` fires a PULL for ranged policies** (`Shovers` yields Push *and* Pull; `TryShove` runs for Skirmisher/Artillery). A kiter that owns a pull will drag an enemy *toward itself* across a hazard for a few chip damage, wrecking the kite. **Fix:** don't pull for ranged policies, and/or penalize a shift that reduces distance to the caster.
4. **MED — `SettleTurn` retreats bruisers/tanks off any hazard cell to the *farthest* safe cell** (`Policy.cs`, the `Danger>0` branch isn't policy-gated). The doc says "bruisers hold the line," but a bruiser on an ember-adjacent melee cell gets sent to the farthest safe cell → charge-in / attack / settle-away / charge-back thrash. **Fix:** prefer a **safe cell that still threatens melee** (restore the old `StepOffHazard` ordering by `CanHitAnyEnemyFrom`), keep bruisers close.
5. **LOW/MED — dead `StepOffHazard`, lost firing-cell logic.** `StepOffHazard` is now unreachable; `SettleTurn` replaced it but dropped its "keep a cell you can still shoot from" ordering, so a ranged unit can settle out of firing position. **Fix:** fold the `CanHitAnyEnemyFrom` preference back into `SettleTurn`; delete the dead method.
6. **LOW — `Charge` push-setup bias** can detour past a nearer attack cell or onto fire (soft inefficiency; no loop). And the `>= 200` threshold comment is inaccurate (a single 3-dmg spike scores 120 < 200).

**Not bugs (verified clean):** `ForecastShift` geometry/void/collision/spike math mirrors `ApplyShift` exactly; no infinite-loop/stalemate (all actions spend resources, loop is 24-guarded); null-shover handling is safe; `poison`/`lifesteal` are fully engine-supported; the mob-kit JSON is well-formed with no dangling refs; essence mappings intact; no save-compat break.

### 7b. DofusSlice backlog (the go-forward game) — from `DofusSlice/docs/`

- **`docs/AUDIT-129.md`** — Dofus-1.29 fidelity audit, **21 findings still open**. High-value: crits still fire in the hero path despite "no crits" (`CombatEngine.cs:281-289` + `SpellLibrary.cs`); `castsPerTarget` missing from the spell pipeline; linear/area shapes not expressible in Tithe skill data; party XP split missing the 1.29 group coefficient; Adventurer Set is 7 pieces incl. weapon vs the real 6-piece no-weapon set; items don't roll min–max jets; grade scaling too steep; aggro is one global 6-cell radius vs per-type; placement shows only blue cells not both teams.
- **`docs/OVERNIGHT-LOG.md:113`** — open item **8: Balance pass (M5)** — give cautious play real risk, make the Crypt a fair big bet, tune income-per-dive, fix the survive-forever/wipe-fast bimodal spread. Plus (item 9) a second full set + more bestiary numbers.
- **`docs/TITHE_slice_bible.md`** — **§9 Dofus-1.29 mining plan** is the big outstanding data task (extract experience.csv, xp_split, formulas, spell_schema, mob/item/set CSVs into a `/reference/` dir). §3.3 Deferred (elemental damage channels, traps/light economy, affixed elites, reinforcements, 2nd floor). §10 M5 Tuning is the remaining milestone. §12 Open Questions (title, essence-removal refund vs destroy, merc-death kit drop-vs-lost, currency name, betrayal-chase timing).
- **`docs/COMBAT-PARITY.md:42`** — still missing: weapons as a distinct system, spell levels/variants, glyphs/traps/portals, flee, tombstones/resurrection, exact Dofus corner-based LoS (current is Bresenham centre-to-centre).
- **`docs/ENGINE-GAP-ANALYSIS.md:59-99`** — ground elevation & overhead layers, summon turn-*end* queue.
- `docs/ROADMAP-POLISH.md`, `docs/ROADMAP-LEADER.md` — fully ticked.
- No literal `TODO`/`FIXME`/`HACK` in the C# source; "placeholder" comments only flag data numbers awaiting the §9 mining pass.

### 7c. Cross-game caveat — ✅ RESOLVED: the Gauntlet-tuned changes are kept in DofusSlice

This session's earlier combat changes (below) live in the shared engine/content and were originally tuned against the **Gauntlet** sim. With the Gauntlet now archived (§0), DofusSlice's balance is the only one that matters — and after the §7a fixes the DofusSlice sims came back **bit-identical** (Graveyard 100% win / 1.35 downed; Sexton 25% / 2.75 downed), so the changes are **kept as-is**. In DofusSlice they only ever manifest as sensible wall-slam shoves: DofusSlice sets neither `LethalVoid` nor ember `TileDanger` and has no spike tiles, so a shove there is collision-only — now correctly bounded by the §7a `TryShove` fix (never steals a lethal swing). A dedicated DofusSlice balance pass (§7b, M5) can retune later if desired.
- Risk/reward AI in `Policy.cs` (shove-into-void/hazard, `SettleTurn` step-off) — DofusSlice crew + enemies use it; reduces to wall-slam shoves there, and the §7a fix stops it stealing lethal swings or thrashing.
- `slam` push **2** (bulwark class skill) — kept; a bigger shove = more wall-slam / repositioning in DofusSlice.
- Mob kits: Cairn Brute `brute_hurl` (push 2, a Gauntlet-only mob), Grave Ghoul `ghoul_rend` lifesteal 8-12, Marrow Spitter `marrow_rot` (ranged poison). Ghoul & spitter appear in DofusSlice packs; the sims show no regression.
- If a future call reverses this, the clean options remain: (a) gate the AI shove logic behind an engine flag, or (b) split the class/mob content each game needs.

---

## 8. What we did this session (newest → older)

### 2026-07-25 — the audit programme (12 commits)
Run `git log --oneline 5594cd7..HEAD` for the list. In order:
1. **Four shipping bugs** — placement rank-up inert (Fighter stats are an init-only snapshot; `CombatEngine.ReplaceFighter` now swaps the avatar in before `Start()`), non-deterministic arena (`string.GetHashCode()` → FNV-1a), placement cells from the yard map not the arena, and no crits in the campaign (skill rows now carry `crit`/`critFail`).
2. **Unfroze the player's turn** — `BlocksInput` (queue only) instead of `IsBusy` (which counted corpse fade, so a kill cost ~2s of frozen input); AI intent telegraphs no longer replay for your own actions; fast-forward no longer bleeds into your turn.
3. **Class picker** — the avatar was hardcoded to `cannon`, so the archer and bulwark had literally never been playable. Also added the 9 missing `PixelFont` glyphs (`·` `—` `=` `>` `<` `[` `]` `*` `;`) that were rendering as invisible gaps.
4. **AI uses its whole kit** — `TryShootBest` took the priciest castable damage spell and stopped; it now scores every (spell, target) pair by damage **plus payload**, and can never gift an enemy a buff.
5. **Three backlog bugs** — `CanHitFrom` used authored not effective range (a leashed unit repositioned then did nothing); the sim over-charged every fight ~50% (`FightCost` predates `CombatPace`); the Temple's 6th service was unclickable.
6. **A: healing / summons / riding theft** — all three were engine-complete and unreachable (0 of 27 skill rows healed; the loader had no `summon` case and both builders dropped the team, so a player summon joined the ENEMY).
7. **B: UI honesty** — cooldowns visible and unusable spells refused with a reason (the reach overlay could previously paint cells the engine would refuse); `CanCast` reasons surfaced; stones shown in the City; the sheet teaches its stats and states the class passive; an **H** help card.
8. **C: feel** — status pips were unreachable on the board; statuses now flash and name themselves; per-cell footsteps, audible END TURN, a distinct refusal sound, turn-clock warnings. *(Impact-scaling-with-damage deliberately skipped at the owner's request.)*
9. **D: companions** — crew tabs to gear/read/spend for any member, real names, hidden temperament on hire.
10. **E: stakes** — the failure states, the Sexton as an escalating ending, duplicate gear salvages for stones.
11. **Balance** — partial tithe payment avoids a strike; pack reach jitters ±25% per dive so the yard stops printing identical dives; the crypt breather mends 70% and clears wounds. **The sim's crypt runner now models the rest beat** — it didn't, so the audit's "2 rooms in 60" measured a Crypt the player never fights.
12. **Class kits → Crâ / Sacrieur / Iop shapes**, ladders 4 → 7 spells. NOTE: the first attempt collapsed the crypt 20 → 4 of 72 because `ClassSkillsAt` is `Skills.Take(level)` — whatever sits in slots 1-4 IS the low-level kit, and utility there strips a level-3 party of its damage. Reordered; back to 20.
13. **Spell window** — a "STILL TO COME" block listing locked spells and the level that grants them, and the rank-up line is now a BEFORE > AFTER diff of only the fields that change.
14. **Level band to 30** — `XpCurve` extended on its own shape, `Campaign.MaxLevel` added, `GainXp` stops at the ceiling instead of extrapolating forever.
15. **`docs/challenges.md`** — the fight-challenge system specced against this engine (see below).

### ⚠️ Provenance note — read before touching class numbers
The class kits are **modelled on** Crâ / Sacrieur / Iop mechanics from general knowledge. **Every
number in them is ours** — AP costs, ranges, damage bands, cooldowns, crit rates. No Dofus stat
table was ever fetched; a web search returned forum threads and guides only. Do not describe these
values as "1.29 accurate". If real values are wanted, the 1.29 **emulator projects** (Araknemu,
SunDofus, Shivas, Jumbo, GDCore) ship spell tables as SQL — that is the place to get them, and the
rank rows need widening from 3 to 5-6 first so ranks 4-6 have somewhere to live.

### Next up, highest value first
1. **Fight challenges** — fully specced in `DofusSlice/docs/challenges.md`. ~22 rules need NO
   engine change; entry points are `UpdateTithePlacement` (offer), `WireEngine` (a `ChallengeWatch`
   that fails LOUDLY the moment a rule breaks) and `TitheResolution.Resolve` (multiplier). This is
   the cheapest fix for "every crypt run is identical", the biggest replayability problem found.
2. **The progression curve** — the real reason the Crypt stalls at ~2/6 rooms. A level-3 party
   cannot beat grade-3 rooms; healing them harder barely moved it. Needs a difficulty target.
3. Weapon attacks · glyphs/traps · enemy threat overlay · title screen · rank rows to 5-6.
4. **Input/action stacking** — the owner asked for it directly. Today the only queue is
   `BattleAnimator._queue`, which is the *animation* queue; a click during a busy animation is
   dropped, not banked. Wanted: a small intent queue on the piloted turn (move → cast → move)
   drained as the animator frees up, with the pending intents drawn on the board so it never
   feels like input roulette.

### The tube (CRT/VHS pass) — shipped, tuning notes
`DofusSlice.Game/Rendering/CrtPass.cs` brackets the whole of `Draw`. **F8** cycles OFF → SOFT →
FULL; `--crt=off|soft|full` sets the starting level. It is shader-free on purpose: this repo has
no content pipeline (no `.mgcb`, no `.fx`) and runs DesktopGL, so everything is SpriteBatch blits
plus procedurally-built textures.
- The scanline texture is 4x4 (power-of-two, so `PointWrap` tiling is legal everywhere) with a
  **2px pitch that matches `WorldPx`** — one dark row per fat world pixel, so the grille aligns
  instead of moiring. Changing `WorldPx` means changing the grille.
- Bloom is a linear downsample re-blitted additively, with **no bright-pass threshold** — every
  lit pixel blooms in proportion to itself, flat mid-grey fields included. That is a deliberate
  choice, not an oversight: a squaring pass (`dst = dst * src` over the tap) was tried and looked
  more correct — crisper labels, no clipping — and was rejected as too clean for the look wanted.
  The consequence to know about: at `glow` 0.68 the cast-range overlay saturates to near-white,
  and dim HUD labels haze. If either ever needs fixing, dim those two *sources* rather than
  reaching for a threshold here.
- **Generated UI marks** (`Primitives`): panels wear a hairline frame with an ornate bracket at
  each corner (`BracketRect`), and empty cells wear a dithered quatrefoil (`SlotGlyph`). Both are
  built at load time from geometry — no art files — which is the direction the UI is heading:
  the kit that inspired it (Hexany/Batuhan) is local-only and never shipped, so anything that
  must appear in a release has to be generated. Glyph sizes are tuned to the *bloom*, not to
  taste: 16px art pixels dissolve to grey under it and 8px lobes merge into a square, so 12x12
  with a 2x2-block dither is the size that survives. Re-tune if the bloom changes.
- **F6 = the TERMINAL renderer** (`Rendering/AsciiPass.cs`, `--ascii[=mono]`, F5 swaps colour).
  Re-renders the composed frame as a glyph grid. It consumes the same offscreen frame the tube
  would have resolved, so the two are **alternatives, not a stack** — and it needs that frame, so
  enabling it forces the tube on if it was off. The cheap trick: drawing the frame into a
  one-texel-per-cell render target with linear filtering IS the per-cell average, so the CPU only
  does one small GetData. Glyphs are rasterised once into an atlas because at ~20k cells a
  per-pixel glyph draw would be ~700k quads a frame.
  **It is a toggle, not a candidate default:** at a 6x8 cell the HUD's 5x7 text is re-sampled to
  roughly one cell per glyph and becomes unreadable. Making it shippable means the HUD stops being
  drawn-then-converted and starts being *authored* on the character grid — a different UI, not a
  setting.
- **F7** cycles how far the grid reaches: OFF (HUD at native res) / SOFT (HUD chrome on the grid,
  cross-faded with the original at half weight so the 5x7 font keeps a legible 1px core) / HARD
  (no exceptions — and it destroys the font). `--pixels=` sets the start. SOFT is the default.
- **HARD is not shippable without a HUD pass.** Seven glyph rows on a 2px grid is fourteen screen
  pixels; every panel, row and tooltip is laid out for seven. Doing it properly means a 3x5
  micro-font at 2x scale (6x10 screen px, ~40% taller than today) and re-flowing ~230 font call
  sites plus the panel rects. HARD exists so the tradeoff can be looked at, not argued about.
- FULL adds a ±2px chromatic fringe (again `WorldPx` — at 3px the 7px HUD font visibly doubled)
  and a drifting tracking band.
- `EndWorld()` must restore `_crt.FrameTarget`, **not** `null`, or the world pass punches a hole
  out of the composed frame.


Branch `claude/dofus-engine-vertical-slice-7ijlfm`, most recent first:
- **(this session, follow-up) Honour the warm/BONE avatar's ember exemption in `ForecastShift`** — the 7th defect a post-fix verification caught (§7a). New `Fighter.SoftHazardImmune` flag (set by the Gauntlet's `Bless` on the warm/BONE avatar) tells the shove forecast to skip the ember pass for that victim, so an over-counted ember can no longer feed the spike a phantom kill. Gauntlet-only; DofusSlice unaffected. `sim effects` → 26/26; sims unchanged.
- **(this session) Apply §7a AI/shove fixes + archive the Gauntlet** — the six shared-engine defects from the bug-hunt are fixed: `ForecastShift` now walks HP honestly (void/collision/spikes lethal, embers/soft-hazard non-lethal & floored) with a `Kills` flag + optional `preDamage`; `TryShove` only pre-empts when it kills or strictly beats the best plain attack, never chip-shoves a currently-killable enemy and never pulls for a kiter; `SettleTurn` absorbed the dead `StepOffHazard` so bruisers hold the line instead of thrashing; `Charge`'s push-setup detour is capped to one cell. Added a `ForecastShift` regression test (`sim effects` → 26/26). **Archived the Gauntlet**: its CI is now `workflow_dispatch`-only (push trigger removed), `Gauntlet/ARCHIVED.md` added, HANDOVER/CI docs updated. DofusSlice sims came back bit-identical; Gauntlet bulwark 38.8%→46.0%.
- `4373803` **Risk/reward AI + mob variety** — `ForecastShift` predictor; AI shoves enemies into the void/hazards, sets up the drop on approach, wounded flankers retreat; Cairn Brute hurls, Ghoul feeds, Spitter rots; bulwark Slam → push 2 to play the same shove game. (§7a fixed the follow-up bugs this introduced.)
- `1125367` **Doubled the road's stranger things** — 3→6 events, 3→6 mysteries (Gauntlet). Balance-verified.
- `9021bde` **Animated the default silhouettes** — real walk gaits + attack swings for the committed `assets-default/` sprites (they used to only breathe).
- `4cc209d` **Audit pass** — Sexton void-anchor (can be shoved for tempo but not one-cast off the map), save-load hardening (validate class/skill ids), ember-on-push, dedup helpers.
- `eb0888f` spell + gear icons wired through wells/slots. `8eadd3e` committed original silhouette fallbacks (fixed the placeholder letter-balls). `f69cd53` g13 THE HAND (deep UX/feel pass). Earlier: g10–g13 Gauntlet milestones, fullscreen, Crawl-style low-res world, size polish.

---

## 9. Conventions & constraints (must follow)

- **Develop on branch `claude/dofus-engine-vertical-slice-7ijlfm`.** Both games build from it. Don't push elsewhere without explicit permission.
- **Never commit third-party art** (see §5). Only original procedural art (`assets-default/`, `gen_*` scripts) is committable. Pack-baked art (`assets/`, `bake_*`) is delivered locally, never via git.
- **Commit-message trailers** (end every commit with):
  ```
  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01SDSyNYemg1wWfQW7JzGyUq
  ```
- **Never** put the model identifier in commits, PRs, code, or any pushed artifact — chat replies only.
- **No pull requests unless explicitly asked.** Pushing to the branch auto-triggers the CI release for the touched game.
- After pushing, CI republishes the matching `*-latest` release; a new release does **not** disturb an already-downloaded copy.

---

## 10. Verification / QA methods

- **Balance sim (deterministic):** `Gauntlet` → `dotnet run -c Release -- --sim 500 all [seed]` prints a per-class win-rate ledger in ~6s; `DofusSlice.Sim` is the campaign-side headless harness. Use these to catch balance regressions before shipping.
- **Live QA (headless):** `Xvfb :7` + `xdotool` (held keys / clicks) + screenshot, then read the PNG. Run the built `.exe` directly (not `dotnet run`) when testing with pack art hidden to prove the committed defaults render. Kick the game, screenshot a sequence to confirm progression (start → combat → victory/loot) — a static scene usually means autoplay is off, not a stall.
- **CI status:** the GitHub Actions list response is large; parse the saved tool-result file for `head_sha` / `status` / `conclusion`, or query a specific run/job by id.
- **Adversarial review:** spin up parallel review agents (find → verify) on the newest diff before trusting complex logic — that's how §7a was found.

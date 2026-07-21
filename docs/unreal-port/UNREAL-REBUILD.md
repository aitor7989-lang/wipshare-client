# DofusSlice → Unreal Engine 5 — rebuild spec (3D, Blueprint-first)

_A plan to rebuild the TITHE campaign natively in Unreal, keeping the design + balance you
already validated. Chosen approach: **rebuild fresh in Unreal**, **Blueprint-first** (minimal
C++), **stylized low-poly asset packs** (Synty-style)._

> **The core idea:** don't port the C# code — port the *design*. Your existing game is a
> complete, working spec: the rules live in `DofusSlice.Core` (read them as the reference
> implementation), the numbers live in the content tables (already exported to
> `docs/unreal-port/data/` for direct Unreal import). Unreal rebuilds the *presentation and
> the turn loop*; the grid/combat math is transcribed from the C# spec, not invented.

---

## 0. What's the source of truth

| You need… | Read / import from |
|---|---|
| The combat math (damage, LoS, range, AoE, push/pull, hazards, initiative) | `DofusSlice/DofusSlice.Core/Combat/CombatEngine.cs` — transcribe the formulas |
| How a fighter is assembled from data (stats → effective stats, grade scaling) | `DofusSlice/DofusSlice.Core/Content/Tithe/TitheContent.cs` (`MakeMob`, `MakeCrewMember`) |
| AI behaviour per role | `DofusSlice/DofusSlice.Core/AI/Policy.cs` (Bruiser/Flanker/Skirmisher/Artillery/Support) |
| The numbers (classes, mobs, skills, items, sets, essences) | `docs/unreal-port/data/*.json` + `*.csv` (exported; see §6) |
| Systems, deferred features, open design questions | `DofusSlice/docs/` (the bible, AUDIT-129, COMBAT-PARITY, ENGINE-GAP-ANALYSIS) |

**Damage model** (from the class table): `base × (100 + stat + Power) / 100`, where the stat is
the element's characteristic (Str→Earth, Int→Fire, Cha→Water, Agi→Air). **Grade scaling** (deeper
packs): +30% HP, +15% stats, +25% XP/gold per grade step. **Vitality** = +1 max HP each. These are
the transcription targets — verify against `CombatEngine.cs`.

---

## 1. Project setup

- **Unreal 5.4+**, start from the **Blank** or **Top-Down** template (Top-Down gives you a tactical-ish camera + click-to-move to cannibalise).
- **Blueprint-first.** Almost everything below is a Blueprint class. Add C++ only if a hot path (grid flood-fill, LoS) needs it later — not at the start.
- Enable plugins: **Enhanced Input** (already default), **Niagara** (VFX), optionally **Gameplay Abilities (GAS)** — but see §3.4 before committing to GAS.
- Turn-based timing in a real-time engine: model the fight as an explicit **state machine** (a `TurnManager` Blueprint), and play unit actions as **Timelines / latent "PlayAnim → wait → continue"** nodes. Never assume a frame == a turn.

---

## 2. The grid, in 3D

Keep the **logical grid 2D** (integer cell coords) and render it in 3D — exactly like Dofus, XCOM,
Wartales. The grid is the substrate; 3D is the camera + meshes.

- **`GridManager`** (Actor/Subsystem): owns a `Map<IntPoint, CellData>` — walkable / obstacle /
  void / hazard(spikes) per cell, plus occupancy (who stands where). This is the direct analogue of
  the C# `Battlefield` + `TileKind`.
- **Coordinate mapping:** `world = origin + IntPoint(x,y) * CellSize` (e.g. 100uu cells). One helper
  each way (`CellToWorld` / `WorldToCell`).
- **Reach / range / AoE / LoS:** transcribe from `CombatEngine.cs` — they're pure grid math and stay
  identical in 3D. Movement reach = BFS/flood-fill over walkable cells within MP; LoS = the same
  Bresenham/among-cells test (the bible flags a future upgrade to Dofus corner-based LoS — note it,
  don't block on it). **Do not use Unreal NavMesh** for tactical movement; it's for free-roam AI, not
  a cell grid.
- **Rendering the board:** either instanced tile meshes (one Static Mesh per cell from a Synty
  dungeon pack) or a single ground mesh with a **decal/spline grid overlay** + per-cell highlight
  decals (move = blue, range = red, hazard = orange) driven by a Material with an instanced param.

---

## 3. The combat systems (Blueprint classes)

### 3.1 `GridUnit` (Actor) — a fighter on the board
- Components: Skeletal Mesh (Synty character) + `AnimBlueprint`, a selection/HP widget (WidgetComponent), a highlight decal.
- Runtime state mirrors the C# `Fighter`: current/max HP, AP, MP, position (cell), team, facing, statuses, the unit's spell list, `AiPolicy`.
- Built from data at spawn (see §6): a class row or a mob row → base stats → effective stats.
- Anim states needed: **Idle, Walk, Attack (melee), Cast (ranged), Hurt, Die** (+ optional Push-recoil). Synty packs ship most of these; retarget to one skeleton.

### 3.2 `TurnManager` (Actor/Subsystem) — the watched turn loop
- Builds the initiative order (transcribe from the C# initiative rule), then runs turns one unit at a
  time. For AI units it asks the `AIController`/policy for actions; for the campaign this is a
  **watched** fight (auto-run), matching the current game — so even the "player" crew is policy-driven,
  with the human controlling placement, engagement and **playback speed (1×/2×/4×)**.
- Drives an **event → animation** pipeline (this is what the C# `CombatEvent` stream already does):
  each resolved action (`Moved`, `AttackHit`, `Pushed`, `Died`, …) is a step the presentation plays to
  completion before the next. Model it as a queue the `TurnManager` drains via latent nodes.

### 3.3 Ability / spell resolution (data-driven)
- An **`AbilityComponent`** on each unit reads its skills from the `skills` DataTable (§6). A skill is
  `{ ap, minRange, maxRange, los, cooldown, effects[] }`; `effects[]` is a list of
  `{ kind, element, min, max, cells, status, mag, turns }` — the exact shape in `skills.json`.
- Resolve an effect by `kind`: **Damage / Lifesteal** (roll min–max, apply the damage formula),
  **Push / Pull** (`cells`, along the caster→target axis; port `ApplyShift` — includes void death,
  wall-slam collision `(cells-moved)*5`, hazard crossings), **Heal**, **ApplyStatus**
  (poison/shield/rooted/stabilized/mp-drain — `mag`,`turns`), **GrantAp**, **Teleport**, **Swap**.
  All of these are specified in `CombatEngine.cs` — transcribe, don't reinvent.
- **Cooldowns / casts-per-turn** come straight from the skill fields.

### 3.4 AI — the policies
- Five roles already designed in `Policy.cs`: **Bruiser** (close + hold), **Flanker** (dive the
  softest), **Skirmisher/Artillery** (kite, shoot softest, keep range band), **Support** (feed AP,
  stay back). Two ways to rebuild them Blueprint-first:
  - **(recommended) A utility scorer** in an `AIController` Blueprint that mirrors `Policy.cs`: score
    candidate actions (attack that secures a kill > chip the softest; move to an attack/firing cell;
    step off hazards; retreat when wounded). It's a direct transcription and stays legible.
  - **Behavior Trees** — one BT per role. More Unreal-idiomatic but tactical grid decisions fight the
    BT model; most teams end up with a scorer anyway. Start with the scorer.
- **Do transcribe the risk/reward rules** you just validated: don't shove a killable enemy out of
  reach; only push into hazard/void when it kills or beats a plain hit; wounded flankers retreat.

### 3.5 Progression & meta (later phases)
- XP curve + auto-spent characteristic points, essences (teach a skill into 2 permanent slots), sets
  (tier bonuses by piece count), the doll inventory, hiring, the Bell/tithe economy — all specified in
  the bible + the exported `sets.json`/`items.json`/`essences.json` + `PricesJson`.

---

## 4. Presentation

- **Camera:** a `SpringArm + Camera` pawn with tactical orbit/pan/zoom (Top-Down template's is a fine
  start). Snap-to-unit on turn start; free pan otherwise.
- **VFX (Niagara):** hit sparks, element-tinted projectiles (fire/air/water/earth), push dust, ember
  graves, death dissolve. One System per effect, tinted by element param.
- **UI (UMG):** rebuild the panels from the current game — the HP/AP/MP band, the spell wells, the
  **doll + slots** inventory (Weapon/Hat/Cape/Amulet/2×Ring/Belt/Boots), the CARACTÉRISTIQUES sheet,
  the SORTS book. Drive them from the same DataTables + unit state.
- **Audio:** the current game uses fully *synthesized* SFX/music — in Unreal, use MetaSounds (or drop
  in a CC0 pack). Not on the critical path.

---

## 5. Phased build plan

- **P1 — Grid + camera + one moving unit.** `GridManager`, cell↔world, click a cell → a Synty
  character walks there (highlight the reachable cells). No combat. _Proves the 3D loop._
- **P2 — One fight, end to end.** `TurnManager` + initiative + PA/PM; one melee + one ranged skill
  resolved from data; the event→animation queue; win/lose. Two units, hand-placed. _Proves combat._
- **P3 — Content + AI.** Import all DataTables (§6); spawn real classes/mobs; the utility-scorer AI;
  push/pull + hazards + void; Niagara VFX; the HUD band + spell wells. _A real watched pack fight._
- **P4 — Meta loop.** Inventory doll + equip, essences, sets, XP/level, the City ↔ Graveyard ↔ Crypt
  flow, a campaign save. _The game._
- **P5 — Feel + balance.** Stepped anim, hit-stop, camera shake; then a fresh balance pass in 3D
  (your `DofusSlice.Sim` methodology can be reborn as an Unreal commandlet or kept in C# as an
  external oracle if you want numeric parity).

Art runs in parallel from P1 and usually sets the timeline (§7).

---

## 6. The data (already exported → `docs/unreal-port/data/`)

`tools/export_unreal_datatables.py` transcribes the TITHE tables to Unreal-ready files:

| File | Rows | Use |
|---|---|---|
| `classes.json` / `.csv` | 3 | class base stats, element, policy, starting skills, growth |
| `mobs.json` / `.csv` | 10 | mob stats, policy, skills, essence drop, XP/stones/gear |
| `skills.json` | 25 | spells — `ap/range/los/cooldown` + nested `effects[]` + `ranks[]` |
| `items.json` / `.csv` | 14 | gear: slot, set, stat lines |
| `sets.json` | 2 | panoply tier bonuses by piece count |
| `essences.json` / `.csv` | 9 | essence → taught skill |

- The **JSON** files are already in Unreal **DataTable JSON-import** shape (`[{"Name": <id>, …}]`).
  Define a UStruct (a `USTRUCT` in a tiny C++ header, or a Blueprint Structure) whose members match
  the field names, then _Import_ the JSON onto a DataTable of that row type. Nested `effects`/`ranks`/
  `tiers` map to `TArray<FStruct>` members — Unreal parses them.
- The **CSV** files (flat tables) are for spreadsheet editing / simple DataTable import; the nested
  columns are intentionally omitted (they live in the JSON) — the exporter prints which.
- Re-run the exporter any time the C# tables change to keep numbers in sync (until Unreal becomes the
  source of truth, then retire it).

**Suggested row structs** (names match the data): `FClassRow`, `FMobRow`, `FSkillRow`
(`+ FSkillEffect`, `FSkillRank`), `FItemRow`, `FSetRow` (`+ FSetTier`), `FEssenceRow`.

---

## 7. Art — Synty low-poly (the biggest lift)

- **Packs that fit** a dark Dofus-ish crawl: _POLYGON — Dungeon Realms / Dungeons / Fantasy Kingdom /
  Knights / Nature_. One character skeleton across them lets you **retarget** a single animation set.
- **Cast list to source** (9 characters + the boss): the 3 classes (archer / bulwark / cannon) and
  the mobs (husk, hound, spitter, mite, piper, wraith, ghoul, warden, brute, **Sexton**). Map each to
  the nearest Synty skeleton/skin; tint per element/family for reads.
- **Animations required per character:** Idle, Walk, Attack, Cast, Hurt, Die (Synty anim packs +
  Mixamo cover these; retarget to the shared skeleton).
- **Tiles/props:** a dungeon/graveyard tile kit for the board + tombstones, an ember/void material.
- **The old 1-bit sprites don't carry over** — but the silhouettes are a useful shape reference for
  proportions and reads. Keep the same "small mite, man-sized husk, towering Sexton" size law.
- Art licensing note: **Synty (and Mixamo) are fine to USE in the game build, but — like the current
  repo rule — don't commit third-party pack source into git.** Keep purchased/licensed assets in the
  Unreal project's `Content/` (which lives in the game repo per Unreal norms) but out of any shared
  data export; check each pack's license before redistribution.

---

## 8. Gotchas

- **Turn-based in real-time:** resolve the fight in a state machine + latent action queue, not in Tick.
- **Grid ≠ NavMesh:** custom cell pathfinding (BFS), not Unreal navigation.
- **GAS is optional and heavy:** the data-driven `AbilityComponent` above is enough and stays
  Blueprint-legible; adopt GAS only if you later want its buff/cooldown/replication machinery.
- **Determinism matters less now** (no shared C#/Unreal sim), so you can use Unreal's RNG — but if you
  want the C# `DofusSlice.Sim` to keep validating balance, seed both the same way.
- **Keep the logical layer separate from actors** so a headless balance sim stays possible in Unreal
  (a commandlet running `GridManager`+`TurnManager` with no rendering).

---

## 9. First concrete step

Spin up the UE5 project (Top-Down template), make a `GridManager` that maps a 15×13 cell grid to the
world and highlights reachable cells, and click-to-walk one Synty character across it (**P1**). In
parallel, import `classes.json` / `skills.json` onto DataTables to confirm the struct shapes. From
there, P2 (one fight from the event queue) is the moment it starts feeling like the game again.

# Dofus Slice — Incarnam Combat (from-scratch engine)

A tiny, self-contained tactical-combat engine written from scratch in **C# + MonoGame**,
recreating the feel of Dofus 1.29's turn-based grid fights. This is a fun learning side
project — **not** related to the main product in this repo — living entirely under
`DofusSlice/` so it never touches the WipShare client.

The current vertical slice is a **combat sandbox**: drop straight into one Incarnam map as an
**Iop** and fight a group of mobs (a Gobball and two Boars) with the full turn loop —
movement points, action points, spells with range / line-of-sight / area-of-effect, mob AI,
and win/lose.

![Incarnam combat sandbox](docs/incarnam-combat.png)

> **On assets & IP.** This project reimplements *mechanics* only. All art here is drawn
> procedurally (colored iso diamonds, disc tokens, an embedded bitmap font) — there are **no
> Ankama assets** in the repo. The data layer is deliberately shaped like a datamined spell /
> monster row so you can plug your own art or datamined data in later, but shipping Ankama's
> copyrighted art/sound publicly would be a copyright issue — keep any such assets local.

## Using your own art (sprite pipeline)

The renderer looks for optional PNG sprites in an `assets/` folder next to the executable
and uses them automatically, falling back to procedural placeholders for anything missing.
See `DofusSlice.Game/assets/README.md` for the recognised filenames (`iop`, `boar`,
`gobball`, `piou`, `tile_grass`, `tile_rock`). Fighter sprites are drawn anchored at the
feet so tall art stands out of its tile and depth-sorts correctly.

That folder is **gitignored** — art you drop in stays strictly local and is never committed
or distributed, so you can point it at your own (or locally-kept datamined) art without any
third-party assets ending up in the repo. Cell colours follow the usual tactical convention:
**green = movement (PM)**, **red = spell/attack range (PA)**, **orange = area of effect**.

## Running it

Requires the .NET 8 SDK.

```bash
cd DofusSlice
dotnet run --project DofusSlice.Game     # the playable window (MonoGame DesktopGL)
dotnet run --project DofusSlice.Sim      # headless auto-played fight, prints the turn log
dotnet run --project DofusSlice.Sim 42   # ...with a specific RNG seed
```

MonoGame DesktopGL is cross-platform (Windows / macOS / Linux via OpenGL). No content
pipeline and no asset files are needed — everything is generated at load time.

## Controls

| Input | Action |
|-------|--------|
| **Left-click** a highlighted tile | Move the Iop (costs MP) |
| **1 – 4** | Select a spell (Pressure / Iop's Wrath / Jump / Intimidation) |
| **Left-click** a target tile | Cast the selected spell |
| **Right-click** or **Esc** | Deselect the spell |
| **Space** | End your turn |
| **R** | Restart the fight (new RNG seed) |

## The Iop kit

| Spell | AP | Range | Effect |
|-------|----|-------|--------|
| **Pressure** | 3 | 1–6 | Ranged neutral damage — the reliable poke |
| **Iop's Wrath** | 5 | 1 (melee) | Big earth burst, 2-turn cooldown |
| **Jump** | 3 | 1–6 | Teleport to a free cell in sight (gap-closer) |
| **Intimidation** | 2 | 1 (melee) | Small damage + knock the target back 2 cells (collision damage on impact) |

## Architecture

Three projects, split so the rules never depend on the renderer:

- **`DofusSlice.Core`** — pure C#, zero MonoGame dependency. All the rules live here:
  - `Grid/` — the iso lattice (`CellCoord`, `Battlefield`), A* pathfinding + a geodesic
    distance field, Bresenham line-of-sight.
  - `Combat/` — `Fighter` (stats, AP/MP, cooldowns), `CombatEngine`, the single
    authoritative turn-based state machine that validates and applies every move and cast,
    and `CombatEvent`s — a structured play-by-play the engine raises for the renderer.
  - `Spells/` — data-driven spell defs (`SpellDef`, `SpellEffect`, `AreaShape`).
  - `AI/` — `MobBrain`, a greedy "close the distance and hit the nearest hero" mob AI.
  - `Content/` — hand-authored Iop spells, the bestiary, and the Incarnam encounter.
- **`DofusSlice.Game`** — MonoGame presentation + input only. Iso projection, procedural
  textures, an embedded ASCII-art bitmap font, the HUD, and `Animation/BattleAnimator`,
  which replays the engine's event stream as timed animations. Drives the engine through
  its public API; contains no rules.
- **`DofusSlice.Sim`** — a headless harness that auto-plays a whole fight and prints the
  combat log. Because `Core` has no rendering dependency, the entire ruleset is testable
  without a display — handy for CI and for tuning balance.

## What's modelled (Dofus-faithful bits)

- Isometric cell grid with obstacles that block both movement and line of sight.
- Orthogonal movement, one MP per cell, pathfinding that routes around walls.
- Initiative-ordered turns; AP/MP refresh at the start of each turn.
- Per-turn 30-second countdown shown as a draining gauge; the turn auto-ends when it
  expires, and you can end early with the END TURN button or Space.
- Turn-order timeline across the top: every fighter in initiative order, the active one
  highlighted, dead fighters greyed out.
- Spells with min/max range, line-of-sight, line-only casting, per-turn cast caps and
  cooldowns; area shapes (single / circle / cross).
- Elemental damage scaled by the caster's characteristic and reduced by target resistance.
- Push / knockback with collision damage; self-teleport (Jump).
- Animated combat: tokens slide along their path, casters lunge with an elemental impact
  flash, floating damage/heal numbers, hit flashes and death fades — driven by a structured
  event stream from the engine, so the rules layer stays instant and deterministic.

## Roadmap ideas

- Swap procedural tokens for real sprites (your own art, or datamined `.d2o`/`.swl` decoded
  into the existing `SpellDef` / `Bestiary` shapes).
- More classes (Cra, Ecaflip…), more spells, spell leveling.
- Overworld exploration on a real Incarnam map with encounter triggers, then hand off to
  this combat slice.
- Summons, buffs/debuffs over time, and a proper end-of-turn effect queue (the engine
  already has a hook for it).

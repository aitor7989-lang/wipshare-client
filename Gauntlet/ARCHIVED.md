# THE GAUNTLET OF THE BELL — archived

_Status: **archived** as of 2026-07-21. The go-forward game is **DofusSlice** (the TITHE campaign,
`../DofusSlice/`). The Gauntlet is kept in the repo as a complete, buildable reference — it is not
deleted — but it is no longer actively developed or auto-released._

## Why

Both games share one engine (`DofusSlice.Core`). Given the choice between the two, the owner finds
the older DofusSlice **campaign** more fun than this **roguelite**, so development focus consolidates
on DofusSlice. Rather than let the Gauntlet rot silently, it is formally frozen here.

## What "archived" means concretely

- **The code stays.** `Gauntlet/` still compiles against the current shared engine and still runs.
  Nothing here was deleted.
- **CI no longer auto-builds it.** `.github/workflows/gauntlet-build.yml` was switched to
  `workflow_dispatch` (manual) only — the push trigger (including the one on
  `DofusSlice/DofusSlice.Core/**`) was removed. Pushing engine changes no longer rebuilds or
  republishes the Gauntlet.
- **The last release stays live.** The previously published
  [`gauntlet-latest`](https://github.com/aitor7989-lang/wipshare-client/releases/download/gauntlet-latest/Gauntlet-windows.zip)
  download is left in place as a frozen snapshot. It is not updated going forward.
- **The engine is still shared.** Because the Gauntlet references `DofusSlice.Core`, any future
  engine change can still affect it. Whoever changes the engine for DofusSlice is not obliged to
  re-tune the Gauntlet, but should keep it **compiling** (a quick `dotnet build Gauntlet/Gauntlet.csproj`).

## Building / running it anyway

```bash
cd Gauntlet
dotnet build Gauntlet.csproj -c Release          # references ..\DofusSlice\DofusSlice.Core
dotnet run   -c Release                           # → Gauntlet.exe
dotnet run   -c Release -- --sim 500 all          # headless balance ledger (cannon/archer/bulwark)
```

Or trigger the archived CI workflow by hand from the repo's **Actions** tab (Run workflow) to get a
fresh self-contained Windows zip.

## What it was

A tighter roguelite built on the DofusSlice engine: one dealt road of ~11 rooms, a bell/toll clock,
run-and-done. Three classes (cannon / archer / bulwark), ragged void-islands where any coastline is a
weapon (`LethalVoid`), ember graves as the board hazard, traders and mysteries along the road. All
original procedural art (`assets-default/`), pack art local-only (`assets/`). See
`../DofusSlice/docs/GAUNTLET-DESIGN.md` for the design notes.

_Last engine sync before archival left the balance ledger at roughly cannon ~42% / archer ~29% /
bulwark ~46% win-rate over 500 runs/class — the bulwark benefiting from the shared-engine AI shove
fixes that landed alongside this archival._

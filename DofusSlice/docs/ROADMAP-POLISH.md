# TITHE — The Polish Roadmap (owner feedback batch, v8.7 → v9.x)

Every item from the owner's feedback batch, segmented into four shippable passes.
Each pass builds, gets QA'd, committed, and delivered as its own playable build.
Tick boxes as they land; log each pass in OVERNIGHT-LOG.md.

## PASS 1 — "READ THE FIGHT" (color language + combat readability)

The vitals color code, applied EVERYWHERE (bars, numbers, floats, icons, rows):
- [x] 1.1 HP = RED, AP = BLUE, MP = GREEN — one rule across combat band, character
      sheet, team column, floats, hover cards. (AP/MP floats already match; extend.)
- [x] 1.2 Spell glyphs tinted by their ELEMENT (fire/earth/air/water each get a
      functional color) — combat wells, spell book, spell cards.
- [x] 1.3 Ally circles BLUE, enemies stay red — under the units in the world AND
      on the turn order.
- [x] 1.4 Turn order wears the fighters' HEADS (sprite portraits), with STATUS
      icons (poison, shield, buffs…) underneath each card — allies and enemies.
- [x] 1.5 The fight log moves to the BOTTOM-LEFT, Dofus-style (out of the way of
      the turn order).
- [x] 1.6 PATH PREVIEW: hovering a walkable cell while piloting shows the route
      your avatar will take before you click.

## PASS 2 — "ONE BAR, EVERYWHERE" (HUD unification + declutter)

- [x] 2.1 The SAME bottom bar outside combat: heart/AP/MP vitals stay, the spell
      wells become QUICK-ACCESS ITEM slots (bread, draughts…). Using one updates
      the HP in the bar live.
- [x] 2.2 Healing shows in the WORLD too: the same floating letters as damage,
      but "+N" in green, over the healed character.
- [x] 2.3 PLACEMENT uses that same bar — the END TURN button IS the FIGHT button
      while getting ready. One button, one place, all phases. Keep it simple.
- [x] 2.4 Kill the redundant hint text ("C: CHARACTER · S: SPELLS · I: BAG"…) —
      a Dofus-style ICON menu in the BOTTOM-RIGHT opens each window.
- [x] 2.5 The bottom-left pile (coin, bread chips, tithe due…) moves INTO the
      inventory window. The HUD breathes.

## PASS 3 — "FAIR TURNS, HONEST AI" (systems + fixes)

- [x] 3.1 INITIATIVE, Dofus-style: order from the fighters' stats, ALTERNATING
      sides — one ally, one enemy, one ally… (or enemy-first if their best
      initiative wins).
- [x] 3.2 CANNON AI FIX: Blood Pact (pay HP for AP) only when the extra AP
      actually enables a cast this turn — never for nothing, and never when the
      target already dies to what's armed. No more self-harm for zero value.
- [x] 3.3 Spell progression rework: START WITH ONE spell, gain a NEW one every
      level (kits become a ladder, not a bundle).
- [x] 3.4 LEVEL UP = FULL HEAL. Your life refills the moment you ding.
- [x] 3.4b LEVEL UP deserves a MOMENT, not a line in the loot UI: a Dofus-style
      celebration — its own popup/flash ("LEVEL 4!", the new spell unveiled, the
      full-heal shown), sound, a beat of wow before play resumes.
- [x] 3.5 REMOVE the AUTO-SPEND ALL button — points are spent by hand, always.
- [x] 3.6 MOB RESPAWN: cleared packs return to the yard after a timer.

## PASS 4 — "TOUCH THE LOOT" (inventory feel + drops)

- [x] 4.1 DRAG & DROP in the inventory: pick a piece up from the stash and drop
      it on the doll (and back). Click-to-equip stays as the fallback.
- [x] 4.2 ESSENCE DROPS IN THE WORLD: when an essence falls, it appears on the
      field — shiny, animated, unmistakably not gear — before it goes to the bag.

## Ordering rationale

Pass 1 is pure paint on existing systems (fast, zero risk, transforms readability).
Pass 2 moves UI real estate around while the color language is fresh. Pass 3 touches
engine/AI/progression (needs the most testing — sim + live QA). Pass 4 is input
plumbing (drag state) and a new world-entity, the most isolated work, last.

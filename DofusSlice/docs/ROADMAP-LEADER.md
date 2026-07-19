# TITHE — The Leader Update: detailed plan

*You are the leader. You level, you gear, you carry the bag. The crew are people who
walk with you — they fight their own way, take their cut, and manage themselves. This
plan covers the main features in detail, then the small UX fixes as a separate pass.*

---

# PART A — MAIN FEATURES (detailed)

## A1. Only YOU progress by hand — the crew are independent

**Design.** Stat points, spell points, gear: avatar only. Mercenaries level up on their
own (auto-spend stays ON for them, silently), keep their hire kit, and never appear as
editable. They are companions, not property.

**Build.**
- Kit/character screens show ONLY the avatar as editable. Companions appear as read-only
  cards (name, level, kit, nature if vetted) — a roster, not a workbench.
- Remove merc tabs from the spend flow; `AutoSpendStats` + `AutoSpendSpellPoints` run for
  mercs automatically on level-up (already exists — just stop exposing manual spend).
- Files: `SliceGame.DrawEquipPanel` (tab logic), `Campaign`/`TitheContent` (auto-spend on
  merc GainXp level-ups).

## A2. Loot is SPLIT with the party

**Design.** The crew works for a share, like real company. On a win:
- **Gold**: split evenly across living party members — their shares just leave (it is
  their pay; flavor line in the report: "BULWARK-MERC TAKES 12g"). You bank only YOUR share.
- **Items/essences**: yours (you are the one with the bag), but each merc's share line
  makes the split visible so the party feels alive.
- Wounded mercs still take their cut; dead ones don't (their share is lost with them —
  a reason to keep them standing).

**Build.**
- `DiveSession.ApplyResult`: `int share = gold / party.Count; _campaign.Gold += share;`
  report lists per-member shares. FightReport gains `Shares: (name, amount)[]`.
- Fight report window: one line per member "NAME +12g" under the gold headline.

## A3. The Character panel (1.29 CARACTÉRISTIQUES, avatar only)

**Design.** A proper Dofus-style characteristics window, opened with `C` (and from a
small button in the campaign HUD):
- Header: name, class, level, XP gauge with numbers.
- Vitals block: HP, AP, MP as icon rows (heart/star/shield — same icons as the band).
- The six characteristics as rows: VIT / STR / INT / CHA / AGI / WIS, each with its
  current EFFECTIVE value, the base→gear breakdown on hover, and a [+] spender when
  points are banked. Breakpoint hints under each ("AGI: dodge/initiative — every point
  = +1 init").
- Footer: POINTS TO SPEND pill + AUTO-SPEND ALL.

**Build.**
- New `DrawCharacterPanel()` (window 560×520 centred) replacing the stats half of the
  current kit screen; kit screen keeps gear + crew roster.
- Input: `C` toggles; clicks reuse `StatPlusRect`-style rects; ESC closes.
- Data: all getters exist (`TitheContent.StatsOf`, `SpendStat`, `XpForNextLevel`).

## A4. The Spell panel (1.29 SORTS, avatar only)

**Design.** Opened with `S`: your full spell book.
- One row per spell: icon well, name, RANK n/m as pips, the current effect line, and the
  NEXT-rank line permanently visible in dim under it (not only on hover).
- RANK UP button per row when spell points are banked; before→after flashes on click.
- Spells learned from essences appear here too, marked with their source mob.

**Build.**
- New `DrawSpellPanel()`; rank-up plumbing already exists (`RankUp`, `SkillAtRank`,
  `UnitSkillKeys`). The panel is mostly the existing kit spell-rows, promoted to a real
  window with room for 6+ rows (essence spells).

## A5. Inventory + consumables (the leader's bag)

**Design.** Opened with `I`: a Dofus-style bag.
- **Consumables**: Hard Bread (out-of-combat mend — click to eat NOW instead of the
  automatic pre-fight mend), Healing Draught (NEW: usable IN combat on your turn for 2 AP,
  heals 30% — finally a comeback tool), Essences (click → choose who learns it).
- **Equipment**: the stash, as a grid with the doll beside it (equip/unequip by click,
  avatar only — merged from the current kit screen).
- **Quantities** stack ("HARD BREAD ×3"), tooltips on hover with full item cards.

**Build.**
- `Campaign` already tracks Bread/Draughts/Essences/Stash — this is a UI unification plus
  two new verbs: `EatBread()` (out-of-combat heal-most-hurt) and `UseDraught(unit)`
  (in-combat: engine effect Heal 30%, costs 2 AP on your turn — add a synthetic
  "drink draught" action next to the spell bar when a draught is carried).
- New `DrawInventoryPanel()`; combat band gains a small flask well (right of spell wells,
  key `Q`) when draughts > 0.

## A6. Item drops + the ADVENTURER SET, properly

**Design.** Make loot a reason to fight specific packs:
- **Drop tables per mob family** (data-driven in TitheTables): husks drop belt/boots-ish
  pieces, wardens weapons, the Sexton a guaranteed unowned piece. Rarity: common
  consumables (bread/draughts as drops too, not just shop) / uncommon set pieces /
  rare essences (existing chance).
- **The Adventurer set (aventurero)**, all 7 pieces (`adv_blade, adv_hat, adv_cape,
  adv_amulet, adv_ring, adv_belt, adv_boots` — ids already exist): each piece has a
  stat line, and SET BONUSES at 3/5/7 pieces shown in the inventory ("3: +25 HP ·
  5: +1 MP · 7: +1 AP"). The 7/7 bonus is the run's mid-term dream.
- Fight report lists drops with the receiving line ("YOU find: ADVENTURER'S BELT").

**Build.**
- Extend TitheTables with `dropTable` per mob id (piece pools + weights + consumable
  chances); `DiveSession.ApplyResult` rolls per killed mob instead of the current single
  gear roll.
- `TitheContent.SetPiecesEquipped` + set bonus already partially exist — add the 3/5/7
  tiers to `StatsOf` and print them in the inventory panel.

## A7. Spell depth — the "sacro" example and one new spell per class

**Design.** Spells whose CONDITIONS create positioning play:
- **Blood Price** (the sacro example): melee range 1 ONLY, straight face-to-face —
  damage, and you heal for 50% of the damage dealt. (Engine already has `Lifesteal`
  effect — this is authoring + the melee-only constraint, which `min:1,max:1` gives.)
  Give it to the BULWARK (fits the frontline fantasy) or gate it behind an essence.
- One conditional spell per class in the same spirit later (archer: bonus at max range —
  the Long Shot passive already leans there; cannon: bonus on unmoved turn).

**Build.** TitheTables entry:
`{ "key": "blood_price", ap: 4, min: 1, max: 1, los: true,
   effects: [ lifesteal earth 14-20 @50% ] }` — the Lifesteal kind exists in the engine
and already floats heals. Add to bulwark's kit at level 4 (first "new spell" milestone).

## Build order (main features)

1. **A1 + A2** (leader-only progression, loot split) — small code, big fantasy shift.
2. **A5 inventory** (the bag + draughts-in-combat: the comeback tool).
3. **A3 + A4** (character + spell panels — they replace the crowded kit screen).
4. **A6 drops + set** (needs the inventory to show it off).
5. **A7 spell content** (blood price first).

Each step ships as its own build (v8.1 → v8.5) so every change gets playtested alone.

---

# PART B — SMALL FIXES (the UX pass, one build)

1. **Range colors**: move range = muted GREEN (not white) `(120,190,110) @~55α`; spell
   castable cells = muted BLUE `(96,150,220) @~120α`; AoE stays red; reach stays dim
   grey. The 1-bit rule bends for FUNCTIONAL color: board information may use
   green/blue/red; art and chrome stay ink-on-black. (Palette.MoveRange/CastRange edits.)
2. **Resource floats**: every action floats its cost/effect over the actor —
   `-4 AP` (blue), `-2 MP` (green), damage `-20` (red on the victim), heals `+20`
   (green). Today only damage/heal floats exist; add AP/MP floats on SpellCast/Moved
   events in `BattleAnimator` (small FloatingText with a short rise).
3. **Countdown urgency**: END TURN clock turns red under 10s.
4. **Armed-spell cursor**: hovering an invalid target while armed shows a dim ✕ on the
   cell instead of nothing.
5. **Turn banner**: "YOUR TURN" flash-banner over the avatar when your turn starts
   (the enemy names already get one).

*Est. scope: Part B is one focused session; Part A items are one session each, roughly.*

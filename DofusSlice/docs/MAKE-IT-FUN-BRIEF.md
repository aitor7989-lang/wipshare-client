# TITHE — Make It Fun brief

*Prototype honesty pass. The v7.x build looks right and runs everywhere; it just isn't fun
enough yet. This brief says why, what to keep, and what to build next — anchored on the
pivot: YOU pilot your avatar, Dofus-style; the crew are companions who play themselves.*

---

## 1. Where the fun is leaking (diagnosis)

**You don't play — you sponsor.** A fight currently asks the player for exactly two
decisions: which pack to tap, and where to drop the tokens. Then RNG resolves. Dofus 1.29
is fun because *every turn is a small puzzle*: spend 6 AP and 3 MP against a board state —
kite the melee, break line-of-sight, save AP for the finisher. We removed the puzzle and
kept the arithmetic.

**One spell = no decisions even when watching.** With a single ability per unit there is
nothing to anticipate ("will the cannon burn or root?"), nothing to sequence, no combo to
hope for. The watched fight is a coin being flipped slowly.

**No comeback tools.** Nothing in a fight lets a player (or the AI) turn a bad start
around: no heal, no reposition trick, no defensive stance. Fights feel decided at
placement, and they mostly are.

**Progression is real but invisible.** +1 AGI moves a hidden formula. Rank-ups change
economics (AP cost, range) which is the RIGHT kind of upgrade — but the game never shows
the before/after, so it feels like nothing happened.

**The yard is dead time.** Walking between packs is waiting. The bell is pressure without
choices — you can't spend time on anything except more fights, so the timer just trims
content.

**Classes play the same.** Archer, bulwark, cannon are all "move toward range N, cast the
one spell". Silhouettes differ; hands-on-keyboard feel doesn't.

## 2. What is working (protect these)

- **The fantasy and tone.** The 1-bit graveyard, the bell, paying tithe to keep diving —
  distinct and coherent. Nobody else's prototype looks or feels like this.
- **The macro loop.** City → lychgate → three yards → the crypt → the Sexton, with wounds,
  gold, bread and the clock. The RISK layer (go deeper vs. get out) is genuinely good.
- **Readability.** The 1-bit board reads instantly: ink crew, red threat, one accent.
  Hover cards, unit plates, the log — the information layer is done.
- **Death drama.** Sequenced deaths, corpses, projectiles — the replay layer will pay off
  even more once one of the units is *you*.
- **The tech secret: piloted mode already exists.** The pre-TITHE standalone engine has
  full Dofus controls working TODAY — MP range shading, castable-cell shading, AoE
  preview, hover damage estimates, click-to-move, spell bar, end-turn button. The pivot is
  wiring it to the avatar's turn inside campaign fights, not building a combat UI.

## 3. The pivot (agreed direction)

> You control YOUR character, Dofus-style — turn, movement, abilities, level-ups. The
> mercenaries and friends who follow you are NOT puppets: they take their own turns,
> with their own kits, alongside you.

Concretely:

- **Your turn**: turn timer, MP pathing, spell bar (1–5 + click), damage preview on hover,
  end turn. Exactly the 1.29 grammar. Everything else keeps the watched cadence.
- **Their turns**: existing AI, unchanged — but visible personality via kit (the bulwark
  friend bodyblocks; the archer merc kites). Out of combat you may set a light **stance**
  per companion (Hold near me / Hunt / Protect me) — one dropdown, not an order queue.
- **Watching stays a feature**: SPACE to fast-forward your own turn keeps the "autobattler
  zen mode" for grinding, and 1/2/3 speed still rules enemy turns.

## 4. What needs work to be fun (prioritized)

### P0 — feel it in the very next build ("I made a play")
1. **Pilot the avatar in campaign fights.** Reuse the standalone piloted path for the
   avatar's turn only; AI plays everyone else. End-turn button + 30s timer.
2. **A real kit at level 1: three spells, not one.** Per class: a damage spell, a
   mobility/utility spell, a signature.
   - *Cannon*: Ruin Bolt (nuke) · Flashfire (blink 2 cells, costs MP gain) · Ember Field
     (small AoE burn, CD 3).
   - *Archer*: Piercing Shot (line) · Disengage (leap back 2 + slow) · Crippling Arrow
     (-2 MP, CD 2).
   - *Bulwark*: Slam (melee push) · Bastion (self-shield + taunt, CD 3) · Seize (pull 2).
   - Cooldowns + AP costs tuned so a 6-AP turn is always a choice of two lines, never
     "cast the spell, end turn".
3. **New spell every 2 levels, rank-up on click** (already built) — but show a
   BEFORE → AFTER card when ranking ("RANGE 3-6 → 3-8").

### P1 — make enemy turns matter (threats you answer)
4. **Three signature threats** (the rest stay simple): *marrow spitter* poison pools you
   must step out of; *bone piper* summons a mite every 2 turns until killed (priority
   target!); *crypt warden* shields the pack until slammed/pulled out of formation.
5. **Collision pushes** (1.29 classic): pushing into a wall/obstacle/unit deals bonus
   damage. Instantly makes positioning and the bulwark interesting.
6. **Line of sight play**: tombstones already block LoS — show the blocked-cell shading
   during aiming so hiding behind graves becomes a tactic.

### P2 — progression you can feel
7. **Breakpoints, not trickles**: stats grant visible thresholds (every 20 AGI = +1 MP
   once per fight dash; every 25 INT = burn +1 tick). Say it on the kit screen.
8. **Gear with a proc line**, not just +stats ("Gravewalkers: your first push each fight
   is free"). The adventurer set bonus already exists — surface it.
9. **Essences = build identity**: slotting an essence teaches that enemy's move (already
   designed — make the taught spell actually enter the spell bar).

### P3 — the world between fights
10. **Kill dead time**: double overworld walk speed, click-queue path, ESC skips the walk.
11. **Bell events instead of a silent fuse**: at 50% a hunting pack spawns at the gate;
    at 25% all packs shuffle one yard deeper. The timer becomes drama, not erosion.
12. **One shrine per yard** (pick one of two blessings: +2 AP this dive / mend 30% /
    reroll a pack's loot) — a decision between fights.

## 5. How we'll know it worked (the fun test)

A 20-minute session where the player can honestly answer YES to all three:
1. *"Did a fight turn on a decision I made mid-fight?"* (not placement, not stats)
2. *"Am I working toward a build?"* (can name the next spell/rank/gear they want)
3. *"Did I want one more dive when the bell rang?"*

## 6. Decisions (locked with the designer)

1. **Merc control**: PURE AI, no input at all — companions are their own people; their
   kit and personality carry them. (No stances, no pings.)
2. **Turn timer**: 30s Dofus-style from day one; end-turn auto-fires at zero.
3. **Autoresolve**: YES — SPACE during your turn hands it to the AI (the zen-grind mode
   survives as a per-turn choice).
4. **Avatar death**: dragged out wounded — the dive ends on the spot and its unbanked
   loot is lost; the campaign continues. (Hardcore mode can return as an option later.)
5. **Party cap**: stays at 3 (you + 2) for board readability.

*Recommended build order: P0 (1→2→3) as one vertical slice on the current save format,
playtest, then P1 items 4–5 before anything else. P2/P3 only after the turn puzzle is fun
on its own.*

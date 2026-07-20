# TITHE — "More fun, more simple" research brief

What the best small tactics games do, and what each lesson would mean for TITHE.
Gathered July 2026 from design writing on Into the Breach, Loop Hero, Shogun
Showdown, Tactical Breach Wizards, Slay the Spire / Darkest Dungeon, and the
prep-then-watch autobattlers (Backpack Battles, Despot's Game).

## The six lessons

### 1. Into the Breach — perfect information turns fights into puzzles
Enemies TELEGRAPH their next action; the player sees exactly what will happen if
they do nothing. Every decision has weight because the stakes are visible. Fights
are tiny (8x8, 3 units, ~5 enemies) and hand-shaped; pushing matters more than
damage because one tile changes five outcomes.
- FOR TITHE: show ENEMY INTENT during your turn (who each mob will strike, for how
  much — an arrow + number on the board and on their timeline card). Shrink fights:
  smaller boards, packs capped at 3-4. We already have push/pull — lean into them.

### 2. Loop Hero — the loop IS the game; cut everything that isn't a decision
Core loop: build → autofight → equip → repeat, distilled to a "thimble of
hyperconcentrated essence". Indirect control (you manage, the hero acts) plus one
recurring dilemma: commit to another lap or retreat with the loot.
- FOR TITHE: the graveyard's free-roam walking is dead time between decisions.
  Replace it with a NODE PROCESSION: the crew advances stop by stop (pack → shrine
  → pack → gate → crypt); at each stop you choose FIGHT / PASS / LEAVE WITH THE
  HAUL. The bell keeps ticking; "one more pack?" becomes the game's heartbeat.
  (Companions already play themselves — our prep-then-watch DNA fits this.)

### 3. Shogun Showdown — radical spatial simplicity, tiny readable numbers
Fights on a 1-D lane of 5-9 tiles, enemy intents always visible, damage in single
digits. Simplifying SPACE and NUMBERS made it deeper, not shallower.
- FOR TITHE: a SMALL NUMBERS pass — divide HP/damage by ~6 (58 HP → 10, Ruin Bolt
  18-24 → 3). Every hit becomes countable at a glance; "can I kill it this turn"
  becomes mental math anyone can do. (Trade-off: drifts from Dofus-scale numbers.)

### 4. Tactical Breach Wizards — free undo until you commit
Undo any action until the turn is committed, no cost. It "encourages
experimentation and makes the game about finding creative solutions rather than
punishing mistakes" — and it only works because outcomes are DETERMINISTIC.
- FOR TITHE: make damage FIXED (no min-max roll — the average, always), then allow
  full UNDO within your turn. Your turn becomes a solvable puzzle; the fun moves
  from "did the dice like me" to "did I find the line". Deterministic damage +
  intents + undo is the Into-the-Breach trinity, and each needs the others.

### 5. Slay the Spire / Darkest Dungeon — short runs, failure that pays
Runs are 10-30 minutes so restarting is cheap; meta-progression converts every
failure into unlocks, so the brain reads loss as learning. Near-misses drive
"one more run".
- FOR TITHE: dives are already ~4 minutes (good). What's missing: WHEN A CAMPAIGN
  ENDS, SOMETHING PERSISTS. A Reliquary of unlocks — tithes ever paid, Sextons ever
  felled — opening new avatar classes, alternate starting kits, starting trinkets.
  Death stops being a wall and becomes a ratchet.

### 6. Backpack Battles / Despot's Game — the prep phase is a toy
The real choices happen BEFORE the fight (who stands where, what supports what);
the fight itself is the payoff you watch. The prep phase itself feels good to
touch (the "neatly packing a closet" pleasure).
- FOR TITHE: we already own prep-then-watch (placement + AI companions). Deepen
  placement: show enemy intents DURING placement, remember your last formation,
  make dropping a unit feel tactile. The band's quick wells and the doll are our
  "backpack" — keep making them satisfying to touch.

## The recommended slate — three phases

### F1 "THE PUZZLE TURN" (fun-per-minute, fastest win)
- Deterministic damage (fixed = the old average; Overchannel/Rage keep their rules)
- Enemy intent telegraphs (board arrows + timeline cards + placement preview)
- Free UNDO of moves and casts until END TURN commits
- Pack sizes capped 3-4 in the yards; boards trimmed

### F2 "THE PROCESSION" (the Loop Hero cut)
- The yard becomes a chain of stops; FIGHT / PASS / LEAVE at each
- The bell + "next stop risks X, pays ~Y" makes commit-or-retreat explicit
- Walking, pathing, hunting-pack chase code all retire (huge simplification)

### F3 "THE RELIQUARY" (one more campaign)
- Campaign end screen: what this life banked (tithes paid, floors cleared)
- Persistent unlocks: 2 new starting classes' kits, starting trinkets, class variants
- Small numbers pass rides along here if F1 plays well

## Sources
- https://after-strategy.com/en/into-the-breach-complete-guide-tactical-strategy-2026/
- https://atomicbobomb.home.blog/2020/05/17/into-the-breach-enemy-intentions/
- https://www.gamedeveloper.com/design/reimagining-failure-in-strategy-game-design-in-i-into-the-breach-i-
- https://www.pcgamer.com/best-design-2021-loop-hero/
- https://medium.com/@sacitsivri/game-design-breakdown-loop-hero-4a86d55142b8
- https://frostilyte.ca/2021/03/22/that-one-about-loop-hero/
- https://rogueliker.com/shogun-showdown-review/
- https://en.wikipedia.org/wiki/Shogun_Showdown
- https://thinkygames.com/reviews/tactical-breach-wizards-review/
- https://medium.com/@bryukh/rewind-and-redo-how-tactical-breach-wizards-brings-puzzle-mechanics-to-tactical-games-364665cce4c1
- https://david.reviews/articles/tactical-breach-wizards-review/
- https://medium.com/@haleymckenzie710/the-psychology-of-just-one-more-run-why-roguelikes-are-addictive-07332dc66b07
- https://kotaku.com/darkest-dungeon-ii-tries-something-new-and-it-mostly-w-1847948896
- https://ithy.com/article/auto-battler-game-design-cffwdacd
- https://en.wikipedia.org/wiki/Backpack_Battles

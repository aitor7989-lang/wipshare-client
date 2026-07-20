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

## PART 2 — THE BIG SWINGS (owner: "I might want huge changes")

Second research round: Loop Hero's family, and the itch.io dice games (Dicey
Dungeons and Slice & Dice — both itch-born, both "cool and easy"), plus He Is
Coming (2025's minimalist auto-battler) and Guild of Dungeoneering (indirect
control). These are whole-shape candidates, not features.

### Swing A — "THE DICE OF THE DEAD"  (the Slice & Dice shape)
Slice & Dice: five heroes ARE five dice; faces are their abilities; each turn you
roll, reroll twice (push-your-luck), assign faces against fully-telegraphed enemy
attacks. Perfect information + a gamble you chose yourself. Dicey Dungeons: dice
feed EQUIPMENT slots; static loadouts, chaos tamed by design; every class riffs
the same core rule differently.
- TITHE version: each crew member IS a die. Their spells are its faces (our spell
  glyphs become die faces 1:1). Level-ups upgrade faces, gear and ESSENCES add or
  transform faces, wounds crack a face blank. Enemies telegraph their strike; you
  roll the crew, reroll at your nerve's cost, assign. NO GRID — one screen.
- Keeps: the whole campaign (city, bell, dives, stones, tithe, crushing, sets,
  hires, Grasping mercs, drops, the celebration). Retires: the tactical grid,
  movement, AP/MP (the dice ARE the economy).
- Character: the crew as a fistful of carved bone dice — thematically PERFECT.

### Swing B — "THE ENDLESS PROCESSION"  (the full Loop Hero shape)
Loop Hero: the hero walks the loop alone; your hands touch only the world (cards)
and the gear; the one recurring decision is another-lap-or-retreat. He Is Coming:
"absolute videogame minimalism" — no move choices, no stat picks; strategy IS the
inventory, a boss arrives every three days. Guild of Dungeoneering: you build the
dungeon around a hero you never control.
- TITHE version: the crew walks the yard ON RAILS, lap after lap; fights resolve
  through the WATCHED AUTOBATTLER WE ALREADY BUILT. Your hands: play GRAVE-CARDS
  earned from kills onto the yard (a mausoleum spawns wardens + gear odds, a bone
  orchard breeds ghouls + essences, a shrine mends), manage gear/essences between
  laps, and answer the bell: one more lap, or leave with the haul. The Sexton
  alone demands your hand — piloted boss fights keep the Dofus turn as the CLIMAX
  instead of the routine.
- Keeps: ~80% of everything (the AI policies, packs, drops, bell, economy, mercs,
  betrayals — the game BEGAN as a watched autobattler; this is a homecoming).
  Retires: routine piloting, free-roam walking, per-fight placement.

### Swing C — "THE GRAVE ARCHITECT"  (the inversion — noted, not recommended)
Play the Tithe-Keeper: bait adventurers INTO the yard you build. The biggest
fiction flip, the least reuse. Shelved unless the others stale.

### Swing D — "THE PUZZLE TURN"  (Part 1's F1+F2 — the conservative big change)
Keep the grid; make it Into the Breach: deterministic damage, enemy intents, free
undo, yard as stops. The evolution path if A/B feel like too much surgery.

### Swing E — "THE BONE BOARD"  (the Die in the Dungeon shape — owner's pick to study)
Die in the Dungeon (ALARTS, itch.io -> Steam, 93% positive): a dice-building
roguelite. Draw dice from your BAG, roll them, PLACE them on a small battle
board. Color is role — red attacks, blue shields, green heals, purple MULTIPLIES
its neighbors. Placement is the whole puzzle: multiplier zones, adjacency
bonuses, board tiles with their own rules, relics that bend both. The enemy
telegraphs; your placed board resolves against it. One screen, four colors, one
decision type (where does this die go) — easy to learn, endlessly buildable
(31 dice, 142 relics, 4 frogs with unique bags).
- TITHE version: THE CREW BECOMES THE BAG. Every crew member contributes their
  spells as carved bone dice (hire a bulwark -> his slam and bastion dice drop
  into the bag; he stands behind the board and swings when his die resolves).
  Draw, roll, place on the BONE BOARD — a 3x3+ slab of grave-tiles. Attack dice
  red, shield dice blue, heal dice green: OUR COLOR LAW ALREADY SAYS THIS
  (HP red / AP blue / MP green — the palette was waiting for it). Essences =
  unique dice (Seize pulls, Ironhide shields the row). Gear = relics and board
  tiles (the Bonewrought set could BE board tiles). Wounds crack dice blank.
  Level-ups upgrade faces. The pack telegraphs its strike over the board.
- Keeps: the entire campaign shell (city, bell, dives, stones, tithe, crushing,
  hires, traitors, sets-as-relics, drops, the celebration, the color law, the
  icons — spell glyphs become die faces 1:1). Retires: the tactical grid,
  movement, AP/MP, LoS — the whole heavy half of the engine.
- Why it beats plain Slice & Dice for us: PLACEMENT keeps a board — TITHE stays
  a game about ground, formation and position, just distilled to one slab.

### The honest comparison
- Swing A: boldest, most "cool and easy", one-screen fights, medium build (new
  combat resolver, ~all campaign code survives). The crew-as-bone-dice image is
  the strongest single idea either round of research produced.
- Swing B: highest fun-per-effort — the autobattler, packs, and economy already
  exist; it mostly DELETES code (piloting routine, pathing, placement). Loop
  Hero's proven heartbeat (commit-or-retreat) is already our bell.
- Swing D: safest, keeps the most; also keeps the most COMPLEXITY (grid, AP/MP,
  LoS) — the thing the owner wants less of.
- Swing E: the owner's own reference game, and the best fit found: one-screen
  placement-puzzle fights (the fun), radical simplification (the ask), the crew
  and the whole campaign survive as the bag and the shell, and even our color
  law and spell icons carry straight over. Medium-large build: a new combat
  scene + dice model; everything outside combat stands.
- RECOMMENDATION (revised after Swing E research): E is the swing. The Bone
  Board replaces grid combat; the campaign shell stays whole; B's lap-procession
  can still frame the yard BETWEEN fights later if wanted. Prototype the board
  fight first as its own scene (keep the old combat behind a flag until the new
  one proves out).

## PART 3 — THE CONVERGENCE (owner's likes, named precisely)

The owner's list: fight after fight with NO roaming · a reward pick after EVERY
battle · gear simple enough to read in one glance (Loop Hero) · an essence system
like The Binding of Isaac · and Mewgenics ("wow") proving tactics can carry it.
Round-four research: Mewgenics (McMillen/Glaiel, 14 years, review consensus "the
pinnacle of the genre") and Isaac's item philosophy.

### What Mewgenics actually does (and proves)
- GRID TACTICS STAY: "excellent turn-based tactical combat... careful balance
  between simplicity and depth. Movement, positioning, abilities, turn order all
  clearly communicated — welcoming even for players who don't gravitate to
  tactics." The action economy is the key simplification: MOVE once + ATTACK
  once per turn, spells on a slow MP trickle. No AP arithmetic.
- FIGHT AFTER FIGHT: an Adventure is a chain of combat/event/treasure nodes.
  No free roaming. EVERY combat win levels a cat and opens a PICK — one of
  several randomized abilities / passives / stat boosts. The reward IS the beat.
- ISAAC DNA: items amass continuously, are part of SETS (3+ equipped = bonus),
  and the fun is evaluating new loot against your build after every fight.
- Party of 1-4; falling in battle = a PERMANENT INJURY, not death (our Wounded!).

### What Isaac's essence philosophy actually is
- Items are PASSIVE and AUTOMATIC — no activation decisions, no text needed;
  you SEE what they do (tears change, body changes).
- SYNERGY is the game: 719 items whose combinations exceed their parts;
  discovering a broken combo is the core joy.
- TRANSFORMATIONS: collect 3 items of a theme -> you visibly BECOME something
  (Guppy, Beast...) with a new power. Collection has a destination.

### Swing F — "THE GAUNTLET OF THE BELL"  (the shape that hits every stated like)
The dive is no longer a map. It is a CHAIN:

    FIGHT -> pick 1 of 3 -> FIGHT -> pick 1 of 3 -> ... -> THE SEXTON

- KEEP (the heart): our piloted-avatar grid fight with AI companions — Mewgenics
  proves this is the right engine. Optionally adopt its economy: MOVE once +
  ATTACK once + spells on MP (retiring AP arithmetic).
- DELETE (the fat): graveyard roaming, pack maps, walking, hunting, gates, the
  wide city hub. Between-fight time becomes ONE reward screen.
- THE PICK (after EVERY fight), three cards, one click:
    [ GEAR  ]  one number: "+3 damage" / "+8 HP" / "+1 MP" (Loop Hero clarity)
    [ESSENCE]  an Isaac passive: automatic, visible, synergizing
    [BLESSING] immediate relief: mend / bread / stones
- ESSENCES, ISAAC-STYLE (grown from our existing system): passive and always-on;
  they visibly change the avatar (tint, aura, grafted glyph); they SYNERGIZE
  ("your hits poison" x "poison spreads on death" x "the poisoned fear you");
  and 3 OF A THEME = A TRANSFORMATION — collect three bone essences and you
  BECOME THE REVENANT, half-dead, feared by the dead. Collection has a
  destination, builds have names.
- THE BELL still tolls: each fight costs its seconds; the Sexton arrives when
  it rings (He Is Coming's boss clock) — leave with the haul or meet him.
- LEVEL-UP becomes the Mewgenics pick (1 of 3: new spell rank / passive / stat)
  — the C and S bookkeeping panels retire from the run entirely.
- Falling = permanent scar on that crew member (Wounded grows teeth), death of
  the campaign only when the leader falls with no one to drag them out.
- The city shrinks to ONE screen between dives: the Keeper (tithe, crush),
  the Post (hire), depart.

### Why F over A/B/E
Every earlier swing traded away the grid fight. The owner's "wow" at Mewgenics
says the OPPOSITE: the grid fight is the keeper — it's everything AROUND it
that must compress into fight/reward/fight. F reuses the combat engine 100%,
deletes the roaming layer, and pours the essence system (already in the
fiction: essences, stones, the Tithe) into Isaac's proven mold.

RECOMMENDATION: F is the redesign. Next artifact: a GAUNTLET design doc (reward
tables, 20-30 Isaac-style essences with synergy rules, 3 transformations, the
simplified gear list, the Mewgenics action economy decision), then a playable
prototype: new run controller over the untouched combat engine.

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

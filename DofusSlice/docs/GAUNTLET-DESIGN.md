# THE GAUNTLET OF THE BELL — TITHE v10 design (the Mewgenics-shaped rebirth)

Owner decision: this is the direction. Fight after fight, no roaming, a pick after
every battle, one-glance gear, Isaac-mold essences — on top of our tactical grid,
reshaped to Mewgenics' proven economy.

## 1. The Mewgenics anatomy (research digest, July 2026)

COMBAT (the part the owner called out):
- Battlefield: a 10x10 tile grid. TILES MATTER — hazards, traps, terrain; veterans
  say knocking enemies into fire/pits/spikes "often deals more damage than direct
  attacks". Knockback is one of the strongest tools in the game.
- The turn: ONE move (range from the Speed stat, one contiguous walk) + ONE basic
  attack (costs nothing) + UNLIMITED spells as long as MANA funds them. Mana
  regenerates each turn and is capped by a stat — a slow trickle, not an AP purse.
  Move-then-attack or attack-then-move both legal; positioning = attacking.
- BACKSTAB: +25% damage from the target's back arc. A 3-tile walk into the back
  arc costs nothing and boosts the whole round — free tactics from geometry.
- Turn order by Speed, always visible. Statuses follow real-life logic (water
  douses Burn; Burn/Freeze/Blind stacks).
- Falling in combat = a PERMANENT INJURY on that cat, not death.

THE RUN:
- An Adventure map is a LINEAR NODE PATH: battle → event → battle → treasure →
  event → miniboss → shop → then a FORK: easy path (event/battle/treasure) or
  hard path (champion battles, rare treasure, an EXTRA level-up) → both converge
  on the chapter boss.
- EVERY combat win = a level-up: the game DRAFTS FOUR options (class-flavored
  abilities / passives / stats), you keep ONE. Dice items let you reroll drafts.
- Items amass continuously from fights/events/shops; items belong to SETS (3+
  equipped on one cat = a bonus); the between-fight game is re-evaluating loot.

## 2. THE GAUNTLET — TITHE v10 specification

### 2.1 Combat, reshaped (engine reused, economy replaced)
- THE TURN: MOVE once (range = the MOVE stat) + STRIKE once (each class gets a
  free basic attack: the Bulwark's shove, the Archer's loosed arrow, the
  Cannon's spark) + spells funded by MANA (regen +2/turn, cap from a stat).
  AP RETIRES. The 30s clock, SPACE-to-auto, END TURN button, undo-nothing —
  all unchanged.
- BACKSTAB: +25% from the back arc (we track Facing4 already). The fight log
  says "from behind!"; the float goes gold.
- HAZARD TILES on every board: ember graves (Burn 2), open pits (fall = gone),
  bone spikes (3 on entry). Our push/pull spells become the Mewgenics knockback
  game. Slam into a pit IS the win condition some turns.
- Board: trim to ~9x9. Statuses: keep poison/shield/regen/root, add BURN
  (stacks, water logic later). Turn order: our alternating weave stays.

### 2.2 The run (the graveyard scene retires)
- A dive is a NODE CHAIN, no map, no walking:
    v1:  FIGHT → PICK → FIGHT → PICK → FIGHT → PICK → THE SEXTON
    v2:  + EVENT nodes (grim little choices), TREASURE nodes, and the
         MIDPOINT FORK: the Quiet Row (easy) or the Screaming Row (hard:
         champion packs, rare gear, an extra pick) — converging on the boss.
- THE PICK (after EVERY fight, one screen, one click) — three cards drawn from:
    [ GEAR ]     one number, Loop Hero clarity ("Blade +2 DMG")
    [ ESSENCE ]  an Isaac passive (below) — automatic, visible, synergizing
    [ BLESSING ] instant relief: mend the crew / +bread / +stones
  Every win ALSO banks the Mewgenics level pick when a level is earned:
  draft 3 (new spell / spell rank / +stat), keep 1. No more point menus.
- THE BELL: each fight costs its seconds; when it rings, the SEXTON ARRIVES
  wherever you are (the He Is Coming clock). Beat him or be dragged out.
- Falling mid-gauntlet = a SCAR on that crew member (permanent stat notch,
  named: "the Sexton's Kiss, -2 HP"). The campaign ends only when the leader
  falls with nobody left standing.

### 2.3 Essences, the Isaac mold (grown from our existing fiction)
Rules: always-on, no activation, VISIBLE on the avatar (tint/aura/grafted
glyph), stack and synergize; THREE OF A THEME = A TRANSFORMATION.
Starter pool (15, three themes):
- MARROW (rot): Spitter's Gift (your strikes poison 1) · Piper's Rot (poisoned
  enemies spread 1 on death) · Mite's Hunger (heal 1 when a poisoned dies) ·
  Wraith's Breath (poison ticks twice below half HP) · Grave Damp (+1 poison mag)
  → 3 MARROW = THE BLIGHTED: your aura poisons adjacent enemies each round.
- BONE (force): Husk's Grip (+1 push on every push) · Warden's Hide (shield 2
  at fight start) · Ghoul's Weight (your pushes deal +2 on wall hits) ·
  Sexton's Knuckle (+25% strike vs full-HP targets) · Ossuary Dust (pit kills
  pay double stones)
  → 3 BONE = THE REVENANT: half-dead; hazards no longer hurt you.
- BELL (time): Toll Keeper (+5s bell per kill) · Last Echo (once per fight,
  survive a killing blow at 1) · Hour Thief (+1 mana regen) · Quiet Step
  (+1 MOVE) · Grasp's Coin (+1 stone per kill)
  → 3 BELL = THE TITHED: the Sexton arrives 60s later; you hear him coming.

### 2.4 Gear, Loop Hero clarity
Four slots (weapon / armor / boots / trinket), ONE number each:
Blade +2 DMG · Plate +6 HP · Boots +1 MOVE · Charm +1 mana regen · rare pieces
roll one keyword ("burning: strikes apply Burn 1"). The Dofus 6-stat block
compresses to five readable numbers: HP · DMG · MOVE · MANA · SPEED.
Sets survive as Mewgenics does them: 3+ pieces of a family = one bonus line.

### 2.5 Look & feel — the CRAWL standard (owner: "I like Crawl's aesthetics
and how brutal it feels")
Crawl's brutality recipe (Powerhoof / Barney Cumming, from their art deep-dives):
- SUPER LOW-RES ON PURPOSE (~150px-tall screens): detail spent on atmosphere and
  movement, never on rendering. Our half-res 1-bit world is already this creed —
  commit harder, not softer.
- STACCATO ANIMATION: no smooth inbetweens — the strike happens BETWEEN two
  frames with a big MOTION-BLUR SMEAR; delayed follow-through shows weight.
- CARTOON GORE + OCCULT DRESSING: blood, dismemberment, pentagrams — the
  violence is loud and the theme is unapologetic.
- A NARRATOR with a Vincent Price spiel gives the dungeon a voice.
FOR THE GAUNTLET (our one accent color IS blood):
- BLOOD PERSISTENCE: hits splatter RED onto the board and it STAYS; corpses
  stay; by round five the arena is a painting of the fight. Kills pop bone
  fragments; the Danger red that today means "enemy" becomes "what is left".
- IMPACT: hit-stop (~80ms freeze on every landed strike, longer on kills),
  procedural SMEAR (stretch the striker's sprite along the strike vector for
  one frame), heavier kill-shake, deeper thud layered under lethal blows.
- THE NARRATOR: the fight log grows a voice — grim one-line couplets on first
  blood, crits, kills, transformations and the Sexton's arrival ("another
  mouth for the earth", "the bell is patient; he is not"). Big beats print
  center-screen in the old-print style.
- OCCULT DRESSING: candle-lit hazard tiles, a pentagram placement zone, the
  Sexton's arrival staged as a ritual (the board darkens, wax gutters).
- A soft VIGNETTE + candle flicker on the world pass; the 1-bit palette holds.

### 2.6 What stays / what retires
STAYS: the combat engine + animator + band UI + icons + color law + celebration
+ sound + city (ONE screen: Keeper for tithe/crush, Post for hires, DEPART) +
stones/tithe/bell + Wounded (as scars) + Grasping mercs (they may walk mid-
gauntlet with a cut).
RETIRES: graveyard roam scene, pack maps, hunting, gates, walking, the crypt
room-chain (folds into the gauntlet's fork), AP, stat-point spending, the C/S
panels during runs (the pick replaces them), the wide city.

## 3. Build plan
- G1 PROTOTYPE (flagged, old game intact): run controller (fight→pick chain of
  3 + Sexton), the pick screen, mana/move/strike economy, basic hazard tiles —
  PLUS the first feel layer: hit-stop, smears, blood persistence, narrator lines.
- G2: essence pool + synergy engine + visible auras; gear simplification.
- G3: events, the fork, transformations, scars, the Sexton-arrival clock.
- G4: retire the old scenes; the gauntlet becomes THE game; balance sims.

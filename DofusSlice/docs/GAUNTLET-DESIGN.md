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

### 2.1 Combat, reshaped (engine reused; the AP/MP purse RESTORED in g10)
- THE TURN (g10 owner's call: "the PA/PM system was working better — it was
  more fun"): the full Dofus purse is back. AP refills every turn and funds
  spells INCLUDING the class weapon (Spark/Loosed Arrow/Shove, 3 AP, twice a
  turn); MP refills every turn and buys movement — as many separate walks as
  the points allow. The mana trickle retired. The DEAD keep the one-blow law:
  mob AP is capped at their priciest skill (+1 small change) — you are one
  body against a host, and the sim priced full mob AP at sub-8% winrates.
- SPACE toggles AUTOPLAY (the Policy plays your hand); ENTER ends the turn.
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
- THE BELL (g4.8 repricing — the wall-clock is dead): the run grants 20 TOLLS
  and every turn the leader takes tolls the bell once. Thinking is free;
  stalling is not — kiting forever, turtling, wandering all spend tolls. At
  zero the SEXTON ARRIVES after the current fight, wherever you are. Toll
  Keeper pays +1 toll per kill; Oil for the Bell +5. (The original real-time
  clock punished deliberation — the one thing a tactics game must never tax —
  and rang mid-run in every QA playthrough regardless of skill.)
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

### 2.3.1 THE COVENANT (g5, built) — the two synergy engines, stolen whole
Research (July 2026): Isaac's synergy engine works because an item is a RULE on a
shared verb (every item modifies the same tear pipeline, so any two stack), and
three themed pickups TRANSFORM you (Guppy). Mewgenics' engine works through
FAMILIES: 2-3 pieces of a set pay a bonus line, and committing to one set beats
stat soup; statuses (Burn stacks) glue abilities together.
OURS: essences are Isaac (rules on our verbs — strike, push, kill, corpse, toll;
three of a theme = transformation); gear is Loop Hero numbers in Mewgenics
families (GRAVE plain-and-big, EMBER fire rules, BONE push rules; 3 of a family
= a set line). Rot and fire both ride the Poison status so MARROW essences
(magnitude/duration/on-death rules) amplify EMBER gear — cross-system synergy.

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

## 3. Build plan — SHIPPED (July 2026)
- g1-g3: run controller (fight→pick→Sexton), mana/strike economy, the Crawl
  feel layer, isometric ragged islands with clustered stones.
- g4 THE FALL: lethal coastlines (push/pull into void), bone spikes, backstab
  +25% with facing, the level draft, the hired Sellsword.
- g4.5-4.7 THE READ/AUDIT/PLAYTEST: tooltips, damage previews, run strip,
  banners, vignette; nine defects found by audit + live play, fixed.
- g4.8 THE TOLL: the bell repriced from seconds to 20 turn-tolls.
- g5 THE COVENANT: gear in four slots and three families with set bonuses;
  the full fifteen essences; three transformations worn as halo rings.
- g6 THE HOST: drafted wave budgets, grades, fight-3 champions, and the
  Cairn Brute — the enemy that shoves YOU.
- g7 THE RITE: the fork (quiet/screaming rows), one grim event a run, and
  the save file (banked stones, class, level, Sellsword persist).
- g8 THE KEEPER: the Sexton rebuilt — retuned smash, the Gravedigger's Hook
  (pull 2), the ritual turning at half health (graves open), the dark stage,
  the guaranteed pre-boss mend.
- g9 THE LESSON: level drafts offer real class spells (LEARN ahead of the
  ladder, DEEPEN a known rank).
- g10 THE MOTION: bodies WALK their paths (145 px/s, per-cell facing), attack
  lunges and flying projectiles, impact FX sequenced behind the walk queue;
  hover a cell for the path preview, hover/arm a spell for its painted reach
  (geometric reach dim, legal targets bright — visible even off-turn); click
  an enemy to INSPECT (kit, prices, ranges, damage-vs-you, threat painted red
  on the ground); click-away cancels an armed spell; SPACE autoplay. The AP/MP
  purse restored (§2.1). AI reads tile danger: no parking on spikes/embers,
  danger-aware route tie-breaks, step-off-the-coals fallback; walking THROUGH
  an ember grave now burns per cell. RunCore.cs split the run's rules out of
  the renderer, and `Gauntlet --sim N` plays hundreds of full headless runs a
  minute: the ledger caught the Bulwark at 0% (now braced: +14 HP, +1 AP,
  Shield 3-4/turn, a vampiric 8-11 Shove — 17%), the screaming row cliff
  (+2 budget -> +1), and the pre-boss MEND being crowded out of a full hand
  (the last supper now keeps its seat). Bot winrates: 21/19/17% across
  cannon/archer/bulwark at level ~2; humans with coastlines do better.
- g11 THE ROAD: the fork retired (owner: "you can choose the path so it's
  lame") — a run now DEALS an eleven-room road, shown whole at the top of
  every screen: six fights with climbing budgets (3..8, champions at fights
  4 and 6, grade-2 floors at the end), TWO TRADERS (run stones finally spend
  mid-run: mend 15, oil 20, gear 25, essence 30 — multi-buy, then LEAVE),
  one grim event and one MYSTERY (a sealed gamble with an honest safe hand:
  the unmarked grave, the gambler's bones, the sealed reliquary), then HIM.
  The bell holds 45 tolls priced per fighting turn. XP repriced (60+30/fight,
  3-4 dings a run) so the power curve is FELT. Decision depth: blades carry
  an ELEMENT now (fire/earth/air riding Int/Str/Agi) and the cards say
  plainly "YOUR element" / "not your element" — matching hand to blade, set
  to family, and purse to trader is where runs are won; nothing rescues a
  contradictory build. HP bars moved ABOVE every head, numbers only on
  hover. VFX feel pass: elemental impact rings + spark motes, muzzle
  flashes, walk dust, ambient ember fireflies, slam sparks. Sim rebalanced
  on the long road: cannon 41% / bulwark 38% / archer 29% (bot); the wall
  now CALCIFIES (+3% all-res per pack survived, cap 18) on top of its brace.
STILL OPEN: more events/mysteries, mob walk/attack strips for the six
unmapped mobs (husk/hound/spitter/mite/piper/wraith still single-frame),
a mid-run sellsword offer at the trader, retiring old TITHE once the
Gauntlet has clearly won.

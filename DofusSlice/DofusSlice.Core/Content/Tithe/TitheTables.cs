namespace DofusSlice.Core.Content.Tithe;

/// <summary>
/// TITHE data tables — the single source of truth for the prototype's classes, skills, mobs,
/// arena and encounters. Per the Slice Bible (§8) all rules and numbers live in data, never in
/// code; marketplace/engine code only renders and executes these rows. Numbers here are honest
/// hand-tuned placeholders (Bible "planes, cubes, honest data") flagged for the §9 Dofus-1.29
/// mining pass. Kept as JSON strings so the headless sim and the game consume identical data
/// with no file-path plumbing.
/// </summary>
public static class TitheTables
{
    // Three playable archetypes. policy = how the autobattler flies it (watched, never piloted);
    // passive = a rule hook that changes what you watch happen (Bible §5 class table).
    //
    // Stats are the Dofus 1.29 block (Bible §6.2): baseHp is the level-1 body, and each point of
    // Vitality is +1 max HP (so class HP = baseHp + vitality). Each class is element-themed and its
    // element's stat scales its damage 1.29-style (Str→Earth, Int→Fire, Cha→Water, Agi→Air) via
    // base × (100 + stat + Power)/100. On each level the class auto-spends 5 characteristic points
    // by its "growth" ratio (Bible §6.2/§6.3 auto-template spending) — Bulwark banks Vitality to
    // tank, Cannon banks Intelligence to nuke, Archer banks Agility. Wisdom = +1% XP per point
    // (the 1.29 rule) and AP/MP-loss resistance.
    public const string ClassesJson = """
    [
      { "id": "archer",  "name": "Archer",  "policy": "skirmisher", "passive": "long_shot", "element": "air", "baseHp": 26, "ap": 6, "mp": 4,
        "vitality": 14, "strength": 6, "intelligence": 4, "chance": 4, "agility": 20, "wisdom": 14, "initiative": 15, "prefRangeMin": 4, "prefRangeMax": 8,
        "growth": { "vitality": 1, "agility": 4 },
        "skills": ["piercing_shot", "crippling_arrow", "deadeye", "barbed_quill"], "blurb": "An Air archer: keeps max distance, shoots the softest target; Long Shot hits harder from afar. Agility is its damage." },
      { "id": "bulwark", "name": "Bulwark", "policy": "bruiser", "passive": "rage_below", "element": "earth", "baseHp": 48, "ap": 6, "mp": 3,
        "vitality": 52, "strength": 20, "intelligence": 4, "chance": 4, "agility": 12, "wisdom": 10, "initiative": 8,  "prefRangeMin": 1, "prefRangeMax": 1,
        "growth": { "vitality": 3, "strength": 2 },
        "skills": ["slam", "seize", "bastion", "blood_price"], "blurb": "An Earth bulwark: advances, body-blocks, shoves with Slam; Rage Below hits harder when bloodied. Strength is its damage." },
      { "id": "cannon",  "name": "Cannon",  "policy": "artillery", "passive": "overchannel", "element": "fire", "baseHp": 34, "ap": 7, "mp": 3,
        "vitality": 24, "strength": 6, "intelligence": 30, "chance": 4, "agility": 10, "wisdom": 12, "initiative": 11, "prefRangeMin": 3, "prefRangeMax": 6,
        "growth": { "vitality": 2, "intelligence": 3 },
        "skills": ["ruin_bolt", "flashfire", "blood_pact", "backblast"], "blurb": "A Fire cannon: holds a sightline and nukes; Overchannel banks its unspent AP into the hit. Intelligence is its damage." }
    ]
    """;

    // Graveyard skins of the locked archetypes (Bible §5 bestiary). Each drops its essence at a
    // low rate; the Sexton (boss) is a guaranteed essence roll.
    // "gear" is the percent chance this mob drops an Adventurer-set piece (Bible §5: set pieces
    // drop from mobs and boss at rates worth chasing; the Sexton is a drop-table peak). Rates are
    // placeholders flagged for the §9 mining / M5 tuning pass.
    public const string MobsJson = """
    [
      { "id": "barrow_husk",   "name": "Barrow Husk",   "policy": "melee",   "maxHp": 54, "ap": 6, "mp": 3,
        "strength": 18, "agility": 8,  "initiative": 5,  "skills": ["husk_strike"], "xp": 110, "stones": 18,
        "essence": "Seize", "drop": 6, "gear": 4 },
      { "id": "marrow_spitter","name": "Marrow Spitter", "policy": "ranged",  "maxHp": 30, "ap": 6, "mp": 3,
        "strength": 14, "chance": 14, "agility": 10, "initiative": 9,  "prefRangeMin": 3, "prefRangeMax": 6,
        "skills": ["marrow_spit", "marrow_rot"], "xp": 90, "stones": 15, "essence": "Marrow Spit", "drop": 8, "gear": 4 },
      { "id": "gravehound",    "name": "Gravehound",    "policy": "flanker", "maxHp": 36, "ap": 6, "mp": 5,
        "strength": 20, "agility": 16, "initiative": 15, "skills": ["grave_bite"], "xp": 130, "stones": 22,
        "essence": "Pounce", "drop": 5, "gear": 5, "resAir": 20 },
      { "id": "crypt_warden",  "name": "Crypt Warden",  "policy": "melee",   "maxHp": 72, "ap": 6, "mp": 2,
        "strength": 18, "agility": 10, "initiative": 6,  "skills": ["husk_strike", "warden_ironhide"], "xp": 170, "stones": 26,
        "essence": "Ironhide", "drop": 5, "gear": 7, "resEarth": 35, "resFire": 15 },
      { "id": "grave_mite",    "name": "Grave Mite",    "policy": "flanker", "maxHp": 12, "ap": 6, "mp": 4,
        "strength": 8,  "agility": 12, "initiative": 11, "skills": ["mite_sap"], "xp": 35, "stones": 6,
        "essence": "Sap", "drop": 10, "gear": 2 },
      { "id": "bone_piper",    "name": "Bone Piper",    "policy": "support", "maxHp": 26, "ap": 6, "mp": 3,
        "strength": 10, "agility": 10, "initiative": 10, "prefRangeMin": 3, "prefRangeMax": 6,
        "skills": ["piper_gift"], "xp": 120, "stones": 20, "essence": "Piper's Gift", "drop": 8, "gear": 5 },
      { "id": "tomb_wraith",   "name": "Tomb Wraith",   "policy": "ranged",  "maxHp": 24, "ap": 6, "mp": 3,
        "strength": 8, "agility": 18, "initiative": 14, "prefRangeMin": 4, "prefRangeMax": 7,
        "skills": ["wraith_wail", "wraith_leash"], "xp": 140, "stones": 24, "essence": "Marrow Spit", "drop": 6, "gear": 5, "resAir": 30 },
      { "id": "grave_ghoul",   "name": "Grave Ghoul",   "policy": "melee",   "maxHp": 44, "ap": 6, "mp": 4,
        "strength": 24, "agility": 10, "initiative": 12, "skills": ["ghoul_rend"], "xp": 150, "stones": 24,
        "essence": "Seize", "drop": 5, "gear": 5, "resEarth": 20, "resWater": -20 },
      { "id": "cairn_brute",   "name": "Cairn Brute",   "policy": "melee",   "maxHp": 40, "ap": 6, "mp": 3,
        "strength": 20, "agility": 8,  "initiative": 8,  "skills": ["brute_hurl"], "xp": 140, "stones": 22,
        "essence": "Seize", "drop": 5, "gear": 5, "resEarth": 15 },
      { "id": "sexton",        "name": "The Sexton",    "policy": "melee",   "maxHp": 150, "ap": 8, "mp": 2,
        "strength": 22, "agility": 6,  "initiative": 7,  "skills": ["sexton_smash", "sexton_hook", "husk_strike"], "xp": 700, "stones": 120,
        "essence": "Sexton's Toll", "drop": 100, "gear": 65, "resEarth": 20, "resFire": 20, "resAir": 20, "resWater": 20 }
    ]
    """;

    // Few skills, simple skills, tactical limits (Bible §3.1.2). effects use the engine's closed
    // set; cooldown / castsPerTurn exercise the Dofus limit fields.
    //
    // "ranks" are the Dofus spell levels (Bible §6.3: 1 spell point per level buys ranks; a rank
    // changes ECONOMICS or SHAPE, never just damage). Each row is a cumulative override on the
    // previous rank — rank 2 is the first row, rank 3 the second.
    public const string SkillsJson = """
    [
      { "key": "piercing_shot", "name": "Piercing Shot", "ap": 4, "min": 4, "max": 8, "los": true,
        "ranks": [ { "min": 3 }, { "max": 9 } ],
        "effects": [ { "kind": "damage", "element": "air", "min": 11, "max": 16 } ] },
      { "key": "slam", "name": "Slam", "ap": 4, "min": 1, "max": 1, "los": true,
        "ranks": [ { "max": 2 }, { "ap": 3 } ],
        "effects": [ { "kind": "damage", "element": "earth", "min": 12, "max": 18 }, { "kind": "push", "cells": 2 } ] },
      { "key": "ruin_bolt", "name": "Ruin Bolt", "ap": 4, "min": 3, "max": 6, "los": true,
        "ranks": [ { "max": 7 }, { "ap": 3 } ],
        "effects": [ { "kind": "damage", "element": "fire", "min": 18, "max": 24 } ] },
      { "key": "barbed_quill", "name": "Barbed Quill", "ap": 3, "min": 4, "max": 7, "los": true, "cooldown": 1,
        "ranks": [ { "max": 8 }, { "cooldown": 0 } ],
        "effects": [ { "kind": "damage", "element": "air", "min": 6, "max": 9 }, { "kind": "status", "status": "poison", "mag": 4, "turns": 2 }, { "kind": "status", "status": "vulnerable", "mag": 15, "turns": 2 } ] },
      { "key": "backblast", "name": "Backblast", "ap": 3, "min": 1, "max": 3, "los": true, "cooldown": 1,
        "ranks": [ { "max": 4 }, { "ap": 2 } ],
        "effects": [ { "kind": "damage", "element": "fire", "min": 10, "max": 15 }, { "kind": "push", "cells": 2 } ] },
      { "key": "husk_strike", "name": "Husk Strike", "ap": 3, "min": 1, "max": 1, "los": true,
        "effects": [ { "kind": "damage", "element": "earth", "min": 12, "max": 18 } ] },
      { "key": "marrow_spit", "name": "Marrow Spit", "ap": 3, "min": 3, "max": 6, "los": true,
        "effects": [ { "kind": "damage", "element": "water", "min": 10, "max": 15 } ] },
      { "key": "grave_bite", "name": "Grave Bite", "ap": 3, "min": 1, "max": 1, "los": true,
        "effects": [ { "kind": "damage", "element": "air", "min": 16, "max": 24 } ] },
      { "key": "warden_ironhide", "name": "Ironhide", "ap": 2, "min": 0, "max": 0, "los": false, "cooldown": 2,
        "effects": [ { "kind": "status", "status": "shield", "mag": 8, "turns": 1 } ] },
      { "key": "mite_sap", "name": "Sap", "ap": 2, "min": 1, "max": 1, "los": true,
        "effects": [ { "kind": "damage", "element": "air", "min": 4, "max": 7 }, { "kind": "status", "status": "mpdrain", "mag": 1, "turns": 1 } ] },
      { "key": "wraith_wail", "name": "Wraith Wail", "ap": 3, "min": 3, "max": 7, "los": true,
        "effects": [ { "kind": "damage", "element": "air", "min": 11, "max": 15 } ] },
      { "key": "wraith_leash", "name": "Wraith Leash", "ap": 3, "min": 2, "max": 5, "los": true, "cooldown": 2,
        "effects": [ { "kind": "damage", "element": "air", "min": 6, "max": 9 }, { "kind": "steal_range", "cells": 2, "turns": 2 } ] },
      { "key": "ghoul_rend", "name": "Ghoul Rend", "ap": 3, "min": 1, "max": 1, "los": true,
        "effects": [ { "kind": "lifesteal", "element": "earth", "min": 8, "max": 12 } ] },
      { "key": "marrow_rot", "name": "Marrow Rot", "ap": 3, "min": 2, "max": 6, "los": true, "cooldown": 3,
        "effects": [ { "kind": "damage", "element": "water", "min": 6, "max": 10 }, { "kind": "status", "status": "poison", "mag": 3, "turns": 2 } ] },
      { "key": "brute_hurl", "name": "Hurl", "ap": 4, "min": 1, "max": 1, "los": true,
        "effects": [ { "kind": "damage", "element": "earth", "min": 8, "max": 12 }, { "kind": "push", "cells": 2 } ] },
      { "key": "piper_gift", "name": "Piper's Gift", "ap": 3, "min": 1, "max": 4, "los": true, "castsPerTurn": 1,
        "effects": [ { "kind": "grant_ap", "min": 2 } ] },
      { "key": "sexton_smash", "name": "Sexton's Toll", "ap": 5, "min": 1, "max": 1, "los": true, "cooldown": 2,
        "effects": [ { "kind": "damage", "element": "earth", "min": 18, "max": 26 }, { "kind": "push", "cells": 2 } ] },
      { "key": "sexton_hook", "name": "Gravedigger's Hook", "ap": 4, "min": 2, "max": 5, "los": true, "cooldown": 1,
        "effects": [ { "kind": "damage", "element": "earth", "min": 8, "max": 12 }, { "kind": "pull", "cells": 2 } ] },
      { "key": "seize", "name": "Seize", "ap": 3, "min": 1, "max": 1, "los": true, "cooldown": 1,
        "effects": [ { "kind": "damage", "element": "earth", "min": 8, "max": 12 }, { "kind": "status", "status": "seized", "mag": 0, "turns": 1 } ] },
      { "key": "bastion", "name": "Bastion", "ap": 3, "min": 0, "max": 0, "los": false, "cooldown": 3,
        "ranks": [ { "cooldown": 2 } ],
        "effects": [ { "kind": "status", "status": "shield", "mag": 10, "turns": 1 }, { "kind": "status", "status": "defensebuff", "mag": 25, "turns": 2 } ] },
      { "key": "crippling_arrow", "name": "Crippling Arrow", "ap": 2, "min": 3, "max": 7, "los": true, "cooldown": 1,
        "ranks": [ { "max": 8 }, { "cooldown": 0 } ],
        "effects": [ { "kind": "damage", "element": "air", "min": 6, "max": 10 }, { "kind": "status", "status": "mpdrain", "mag": 1, "turns": 1 } ] },
      { "key": "flashfire", "name": "Flashfire", "ap": 3, "min": 1, "max": 2, "los": true, "cooldown": 1,
        "ranks": [ { "ap": 2 } ],
        "effects": [ { "kind": "damage", "element": "fire", "min": 12, "max": 16 }, { "kind": "push", "cells": 1 } ] },
      { "key": "blood_pact", "name": "Blood Pact", "ap": 1, "min": 0, "max": 0, "los": false, "castsPerTurn": 1,
        "effects": [ { "kind": "self_damage", "min": 10 }, { "kind": "grant_ap", "min": 3 }, { "kind": "status", "status": "damagebuff", "mag": 20, "turns": 1 } ] },
      { "key": "blink", "name": "Blink", "ap": 2, "min": 1, "max": 2, "los": false, "cooldown": 30, "targetsGround": true,
        "effects": [ { "kind": "teleport" } ] },
      { "key": "deadeye", "name": "Deadeye", "ap": 5, "min": 5, "max": 8, "los": true, "cooldown": 1,
        "ranks": [ { "max": 9 }, { "cooldown": 0 } ],
        "effects": [ { "kind": "damage", "element": "air", "min": 16, "max": 22 } ] },
      { "key": "blood_price", "name": "Blood Price", "ap": 4, "min": 1, "max": 1, "los": true, "cooldown": 1,
        "ranks": [ { "cooldown": 0 } ],
        "effects": [ { "kind": "lifesteal", "element": "earth", "min": 14, "max": 20 } ] }
    ]
    """;

    // The essence catalog (Bible §6.5): each mob's rare drop is a consumable that teaches its
    // signature skill to ONE chosen unit, filling one of two campaign-permanent essence slots.
    // Learning is consumption; essences never check class — a bad fit is allowed and wasted.
    public const string EssencesJson = """
    [
      { "name": "Seize",         "skill": "seize",           "blurb": "The husk's grip: a rooting strike." },
      { "name": "Marrow Spit",   "skill": "marrow_spit",     "blurb": "The spitter's ranged bile." },
      { "name": "Pounce",        "skill": "grave_bite",      "blurb": "The hound's lunging bite." },
      { "name": "Ironhide",      "skill": "warden_ironhide", "blurb": "The warden's self-shield." },
      { "name": "Sap",           "skill": "mite_sap",        "blurb": "The mite's leeching sting." },
      { "name": "Piper's Gift",  "skill": "piper_gift",      "blurb": "The piper's gift of action." },
      { "name": "Sexton's Toll", "skill": "sexton_smash",    "blurb": "The keeper's crushing toll." },
      { "name": "Blood Pact",    "skill": "blood_pact",      "blurb": "Temple exclusive: trade blood for action (1 AP + 10 HP → +3 AP, once a turn)." },
      { "name": "Blink",         "skill": "blink",           "blurb": "Temple exclusive: a 2-cell escape leap, once a fight." }
    ]
    """;

    // The Graveyard graybox: open dark ground, scattered tombstones (# rock) and dead trees (T),
    // crew start zone on the left ('s' cells, 'P' anchor). Enemies are placed by the encounter.
    public const string ArenaJson = """
    {
      "name": "The Graveyard",
      "rows": [
        "...............",
        "...#.......#...",
        "ss....T........",
        "ss......#......",
        "sP....#........",
        "ss.......#.....",
        "ss....T....#...",
        "ss......#......",
        "ss....#........",
        "...#..........T",
        ".........T.....",
        "...#.......#...",
        "..............."
      ]
    }
    """;

    // Alternate fight grounds so the yard's packs don't all brawl on one field: a sunken yard
    // split by pits, a tomb row of long walls, and an open barrow circle. Same 15x13 frame.
    public const string ArenaJson2 = """
    {
      "name": "The Sunken Yard",
      "rows": [
        "...............",
        "....oo....#....",
        "ss..oo.........",
        "ss.......T.....",
        "sP...#....oo...",
        "ss........oo...",
        "ss....#........",
        "ss.............",
        "ss..oo...#.....",
        "....oo......T..",
        ".........#.....",
        "...T...........",
        "..............."
      ]
    }
    """;

    public const string ArenaJson3 = """
    {
      "name": "The Tomb Rows",
      "rows": [
        "...............",
        "...#.#.....#...",
        "ss.............",
        "ss...#.#.#.....",
        "sP.............",
        "ss.....#.#.#...",
        "ss.............",
        "ss...#.#.#.....",
        "ss.............",
        "...#.....#.#...",
        "......T........",
        "...............",
        "..............."
      ]
    }
    """;

    public const string ArenaJson4 = """
    {
      "name": "The Barrow Circle",
      "rows": [
        "...............",
        "......###......",
        "ss...#...#.....",
        "ss..#..T..#....",
        "sP.............",
        "ss..#.....#....",
        "ss...#...#.....",
        "ss....###......",
        "ss.............",
        "........T......",
        "..T............",
        "...........#...",
        "..............."
      ]
    }
    """;

    // A skeleton pack: composition + spawn cells (right side). Data owns the encounter.
    public const string EncounterJson = """
    {
      "name": "Skeleton Pack",
      "spawns": [
        { "mob": "gravehound",     "x": 7,  "y": 2 },
        { "mob": "gravehound",     "x": 7,  "y": 10 },
        { "mob": "barrow_husk",    "x": 10, "y": 4 },
        { "mob": "barrow_husk",    "x": 10, "y": 8 },
        { "mob": "barrow_husk",    "x": 12, "y": 6 },
        { "mob": "marrow_spitter", "x": 14, "y": 6 }
      ]
    }
    """;

    // The Sexton's court (Bible §5 boss): one big slow monster whose retinue does the tactical
    // work — armored Wardens up front, a swarm of Mites, a Bone Piper feeding the boss AP.
    public const string EncounterBossJson = """
    {
      "name": "The Sexton's Court",
      "spawns": [
        { "mob": "sexton",       "x": 11, "y": 5 },
        { "mob": "crypt_warden", "x": 9,  "y": 4 },
        { "mob": "crypt_warden", "x": 9,  "y": 8 },
        { "mob": "grave_mite",   "x": 7,  "y": 2 },
        { "mob": "grave_mite",   "x": 7,  "y": 10 },
        { "mob": "bone_piper",   "x": 14, "y": 6 }
      ]
    }
    """;

    // A little town square for the City scene (Bible §6.13: clickable NPCs, Dofus idiom). Just a
    // floor — the NPCs and the Lychgate are placed by the game.
    public const string CityMapJson = """
    {
      "name": "The City",
      "rows": [
        "ddddddddddddd",
        "d.d.d.d.d.d.d",
        "d.ppppppppp.d",
        "d.p......p..d",
        "d.p.pppp.p..d",
        "d.p.p..p.p..d",
        "d.p.pppp.p..d",
        "d.p......p..d",
        "d.ppppppppppd",
        "d.d.d.d.d.d.d",
        "ddddddddddddd"
      ]
    }
    """;

    // The Crypt (Bible §4, §6.8): a strictly linear chain of sealing-door rooms, escalating to the
    // Sexton's court, then the altar teleports the crew out. The dive's big bet.
    public const string CryptJson = """
    [
      { "name": "The Ossuary",        "comp": ["barrow_husk", "barrow_husk", "gravehound"], "grade": 1 },
      { "name": "The Nave",           "comp": ["crypt_warden", "grave_mite", "grave_mite", "gravehound"], "grade": 2 },
      { "name": "The Reliquary",      "comp": ["crypt_warden", "crypt_warden", "marrow_spitter", "gravehound", "gravehound"], "grade": 2 },
      { "name": "The Wailing Gallery", "comp": ["tomb_wraith", "tomb_wraith", "bone_piper", "grave_mite"], "grade": 2 },
      { "name": "The Bone Orchard",    "comp": ["grave_ghoul", "grave_ghoul", "tomb_wraith", "crypt_warden"], "grade": 3 },
      { "name": "The Sexton's Court", "comp": ["sexton", "crypt_warden", "crypt_warden", "grave_mite", "grave_mite", "bone_piper"], "boss": true, "grade": 3 }
    ]
    """;

    // Equipment (Bible §5, §6.10). Dofus 1.29 slot list: weapon, hat, cape, amulet, ring (×2),
    // belt, boots. The Graveyard's one starter panoply — the Adventurer set — following the
    // Adventurer-set pattern: modest stats per piece, a meaningful full-set bonus (see SetsJson).
    // Values are honest placeholders for the §9 mining / M5 tuning pass.
    // The two set-less rows are the behavior-rule UNIQUES (Bible §5: "a +1 PM boot equivalent
    // stays the canonical screaming find"): Gravewalkers (+1 MP) and the Piper's Whistle (+1 AP).
    // They surface rarely through the same gear-drop channel (a successful roll is a unique
    // ~15% of the time) and compete with set pieces for their slot — breaking the panoply for
    // the unique is the classic Dofus dilemma, and it's the player's to make.
    public const string ItemsJson = """
    [
      { "id": "adv_blade",  "name": "Adventurer Blade",  "slot": "weapon", "set": "adventurer", "strength": 6, "intelligence": 6, "power": 3 },
      { "id": "adv_hat",    "name": "Adventurer Hat",    "slot": "hat",    "set": "adventurer", "vitality": 8, "intelligence": 4, "wisdom": 4 },
      { "id": "adv_cape",   "name": "Adventurer Cape",   "slot": "cape",   "set": "adventurer", "strength": 4, "agility": 4, "power": 2 },
      { "id": "adv_amulet", "name": "Adventurer Amulet", "slot": "amulet", "set": "adventurer", "vitality": 10, "intelligence": 5, "wisdom": 3 },
      { "id": "adv_ring",   "name": "Adventurer Ring",   "slot": "ring",   "set": "adventurer", "strength": 4, "intelligence": 4, "vitality": 6 },
      { "id": "adv_belt",   "name": "Adventurer Belt",   "slot": "belt",   "set": "adventurer", "vitality": 8, "strength": 3, "power": 2 },
      { "id": "adv_boots",  "name": "Adventurer Boots",  "slot": "boots",  "set": "adventurer", "agility": 6, "chance": 4, "vitality": 6 },

      { "id": "bone_maul",   "name": "Bonewrought Maul",   "slot": "weapon", "set": "bonewrought", "strength": 9, "intelligence": 9, "power": 5 },
      { "id": "bone_crown",  "name": "Bonewrought Crown",  "slot": "hat",    "set": "bonewrought", "vitality": 14, "wisdom": 7, "intelligence": 6 },
      { "id": "bone_mantle", "name": "Bonewrought Mantle", "slot": "cape",   "set": "bonewrought", "vitality": 10, "agility": 7, "power": 4 },
      { "id": "bone_loop",   "name": "Bonewrought Loop",   "slot": "ring",   "set": "bonewrought", "strength": 6, "intelligence": 6, "chance": 6 },
      { "id": "bone_girdle", "name": "Bonewrought Girdle", "slot": "belt",   "set": "bonewrought", "vitality": 12, "strength": 6, "power": 3 },

      { "id": "gravewalkers",  "name": "Gravewalkers",     "slot": "boots",  "agility": 4, "mp": 1 },
      { "id": "pipers_whistle","name": "Piper's Whistle",  "slot": "amulet", "wisdom": 4, "ap": 1 }
    ]
    """;

    // Set (panoply) bonuses (Bible §6.10): a tier table keyed by number of equipped pieces. Each
    // tier's numbers are the TOTAL bonus at that piece count (Dofus applies the highest tier ≤ the
    // equipped count, not the sum of tiers). The full-set jump is deliberately a "screaming find",
    // capped by the classic Adventurer full-set reward: **+1 MP** — a behavior-changing bonus, the
    // bible's canonical loot idiom (loot that changes what you watch happen, Pillar 3).
    public const string SetsJson = """
    [
      { "id": "adventurer", "name": "Adventurer Set", "tiers": [
        { "pieces": 2, "vitality": 10 },
        { "pieces": 3, "vitality": 18, "strength": 3,  "intelligence": 3,  "power": 2 },
        { "pieces": 4, "vitality": 28, "strength": 6,  "intelligence": 6,  "agility": 3,  "power": 4 },
        { "pieces": 5, "vitality": 40, "strength": 10, "intelligence": 10, "agility": 6,  "wisdom": 4,  "power": 6 },
        { "pieces": 6, "vitality": 55, "strength": 15, "intelligence": 15, "agility": 10, "wisdom": 8,  "power": 10 },
        { "pieces": 7, "vitality": 75, "strength": 22, "intelligence": 22, "agility": 15, "wisdom": 12, "power": 15, "mp": 1 }
      ] },
      { "id": "bonewrought", "name": "Bonewrought Set", "tiers": [
        { "pieces": 2, "vitality": 14, "power": 3 },
        { "pieces": 3, "vitality": 26, "strength": 8,  "intelligence": 8,  "power": 6 },
        { "pieces": 4, "vitality": 42, "strength": 14, "intelligence": 14, "wisdom": 8,  "power": 10 },
        { "pieces": 5, "vitality": 60, "strength": 20, "intelligence": 20, "agility": 10, "wisdom": 12, "power": 14, "ap": 1 }
      ] }
    ]
    """;

    // Economy + services (Bible §5 tables, §8). Placeholders for the M5 tuning pass. Stones from a
    // mob equals its XP value (one dial for now); vendoring an essence returns essenceSell.
    public const string PricesJson = """
    {
      "hardBread": 15,      "breadHeal": 22,
      "draught": 80,
      "hireBasePerLevel": 45,
      "essenceSell": 60,   "essenceBuy": 300,   "essenceRemoval": 350,   "vetFee": 60,
      "titheEveryNDives": 3, "titheBase": 100, "titheGrowth": 80
    }
    """;

    // The Graveyard floor: a real-time clock and the skeleton packs that drift on it. reach = the
    // seconds it costs to travel to a pack (route knowledge is the resource); grade = the Dofus
    // 1.29 mob grade (deeper packs field harder grades: +30% HP, +15% stats, +25% XP/gold per
    // grade step); hunts = a wide-aggro
    // type that closes on the crew. The Crypt entrance sits deepest. Clock is short for the
    // prototype loop (Bible's 12-minute floor is the production value).
    public const string GraveyardJson = """
    {
      "clockSeconds": 240,
      "cryptReach": 150,
      "packs": [
        { "id": "straggler",  "comp": ["barrow_husk"],                                                   "reach": 12, "hunts": false, "grade": 1 },
        { "id": "husks-near", "comp": ["barrow_husk", "barrow_husk"],                                    "reach": 18, "hunts": false, "grade": 1 },
        { "id": "bonewash",   "comp": ["barrow_husk", "marrow_spitter"],                                 "reach": 28, "hunts": false, "grade": 1 },
        { "id": "warden-way", "comp": ["crypt_warden", "grave_mite", "grave_mite"],                      "reach": 36, "hunts": false, "grade": 2 },
        { "id": "wailing-row","comp": ["tomb_wraith", "barrow_husk", "grave_mite"],                      "reach": 40, "hunts": false, "grade": 2 },
        { "id": "ghoul-ditch","comp": ["grave_ghoul", "grave_ghoul", "marrow_spitter"],                  "reach": 50, "hunts": false, "grade": 2 },
        { "id": "hound-pack", "comp": ["gravehound", "gravehound", "barrow_husk", "marrow_spitter"],     "reach": 46, "hunts": true,  "grade": 3 },
        { "id": "deep-court", "comp": ["gravehound", "gravehound", "gravehound", "barrow_husk"],         "reach": 56, "hunts": true,  "grade": 3 },
        { "id": "warband",    "comp": ["crypt_warden", "gravehound", "gravehound", "marrow_spitter", "barrow_husk"], "reach": 66, "hunts": true, "grade": 4 }
      ]
    }
    """;
}

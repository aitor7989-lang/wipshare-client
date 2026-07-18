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
      { "id": "archer",  "name": "Archer",  "policy": "skirmisher", "passive": "long_shot", "element": "air", "baseHp": 24, "ap": 6, "mp": 4,
        "vitality": 12, "strength": 6, "intelligence": 4, "chance": 4, "agility": 22, "wisdom": 14, "initiative": 15, "prefRangeMin": 4, "prefRangeMax": 8,
        "growth": { "vitality": 1, "agility": 4 },
        "skills": ["piercing_shot", "crippling_arrow"], "blurb": "An Air archer: keeps max distance, shoots the softest target; Long Shot hits harder from afar. Agility is its damage." },
      { "id": "bulwark", "name": "Bulwark", "policy": "bruiser", "passive": "rage_below", "element": "earth", "baseHp": 38, "ap": 6, "mp": 3,
        "vitality": 40, "strength": 20, "intelligence": 4, "chance": 4, "agility": 12, "wisdom": 10, "initiative": 8,  "prefRangeMin": 1, "prefRangeMax": 1,
        "growth": { "vitality": 3, "strength": 2 },
        "skills": ["slam", "bastion"], "blurb": "An Earth bulwark: advances, body-blocks, shoves with Slam; Rage Below hits harder when bloodied. Strength is its damage." },
      { "id": "cannon",  "name": "Cannon",  "policy": "artillery", "passive": "overchannel", "element": "fire", "baseHp": 27, "ap": 7, "mp": 3,
        "vitality": 15, "strength": 6, "intelligence": 30, "chance": 4, "agility": 10, "wisdom": 12, "initiative": 11, "prefRangeMin": 3, "prefRangeMax": 6,
        "growth": { "vitality": 1, "intelligence": 4 },
        "skills": ["ruin_bolt", "flashfire"], "blurb": "A Fire cannon: holds a sightline and nukes; Overchannel banks its unspent AP into the hit. Intelligence is its damage." }
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
        "strength": 18, "agility": 8,  "initiative": 5,  "skills": ["husk_strike"], "xp": 110, "gold": 18,
        "essence": "Seize", "drop": 6, "gear": 4 },
      { "id": "marrow_spitter","name": "Marrow Spitter", "policy": "ranged",  "maxHp": 30, "ap": 6, "mp": 3,
        "strength": 14, "chance": 14, "agility": 10, "initiative": 9,  "prefRangeMin": 3, "prefRangeMax": 6,
        "skills": ["marrow_spit"], "xp": 90, "gold": 15, "essence": "Marrow Spit", "drop": 8, "gear": 4 },
      { "id": "gravehound",    "name": "Gravehound",    "policy": "flanker", "maxHp": 36, "ap": 6, "mp": 5,
        "strength": 20, "agility": 16, "initiative": 15, "skills": ["grave_bite"], "xp": 130, "gold": 22,
        "essence": "Pounce", "drop": 5, "gear": 5, "resAir": 20 },
      { "id": "crypt_warden",  "name": "Crypt Warden",  "policy": "melee",   "maxHp": 72, "ap": 6, "mp": 2,
        "strength": 18, "agility": 10, "initiative": 6,  "skills": ["husk_strike", "warden_ironhide"], "xp": 170, "gold": 26,
        "essence": "Ironhide", "drop": 5, "gear": 7, "resEarth": 35, "resFire": 15 },
      { "id": "grave_mite",    "name": "Grave Mite",    "policy": "flanker", "maxHp": 12, "ap": 6, "mp": 4,
        "strength": 8,  "agility": 12, "initiative": 11, "skills": ["mite_sap"], "xp": 35, "gold": 6,
        "essence": "Sap", "drop": 10, "gear": 2 },
      { "id": "bone_piper",    "name": "Bone Piper",    "policy": "support", "maxHp": 26, "ap": 6, "mp": 3,
        "strength": 10, "agility": 10, "initiative": 10, "prefRangeMin": 3, "prefRangeMax": 6,
        "skills": ["piper_gift"], "xp": 120, "gold": 20, "essence": "Piper's Gift", "drop": 8, "gear": 5 },
      { "id": "sexton",        "name": "The Sexton",    "policy": "melee",   "maxHp": 190, "ap": 8, "mp": 2,
        "strength": 22, "agility": 6,  "initiative": 7,  "skills": ["sexton_smash", "husk_strike"], "xp": 700, "gold": 120,
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
        "effects": [ { "kind": "damage", "element": "air", "min": 13, "max": 18 } ] },
      { "key": "slam", "name": "Slam", "ap": 4, "min": 1, "max": 1, "los": true,
        "ranks": [ { "max": 2 }, { "ap": 3 } ],
        "effects": [ { "kind": "damage", "element": "earth", "min": 12, "max": 18 }, { "kind": "push", "cells": 1 } ] },
      { "key": "ruin_bolt", "name": "Ruin Bolt", "ap": 4, "min": 3, "max": 6, "los": true,
        "ranks": [ { "max": 7 }, { "ap": 3 } ],
        "effects": [ { "kind": "damage", "element": "fire", "min": 18, "max": 24 } ] },
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
      { "key": "piper_gift", "name": "Piper's Gift", "ap": 3, "min": 1, "max": 4, "los": true, "castsPerTurn": 1,
        "effects": [ { "kind": "grant_ap", "min": 2 } ] },
      { "key": "sexton_smash", "name": "Sexton's Toll", "ap": 5, "min": 1, "max": 1, "los": true, "cooldown": 2,
        "effects": [ { "kind": "damage", "element": "earth", "min": 22, "max": 32 }, { "kind": "push", "cells": 2 } ] },
      { "key": "seize", "name": "Seize", "ap": 3, "min": 1, "max": 1, "los": true, "cooldown": 1,
        "effects": [ { "kind": "damage", "element": "earth", "min": 8, "max": 12 }, { "kind": "status", "status": "seized", "mag": 0, "turns": 1 } ] },
      { "key": "bastion", "name": "Bastion", "ap": 3, "min": 0, "max": 0, "los": false, "cooldown": 3,
        "ranks": [ { "cooldown": 2 } ],
        "effects": [ { "kind": "status", "status": "shield", "mag": 10, "turns": 1 } ] },
      { "key": "crippling_arrow", "name": "Crippling Arrow", "ap": 2, "min": 3, "max": 7, "los": true, "cooldown": 1,
        "ranks": [ { "max": 8 }, { "cooldown": 0 } ],
        "effects": [ { "kind": "damage", "element": "air", "min": 6, "max": 10 }, { "kind": "status", "status": "mpdrain", "mag": 1, "turns": 1 } ] },
      { "key": "flashfire", "name": "Flashfire", "ap": 3, "min": 1, "max": 2, "los": true, "cooldown": 1,
        "ranks": [ { "ap": 2 } ],
        "effects": [ { "kind": "damage", "element": "fire", "min": 12, "max": 16 }, { "kind": "push", "cells": 1 } ] }
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
      { "name": "Sexton's Toll", "skill": "sexton_smash",    "blurb": "The keeper's crushing toll." }
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
      { "name": "The Ossuary",        "comp": ["barrow_husk", "barrow_husk", "gravehound"] },
      { "name": "The Nave",           "comp": ["crypt_warden", "grave_mite", "grave_mite", "gravehound"] },
      { "name": "The Reliquary",      "comp": ["crypt_warden", "crypt_warden", "marrow_spitter", "gravehound", "gravehound"] },
      { "name": "The Sexton's Court", "comp": ["sexton", "crypt_warden", "crypt_warden", "grave_mite", "grave_mite", "bone_piper"], "boss": true }
    ]
    """;

    // Equipment (Bible §5, §6.10). Dofus 1.29 slot list: weapon, hat, cape, amulet, ring (×2),
    // belt, boots. The Graveyard's one starter panoply — the Adventurer set — following the
    // Adventurer-set pattern: modest stats per piece, a meaningful full-set bonus (see SetsJson).
    // Only Strength/Vitality/Agility/Wisdom are live in the prototype (elements deferred). Values
    // are honest placeholders for the §9 mining / M5 tuning pass.
    public const string ItemsJson = """
    [
      { "id": "adv_blade",  "name": "Adventurer Blade",  "slot": "weapon", "set": "adventurer", "strength": 6, "intelligence": 6, "power": 3 },
      { "id": "adv_hat",    "name": "Adventurer Hat",    "slot": "hat",    "set": "adventurer", "vitality": 8, "intelligence": 4, "wisdom": 4 },
      { "id": "adv_cape",   "name": "Adventurer Cape",   "slot": "cape",   "set": "adventurer", "strength": 4, "agility": 4, "power": 2 },
      { "id": "adv_amulet", "name": "Adventurer Amulet", "slot": "amulet", "set": "adventurer", "vitality": 10, "intelligence": 5, "wisdom": 3 },
      { "id": "adv_ring",   "name": "Adventurer Ring",   "slot": "ring",   "set": "adventurer", "strength": 4, "intelligence": 4, "vitality": 6 },
      { "id": "adv_belt",   "name": "Adventurer Belt",   "slot": "belt",   "set": "adventurer", "vitality": 8, "strength": 3, "power": 2 },
      { "id": "adv_boots",  "name": "Adventurer Boots",  "slot": "boots",  "set": "adventurer", "agility": 6, "chance": 4, "vitality": 6 }
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
      ] }
    ]
    """;

    // Economy + services (Bible §5 tables, §8). Placeholders for the M5 tuning pass. Gold from a
    // mob equals its XP value (one dial for now); vendoring an essence returns essenceSell.
    public const string PricesJson = """
    {
      "hardBread": 15,      "breadHeal": 22,
      "draught": 130,
      "hireBasePerLevel": 45,
      "essenceSell": 45,
      "titheEveryNDives": 3, "titheBase": 120, "titheGrowth": 70
    }
    """;

    // The Graveyard floor: a real-time clock and the skeleton packs that drift on it. reach = the
    // seconds it costs to travel to a pack (route knowledge is the resource); hunts = a wide-aggro
    // type that closes on the crew. The Crypt entrance sits deepest. Clock is short for the
    // prototype loop (Bible's 12-minute floor is the production value).
    public const string GraveyardJson = """
    {
      "clockSeconds": 240,
      "cryptReach": 150,
      "packs": [
        { "id": "husks-near", "comp": ["barrow_husk", "barrow_husk"],                                    "reach": 18, "hunts": false },
        { "id": "bonewash",   "comp": ["barrow_husk", "marrow_spitter"],                                 "reach": 28, "hunts": false },
        { "id": "warden-way", "comp": ["crypt_warden", "grave_mite", "grave_mite"],                      "reach": 36, "hunts": false },
        { "id": "hound-pack", "comp": ["gravehound", "gravehound", "barrow_husk", "marrow_spitter"],     "reach": 46, "hunts": true  },
        { "id": "deep-court", "comp": ["gravehound", "gravehound", "gravehound", "barrow_husk"],         "reach": 56, "hunts": true  },
        { "id": "warband",    "comp": ["crypt_warden", "gravehound", "gravehound", "marrow_spitter", "barrow_husk"], "reach": 66, "hunts": true }
      ]
    }
    """;
}

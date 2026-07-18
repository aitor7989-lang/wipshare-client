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
    public const string ClassesJson = """
    [
      { "id": "archer",  "name": "Archer",  "policy": "skirmisher", "passive": "long_shot", "maxHp": 36, "ap": 6, "mp": 4,
        "strength": 22, "agility": 18, "initiative": 15, "prefRangeMin": 4, "prefRangeMax": 8,
        "skills": ["piercing_shot"], "blurb": "Keeps max distance, shoots the softest target; Long Shot hits harder from afar." },
      { "id": "bulwark", "name": "Bulwark", "policy": "bruiser", "passive": "rage_below", "maxHp": 78, "ap": 6, "mp": 3,
        "strength": 20, "agility": 12, "initiative": 8,  "prefRangeMin": 1, "prefRangeMax": 1,
        "skills": ["slam"], "blurb": "Advances, body-blocks, shoves with Slam; Rage Below hits harder when bloodied." },
      { "id": "cannon",  "name": "Cannon",  "policy": "artillery", "passive": "overchannel", "maxHp": 42, "ap": 7, "mp": 3,
        "strength": 30, "agility": 10, "initiative": 11, "prefRangeMin": 3, "prefRangeMax": 6,
        "skills": ["ruin_bolt"], "blurb": "Holds a sightline and nukes; Overchannel banks its unspent AP into the hit." }
    ]
    """;

    // Graveyard skins of the locked archetypes (Bible §5 bestiary). Each drops its essence at a
    // low rate; the Sexton (boss) is a guaranteed essence roll.
    public const string MobsJson = """
    [
      { "id": "barrow_husk",   "name": "Barrow Husk",   "policy": "melee",   "maxHp": 54, "ap": 6, "mp": 3,
        "strength": 18, "agility": 8,  "initiative": 5,  "skills": ["husk_strike"], "xp": 18,
        "essence": "Seize", "drop": 6 },
      { "id": "marrow_spitter","name": "Marrow Spitter", "policy": "ranged",  "maxHp": 30, "ap": 6, "mp": 3,
        "strength": 14, "agility": 10, "initiative": 9,  "prefRangeMin": 3, "prefRangeMax": 6,
        "skills": ["marrow_spit"], "xp": 15, "essence": "Marrow Spit", "drop": 8 },
      { "id": "gravehound",    "name": "Gravehound",    "policy": "flanker", "maxHp": 36, "ap": 6, "mp": 5,
        "strength": 20, "agility": 16, "initiative": 15, "skills": ["grave_bite"], "xp": 22,
        "essence": "Pounce", "drop": 5 },
      { "id": "crypt_warden",  "name": "Crypt Warden",  "policy": "melee",   "maxHp": 72, "ap": 6, "mp": 2,
        "strength": 18, "agility": 10, "initiative": 6,  "skills": ["husk_strike", "warden_ironhide"], "xp": 26,
        "essence": "Ironhide", "drop": 5 },
      { "id": "grave_mite",    "name": "Grave Mite",    "policy": "flanker", "maxHp": 12, "ap": 6, "mp": 4,
        "strength": 8,  "agility": 12, "initiative": 11, "skills": ["mite_sap"], "xp": 6,
        "essence": "Sap", "drop": 10 },
      { "id": "bone_piper",    "name": "Bone Piper",    "policy": "support", "maxHp": 26, "ap": 6, "mp": 3,
        "strength": 10, "agility": 10, "initiative": 10, "prefRangeMin": 3, "prefRangeMax": 6,
        "skills": ["piper_gift"], "xp": 20, "essence": "Piper's Gift", "drop": 8 },
      { "id": "sexton",        "name": "The Sexton",    "policy": "melee",   "maxHp": 190, "ap": 8, "mp": 2,
        "strength": 22, "agility": 6,  "initiative": 7,  "skills": ["sexton_smash", "husk_strike"], "xp": 120,
        "essence": "Sexton's Toll", "drop": 100 }
    ]
    """;

    // Few skills, simple skills, tactical limits (Bible §3.1.2). effects use the engine's closed
    // set; cooldown / castsPerTurn exercise the Dofus limit fields.
    public const string SkillsJson = """
    [
      { "key": "piercing_shot", "name": "Piercing Shot", "ap": 3, "min": 4, "max": 8, "los": true,
        "effects": [ { "kind": "damage", "min": 11, "max": 16 } ] },
      { "key": "slam", "name": "Slam", "ap": 4, "min": 1, "max": 1, "los": true,
        "effects": [ { "kind": "damage", "min": 12, "max": 18 }, { "kind": "push", "cells": 1 } ] },
      { "key": "ruin_bolt", "name": "Ruin Bolt", "ap": 4, "min": 3, "max": 6, "los": true,
        "effects": [ { "kind": "damage", "min": 18, "max": 24 } ] },
      { "key": "husk_strike", "name": "Husk Strike", "ap": 3, "min": 1, "max": 1, "los": true,
        "effects": [ { "kind": "damage", "min": 12, "max": 18 } ] },
      { "key": "marrow_spit", "name": "Marrow Spit", "ap": 3, "min": 3, "max": 6, "los": true,
        "effects": [ { "kind": "damage", "min": 10, "max": 15 } ] },
      { "key": "grave_bite", "name": "Grave Bite", "ap": 3, "min": 1, "max": 1, "los": true,
        "effects": [ { "kind": "damage", "min": 16, "max": 24 } ] },
      { "key": "warden_ironhide", "name": "Ironhide", "ap": 2, "min": 0, "max": 0, "los": false, "cooldown": 2,
        "effects": [ { "kind": "status", "status": "shield", "mag": 8, "turns": 1 } ] },
      { "key": "mite_sap", "name": "Sap", "ap": 2, "min": 1, "max": 1, "los": true,
        "effects": [ { "kind": "damage", "min": 4, "max": 7 }, { "kind": "status", "status": "mpdrain", "mag": 1, "turns": 1 } ] },
      { "key": "piper_gift", "name": "Piper's Gift", "ap": 3, "min": 1, "max": 4, "los": true, "castsPerTurn": 1,
        "effects": [ { "kind": "grant_ap", "min": 2 } ] },
      { "key": "sexton_smash", "name": "Sexton's Toll", "ap": 5, "min": 1, "max": 1, "los": true, "cooldown": 2,
        "effects": [ { "kind": "damage", "min": 22, "max": 32 }, { "kind": "push", "cells": 2 } ] }
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

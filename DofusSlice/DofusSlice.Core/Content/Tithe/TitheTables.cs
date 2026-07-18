namespace DofusSlice.Core.Content.Tithe;

/// <summary>
/// TITHE data tables — the single source of truth for the prototype's classes, skills, mobs,
/// arena and encounter. Per the Slice Bible (§8) all rules and numbers live in data, never in
/// code; marketplace/engine code only renders and executes these rows. Numbers here are honest
/// hand-tuned placeholders (Bible "planes, cubes, honest data") flagged for the §9 Dofus-1.29
/// mining pass. Kept as JSON strings so the headless sim and the game consume identical data
/// with no file-path plumbing.
/// </summary>
public static class TitheTables
{
    // Three playable archetypes. policy = how the autobattler flies it (watched, never piloted).
    public const string ClassesJson = """
    [
      { "id": "archer",  "name": "Archer",  "policy": "skirmisher", "maxHp": 36, "ap": 6, "mp": 4,
        "strength": 22, "agility": 18, "initiative": 15, "prefRangeMin": 4, "prefRangeMax": 8,
        "skills": ["piercing_shot"], "blurb": "Keeps max distance, shoots the softest target in range." },
      { "id": "bulwark", "name": "Bulwark", "policy": "bruiser",    "maxHp": 78, "ap": 6, "mp": 3,
        "strength": 20, "agility": 12, "initiative": 8,  "prefRangeMin": 1, "prefRangeMax": 1,
        "skills": ["slam"], "blurb": "Advances, body-blocks, shoves with Slam." },
      { "id": "cannon",  "name": "Cannon",  "policy": "artillery",  "maxHp": 42, "ap": 7, "mp": 3,
        "strength": 30, "agility": 10, "initiative": 11, "prefRangeMin": 3, "prefRangeMax": 6,
        "skills": ["ruin_bolt"], "blurb": "Holds a safe sightline and nukes the priority target." }
    ]
    """;

    // Graveyard skins of the locked archetypes (Bible §5 bestiary).
    public const string MobsJson = """
    [
      { "id": "barrow_husk",   "name": "Barrow Husk",   "policy": "melee",   "maxHp": 54, "ap": 6, "mp": 3,
        "strength": 18, "agility": 8,  "initiative": 5,  "skills": ["husk_strike"], "xp": 18 },
      { "id": "marrow_spitter","name": "Marrow Spitter", "policy": "ranged",  "maxHp": 30, "ap": 6, "mp": 3,
        "strength": 14, "agility": 10, "initiative": 9,  "prefRangeMin": 3, "prefRangeMax": 6,
        "skills": ["marrow_spit"], "xp": 15 },
      { "id": "gravehound",    "name": "Gravehound",    "policy": "flanker", "maxHp": 36, "ap": 6, "mp": 5,
        "strength": 20, "agility": 16, "initiative": 13, "skills": ["grave_bite"], "xp": 22 }
    ]
    """;

    // Few skills, simple skills, tactical limits (Bible §3.1.2). effects use the engine's closed set.
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
        "effects": [ { "kind": "damage", "min": 15, "max": 22 } ] }
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
        { "mob": "gravehound",     "x": 8,  "y": 2 },
        { "mob": "gravehound",     "x": 8,  "y": 10 },
        { "mob": "barrow_husk",    "x": 10, "y": 4 },
        { "mob": "barrow_husk",    "x": 10, "y": 8 },
        { "mob": "barrow_husk",    "x": 12, "y": 6 },
        { "mob": "marrow_spitter", "x": 14, "y": 6 }
      ]
    }
    """;
}

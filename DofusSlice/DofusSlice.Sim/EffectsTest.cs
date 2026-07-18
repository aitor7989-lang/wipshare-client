using DofusSlice.Core.Combat;
using DofusSlice.Core.Content;
using DofusSlice.Core.Grid;
using DofusSlice.Core.Spells;

namespace DofusSlice.Sim;

/// <summary>
/// Deterministic self-tests for the combat effects the greedy auto-player never exercises
/// (pull, swap, steal, root, stabilize, reflect, regen, line AoE). Run with: `sim effects`.
/// </summary>
public static class EffectsTest
{
    private static int _pass, _fail;

    public static int Run()
    {
        Pull();
        Swap();
        StealAp();
        Rooted();
        Stabilized();
        Reflect();
        Regen();
        LineAoe();
        Tackle();
        Summon();
        DamagePreview();
        MapHardening();

        Console.WriteLine($"\n{_pass} passed, {_fail} failed.");
        return _fail == 0 ? 0 : 1;
    }

    private static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
        if (ok) _pass++; else _fail++;
    }

    private static Fighter Caster(CellCoord pos, params SpellDef[] spells) => new()
    {
        Id = "c", Name = "Caster", Team = Team.Player, PlayerControlled = true, MaxHp = 100, Hp = 100,
        BaseAp = 12, BaseMp = 6, Initiative = 100, Pos = pos, Spells = spells,
    };

    private static Fighter Foe(string id, CellCoord pos) => new()
    {
        Id = id, Name = "Foe" + id, Team = Team.Enemy, MaxHp = 100, Hp = 100,
        BaseAp = 6, BaseMp = 3, Initiative = 10, Pos = pos,
    };

    private static CombatEngine Engine(Fighter caster, params Fighter[] foes)
    {
        var all = new List<Fighter> { caster };
        all.AddRange(foes);
        var eng = new CombatEngine(new Battlefield(11, 9), all, new SystemRng(1));
        eng.Start(); // begins the caster (highest initiative) — gives it AP/MP
        return eng;
    }

    private static void Pull()
    {
        var spell = Spell(1, "Pull", 3, 1, 8, SpellEffect.Pull(3));
        var c = Caster(new(2, 4), spell);
        var t = Foe("t", new(6, 4));
        var eng = Engine(c, t);
        eng.TryCast(c, spell, new(6, 4));
        Check("Pull", t.Pos == new CellCoord(3, 4), $"target moved to {t.Pos} (expected (3,4), adjacent to caster)");
    }

    private static void Swap()
    {
        var spell = Spell(2, "Swap", 3, 1, 8, SpellEffect.Swap());
        var c = Caster(new(2, 4), spell);
        var t = Foe("t", new(6, 4));
        var eng = Engine(c, t);
        eng.TryCast(c, spell, new(6, 4));
        Check("Swap", c.Pos == new CellCoord(6, 4) && t.Pos == new CellCoord(2, 4),
            $"caster@{c.Pos}, target@{t.Pos} (expected swapped)");
    }

    private static void StealAp()
    {
        var spell = Spell(3, "StealAp", 3, 1, 8, SpellEffect.StealAp(3));
        var c = Caster(new(2, 4), spell);
        var t = Foe("t", new(6, 4));
        var eng = Engine(c, t);
        t.BeginTurn(); // give the target AP to steal
        int capBefore = c.CurrentAp, tgtBefore = t.CurrentAp;
        eng.TryCast(c, spell, new(6, 4));
        Check("StealAp", t.CurrentAp == tgtBefore - 3 && c.CurrentAp == capBefore - spell.ApCost + 3,
            $"target AP {tgtBefore}->{t.CurrentAp}, caster gained 3");
    }

    private static void Rooted()
    {
        var spell = Spell(4, "Root", 3, 1, 8, SpellEffect.ApplyStatus(StatusKind.Rooted, 1, 2));
        var c = Caster(new(2, 4), spell);
        var t = Foe("t", new(6, 4));
        var eng = Engine(c, t);
        t.BeginTurn();
        eng.TryCast(c, spell, new(6, 4));
        Check("Rooted", eng.MovementRange(t).Count == 0 && !eng.TryMove(t, new(6, 5)),
            "rooted target has no movement range and cannot move");
    }

    private static void Stabilized()
    {
        var spell = Spell(5, "Stab", 3, 1, 8, SpellEffect.ApplyStatus(StatusKind.Stabilized, 1, 2), SpellEffect.Push(3));
        var c = Caster(new(2, 4), spell);
        var t = Foe("t", new(6, 4));
        var eng = Engine(c, t);
        eng.TryCast(c, spell, new(6, 4)); // stabilize applied before push in the same cast
        Check("Stabilized", t.Pos == new CellCoord(6, 4), $"target stayed at {t.Pos} despite push (stabilized)");
    }

    private static void Reflect()
    {
        var reflectBuff = Spell(6, "Thorns", 0, 0, 0, SpellEffect.ApplyStatus(StatusKind.Reflect, 50, 3));
        var hit = Spell(7, "Hit", 3, 1, 8, SpellEffect.Damage(Element.Neutral, 20, 20));
        var c = Caster(new(2, 4), reflectBuff, hit);
        var t = Foe("t", new(6, 4));
        var eng = Engine(c, t);
        t.Statuses.Add(new StatusEffect(StatusKind.Reflect, 50, 3)); // target reflects 50%
        int hpBefore = c.Hp;
        eng.TryCast(c, hit, new(6, 4)); // 20 dmg -> 10 reflected back
        Check("Reflect", c.Hp == hpBefore - 10, $"caster took {hpBefore - c.Hp} reflected (expected 10)");
    }

    private static void Regen()
    {
        var spell = Spell(8, "Regen", 2, 0, 0, SpellEffect.ApplyStatus(StatusKind.Regen, 8, 3));
        var c = Caster(new(2, 4), spell);
        var t = Foe("t", new(6, 4));
        var eng = Engine(c, t);
        c.Hp = 50;
        eng.TryCast(c, spell, new(2, 4)); // self-buff regen
        eng.EndTurn();                     // regen ticks at end of the caster's turn
        Check("Regen", c.Hp == 58, $"caster healed to {c.Hp} (expected 58)");
    }

    private static void LineAoe()
    {
        var spell = new SpellDef
        {
            Id = 9, Name = "Line", ApCost = 4, MinRange = 2, MaxRange = 6,
            LineOnly = true, Area = AreaShape.Line(2),
            Effects = new[] { SpellEffect.Damage(Element.Neutral, 20, 20) },
        };
        var c = Caster(new(2, 4), spell);
        var a = Foe("a", new(4, 4));
        var b = Foe("b", new(5, 4));
        var d = Foe("d", new(6, 4));
        var eng = Engine(c, a, b, d);
        eng.TryCast(c, spell, new(4, 4)); // line extends along +x through all three
        Check("LineAoe", a.Hp < 100 && b.Hp < 100 && d.Hp < 100,
            $"line hit all three (HP {a.Hp}/{b.Hp}/{d.Hp})");
    }

    private static void Tackle()
    {
        var c = Caster(new(2, 4)); // no spells needed
        var locker = Foe("t", new(3, 4)); // adjacent enemy locks the caster
        var eng = Engine(c, locker);
        int mpAfterMoveIfUnlocked = c.BaseMp - 2; // moving 2 cells costs 2 MP
        eng.TryMove(c, new(2, 6)); // flee out of melee
        Check("Tackle", c.CurrentMp < mpAfterMoveIfUnlocked && c.CurrentAp < c.BaseAp,
            $"lost AP/MP leaving melee (AP {c.CurrentAp}/{c.BaseAp}, MP {c.CurrentMp})");
    }

    private static void Summon()
    {
        var spell = Spell(11, "Summon", 3, 1, 3, SpellEffect.Summon("boar"));
        var c = Caster(new(2, 4), spell);
        var t = Foe("t", new(8, 4));
        var eng = new CombatEngine(new Battlefield(11, 9), new[] { c, t }, new SystemRng(1),
            (kind, team, cell, id) => Bestiary.Create(kind, id, cell, team, isSummon: true));
        eng.Start();
        int before = eng.Fighters.Count;
        eng.TryCast(c, spell, new(3, 4)); // summon onto the free cell next to the caster
        var ally = eng.Fighters.FirstOrDefault(f => f.IsSummon);
        Check("Summon", eng.Fighters.Count == before + 1 && ally is { PlayerControlled: false }
                        && ally.Team == c.Team && ally.Pos == new CellCoord(3, 4),
            $"ally joined on team {ally?.Team} at {ally?.Pos}, roster {before}->{eng.Fighters.Count}");
    }

    private static void DamagePreview()
    {
        var spell = Spell(10, "Poke", 3, 1, 8, SpellEffect.Damage(Element.Neutral, 20, 30));
        var c = Caster(new(2, 4), spell);
        var t = Foe("t", new(5, 4));
        var eng = Engine(c, t);
        var est = eng.EstimateDamage(c, spell, new(5, 4));
        bool ok = est is (int min, int max) && min > 0 && min <= max;
        var empty = eng.EstimateDamage(c, spell, new(8, 8)); // no target there
        Check("Damage preview", ok && empty is null,
            $"estimate {est?.min}-{est?.max} on target, null on empty cell");
    }

    private static void MapHardening()
    {
        // No 'P': the loader must place the hero on a walkable, unclaimed cell (not (0,0) rock, not a mob cell).
        var noP = MapLoader.Parse("""{"legend":{"1":"boar"},"rows":["#1.","..."]}""");
        bool spawnOk = TileKindInfo.IsWalkable(noP.Tile(noP.PlayerSpawn.X, noP.PlayerSpawn.Y))
                       && noP.Enemies.All(e => e.cell != noP.PlayerSpawn);
        Check("Map no-P fallback", spawnOk, $"hero spawned at {noP.PlayerSpawn} on walkable, mob-free cell");

        // Null legend value: must not crash building the encounter.
        bool nullLegendOk;
        try
        {
            var m = MapLoader.Parse("""{"legend":{"1":null},"rows":["1P.."]}""");
            Encounter.FromMap(m, new SystemRng(1)).Start();
            nullLegendOk = true;
        }
        catch { nullLegendOk = false; }
        Check("Map null-legend", nullLegendOk, "null legend entry skipped, encounter built");

        // Oversized map is rejected (so the game falls back to the default instead of freezing).
        string bigRow = new string('.', 65);
        bool rejected;
        try { MapLoader.Parse($$"""{"rows":["{{bigRow}}"]}"""); rejected = false; }
        catch (FormatException) { rejected = true; }
        Check("Map size cap", rejected, "65-wide map rejected with FormatException");
    }

    private static SpellDef Spell(int id, string name, int ap, int min, int max, params SpellEffect[] effects) => new()
    {
        Id = id, Name = name, ApCost = ap, MinRange = min, MaxRange = max,
        NeedsTarget = effects.Any(e => e.Kind is EffectKind.Damage or EffectKind.Pull
            or EffectKind.Swap or EffectKind.StealAp or EffectKind.StealMp) &&
            !effects.Any(e => e.Status is StatusKind.Regen or StatusKind.Reflect),
        Effects = effects,
    };
}

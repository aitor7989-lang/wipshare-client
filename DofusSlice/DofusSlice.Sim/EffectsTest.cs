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
        StealRange();
        DefenseBuff();
        DamageBuffDebuff();
        Rooted();
        Stabilized();
        Reflect();
        Regen();
        LineAoe();
        Tackle();
        Summon();
        DamagePreview();
        BlinkEscape();
        BloodPact();
        ShiftForecast();
        MapHardening();
        TmxImport();

        Console.WriteLine($"\n{_pass} passed, {_fail} failed.");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>Blink (Temple exclusive): a meleed skirmisher with no MP can ONLY escape by
    /// teleporting — the policy must fly it out of the lock.</summary>
    private static void BlinkEscape()
    {
        var blink = new SpellDef
        {
            Id = 900, Name = "Blink", ApCost = 2, MinRange = 1, MaxRange = 2,
            RequiresLineOfSight = false, NeedsTarget = false, NeedsFreeCell = true, Cooldown = 30,
            Effects = new[] { SpellEffect.Teleport() },
        };
        var archer = new Fighter
        {
            Id = "a", Name = "Archer", Team = Team.Player, PlayerControlled = true,
            Policy = AiPolicy.Skirmisher, PreferredRangeMin = 4, PreferredRangeMax = 8,
            MaxHp = 40, Hp = 40, BaseAp = 6, BaseMp = 0, Initiative = 100,
            Pos = new(5, 4), Spells = new[] { blink },
        };
        var eng = new CombatEngine(new Battlefield(11, 9),
            new List<Fighter> { archer, Foe("f1", new(4, 4)), Foe("f2", new(6, 4)) }, new SystemRng(1));
        eng.Start();
        DofusSlice.Core.AI.Policy.TakeTurn(eng, archer);
        int dist = Math.Min(archer.Pos.DistanceTo(new(4, 4)), archer.Pos.DistanceTo(new(6, 4)));
        Check("Blink escape", dist > 1, $"meleed 0-MP skirmisher blinked to {archer.Pos} (dist {dist} from lockers)");
    }

    /// <summary>Blood Pact (Pass 3, honest AI): blood buys AP only when the AP buys a swing.
    /// With a 3-AP attack and 6 AP the pact's net +2 unlocks nothing — REFUSED, no scar.
    /// With 7 AP it unlocks a third swing into a 100-HP target — PAID, exactly once.</summary>
    private static void BloodPact()
    {
        SpellDef Pact() => new()
        {
            Id = 901, Name = "Blood Pact", ApCost = 1, MinRange = 0, MaxRange = 0,
            RequiresLineOfSight = false, NeedsTarget = false, MaxCastsPerTurn = 1,
            Effects = new[] { SpellEffect.SelfHpCost(10), SpellEffect.GrantAp(3) },
        };
        SpellDef Slam() => new()
        {
            Id = 902, Name = "Slam", ApCost = 3, MinRange = 1, MaxRange = 1,
            Effects = new[] { SpellEffect.Damage(Element.Earth, 6, 8) },
        };
        Fighter Bul(int ap) => new()
        {
            Id = "b", Name = "Bulwark", Team = Team.Player, PlayerControlled = true,
            Policy = AiPolicy.Bruiser,
            MaxHp = 100, Hp = 100, BaseAp = ap, BaseMp = 0, Initiative = 100,
            Pos = new(6, 4), Spells = new[] { Pact(), Slam() },
        };

        var b1 = Bul(6); // 2 slams now, still 2 with the pact -> the blood buys nothing
        var eng1 = new CombatEngine(new Battlefield(11, 9),
            new List<Fighter> { b1, Foe("f1", new(7, 4)) }, new SystemRng(1));
        eng1.Start();
        DofusSlice.Core.AI.Policy.TakeTurn(eng1, b1);
        Check("Blood Pact refusal", b1.Hp == 100,
            $"a pact that buys no extra swing is refused (HP stayed {b1.Hp})");

        var b2 = Bul(7); // 2 slams now, 3 with the pact, target far from overkill -> pay once
        var eng2 = new CombatEngine(new Battlefield(11, 9),
            new List<Fighter> { b2, Foe("f2", new(7, 4)) }, new SystemRng(1));
        eng2.Start();
        DofusSlice.Core.AI.Policy.TakeTurn(eng2, b2);
        Check("Blood Pact", b2.Hp == 90,
            $"paid 10 HP for the swing it unlocks (HP {b2.Hp}), one cast only");
    }

    /// <summary>ForecastShift honesty (the §7a fix): the AI's shove predictor must walk the victim's
    /// HP exactly as the live rules do — void/collision/spikes kill, but ember-style soft hazards only
    /// singe (floor at 1 HP, no-op at ≤2), so it can never bank an ember "kill" that can never land.</summary>
    private static void ShiftForecast()
    {
        Fighter Shover(CellCoord pos) => new()
        {
            Id = "c", Name = "Caster", Team = Team.Player, MaxHp = 100, Hp = 100,
            BaseAp = 12, BaseMp = 6, Initiative = 100, Pos = pos,
        };
        Fighter Victim(CellCoord pos, int hp) => new()
        {
            Id = "v", Name = "Victim", Team = Team.Enemy, MaxHp = Math.Max(hp, 1), Hp = hp,
            BaseAp = 6, BaseMp = 3, Initiative = 10, Pos = pos,
        };
        CombatEngine Eng(Battlefield f, Fighter c, Fighter v) =>
            new(f, new List<Fighter> { c, v }, new SystemRng(1));

        // 1) Ember drag is NEVER a kill. A 6-HP victim dragged across 3 ember graves (soft danger 2)
        //    walks 6 -> 4 -> 2 -> (no-op at <=2): 4 real damage, lands at 2, alive. The pre-fix code
        //    summed 3 x 2 = 6 and mispredicted a kill.
        {
            var f = new Battlefield(11, 9);
            var c = Shover(new(2, 4)); var v = Victim(new(3, 4), 6);
            var eng = Eng(f, c, v);
            eng.TileDanger = cell => cell.Y == 4 && cell.X is >= 4 and <= 6 ? 2 : 0;
            var fc = eng.ForecastShift(c, v, cells: 3, pull: false);
            Check("ForecastShift ember non-lethal",
                fc.Valid && !fc.IntoVoid && !fc.Kills && fc.Damage == 4 && fc.Landing == new CellCoord(6, 4),
                $"3 embers on 6 HP -> dmg {fc.Damage}, kills {fc.Kills}, land {fc.Landing} (expected 4, false, (6,4))");
        }

        // 2) A wall slam still kills: a 4-HP victim shoved into a wall takes the lethal collision.
        {
            var f = new Battlefield(11, 9);
            f.SetObstacle(new(5, 4));
            var c = Shover(new(2, 4)); var v = Victim(new(3, 4), 4);
            var fc = Eng(f, c, v).ForecastShift(c, v, cells: 3, pull: false);
            Check("ForecastShift wall-slam kill",
                fc.Valid && !fc.IntoVoid && fc.Kills && fc.Landing == new CellCoord(4, 4),
                $"slam into wall -> kills {fc.Kills}, land {fc.Landing} (expected true, (4,4))");
        }

        // 3) A big direct hit still can't let embers fake a kill: 10 HP, 6 pre-damage, then 3 embers —
        //    starts the walk at 4, singes 4 -> 2 -> (no-op), survives at 2.
        {
            var f = new Battlefield(11, 9);
            var c = Shover(new(2, 4)); var v = Victim(new(3, 4), 10);
            var eng = Eng(f, c, v);
            eng.TileDanger = cell => cell.Y == 4 && cell.X is >= 4 and <= 6 ? 2 : 0;
            var fc = eng.ForecastShift(c, v, cells: 3, pull: false, preDamage: 6);
            Check("ForecastShift preDamage floored embers",
                fc.Valid && !fc.Kills && fc.Damage == 2,
                $"10 HP, 6 direct + 3 embers -> dmg {fc.Damage}, kills {fc.Kills} (expected 2, false)");
        }

        // 4) The void is an instant kill (LethalVoid) — unless the victim is void-anchored, who instead
        //    just slams the void's edge and lives.
        {
            var f = new Battlefield(11, 9);
            f.SetHole(new(4, 4));
            var c = Shover(new(2, 4));
            var v = Victim(new(3, 4), 50);
            var eng = Eng(f, c, v); eng.LethalVoid = true;
            var fc = eng.ForecastShift(c, v, cells: 2, pull: false);

            var anchored = Victim(new(3, 4), 50); anchored.VoidAnchored = true;
            var eng2 = Eng(f, c, anchored); eng2.LethalVoid = true;
            var fa = eng2.ForecastShift(c, anchored, cells: 2, pull: false);
            Check("ForecastShift void plunge",
                fc.IntoVoid && fc.Kills && fc.Damage == 50 && !fa.IntoVoid && !fa.Kills,
                $"void plunge kills {fc.Kills}; anchored survives (intoVoid {fa.IntoVoid}, kills {fa.Kills})");
        }

        // 5) Spikes bite lethally but are SKIPPED for the hazard-immune; ember-style soft hazards singe
        //    everyone, immune or not (mirroring the game's ember tick, which ignores immunity).
        {
            var f = new Battlefield(11, 9);
            f.SetTile(new(4, 4), TileKind.Spikes);
            var c = Shover(new(2, 4));
            var mortal = Victim(new(3, 4), 5);
            var immune = Victim(new(3, 4), 5); immune.HazardImmune = true;
            var spikeMortal = Eng(f, c, mortal).ForecastShift(c, mortal, cells: 1, pull: false);
            var spikeImmune = Eng(f, c, immune).ForecastShift(c, immune, cells: 1, pull: false);

            var f2 = new Battlefield(11, 9);
            var immune2 = Victim(new(3, 4), 5); immune2.HazardImmune = true;
            var eng3 = Eng(f2, c, immune2);
            eng3.TileDanger = cell => cell == new CellCoord(4, 4) ? 2 : 0;
            var emberImmune = eng3.ForecastShift(c, immune2, cells: 1, pull: false);

            Check("ForecastShift spikes vs immunity",
                spikeMortal.Damage == 3 && spikeImmune.Damage == 0 && emberImmune.Damage == 2,
                $"spike hits mortal {spikeMortal.Damage}, spares immune {spikeImmune.Damage}; ember still singes immune {emberImmune.Damage} (expected 3/0/2)");
        }

        // 6) ORDER — collision lands BEFORE embers (ApplyShift applies the slam inline, then raises
        //    FighterPushed which ticks embers synchronously). A 6-HP enemy shoved onto an ember then
        //    into a wall takes collision 6->1 first; the ember then no-ops at 1 HP. NOT a kill.
        {
            var f = new Battlefield(11, 9);
            f.SetObstacle(new(5, 4));
            var c = Shover(new(2, 4)); var v = Victim(new(3, 4), 6);
            var eng = Eng(f, c, v);
            eng.TileDanger = cell => cell == new CellCoord(4, 4) ? 2 : 0;
            var fc = eng.ForecastShift(c, v, cells: 2, pull: false);
            Check("ForecastShift collision-before-ember",
                !fc.Kills && fc.Damage == 5 && fc.Landing == new CellCoord(4, 4),
                $"ember+wall on 6 HP -> dmg {fc.Damage}, kills {fc.Kills} (expected 5, false — collision first, ember no-ops)");
        }

        // 7) ORDER — embers land BEFORE spikes (FighterPushed embers run, THEN ApplyHazards spikes).
        //    A 4-HP victim pushed across an ember then a spike: ember 4->2, spike 2->0 = a real kill.
        //    (Spike-first would leave it at 1 and miss the kill.)
        {
            var f = new Battlefield(11, 9);
            f.SetTile(new(5, 4), TileKind.Spikes);
            var c = Shover(new(2, 4)); var v = Victim(new(3, 4), 4);
            var eng = Eng(f, c, v);
            eng.TileDanger = cell => cell == new CellCoord(4, 4) ? 2 : 0;
            var fc = eng.ForecastShift(c, v, cells: 2, pull: false);
            Check("ForecastShift ember-before-spike",
                fc.Kills && fc.Landing == new CellCoord(5, 4),
                $"ember+spike on 4 HP -> kills {fc.Kills}, land {fc.Landing} (expected true, (5,4) — ember then spike)");
        }

        // 8) SOFT-HAZARD IMMUNITY — a victim the game exempts from embers (the warm / EMBER PLATE avatar,
        //    which is NOT spike-immune) is spared the ember pass, so an over-counted ember can't feed the
        //    spike a kill the live rules deny. Same ember+spike path as (7) but SoftHazardImmune: ember
        //    skipped, spike 4->1, SURVIVES — matching TryEmberBurn's avatar exemption.
        {
            var f = new Battlefield(11, 9);
            f.SetTile(new(5, 4), TileKind.Spikes);
            var c = Shover(new(2, 4));
            var v = Victim(new(3, 4), 4); v.SoftHazardImmune = true;
            var eng = Eng(f, c, v);
            eng.TileDanger = cell => cell == new CellCoord(4, 4) ? 2 : 0;
            var fc = eng.ForecastShift(c, v, cells: 2, pull: false);
            Check("ForecastShift soft-hazard immunity",
                !fc.Kills && fc.Damage == 3,
                $"ember-exempt victim, ember+spike -> dmg {fc.Damage}, kills {fc.Kills} (expected 3, false — ember skipped, spike only)");
        }
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

    private static void StealRange()
    {
        var spell = Spell(30, "StealRange", 3, 1, 8, SpellEffect.StealRange(2, 2));
        var c = Caster(new(2, 4), spell);
        var t = Foe("t", new(6, 4));
        var eng = Engine(c, t);
        eng.TryCast(c, spell, new(6, 4));
        // The victim's spell range shrinks by 2, the caster's grows by 2 (same amount, same clock).
        var probe = Spell(31, "Probe", 2, 1, 8);
        Check("StealRange", c.RangeBonus == 2 && t.RangeBonus == -2
                && eng.EffectiveMaxRange(c, probe) == 10 && eng.EffectiveMaxRange(t, probe) == 6,
            $"caster range {c.RangeBonus:+#;-#;0}, victim {t.RangeBonus:+#;-#;0}; reach c={eng.EffectiveMaxRange(c, probe)} t={eng.EffectiveMaxRange(t, probe)}");
    }

    private static void DefenseBuff()
    {
        // Flat 20 neutral hit; a 50% armor buff halves it to 10, a 50% vulnerability lifts it to 30.
        var spell = Spell(32, "Hit20", 3, 1, 8, SpellEffect.Damage(Element.Neutral, 20, 20));
        var c = Caster(new(2, 4), spell);
        var armored = Foe("a", new(6, 4)); armored.Statuses.Add(new StatusEffect(StatusKind.DefenseBuff, 50, 2));
        var eng = Engine(c, armored);
        eng.TryCast(c, spell, new(6, 4));
        Check("DefenseBuff", armored.Hp == 90, $"armored foe took {100 - armored.Hp} of a 20 hit (expected 10)");

        var c2 = Caster(new(2, 4), spell);
        var weak = Foe("w", new(6, 4)); weak.Statuses.Add(new StatusEffect(StatusKind.Vulnerable, 50, 2));
        var eng2 = Engine(c2, weak);
        eng2.TryCast(c2, spell, new(6, 4));
        Check("Vulnerable", weak.Hp == 70, $"vulnerable foe took {100 - weak.Hp} of a 20 hit (expected 30)");
    }

    private static void DamageBuffDebuff()
    {
        // The caster's own +50% power and a −50% weaken on it net out on the same flat 20 hit.
        var spell = Spell(33, "Hit20b", 3, 1, 8, SpellEffect.Damage(Element.Neutral, 20, 20));
        var c = Caster(new(2, 4), spell); c.Statuses.Add(new StatusEffect(StatusKind.DamageBuff, 50, 2));
        var t = Foe("t", new(6, 4));
        var eng = Engine(c, t);
        eng.TryCast(c, spell, new(6, 4));
        Check("DamageBuff", t.Hp == 70, $"+50% power turned a 20 hit into {100 - t.Hp} (expected 30)");

        var c2 = Caster(new(2, 4), spell); c2.Statuses.Add(new StatusEffect(StatusKind.DamageDebuff, 50, 2));
        var t2 = Foe("t2", new(6, 4));
        var eng2 = Engine(c2, t2);
        eng2.TryCast(c2, spell, new(6, 4));
        Check("DamageDebuff", t2.Hp == 90, $"−50% weaken turned a 20 hit into {100 - t2.Hp} (expected 10)");
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
        // The bible's flattened tackle: a deterministic MP surcharge to leave melee —
        // max(0, (tackler AGI - mover AGI) / 10) — no rolls, and never any AP loss.
        var c = Caster(new(2, 4)); // Caster has AGI 0
        var locker = new Fighter
        {
            Id = "t", Name = "Foet", Team = Team.Enemy, MaxHp = 100, Hp = 100,
            BaseAp = 6, BaseMp = 3, Initiative = 10, Agility = 30, Pos = new(3, 4),
        };
        var eng = Engine(c, locker);
        int surcharge = eng.TackleSurcharge(c);            // (30 - 0) / 10 = 3
        var range = eng.MovementRange(c);
        bool priced = range.TryGetValue(new(2, 6), out int cost) && cost == 2 + surcharge;
        eng.TryMove(c, new(2, 6));                          // flee: 2 steps + surcharge
        Check("Tackle", surcharge == 3 && priced && c.CurrentMp == c.BaseMp - 5 && c.CurrentAp == c.BaseAp,
            $"surcharge {surcharge}, cost {cost} (2+{surcharge}), MP {c.CurrentMp}/{c.BaseMp}, AP untouched {c.CurrentAp}/{c.BaseAp}");
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

    private static void TmxImport()
    {
        // A minimal synthetic Tiled map (our own fixture): a water strip + player and boar spawns.
        const string tmx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <map version="1.10" orientation="orthogonal" width="4" height="3" tilewidth="32" tileheight="32">
         <tileset firstgid="1" name="watertiles-auto" tilewidth="32" tileheight="32" tilecount="4" columns="2"/>
         <layer id="1" name="ground" width="4" height="3">
          <data encoding="csv">
        0,1,1,0,
        0,1,1,0,
        0,0,0,0
        </data>
         </layer>
         <objectgroup name="spawns">
          <object id="1" type="player" x="0" y="0"/>
          <object id="2" type="boar" x="96" y="64"/>
         </objectgroup>
        </map>
        """;
        var map = TmxLoader.Parse(tmx);
        bool ok = map.Width == 4 && map.Height == 3
                  && map.Tile(1, 0) == TileKind.Water && map.Tile(0, 0) == TileKind.Grass
                  && map.PlayerSpawn == new CellCoord(0, 0)
                  && map.Enemies.Any(e => e.kind == "boar" && e.cell == new CellCoord(3, 2));
        Check("Tiled TMX import", ok,
            $"{map.Width}x{map.Height}, water@(1,0)={map.Tile(1, 0)}, player@{map.PlayerSpawn}, {map.Enemies.Count} mob(s)");
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
            or EffectKind.Swap or EffectKind.StealAp or EffectKind.StealMp or EffectKind.StealRange) &&
            !effects.Any(e => e.Status is StatusKind.Regen or StatusKind.Reflect),
        Effects = effects,
    };
}

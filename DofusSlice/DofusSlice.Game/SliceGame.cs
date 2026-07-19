using DofusSlice.Core.AI;
using DofusSlice.Core.Combat;
using DofusSlice.Core.Content;
using DofusSlice.Core.Content.Tithe;
using DofusSlice.Core.Grid;
using DofusSlice.Core.Spells;
using DofusSlice.Game.Animation;
using DofusSlice.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DofusSlice.Game;

/// <summary>
/// The playable slice. Presentation + input only — every rule lives in <see cref="CombatEngine"/>.
/// Two modes share one renderer: the original piloted Dofus fight, and TITHE's <b>watched</b>
/// combat (<see cref="_tithe"/>), where the whole crew and the skeleton pack act by AI policy and
/// the player's agency is placement + speed control (Slice Bible M1).
/// </summary>
public sealed class SliceGame : Microsoft.Xna.Framework.Game
{
    private readonly bool _tithe;
    private bool _boss;                              // TITHE: fight the Sexton's court instead of the pack
    private float _speed = 1f;                       // watched-mode playback: 1x / 2x / 4x
    private Fighter? _selCrew;                        // crew unit being positioned in placement
    private TitheResolution.Result? _aftermath;      // computed once the watched fight ends

    // ----- M2 campaign loop (City -> Graveyard -> Combat -> ...) ---------------------
    private enum Scene { Combat, City, Graveyard }
    private readonly bool _loop;                      // full campaign loop vs a one-off direct fight
    private Scene _scene = Scene.Combat;
    private Campaign _campaign = null!;
    private DiveSession? _dive;
    private IRng _diveRng = null!;
    private DiveSession.PackState? _pendingPack;      // the pack currently being fought
    private DiveSession.FightReport? _fightReport;    // the just-finished fight's result
    private bool _combatResolved;                     // ApplyResult already folded this fight in
    private List<(string name, int level)> _levelUps = new(); // level-ups from the last fight
    private bool _reportSounded;                      // the loot window's one-time stings
    private int _openNpc = -1;                        // which City building's panel is open
    private bool _equipOpen;                          // the stash & kit screen (E in the City)
    private int _equipUnit;                           // which crew member the kit screen shows
    private MapData _cityMap = null!, _graveMap = null!;
    private readonly Dictionary<string, CellCoord> _packCells = new(); // graveyard pack positions

    // Graveyard roaming: real click-to-move party + a level-gated Crypt entrance.
    private Battlefield _graveField = null!;
    private CellCoord _partyCell;
    private Vector2 _partyWorld;
    private readonly Queue<CellCoord> _partyPath = new();
    private DiveSession.PackState? _engageOnArrive;
    private bool _jumpedFight;                        // this fight began as an aggro-catch
    private float _huntTimer;                         // cadence of the hunting packs' steps
    private bool _hireOnArrive;                       // walking to the wandering survivor
    private static readonly CellCoord SurvivorCell = new(9, 7);
    private bool _cryptOnArrive, _cryptCleared, _cryptRun;
    private int _cryptRoom;
    private IReadOnlyList<TitheContent.CryptRoom> _cryptRooms = Array.Empty<TitheContent.CryptRoom>();
    private string _yardMsg = "";
    private float _yardMsgTimer;
    private static readonly CellCoord PartyStart = new(1, 6), CryptCell = new(13, 11);
    private const int CryptLevel = 3;
    private const int ScreenW = 1280;
    private const int ScreenH = 760;
    private const int TileW = 64;
    private const int TileH = 32;
    private const int HudTop = 600;
    private const float EnemyStepDelay = 0.55f;

    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _sb = null!;
    private Primitives _prim = null!;
    private PixelFont _font = null!;
    private IsoProjector _proj = null!;
    private ChamberSet _tiles = null!;                // legacy Chamber props (fallback tokens only)
    private UiSkin _ui = null!;                       // pixel UI panels/buttons (local-only art)
    private UiFont _dfont = null!;                    // baked dungeon font (local-only art)
    private Ui.GumHud _gum = null!;                   // Gum-editor HUD skin (ui/TitheHud.gumx)
    private Audio.SoundBank _sfx = null!;             // synthesized chiptune SFX (no asset files)
    private EwChrome _ew = null!;                     // Emberwick combat chrome (all procedural)
    private float _lastDiveClock = float.MaxValue;    // bell-toll edge detection
    private bool _pixSprites;                         // character animation packs present
    private bool Pix => _pixSprites;                  // sprites present -> full pixel dressing
    private SpriteBank _sprites = null!;
    private Camera2D _camera = null!;

    private CombatEngine _engine = null!;
    private MapData _map = null!;
    private BattleAnimator _anim = null!;
    private int _seed = 1;
    private readonly List<string> _log = new();
    private bool _enemyActed;
    private bool _placing;
    private float _time; // seconds, for tile/water animation

    private MouseState _mouse, _prevMouse;
    private KeyboardState _keys, _prevKeys;

    private int _selectedSpell = -1;
    private CellCoord _hover = CellCoord.Invalid;
    private Dictionary<CellCoord, int> _moveRange = new();
    private float _enemyTimer;

    // Per-turn countdown (Dofus-style): a turn auto-ends when the clock hits zero.
    private const float TurnSeconds = 30f;
    private float _turnClock = TurnSeconds;
    private string _turnOwner = "";

    private Rectangle[] _spellButtons = Array.Empty<Rectangle>();
    private Rectangle _endTurnButton;

    public SliceGame(bool tithe = false, int startSeed = 1, bool boss = false, bool loop = false)
    {
        _tithe = tithe;
        _seed = startSeed;
        _boss = boss;
        _loop = loop;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = ScreenW,
            PreferredBackBufferHeight = ScreenH,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = tithe ? "TITHE — The Graveyard (watched combat)" : "Dofus Slice — Incarnam Combat (Iop)";
    }

    private IReadOnlyList<SpellDef> HeroSpells => SpellLibrary.IopSpells;
    private Fighter? Hero => _engine.Fighters.FirstOrDefault(f => f.PlayerControlled);

    protected override void Initialize()
    {
        _endTurnButton = new Rectangle(1080, 636, 184, 104);

        if (_tithe) { base.Initialize(); return; } // watched mode: no spell bar / END TURN button

        // Fit the spell buttons in the space left of the END TURN button (adapts to spell count).
        int n = HeroSpells.Count;
        const int gap = 8, left = 16;
        int avail = _endTurnButton.X - 16 - left;
        int w = Math.Min(156, (avail - (n - 1) * gap) / Math.Max(1, n));
        _spellButtons = new Rectangle[n];
        for (int i = 0; i < n; i++)
            _spellButtons[i] = new Rectangle(left + i * (w + gap), 636, w, 104);
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _sb = new SpriteBatch(GraphicsDevice);
        _prim = new Primitives(GraphicsDevice, TileW, TileH, 64);
        _font = new PixelFont(_prim.Pixel);
        _sprites = new SpriteBank(GraphicsDevice);
        _tiles = new ChamberSet(_sprites);
        _ui = new UiSkin(_sprites);
        _dfont = new UiFont(_sprites);
        _pixSprites = _sprites.GetSheet("hero", "idle", "se") != null;
        _gum = new Ui.GumHud(this);
        _sfx = new Audio.SoundBank();
        _ew = new EwChrome(GraphicsDevice, _prim.Pixel);

        if (_loop)
        {
            _cityMap = MapLoader.Parse(TitheTables.CityMapJson);
            _graveMap = TitheContent.Arena();
            _diveRng = new SystemRng(_seed);
            _campaign = Campaign.NewGame("cannon");
            EnterCity();
        }
        else
        {
            _map = LoadMap();
            SetupView(_map);
            StartFight();
        }
    }

    /// <summary>Build the iso projector, animator and clamped camera for a scene's map.</summary>
    private void SetupView(MapData map)
    {
        // One projection for both skins: the classic 2:1 diamond grid, Dofus-style.
        _proj = IsoProjector.Centered(map.Width, map.Height, TileW, TileH,
            new Vector2(ScreenW / 2f, (HudTop / 2f) - 20));
        _anim = new BattleAnimator(_proj)
        {
            Sfx = (name, vol) => _sfx.Play(name, vol),
            // Corpses reuse the exact sheet + pixel height the fighter was drawn with.
            CorpseSpriteOf = f =>
            {
                var (sprite, _, scl) = PixActor(f.Archetype);
                var sheet = _sprites.GetSheet(sprite, "die", "se");
                float h = sheet != null ? sheet.FrameHeight * ChamberSet.PxScale * scl : 64f;
                return (sprite, h);
            },
        };

        _camera = new Camera2D(ScreenW, HudTop);
        var corners = new[]
        {
            _proj.CellCenter(0, 0), _proj.CellCenter(map.Width - 1, 0),
            _proj.CellCenter(0, map.Height - 1), _proj.CellCenter(map.Width - 1, map.Height - 1),
        };
        var min = new Vector2(corners.Min(c => c.X) - TileW, corners.Min(c => c.Y) - TileH * 2f);
        var max = new Vector2(corners.Max(c => c.X) + TileW, corners.Max(c => c.Y) + TileH);
        _camera.SetBounds(min, max);
        _camera.Center = (min + max) / 2f;
    }

    /// <summary>Load an external Tiled (.tmx) or JSON map if present, else the embedded default.</summary>
    private MapData LoadMap()
    {
        if (_tithe) return TitheContent.Arena(_seed);
        var dir = Path.Combine(AppContext.BaseDirectory, "maps");
        try
        {
            var tmx = Path.Combine(dir, "incarnam.tmx");
            if (File.Exists(tmx))
                return TmxLoader.Parse(File.ReadAllText(tmx), rel => File.ReadAllText(Path.Combine(dir, rel)));

            var json = Path.Combine(dir, "incarnam.json");
            if (File.Exists(json)) return MapLoader.Parse(File.ReadAllText(json));
        }
        catch { /* fall back to the embedded default on any parse/IO error */ }
        return Encounter.DefaultMap();
    }

    private void StartFight()
    {
        if (_tithe)
        {
            _engine = TitheContent.BuildFight(TitheContent.DefaultCrew, new SystemRng(_seed), _boss);
            _aftermath = null;
            _selCrew = _engine.Fighters.FirstOrDefault(f => f.Team == Team.Player);
        }
        else
        {
            // A map that parses but produces an unbuildable encounter degrades to the default.
            try
            {
                _engine = Encounter.FromMap(_map, new SystemRng(_seed));
            }
            catch
            {
                _map = Encounter.DefaultMap();
                _engine = Encounter.FromMap(_map, new SystemRng(_seed));
            }
        }
        _anim.Reset(_engine.Fighters);
        WireEngine();

        _selectedSpell = -1;
        _enemyTimer = 0f;
        _enemyActed = false;
        _turnClock = TurnSeconds;   // fresh clock so an R-restart never inherits a timed-out turn
        _turnOwner = "";
        _placing = true;            // place the crew before combat begins
    }

    /// <summary>Leave the placement phase and start the turn-based fight.</summary>
    private void BeginFight()
    {
        _sfx.Play("click");
        _placing = false;
        _engine.Start();
    }

    private void WireEngine()
    {
        _log.Clear();
        _engine.Logged += line =>
        {
            _log.Add(line);
            if (_log.Count > 8) _log.RemoveAt(0);
        };
        _engine.Emitted += _anim.OnEvent;
    }

    // ===== M2 campaign loop ==========================================================

    private static readonly CellCoord TitheCell = new(3, 4), TempleCell = new(6, 3),
                                       HireCell = new(9, 4), LychgateCell = new(6, 8);

    private void UpdateLoop(float dt)
    {
        _time += dt;
        _hover = _proj.ScreenToCell(_camera.ScreenToWorld(new Vector2(_mouse.X, _mouse.Y)));
        switch (_scene)
        {
            case Scene.City: UpdateCity(); break;
            case Scene.Graveyard: UpdateGraveyard(dt); break;
            default: UpdateCampaignCombat(dt); break;
        }
    }

    private void EnterCity()
    {
        _scene = Scene.City;
        _openNpc = -1;
        _dive = null;
        if (!_campaign.Over) _campaign.RestCrew();
        SetupView(_cityMap);
    }

    private void UpdateCity()
    {
        if (_campaign.Over)
        {
            if (Pressed(Keys.R)) { _diveRng = new SystemRng(++_seed); _campaign = Campaign.NewGame("cannon"); EnterCity(); }
            return;
        }
        if (Pressed(Keys.Escape)) { _openNpc = -1; _equipOpen = false; return; }
        if (Pressed(Keys.E)) { _equipOpen = !_equipOpen; _openNpc = -1; return; } // stash & kit
        if (Pressed(Keys.Enter) || Pressed(Keys.D)) { StartDive(); return; } // dive (also: click the Lychgate)
        if (!LeftClicked()) return;
        var m = new Point(_mouse.X, _mouse.Y);

        if (_equipOpen) { ClickEquipPanel(m); return; }

        if (_openNpc >= 0)
        {
            var acts = NpcActions(_openNpc);
            for (int i = 0; i < acts.Count; i++)
                if (PanelButton(i).Contains(m)) { if (acts[i].ok) { _sfx.Play("coin", 0.7f); acts[i].act(); } return; }
        }

        if (_hover == TitheCell) { _openNpc = 0; _sfx.Play("click"); }
        else if (_hover == TempleCell) { _openNpc = 1; _sfx.Play("click"); }
        else if (_hover == HireCell) { _openNpc = 2; _sfx.Play("click"); }
        else if (_hover == LychgateCell) StartDive();
        else _openNpc = -1;
    }

    private static Rectangle PanelButton(int i) => new(360, 344 + i * 52, 560, 44);

    // ----- The stash & kit screen (Bible §6.13: manage the stash and equip units) -----

    private static Rectangle EquipRowRect(int i) => new(260, 292 + i * 34, 372, 30);
    private static Rectangle StashRowRect(int i) => new(648, 292 + i * 34, 372, 30);

    /// <summary>Click an equipped row to strip it to the stash; click a stash row to equip it.
    /// Only the avatar re-gears (Bible §6.6.9 — mercs keep the kit they were hired with).</summary>
    private static readonly (string key, string label)[] StatRows =
    {
        ("vit", "VIT"), ("str", "STR"), ("int", "INT"), ("cha", "CHA"), ("agi", "AGI"), ("wis", "WIS"),
    };

    private static Rectangle EquipTabRect(int i) => new(260 + i * 130, 168, 124, 22);
    private static Rectangle StatPlusRect(int i) => new(260 + i * 126 + 96, 226, 20, 18);
    private static Rectangle AutoSpendRect => new(896, 226, 124, 18);

    private CampaignUnit? EquipShownUnit =>
        _campaign.Crew.Count == 0 ? null : _campaign.Crew[Math.Clamp(_equipUnit, 0, _campaign.Crew.Count - 1)];

    private void ClickEquipPanel(Point m)
    {
        for (int i = 0; i < _campaign.Crew.Count; i++)
            if (EquipTabRect(i).Contains(m)) { _equipUnit = i; _sfx.Play("click"); return; }

        var u = EquipShownUnit;
        if (u == null) return;

        if (u.StatPoints > 0)
        {
            for (int i = 0; i < StatRows.Length; i++)
                if (StatPlusRect(i).Contains(m)) { u.SpendStat(StatRows[i].key); _sfx.Play("click"); return; }
            if (AutoSpendRect.Contains(m)) { TitheContent.AutoSpendStats(u); _sfx.Play("coin"); return; }
        }

        // Only the avatar re-gears (Bible §6.10) — mercenaries keep their hire kit.
        if (!u.IsAvatar) return;
        for (int i = 0; i < u.Equipment.Count; i++)
            if (EquipRowRect(i).Contains(m)) { _campaign.Unequip(u, u.Equipment[i]); _sfx.Play("click"); return; }
        for (int i = 0; i < _campaign.Stash.Count; i++)
            if (StashRowRect(i).Contains(m)) { _campaign.Equip(u, _campaign.Stash[i]); _sfx.Play("coin"); return; }
    }

    private void DrawEquipPanel()
    {
        var a = EquipShownUnit;
        if (a == null) return;
        var r = new Rectangle(236, 150, 800, 470);
        _prim.FillRect(_sb, r, new Color(22, 24, 30));
        _prim.StrokeRect(_sb, r, 2, Palette.CurrentRing);
        var mp = new Point(_mouse.X, _mouse.Y);

        // Crew tabs: the kit screen manages every unit's points, gear stays avatar-only.
        for (int i = 0; i < _campaign.Crew.Count; i++)
        {
            var t = EquipTabRect(i);
            bool sel = i == Math.Clamp(_equipUnit, 0, _campaign.Crew.Count - 1);
            _prim.FillRect(_sb, t, sel ? Palette.HudPanelLight : (t.Contains(mp) ? new Color(40, 42, 50) : Palette.HudPanel));
            _prim.StrokeRect(_sb, t, 1, sel ? Palette.CurrentRing : new Color(60, 64, 72));
            _font.DrawCentered(_sb, Trunc(_campaign.Crew[i].Name.ToUpperInvariant(), 12), t.Center.X, t.Y + 7, 1,
                sel ? Palette.Text : Palette.TextDim);
        }

        // Level + XP bar + banked points.
        int need = CampaignUnit.XpForNextLevel(a.Level);
        _font.Draw(_sb, $"LVL {a.Level}", 260, r.Y + 48, 2, Palette.Text);
        _prim.FillRect(_sb, new Rectangle(340, r.Y + 50, 260, 10), Palette.HpBack);
        _prim.FillRect(_sb, new Rectangle(340, r.Y + 50, (int)(260 * Math.Clamp(need <= 0 ? 1f : (float)a.Xp / need, 0f, 1f)), 10), new Color(120, 170, 230));
        _font.Draw(_sb, $"{a.Xp} / {need} XP", 610, r.Y + 48, 1, Palette.TextDim);
        _font.Draw(_sb, a.StatPoints > 0 ? $"POINTS TO SPEND: {a.StatPoints}" : "NO POINTS BANKED",
            820, r.Y + 48, 1, a.StatPoints > 0 ? new Color(240, 208, 120) : Palette.TextDim);

        // The six characteristics with [+] spend buttons (1.29's manual allocation).
        for (int i = 0; i < StatRows.Length; i++)
        {
            var (key, label) = StatRows[i];
            int x = 260 + i * 126;
            int shown = key switch
            {
                "vit" => TitheContent.StatsOf(a).MaxHp,
                "str" => TitheContent.StatsOf(a).Strength,
                "int" => TitheContent.StatsOf(a).Intelligence,
                "cha" => TitheContent.StatsOf(a).Chance,
                "agi" => TitheContent.StatsOf(a).Agility,
                _ => TitheContent.StatsOf(a).Wisdom,
            };
            _font.Draw(_sb, key == "vit" ? $"HP {shown}" : $"{label} {shown}", x, r.Y + 80, 1, Palette.Text);
            if (a.StatPoints > 0)
            {
                var b = StatPlusRect(i);
                _prim.FillRect(_sb, b, b.Contains(mp) ? new Color(96, 170, 96) : new Color(52, 96, 52));
                _font.DrawCentered(_sb, "+", b.Center.X, b.Y + 5, 1, Color.White);
            }
        }
        if (a.StatPoints > 0)
        {
            _prim.FillRect(_sb, AutoSpendRect, AutoSpendRect.Contains(mp) ? Palette.HudPanelLight : Palette.HudPanel);
            _prim.StrokeRect(_sb, AutoSpendRect, 1, new Color(96, 150, 96));
            _font.DrawCentered(_sb, "AUTO-SPEND ALL", AutoSpendRect.Center.X, AutoSpendRect.Y + 5, 1, Palette.Text);
        }

        if (!a.IsAvatar)
        {
            _font.DrawCentered(_sb, "MERCENARIES KEEP THEIR HIRE KIT — ONLY THE AVATAR RE-GEARS",
                r.Center.X, 340, 1, Palette.TextDim);
            DrawEquipFooter(a, r);
            return;
        }

        _font.Draw(_sb, "EQUIPPED  (click to strip)", 260, r.Y + 122, 1, Palette.TextDim);
        _font.Draw(_sb, "STASH  (click to equip)", 648, r.Y + 122, 1, Palette.TextDim);

        for (int i = 0; i < a.Equipment.Count; i++)
        {
            var b = EquipRowRect(i);
            _prim.FillRect(_sb, b, b.Contains(mp) ? Palette.HudPanelLight : Palette.HudPanel);
            _prim.StrokeRect(_sb, b, 1, new Color(96, 150, 96));
            string id = a.Equipment[i];
            _font.Draw(_sb, $"{TitheContent.ItemSlot(id).ToUpperInvariant(),-7} {TitheContent.ItemName(id).ToUpperInvariant()}", b.X + 8, b.Y + 4, 1, Palette.Text);
            _font.Draw(_sb, TitheContent.ItemStatLine(id), b.X + 8, b.Y + 17, 1, Palette.TextDim);
        }
        if (a.Equipment.Count == 0)
            _font.Draw(_sb, "— nothing worn —", 268, 298, 1, Palette.TextDim);

        for (int i = 0; i < _campaign.Stash.Count; i++)
        {
            var b = StashRowRect(i);
            _prim.FillRect(_sb, b, b.Contains(mp) ? Palette.HudPanelLight : Palette.HudPanel);
            _prim.StrokeRect(_sb, b, 1, new Color(150, 140, 96));
            string id = _campaign.Stash[i];
            _font.Draw(_sb, $"{TitheContent.ItemSlot(id).ToUpperInvariant(),-7} {TitheContent.ItemName(id).ToUpperInvariant()}", b.X + 8, b.Y + 4, 1, Palette.Text);
            _font.Draw(_sb, TitheContent.ItemStatLine(id), b.X + 8, b.Y + 17, 1, Palette.TextDim);
        }
        if (_campaign.Stash.Count == 0)
            _font.Draw(_sb, "— the stash is empty —", 656, 298, 1, Palette.TextDim);

        DrawEquipFooter(a, r);
    }

    private void DrawEquipFooter(CampaignUnit a, Rectangle r)
    {
        // Live effective block, so every click's consequence is visible immediately (Dofus idiom).
        var s = TitheContent.StatsOf(a);
        var elem = TitheContent.ClassElement(a.ClassId);
        int set = TitheContent.SetPiecesEquipped(a, TitheContent.GraveyardSet);
        _font.DrawCentered(_sb,
            $"{s.MaxHp} HP   {elem.ToString().ToUpperInvariant()} {TitheContent.DamageStatFor(s, elem)}   AGI {s.Agility}   WIS {s.Wisdom}   POW {s.Power}"
            + (s.ApBonus != 0 ? $"   +{s.ApBonus} AP" : "") + (s.MpBonus != 0 ? $"   +{s.MpBonus} MP" : "")
            + $"   ADV {set}/7",
            r.Center.X, r.Bottom - 44, 1, new Color(240, 208, 120));
        _font.DrawCentered(_sb, "(E OR ESC TO CLOSE)", r.Center.X, r.Bottom - 22, 1, Palette.TextDim);
    }

    /// <summary>The clickable services at each City building (label, affordable, effect).</summary>
    private List<(string label, bool ok, Action act)> NpcActions(int npc)
    {
        var a = new List<(string, bool, Action)>();
        var P = TitheContent.Prices;
        switch (npc)
        {
            case 0: // the Tithe-Keeper
                if (_campaign.TitheDue)
                    a.Add(($"PAY THE TITHE  ({_campaign.TitheAmount}g)", _campaign.Gold >= _campaign.TitheAmount,
                           () => _campaign.PayTithe()));
                a.Add(($"BUY HARD BREAD  ({P.HardBread}g)   [have {_campaign.Bread}]", _campaign.Gold >= P.HardBread,
                       () => _campaign.BuyBread()));
                if (_campaign.Essences.Count > 0)
                    a.Add(($"SELL ESSENCE: {_campaign.Essences[0].ToUpperInvariant()}  (+{P.EssenceSell}g)", true,
                           () => _campaign.SellEssence(_campaign.Essences[0])));
                break;
            case 1: // the Temple Sister
                var w = _campaign.Crew.FirstOrDefault(u => u.Wounded);
                a.Add((w != null ? $"TREAT {w.Name.ToUpperInvariant()}'S WOUNDS  ({P.Draught}g)" : "NO ONE IS WOUNDED",
                       w != null && (_campaign.Draughts > 0 || _campaign.Gold >= P.Draught),
                       () => { if (_campaign.Draughts == 0) _campaign.BuyDraught(); if (w != null) _campaign.TreatWounded(w); }));
                // Essence consumption (Bible §6.5): teach the first held essence's skill to a unit
                // with a free slot. Learning is consumption; the slot is campaign-permanent.
                if (_campaign.Essences.Count > 0)
                {
                    string ess = _campaign.Essences[0];
                    foreach (var u in _campaign.Crew.Where(u => u.HasFreeEssenceSlot && !u.EssenceSlots.Contains(ess)).Take(2))
                        a.Add(($"TEACH {ess.ToUpperInvariant()} TO {u.Name.ToUpperInvariant()}  ({u.EssenceSlots.Count}/{CampaignUnit.MaxEssenceSlots} slots)",
                               true, () => _campaign.TeachEssence(u, ess)));
                }
                // The shelf (rotates each return): everything is for sale, at a painful price.
                string shelf = TitheContent.EssenceForSale(_campaign.Dives);
                a.Add(($"BUY {shelf.ToUpperInvariant()}  ({P.EssenceBuy}g)  — {TitheContent.EssenceSkillName(shelf).ToUpperInvariant()}",
                       _campaign.Gold >= P.EssenceBuy, () => _campaign.BuyEssence(shelf)));
                // Surgery: strip a filled slot — costly, and the essence is destroyed.
                var patient = _campaign.Crew.FirstOrDefault(u => u.EssenceSlots.Count > 0);
                if (patient != null)
                    a.Add(($"SURGERY: STRIP {patient.EssenceSlots[0].ToUpperInvariant()} FROM {patient.Name.ToUpperInvariant()}  ({P.EssenceRemoval}g, DESTROYED)",
                           _campaign.Gold >= P.EssenceRemoval,
                           () => _campaign.RemoveEssence(patient, patient.EssenceSlots[0])));
                // Vetting (Bible §6.12): reveal a survivor's hidden temperament for a fee.
                var suspect = _campaign.Crew.FirstOrDefault(u => u.Temperament != Temperament.None && !u.Vetted);
                if (suspect != null)
                    a.Add(($"VET {suspect.Name.ToUpperInvariant()}  ({P.VetFee}g) — READ THEIR NATURE",
                           _campaign.Gold >= P.VetFee,
                           () => { if (_campaign.Gold >= P.VetFee) { _campaign.Gold -= P.VetFee; suspect.Vetted = true; } }));
                break;
            default: // the Hiring Post
                int lvl = Math.Max(1, _campaign.Avatar?.Level ?? 1);
                int price = _campaign.HirePrice(lvl);
                foreach (var cls in new[] { "bulwark", "archer", "cannon" })
                    a.Add(($"HIRE A {cls.ToUpperInvariant()}  (L{lvl}, {price}g)",
                           _campaign.Crew.Count < 3 && _campaign.Gold >= price,
                           () => _campaign.Hire(cls, $"{cls}-merc", lvl)));
                break;
        }
        return a;
    }

    private void StartDive()
    {
        _dive = new DiveSession(_campaign, _diveRng);
        _scene = Scene.Graveyard;
        SetupView(_graveMap);
        AssignPackCells();

        _graveField = _graveMap.ToBattlefield();
        _partyCell = PartyStart;
        _partyWorld = _proj.CellCenter(PartyStart);
        _partyPath.Clear();
        _engageOnArrive = null; _cryptOnArrive = false; _cryptCleared = false; _cryptRun = false;
        _cryptRoom = 0; _yardMsg = ""; _yardMsgTimer = 0f; _huntTimer = 0f; _jumpedFight = false;
        _lastDiveClock = float.MaxValue;
        _graveMap = TitheContent.Arena(_campaign.Dives); // a fresh yard layout each dive
        _sfx.Play("bell", 0.9f, jitter: false);
        _hireOnArrive = false;
    }

    private static readonly CellCoord[] PackCells =
    {
        new(4, 3), new(6, 9), new(8, 5), new(10, 2), new(11, 10), new(13, 6),
    };

    private void AssignPackCells()
    {
        _packCells.Clear();
        var ordered = _dive!.Packs.OrderBy(p => p.Def.Reach).ToList();
        for (int i = 0; i < ordered.Count && i < PackCells.Length; i++)
            _packCells[ordered[i].Def.Id] = PackCells[i];
    }

    private void UpdateGraveyard(float dt)
    {
        if (_dive == null) { EnterCity(); return; }
        _dive.Tick(dt);
        if (_dive.Ended) { EnterCity(); return; }
        if (_yardMsgTimer > 0f) _yardMsgTimer -= dt;

        MovePartyAlongPath(dt);
        if (_dive.ConsumeDeparture() is { } dep) { _yardMsg = dep; _yardMsgTimer = 4f; } // the Grasping exit
        if (UpdateHunters(dt)) return; // a hunting pack may catch the crew mid-stride

        // Number keys walk the party to packs in reach order (a quick shortcut).
        var ordered = _dive.Packs.Where(p => !p.Cleared).OrderBy(p => p.Def.Reach).ToList();
        for (int i = 0; i < ordered.Count && i < 6; i++)
            if (Pressed(Keys.D1 + i)) { WalkToPack(ordered[i]); return; }

        if (!LeftClicked() || _mouse.Y >= HudTop) return;
        var target = _hover;
        if (!_graveField.InBounds(target)) return;

        foreach (var p in _dive.Packs)
            if (!p.Cleared && _packCells.TryGetValue(p.Def.Id, out var cell) && cell == target) { WalkToPack(p); return; }
        if (target == CryptCell) { WalkTo(CryptCell, null, crypt: true); return; }
        if (_dive.Survivor != null && target == SurvivorCell)
        { WalkTo(SurvivorCell, null, crypt: false); _hireOnArrive = true; return; }
        if (_graveMap.IsWalkable(target)) WalkTo(target, null, crypt: false);
    }

    // ----- Graveyard roaming --------------------------------------------------------

    private const int HuntAggroRadius = 6;    // cells; inside this a hunting pack starts closing
    private const float HuntStepSeconds = 2f; // real-time cadence of a hunter's step

    /// <summary>
    /// The hunting packs actually hunt (Bible §6.6 aggro): every couple of seconds, any uncleared
    /// "hunts" pack with the crew inside its aggro radius takes one step toward them; reaching
    /// adjacency is a CATCH — a Jumped fight, no placement, the pack already around the crew.
    /// Returns true if a fight started (the caller must stop processing the yard).
    /// </summary>
    private bool UpdateHunters(float dt)
    {
        _huntTimer += dt;
        bool step = _huntTimer >= HuntStepSeconds;
        if (step) _huntTimer = 0f;

        foreach (var p in _dive!.Packs.Where(p => !p.Cleared && p.Def.Hunts))
        {
            if (!_packCells.TryGetValue(p.Def.Id, out var cell)) continue;

            if (step && cell.DistanceTo(_partyCell) <= HuntAggroRadius && cell.DistanceTo(_partyCell) > 1)
            {
                var path = Pathfinding.FindPath(_graveField, cell, _partyCell,
                    c => _packCells.Values.Any(pc => pc == c), allowOccupiedGoal: true);
                if (path is { Count: > 1 } && path[1] != _partyCell)
                    _packCells[p.Def.Id] = cell = path[1];
            }

            if (cell.DistanceTo(_partyCell) <= 1) // caught — the yard was never safe
            {
                _partyPath.Clear(); _engageOnArrive = null; _cryptOnArrive = false;
                _cryptRun = false;
                BeginCombat(p, jumped: true);
                return true;
            }
        }
        return false;
    }

    private void WalkTo(CellCoord goal, DiveSession.PackState? engage, bool crypt)
    {
        var path = Pathfinding.FindPath(_graveField, _partyCell, goal, _ => false, allowOccupiedGoal: true);
        _partyPath.Clear();
        _engageOnArrive = engage;
        _cryptOnArrive = crypt;
        _hireOnArrive = false;
        if (path == null) return;
        foreach (var c in path.Skip(1)) _partyPath.Enqueue(c);
        if (_partyPath.Count == 0) ArriveAtTarget(); // already standing on it
    }

    private void WalkToPack(DiveSession.PackState p)
    {
        if (_packCells.TryGetValue(p.Def.Id, out var cell)) WalkTo(cell, p, crypt: false);
    }

    private void MovePartyAlongPath(float dt)
    {
        if (_partyPath.Count == 0) return;
        const float speed = 200f; // world px/sec
        var tgt = _proj.CellCenter(_partyPath.Peek());
        var delta = tgt - _partyWorld;
        float dist = delta.Length();
        if (dist <= speed * dt)
        {
            _partyWorld = tgt;
            _partyCell = _partyPath.Dequeue();
            if (_partyPath.Count == 0) ArriveAtTarget();
        }
        else _partyWorld += delta / dist * speed * dt;
    }

    private void ArriveAtTarget()
    {
        if (_engageOnArrive is { } p) { _engageOnArrive = null; EngagePack(p); }
        else if (_cryptOnArrive) { _cryptOnArrive = false; TryEnterCrypt(); }
        else if (_hireOnArrive)
        {
            _hireOnArrive = false;
            var offer = _dive!.Survivor;
            if (offer != null)
                _yardMsg = _dive.HireSurvivor()
                    ? $"The {offer.ClassId}-survivor falls in with the crew ({offer.Price}g). Their eyes are hard to read."
                    : _campaign.Crew.Count >= 3 ? "The crew is full — the survivor watches you pass."
                    : "You cannot afford the survivor's price.";
            _yardMsgTimer = 3f;
        }
    }

    private void TryEnterCrypt()
    {
        if (_cryptCleared) { _yardMsg = "The altar is spent — the Sexton is dead."; _yardMsgTimer = 2.5f; return; }
        int lvl = _campaign.Avatar?.Level ?? 1;
        if (lvl < CryptLevel) { _yardMsg = $"The crew is too green — reach level {CryptLevel} to enter the Crypt."; _yardMsgTimer = 3f; return; }

        _cryptRooms = TitheContent.CryptRooms();
        _cryptRun = true;
        _cryptRoom = 0;
        BeginCryptRoom();
    }

    /// <summary>Fight the current Crypt room (sealing-door chain; HP and the clock carry through).</summary>
    private void BeginCryptRoom()
    {
        var room = _cryptRooms[_cryptRoom];
        BeginCombat(new DiveSession.PackState { Def = new TitheContent.PackDef($"crypt_{_cryptRoom}", room.Comp, 0, false, room.Grade) });
    }

    private void EngagePack(DiveSession.PackState pack) { _cryptRun = false; BeginCombat(pack); }

    private void BeginCombat(DiveSession.PackState pack, bool jumped = false)
    {
        _engine = _dive!.BeginFight(pack, chargeTravel: false, jumped: jumped); // the walk was the travel cost
        _dive.InFight = true; // the bell keeps draining, but nobody walks out of a battle line
        _map = _graveMap;
        _pendingPack = pack;
        _combatResolved = false;
        _fightReport = null;
        _jumpedFight = jumped;
        SetupView(_graveMap);
        _anim.Reset(_engine.Fighters);
        WireEngine();
        _scene = Scene.Combat;
        _selCrew = _engine.Fighters.FirstOrDefault(f => f.Team == Team.Player);
        _selectedSpell = -1; _enemyTimer = 0f; _enemyActed = false; _turnClock = TurnSeconds; _turnOwner = "";
        // Jumped tier (Bible §6.6): caught in the open — no placement phase, the fight is already on.
        if (jumped) { _placing = false; _engine.Start(); }
        else _placing = true;
    }

    private void UpdateCampaignCombat(float dt)
    {
        if (Pressed(Keys.D1)) _speed = 1f;
        if (Pressed(Keys.D2)) _speed = 2f;
        if (Pressed(Keys.D3)) _speed = 4f;
        float sdt = dt * _speed;

        _anim.Update(sdt, _engine.Fighters);
        _camera.Shake(_anim.ConsumeShake());
        int scroll = _mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (scroll != 0) _camera.ZoomBy(scroll / 1200f);
        var follow = _engine.Current.IsAlive ? _anim.CenterFor(_engine.Current) : _camera.Center;
        _camera.Update(dt, follow);

        if (_placing) { UpdateTithePlacement(); return; }

        _dive?.Tick(dt); // the floor clock never pauses, even in a fight (Bible §3.1.3)

        if (_engine.Outcome != FightOutcome.Ongoing)
        {
            if (!_combatResolved && !_anim.IsBusy)
            {
                var preLevels = _campaign.Crew.ToDictionary(u => u.Id, u => u.Level);
                _fightReport = _dive!.ApplyResult(_pendingPack!, _engine);
                _levelUps = _campaign.Crew
                    .Where(u => preLevels.TryGetValue(u.Id, out int was) && u.Level > was)
                    .Select(u => (u.Name, u.Level)).ToList();
                _reportSounded = false;
                _combatResolved = true;
            }
            if (_combatResolved && (Pressed(Keys.Space) || Pressed(Keys.Enter) || LeftClicked()))
                AdvanceAfterCombat();
            return;
        }

        if (_engine.Current.Id != _turnOwner)
        {
            _turnOwner = _engine.Current.Id;
            _turnClock = TurnSeconds; _enemyTimer = 0f; _enemyActed = false;
        }
        UpdateWatchedTurn(sdt);
    }

    /// <summary>After a fight resolves: eject, chain to the next Crypt room, or return to the yard.</summary>
    private void AdvanceAfterCombat()
    {
        if (_dive!.Ended) { _cryptRun = false; EnterCity(); return; } // bell tolled / campaign over

        if (_cryptRun && _fightReport!.Outcome == FightOutcome.Victory)
        {
            if (_cryptRooms[_cryptRoom].Boss)
            {
                _cryptCleared = true; _cryptRun = false; // the altar tears the crew out of the Crypt
                _scene = Scene.Graveyard; SetupView(_graveMap);
            }
            else { _cryptRoom++; BeginCryptRoom(); }     // the next sealing door grinds open
            return;
        }

        _cryptRun = false;                               // a yard pack cleared
        _scene = Scene.Graveyard; SetupView(_graveMap);
    }

    // ----- Update -------------------------------------------------------------------

    protected override void Update(GameTime gameTime)
    {
        _prevMouse = _mouse; _mouse = Mouse.GetState();
        _prevKeys = _keys; _keys = Keyboard.GetState();
        UpdateGumHud(gameTime);
        if (Pressed(Keys.M)) _sfx.Muted = !_sfx.Muted;
        UpdateAmbient();

        if (_loop) { UpdateLoop((float)gameTime.ElapsedGameTime.TotalSeconds); base.Update(gameTime); return; }

        if (Pressed(Keys.R)) { _seed++; StartFight(); return; }

        // Watched-mode playback speed + encounter toggle.
        if (_tithe)
        {
            if (Pressed(Keys.D1)) _speed = 1f;
            if (Pressed(Keys.D2)) _speed = 2f;
            if (Pressed(Keys.D3)) _speed = 4f;
            if (Pressed(Keys.B)) { _boss = !_boss; StartFight(); return; } // swap pack <-> Sexton's court
        }

        _hover = _proj.ScreenToCell(_camera.ScreenToWorld(new Vector2(_mouse.X, _mouse.Y)));

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        float sdt = _tithe ? dt * _speed : dt; // scale the sim clock by playback speed
        _time += dt;
        _anim.Update(sdt, _engine.Fighters); // animations keep playing even after the fight ends

        // Camera: shake on hits, wheel zoom, follow the active fighter (clamped to the map).
        _camera.Shake(_anim.ConsumeShake());
        int scroll = _mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (scroll != 0) _camera.ZoomBy(scroll / 1200f);
        var follow = _engine.Current.IsAlive ? _anim.CenterFor(_engine.Current) : _camera.Center;
        _camera.Update(dt, follow);

        if (_placing) { UpdatePlacement(); base.Update(gameTime); return; }

        if (_engine.Outcome != FightOutcome.Ongoing)
        {
            // Once the last animation settles, resolve the aftermath (XP, Wounded, merc death).
            if (_tithe && _aftermath == null && !_anim.IsBusy) ResolveWatchedFight();
            base.Update(gameTime); return;
        }

        // A change of active fighter starts a fresh turn clock.
        if (_engine.Current.Id != _turnOwner)
        {
            _turnOwner = _engine.Current.Id;
            _turnClock = TurnSeconds;
            _enemyTimer = 0f;
            _enemyActed = false;
        }

        if (_tithe)
            UpdateWatchedTurn(sdt);        // every unit acts by AI policy
        else if (_engine.Current.PlayerControlled)
            UpdatePlayerTurn(dt);
        else
            UpdateEnemyTurn(dt); // enemies AND allied summons are AI-driven

        base.Update(gameTime);
    }

    private void UpdatePlacement()
    {
        if (_tithe) { UpdateTithePlacement(); return; }

        var hero = Hero;
        if (hero == null) { BeginFight(); return; }

        if (Pressed(Keys.Space)) { BeginFight(); return; }

        if (LeftClicked())
        {
            var m = new Point(_mouse.X, _mouse.Y);
            if (_endTurnButton.Contains(m)) { BeginFight(); return; } // "FIGHT!" button
            if (m.Y < HudTop && _map.PlayerStartCells.Contains(_hover) && _engine.FighterAt(_hover) is null)
                hero.Pos = _hover;
        }
    }

    /// <summary>Place the crew before a watched fight: click a member to select, a start cell to move it.</summary>
    private void UpdateTithePlacement()
    {
        if (Pressed(Keys.Space)) { BeginFight(); return; }

        if (LeftClicked())
        {
            var m = new Point(_mouse.X, _mouse.Y);
            if (_endTurnButton.Contains(m)) { BeginFight(); return; } // "FIGHT!" button
            if (m.Y >= HudTop) return;

            var onCell = _engine.FighterAt(_hover);
            if (_selCrew != null && onCell is { Team: Team.Player } other && other != _selCrew)
            {
                (other.Pos, _selCrew.Pos) = (_selCrew.Pos, other.Pos); // swap two crew members
                _selCrew = null;
                _sfx.Play("click");
            }
            else if (onCell is { Team: Team.Player })
            {
                _selCrew = onCell; // pick up a crew member
                _sfx.Play("click");
            }
            else if (_selCrew != null && _map.PlayerStartCells.Contains(_hover) && onCell is null)
            {
                _selCrew.Pos = _hover; // drop the selected member on a free start cell
                _selCrew = null;
                _sfx.Play("click");
            }
        }
    }

    private void UpdateWatchedTurn(float sdt)
    {
        // Run the unit's whole turn once (queuing its animations), then wait for them to finish
        // before advancing. Identical pacing to the mob driver — now every unit is AI-driven.
        if (!_enemyActed)
        {
            _enemyTimer += sdt;
            if (_enemyTimer < EnemyStepDelay) return;
            Policy.TakeTurn(_engine, _engine.Current);
            _enemyActed = true;
        }
        else if (!_anim.IsBusy)
        {
            _engine.EndTurn();
        }
    }

    /// <summary>Apply the fight's meta outcome once, and mark Wounded survivors with the status.</summary>
    private void ResolveWatchedFight()
    {
        _reportSounded = false; // the end-of-fight window plays the stings
        _aftermath = TitheResolution.Resolve(_engine);
        foreach (var u in _aftermath.Units.Where(u => u.Wounded))
        {
            var f = _engine.Fighters.First(x => x.Id == u.Id);
            f.Statuses.Add(new StatusEffect(StatusKind.MpDrain, 0, 99)); // pip marker for "Wounded"
        }
    }

    private void UpdateEnemyTurn(float dt)
    {
        // Run the mob's whole turn once (queuing its animations), then wait for them to
        // finish playing before advancing to the next fighter.
        if (!_enemyActed)
        {
            _enemyTimer += dt;
            if (_enemyTimer < EnemyStepDelay) return;
            MobBrain.TakeTurn(_engine, _engine.Current);
            _enemyActed = true;
        }
        else if (!_anim.IsBusy)
        {
            _engine.EndTurn();
        }
    }

    private void UpdatePlayerTurn(float dt)
    {
        var hero = Hero;
        if (hero == null) return;
        _moveRange = _engine.MovementRange(hero);

        // Hold input and the turn clock while an action is animating.
        if (_anim.IsBusy) return;

        // Turn timer: auto-end when it runs out.
        _turnClock -= dt;
        if (_turnClock <= 0f) { EndPlayerTurn(); return; }

        // Spell hotkeys.
        if (Pressed(Keys.D1)) ToggleSpell(0);
        if (Pressed(Keys.D2)) ToggleSpell(1);
        if (Pressed(Keys.D3)) ToggleSpell(2);
        if (Pressed(Keys.D4)) ToggleSpell(3);
        if (Pressed(Keys.D5)) ToggleSpell(4);
        if (Pressed(Keys.D6)) ToggleSpell(5);
        if (Pressed(Keys.D7)) ToggleSpell(6);
        if (Pressed(Keys.Escape)) _selectedSpell = -1;
        if (Pressed(Keys.Space)) { EndPlayerTurn(); return; }

        if (RightClicked()) _selectedSpell = -1;

        if (!LeftClicked()) return;
        var m = new Point(_mouse.X, _mouse.Y);

        for (int i = 0; i < _spellButtons.Length; i++)
            if (_spellButtons[i].Contains(m)) { ToggleSpell(i); return; }
        if (_endTurnButton.Contains(m)) { EndPlayerTurn(); return; }
        if (m.Y >= HudTop) return; // clicked empty HUD space

        if (_selectedSpell >= 0)
        {
            var spell = HeroSpells[_selectedSpell];
            if (_engine.CanCast(hero, spell, _hover, out _))
            {
                _engine.TryCast(hero, spell, _hover);
                if (!_engine.CanCast(hero, spell, _hover, out _)) _selectedSpell = -1;
            }
        }
        else if (_moveRange.ContainsKey(_hover))
        {
            _engine.TryMove(hero, _hover);
        }
    }

    private void ToggleSpell(int index)
    {
        if (index >= HeroSpells.Count) return;
        _selectedSpell = _selectedSpell == index ? -1 : index;
    }

    private void EndPlayerTurn()
    {
        if (_anim.IsBusy) return;
        _selectedSpell = -1;
        _engine.EndTurn();
        _enemyTimer = 0f;
    }

    private bool Pressed(Keys k) => _keys.IsKeyDown(k) && _prevKeys.IsKeyUp(k);
    private bool LeftClicked() => _mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
    private bool RightClicked() => _mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;

    // ----- Draw ---------------------------------------------------------------------

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Palette.Background);

        if (_loop && _scene == Scene.City) { DrawCity(); _gum.Draw(); base.Draw(gameTime); return; }
        if (_loop && _scene == Scene.Graveyard) { DrawGraveyard(); _gum.Draw(); base.Draw(gameTime); return; }

        DrawCombatScene();
        if (_loop) DrawDiveCombatOverlay();
        _gum.Draw();
        base.Draw(gameTime);
    }

    private void DrawCombatScene()
    {
        // World pass — everything on the map moves/zooms/shakes with the camera.
        _sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: _camera.View);
        DrawFloor();
        if (_placing) DrawPlacementCells(); else DrawFloorOverlays();
        if (_timelineHover is { IsAlive: true } tlf)   // timeline card hover -> ring its cell
            _prim.HaloAt(_sb, _proj.CellCenter(tlf.Pos) + new Vector2(0, 2), new Color(240, 220, 120));
        DrawEntities();                        // rocks + fighters, one depth-sorted pass
        _anim.DrawEffects(_sb, _prim, _font, _sprites);  // corpses, impact flashes, floating numbers
        _sb.End();

        // HUD pass — screen space, unaffected by the camera.
        _sb.Begin(samplerState: SamplerState.PointClamp);
        if (_placing)
        {
            DrawPlacementHud();
        }
        else
        {
            DrawHud();
            // Non-loop: hold the end screen until the final death/hit animation has played out.
            if (!_loop && _engine.Outcome != FightOutcome.Ongoing && !_anim.IsBusy) DrawEndOverlay();
        }
        _sb.End();
    }

    private IEnumerable<CellCoord> CellsByDepth() =>
        _engine.Field.AllCells().OrderBy(c => c.X + c.Y);

    private static bool IsObstacle(Battlefield f, CellCoord c) =>
        !f.IsWalkable(c) && f.BlocksLineOfSight(c);

    /// <summary>The flat ground: each cell's tile by kind (sprite, or a procedural fallback).</summary>
    private void DrawFloor()
    {
        foreach (var c in CellsByDepth())
            DrawTacticalCell(c, _engine.Field.TileAt(c), PixFamNow());
    }

    /// <summary>
    /// One 1.29 tactical-mode ground cell (TILESET-RULES §8): a flat two-tone diamond with a
    /// hairline seam, floating in black. Void and water cells simply are not drawn — holes in
    /// the map, exactly like the reference. Obstacle cells get their raised block later, in
    /// the depth-sorted entity pass, so units can stand behind them.
    /// </summary>
    private void DrawTacticalCell(CellCoord c, TileKind kind, PixFam fam)
    {
        if (kind is TileKind.Void or TileKind.Water) return;
        var center = _proj.CellCenter(c);
        var (light, dark) = TacticalTones(fam);
        _prim.DiamondAt(_sb, center, (c.X + c.Y) % 2 == 0 ? light : dark);
        DrawTileOutline(center, new Color(0, 0, 0, 30));
    }

    /// <summary>The tactical floor checker per family, sampled from the 1.29 references
    /// (Astrub ~(155,143,105)/(141,131,96)); the yard leans mossy, the crypt cold.</summary>
    private static (Color light, Color dark) TacticalTones(PixFam fam) => fam switch
    {
        PixFam.City => (new Color(155, 143, 105), new Color(141, 131, 96)),
        PixFam.Yard => (new Color(133, 133, 96), new Color(120, 121, 88)),
        _ => (new Color(129, 132, 122), new Color(117, 121, 113)),
    };

    /// <summary>An iso water tile drawn in our own style: a blue diamond with drifting ripples.</summary>
    private void DrawWater(CellCoord c, Vector2 center)
    {
        _prim.DiamondAt(_sb, center, new Color(46, 96, 156));
        _prim.DiamondAt(_sb, center + new Vector2(0, 2), new Color(38, 82, 138, 160));

        // A couple of highlight strokes that drift over time, offset per cell so it's not uniform.
        float phase = _time * 1.6f + (c.X * 0.9f + c.Y * 1.3f);
        for (int i = 0; i < 2; i++)
        {
            float t = phase + i * 1.7f;
            float ox = MathF.Sin(t) * (TileW * 0.16f);
            float oy = -3f + i * 5f + MathF.Cos(t * 0.7f) * 1.5f;
            var a = center + new Vector2(ox - 7, oy);
            var b = center + new Vector2(ox + 7, oy);
            _prim.Line(_sb, a, b, 1.5f, new Color(150, 200, 240, 150));
        }
    }

    private static Color FloorColor(TileKind k, CellCoord c) => k switch
    {
        TileKind.Grass2 => new Color(92, 116, 74),
        TileKind.Dirt or TileKind.Path => new Color(122, 98, 72),
        _ => ((c.X + c.Y) % 2 == 0) ? Palette.TileA : Palette.TileB,
    };

    /// <summary>An idle-animated pixel character for overworld tokens (no combat pose).</summary>
    private void DrawPixActorIdle(string sprite, Vector2 center, Color tint)
    {
        var sheet = _sprites.GetSheet(sprite, "idle", "se");
        var feet = center + new Vector2(0, TileH / 4f);
        if (sheet == null)
        {
            _tiles.Knight(_sb, "idle", (int)(_time * 3), feet, tint);
            return;
        }
        int frame = (int)(_time * 6 + (center.X * 0.13f)) % sheet.FrameCount;
        SpriteDraw.Feet(_sb, sheet, feet, tint, sheet.FrameHeight * ChamberSet.PxScale, frame);
    }

    // ----- Pixel UI chrome (local-only art; flat procedural fills when absent) --------

    private static readonly Color UiInk = new(72, 52, 40);      // dark text on cream panels
    private static readonly Color UiInkDim = new(126, 104, 86);
    private static readonly Color UiInkOnGreen = new(26, 54, 32);

    /// <summary>True when the pixel UI panels are available — text on them flips to dark ink.</summary>
    private bool UiSkinned => _ui.Loaded;

    private void UiPanelBg(Rectangle r)
    {
        if (_ui.Panel != null) { _ui.Panel.Draw(_sb, r, Color.White); return; }
        _prim.FillRect(_sb, r, new Color(22, 24, 30));
        _prim.StrokeRect(_sb, r, 2, Palette.CurrentRing);
    }

    private void UiButtonBg(Rectangle r, bool down, Color? tint = null)
    {
        var slice = down ? _ui.ButtonDown ?? _ui.Button : _ui.Button;
        if (slice != null) { slice.Draw(_sb, r, tint ?? Color.White); return; }
        _prim.FillRect(_sb, r, down ? Palette.HudPanelLight : Palette.HudPanel);
        _prim.StrokeRect(_sb, r, 2, Palette.HpFill);
    }

    /// <summary>Headline text (plain chunky PixelFont — the blackletter experiment is retired).</summary>
    private void UiTitle(string text, int x, int y, Color color) =>
        _font.Draw(_sb, text, x, y, 3, color);

    private void DrawTileOutline(Vector2 center, Color color)
    {
        var top = center + new Vector2(0, -TileH / 2f);
        var right = center + new Vector2(TileW / 2f, 0);
        var bottom = center + new Vector2(0, TileH / 2f);
        var left = center + new Vector2(-TileW / 2f, 0);
        _prim.Line(_sb, top, right, 1f, color);
        _prim.Line(_sb, right, bottom, 1f, color);
        _prim.Line(_sb, bottom, left, 1f, color);
        _prim.Line(_sb, left, top, 1f, color);
    }

    // ----- 8-bit tileset terrain (assets/tileset.png present) -------------------------

    private enum PixFam { City, Yard, Crypt }

    /// <summary>Which tile family the current scene wears: purple room, mossy yard, blue crypt.</summary>
    private PixFam PixFamNow() => !_loop ? PixFam.Yard
        : _scene == Scene.City ? PixFam.City
        : _scene == Scene.Combat && _cryptRun ? PixFam.Crypt
        : PixFam.Yard;

    private static int PixHash(CellCoord c) => (c.X * 73856093 ^ c.Y * 19349663) & 0x7fffffff;

    /// <summary>The scene tint: one stone family ships, so families differ by tint (§5.3).</summary>
    private static Color PixTint(PixFam fam) => fam switch
    {
        PixFam.City => Ch.CityTint,
        PixFam.Yard => Ch.YardTint,
        _ => Ch.CryptTint,
    };



    /// <summary>
    /// Character sheet + tint + integer scale per archetype (TILESET-RULES §7): the crew are
    /// the pack's humans, the dead wear the orc and slime in rust/bone tints — a level of
    /// abstraction the watched fight reads instantly. Scale multiplies the base 2× density.
    /// </summary>
    private static (string sprite, Color tint, int scale) PixActor(string archetype) => archetype switch
    {
        // archer = the pack's bowman (Soldier's Attack03 IS a bow shot); bulwark = the
        // sword-and-shield hero (a tank's silhouette); cannon = the hero re-forged in
        // ember reds so the two never read as the same person.
        "archer" => ("archer", Color.White, 1),
        "bulwark" => ("hero", Color.White, 1),
        "cannon" => ("cannon", Color.White, 1),
        "barrow_husk" => ("orc", new Color(212, 186, 158), 1),
        "gravehound" => ("slime", new Color(206, 128, 108), 1),
        "marrow_spitter" => ("slime", new Color(196, 146, 192), 1),
        "grave_mite" => ("slime", new Color(176, 176, 122), 1),
        "bone_piper" => ("orc", new Color(222, 206, 152), 1),
        "tomb_wraith" => ("slime", new Color(170, 210, 230), 1),
        "grave_ghoul" => ("orc", new Color(160, 190, 140), 1),
        "crypt_warden" => ("soldier", new Color(142, 160, 198), 1),
        "sexton" => ("orc", new Color(140, 120, 146), 2),
        _ => ("hero", Color.White, 1),
    };

    private void DrawFloorOverlays()
    {
        if (_engine.Outcome != FightOutcome.Ongoing) return;

        // The AI's visible thinking: turn ring, movement path, spell range + target, drawn
        // under the units exactly when the animator replays that beat.
        foreach (var (cell, color) in _anim.TelegraphCells)
            if (_engine.Field.InBounds(cell))
                _prim.DiamondAt(_sb, _proj.CellCenter(cell), color);

        // Always mark the active fighter's cell so the turn reads on the board.
        if (_engine.Current.IsAlive)
            _prim.DiamondAt(_sb, _anim.CenterFor(_engine.Current), Palette.CurrentRing * 0.28f);

        if (_tithe) return;       // watched combat isn't piloted — no move/spell range hints

        if (_anim.IsBusy) return; // hide range hints mid-action

        var hero = Hero;
        bool playerTurn = _engine.Current.PlayerControlled && hero != null;

        if (playerTurn && _selectedSpell < 0)
        {
            foreach (var cell in _moveRange.Keys)
                _prim.DiamondAt(_sb, _proj.CellCenter(cell), Palette.MoveRange);

            // Hover preview: MP cost to reach the hovered cell.
            if (_moveRange.TryGetValue(_hover, out int cost))
                DrawCellLabel($"{cost} MP", _hover, Palette.MpPip);
        }

        if (playerTurn && _selectedSpell >= 0)
        {
            var spell = HeroSpells[_selectedSpell];
            foreach (var cell in _engine.SpellReachCells(hero!, spell))
                _prim.DiamondAt(_sb, _proj.CellCenter(cell), Palette.CastReach);

            var castable = _engine.CastableCells(hero!, spell);
            foreach (var cell in castable)
                _prim.DiamondAt(_sb, _proj.CellCenter(cell), Palette.CastRange);

            if (castable.Contains(_hover))
            {
                foreach (var cell in _engine.AreaCells(spell, _hover, hero!.Pos))
                    _prim.DiamondAt(_sb, _proj.CellCenter(cell), Palette.Aoe);

                // Hover preview: estimated damage to the target on the hovered cell.
                if (_engine.EstimateDamage(hero!, spell, _hover) is (int min, int max))
                    DrawCellLabel(min == max ? $"{min}" : $"{min}-{max}", _hover, new Color(255, 210, 120));
            }
        }

        if (_engine.Field.InBounds(_hover) && _hover.Y >= 0)
            DrawTileOutline(_proj.CellCenter(_hover), Color.White);
    }

    private void DrawPlacementCells()
    {
        // 1.29 shows both teams' ground: red for your side, blue under the enemy.
        foreach (var cell in _map.PlayerStartCells)
            if (_engine.FighterAt(cell) is null)
                _prim.DiamondAt(_sb, _proj.CellCenter(cell), new Color(196, 44, 22) * 0.55f);
        foreach (var f in _engine.Fighters.Where(x => x.IsAlive && x.Team != Team.Player))
            _prim.DiamondAt(_sb, _proj.CellCenter(f.Pos), new Color(96, 104, 190) * 0.5f);

        if (_engine.Field.InBounds(_hover) && _hover.Y >= 0)
            DrawTileOutline(_proj.CellCenter(_hover), Color.White);
    }

    private void DrawPlacementHud()
    {
        UiTitle("PLACEMENT", 16, 12, Palette.Text);
        if (_tithe)
        {
            _font.Draw(_sb, "CLICK A CREW MEMBER, THEN A RED CELL TO POSITION THEM", 16, 40, 1, Palette.TextDim);
            _font.Draw(_sb, "PLACE THE SQUISHY BACKLINE SAFE FROM THE FLANKING GRAVEHOUNDS", 16, 54, 1, Palette.TextDim);
        }
        else
        {
            _font.Draw(_sb, "CLICK A RED CELL TO POSITION YOUR IOP", 16, 40, 1, Palette.TextDim);
            _font.Draw(_sb, "THEN PRESS FIGHT (OR SPACE) TO BEGIN", 16, 54, 1, Palette.TextDim);
        }
        DrawTurnTimeline(); // preview the fighters you'll face

        _ew.Panel(_sb, new Rectangle(-6, HudTop + 2, ScreenW + 12, ScreenH - HudTop + 16));
        if (_tithe) DrawCrewRoster();
        _font.DrawCentered(_sb, _tithe ? "PLACE YOUR CREW, THEN PRESS FIGHT — THEN WATCH"
                                       : "POSITION YOUR HERO ON A BLUE STARTING CELL, THEN FIGHT",
            ScreenW / 2, _tithe ? HudTop + 14 : HudTop + 60, 2, Palette.Text);

        var r = _endTurnButton;
        bool hover = r.Contains(new Point(_mouse.X, _mouse.Y));
        var pill = new Rectangle(r.X, r.Y + 20, r.Width, 56);
        _ew.Pill(_sb, pill, gold: true, pressed: hover);
        _font.DrawCentered(_sb, "FIGHT!", pill.Center.X, pill.Y + 16, 3, Color.White);
        _font.DrawCentered(_sb, "(SPACE)", r.Center.X, pill.Bottom + 10, 1, Ew.InkSoft);
        DrawHoverUnitInfo();
    }

    /// <summary>Draw a small centered label floating just above a cell (world space).</summary>
    private void DrawCellLabel(string text, CellCoord cell, Color color)
    {
        var p = _proj.CellCenter(cell) + new Vector2(0, -26);
        int w = _font.Measure(text, 2);
        _prim.FillRect(_sb, new Rectangle((int)p.X - w / 2 - 3, (int)p.Y - 2, w + 6, 15), new Color(0, 0, 0, 170));
        _font.DrawCentered(_sb, text, (int)p.X, (int)p.Y, 2, color);
    }

    /// <summary>
    /// Rocks and fighters in a single pass sorted by feet (base) screen-Y, so a taller
    /// sprite standing on a nearer cell correctly occludes whatever is behind it. This is the
    /// fix for the height-sorting issue: props and characters share one depth order instead
    /// of drawing all tiles then all fighters.
    /// </summary>
    private void DrawEntities()
    {
        var items = new List<(float depth, int tie, Action draw)>();

        foreach (var c in _engine.Field.AllCells())
            if (IsObstacle(_engine.Field, c))
            {
                var cell = c;
                items.Add((_proj.CellCenter(cell).Y, 0, () => DrawObstacle(cell)));
            }

        // The engine kills instantly; the replay doesn't. Anyone whose death hasn't replayed
        // yet (StillShown) keeps standing until the killing blow actually lands on screen.
        foreach (var f in _engine.Fighters.Where(x => x.IsAlive || _anim.StillShown(x.Id)))
        {
            var fighter = f;
            items.Add((_anim.CenterFor(fighter).Y, 1, () => DrawFighter(fighter)));
        }

        foreach (var it in items.OrderBy(i => i.depth).ThenBy(i => i.tie))
            it.draw();
    }

    private void DrawObstacle(CellCoord c) => DrawObstacleKind(_proj.CellCenter(c), _engine.Field.TileAt(c));

    /// <summary>
    /// Obstacles are slightly ELEVATED tiles, 1.29-tactical style (TILESET-RULES §8): the
    /// cell's diamond lifted by a fixed extrusion with two darker faces below. Trees keep a
    /// mossy top so the two blocker kinds stay distinguishable at a glance.
    /// </summary>
    private void DrawObstacleKind(Vector2 center, TileKind kind)
    {
        var (light, _) = TacticalTones(PixFamNow());
        var top = kind == TileKind.Tree
            ? new Color(light.R * 52 / 100, light.G * 62 / 100, light.B * 46 / 100)
            : new Color(light.R * 62 / 100, light.G * 62 / 100, light.B * 62 / 100);
        var faceL = new Color(top.R * 68 / 100, top.G * 68 / 100, top.B * 68 / 100);
        var faceR = new Color(top.R * 84 / 100, top.G * 84 / 100, top.B * 84 / 100);
        _prim.BlockAt(_sb, center, top, faceL, faceR);
    }

    // Fallback vector tree when no tile_tree sprite is present: shadow, trunk, three foliage tiers.
    private void DrawProceduralTree(Vector2 center)
    {
        _prim.DiscAt(_sb, center + new Vector2(0, 2), 11, Palette.Shadow);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 3, (int)center.Y - 22, 6, 24), new Color(96, 66, 42));
        _prim.DiscAt(_sb, center + new Vector2(0, -22), 12, new Color(52, 104, 60));
        _prim.DiscAt(_sb, center + new Vector2(0, -32), 11, new Color(64, 122, 70));
        _prim.DiscAt(_sb, center + new Vector2(0, -42), 9, new Color(78, 140, 84));
        _prim.DiscAt(_sb, center + new Vector2(-3, -44), 4, new Color(96, 160, 100));
    }

    private const float AnimFps = 10f;

    private void DrawFighter(Fighter f)
    {
        if (_tithe) { DrawTitheToken(f); return; }

        var center = _anim.CenterFor(f);
        float flash = _anim.FlashAmount(f.Id);

        // Ground shadow.
        _prim.DiscAt(_sb, center + new Vector2(0, 2), 12, Palette.Shadow);

        string name = f.PlayerControlled ? "iop" : f.Name.ToLowerInvariant();
        var pose = _anim.PoseFor(f);
        string state = pose.State switch
        {
            AnimState.Walk => "walk",
            AnimState.Cast => "cast",
            AnimState.Hurt => "hurt",
            _ => "idle",
        };
        var sheet = _sprites.GetSheet(name, state, pose.Dir.ToKey());

        float topY;
        if (sheet != null)
        {
            var tint = flash > 0f ? Color.Lerp(Color.White, new Color(255, 90, 90), flash) : Color.White;
            float h = TileH * 2.4f;
            SpriteDraw.Feet(_sb, sheet, center + new Vector2(0, 4), tint, h, FrameIndex(pose, sheet));
            topY = center.Y + 4 - h;
        }
        else
        {
            var head = center + new Vector2(0, -16);
            var body = f.PlayerControlled ? Palette.HeroColor : Palette.CreatureColor(f.Name);
            if (flash > 0f) body = Color.Lerp(body, new Color(255, 80, 80), flash);
            _prim.DiscAt(_sb, head, 15, new Color(20, 20, 24));
            _prim.DiscAt(_sb, head, 13, body);
            _prim.DiscAt(_sb, head + new Vector2(0, -3), 8, body * 1.15f); // subtle head highlight
            topY = head.Y - 15;
        }

        DrawHpBar(f, center.X, topY - 10);
        DrawStatusPips(f, center.X, topY - 2);
    }

    /// <summary>Prototype "honest token" for a TITHE unit: a coloured pawn with a class glyph.</summary>
    private void DrawTitheToken(Fighter f)
    {
        var center = _anim.CenterFor(f);
        float flash = _anim.FlashAmount(f.Id);
        var col = TitheTokenColor(f.Archetype);
        if (flash > 0f) col = Color.Lerp(col, new Color(255, 80, 80), flash);
        bool crew = f.Team == Team.Player;
        var outline = new Color(16, 16, 20);
        float baseY = center.Y;
        float s = f.Archetype == "sexton" ? 1.7f : 1f;      // the boss looms larger than its court
        int Sz(float v) => (int)MathF.Round(v * s);

        if (Pix)
        {
            if (_placing && f == _selCrew)                  // highlight the crew member being placed
                _prim.DiamondAt(_sb, center, new Color(245, 224, 120) * 0.35f);
            // 1.29 rule: no UI above heads. The only marker is the team halo at the feet —
            // red for the crew, blue for the dead — brighter while it is this unit's turn.
            var halo = crew ? new Color(214, 40, 22) : new Color(64, 92, 208);
            bool active = !_placing && _engine.Outcome == FightOutcome.Ongoing && f == _engine.Current;
            _prim.HaloAt(_sb, center + new Vector2(0, 2), halo * (active ? 1f : 0.62f));
            var pose = _anim.PoseFor(f);
            string state = pose.State switch
            {
                AnimState.Walk => "walk",
                AnimState.Cast => "cast",
                AnimState.Hurt => "hurt",
                _ => "idle",
            };
            var (spriteName, stint, scl) = PixActor(f.Archetype);
            if (flash > 0f) stint = Color.Lerp(stint, new Color(255, 90, 90), flash);
            var sheet = _sprites.GetSheet(spriteName, state, pose.Dir.ToKey());
            var feet = center + new Vector2(0, TileH / 4f);
            if (sheet != null)
            {
                // Integer pixel scale: frame height × the skin's 2× density × archetype scale.
                float hpx = sheet.FrameHeight * ChamberSet.PxScale * scl;
                SpriteDraw.Feet(_sb, sheet, feet, stint, hpx, FrameIndex(pose, sheet));
            }
            else
            {
                _tiles.Knight(_sb, state == "cast" ? "use" : state, (int)(_time * 3), feet, stint,
                    pose.Dir is Facing4.Sw or Facing4.Nw, scl);
            }
            return; // no HP bar, no pips — hover the unit for name and health (1.29 style)
        }

        _prim.DiscAt(_sb, center + new Vector2(0, 2), Sz(12), Palette.Shadow);
        if (_placing && f == _selCrew)                      // highlight the crew member being placed
            _prim.DiscAt(_sb, center + new Vector2(0, 2), 16, new Color(245, 224, 120) * 0.45f);

        _prim.DiscAt(_sb, new Vector2(center.X, baseY), Sz(11), outline);        // base
        _prim.DiscAt(_sb, new Vector2(center.X, baseY), Sz(9), col * 0.7f);
        _prim.FillRect(_sb, new Rectangle((int)center.X - Sz(9), (int)baseY - Sz(22), Sz(18), Sz(22)), outline); // body
        _prim.FillRect(_sb, new Rectangle((int)center.X - Sz(7), (int)baseY - Sz(20), Sz(14), Sz(20)), col);
        var head = new Vector2(center.X, baseY - Sz(26));                        // head
        _prim.DiscAt(_sb, head, Sz(11), outline);
        _prim.DiscAt(_sb, head, Sz(9), col);
        _prim.DiscAt(_sb, head + new Vector2(0, -2), Sz(5), col * 1.2f);

        _font.DrawCentered(_sb, TitheGlyph(f.Archetype), (int)center.X, (int)(baseY - Sz(16)), s > 1f ? 2 : 1,
            crew ? Color.White : new Color(34, 32, 38));

        float topY = head.Y - Sz(11);
        DrawHpBar(f, center.X, topY - 10);
        DrawStatusPips(f, center.X, topY - 2);
    }

    private static Color TitheTokenColor(string archetype) => archetype switch
    {
        "archer" => new Color(110, 194, 112),
        "bulwark" => new Color(108, 150, 224),
        "cannon" => new Color(230, 138, 70),
        "barrow_husk" => new Color(206, 200, 180),
        "marrow_spitter" => new Color(150, 192, 142),
        "gravehound" => new Color(176, 110, 92),
        "crypt_warden" => new Color(150, 152, 168),
        "grave_mite" => new Color(140, 168, 92),
        "bone_piper" => new Color(206, 184, 216),
        "sexton" => new Color(168, 70, 90),
        _ => new Color(170, 170, 176),
    };

    private static string TitheGlyph(string archetype) => archetype switch
    {
        "archer" => "A", "bulwark" => "B", "cannon" => "C",
        "barrow_husk" => "H", "marrow_spitter" => "S", "gravehound" => "G",
        "crypt_warden" => "W", "grave_mite" => "M", "bone_piper" => "P", "sexton" => "X",
        _ => "?",
    };

    private void DrawStatusPips(Fighter f, float centerX, float y)
    {
        if (f.Statuses.Count == 0) return;
        const int pw = 6, gap = 2;
        int total = f.Statuses.Count * pw + (f.Statuses.Count - 1) * gap;
        int x = (int)centerX - total / 2;
        foreach (var s in f.Statuses)
        {
            _prim.FillRect(_sb, new Rectangle(x - 1, (int)y - 1, pw + 2, pw + 2), new Color(0, 0, 0, 160));
            _prim.FillRect(_sb, new Rectangle(x, (int)y, pw, pw), StatusColor(s.Kind));
            x += pw + gap;
        }
    }

    private static Color StatusColor(StatusKind k) => k switch
    {
        StatusKind.DamageBuff => new Color(240, 160, 60),
        StatusKind.Shield => new Color(90, 180, 240),
        StatusKind.Poison => new Color(120, 200, 90),
        StatusKind.MpDrain => new Color(170, 120, 210),
        _ => Color.White,
    };

    private static int FrameIndex(Pose pose, SpriteSheet sheet)
    {
        if (sheet.FrameCount <= 1) return 0;
        // Hurt plays once and holds the last frame; idle/walk/cast loop.
        return pose.State == AnimState.Hurt
            ? Math.Min((int)(pose.Clock * 12f), sheet.FrameCount - 1)
            : (int)(pose.Clock * AnimFps) % sheet.FrameCount;
    }

    /// <summary>Draw a sprite anchored at its feet (bottom-centre) on the cell centre.</summary>
    private void DrawSpriteFeet(Texture2D tex, Vector2 feet, Color tint, float targetHeight)
    {
        float scale = targetHeight / tex.Height;
        _sb.Draw(tex, feet, null, tint, 0f, new Vector2(tex.Width / 2f, tex.Height),
            scale, SpriteEffects.None, 0f);
    }

    private void DrawHpBar(Fighter f, float centerX, float y)
    {
        float dhp = _anim.DisplayHp(f);
        const int barW = 32;
        int x = (int)centerX - barW / 2;
        _prim.FillRect(_sb, new Rectangle(x - 1, (int)y - 1, barW + 2, 7), new Color(0, 0, 0, 140));
        _prim.FillRect(_sb, new Rectangle(x, (int)y, barW, 5), Palette.HpBack);
        int fill = (int)MathF.Round(barW * Math.Clamp(dhp / f.MaxHp, 0f, 1f));
        _prim.FillRect(_sb, new Rectangle(x, (int)y, fill, 5),
            f.Team == Team.Player ? Palette.HpFill : new Color(214, 96, 88));
        _font.DrawCentered(_sb, ((int)MathF.Round(dhp)).ToString(), (int)centerX, (int)y - 10, 1, Palette.Text);
    }

    // ----- HUD ----------------------------------------------------------------------

    /// <summary>
    /// The 1.29 rollover: units carry no overhead UI, so hovering one shows a small plate
    /// with its name and health number next to the cursor (screen space, HUD pass).
    /// </summary>
    /// <summary>
    /// The ambient bed per scene (graveyard wind, crypt drone) and the bell tolling as the
    /// dive clock crosses 30 and 10 seconds — the Grasp closing in, audibly.
    /// </summary>
    private void UpdateAmbient()
    {
        string? want = null;
        if (_loop && _scene == Scene.Graveyard) want = "wind";
        else if (_loop && _scene == Scene.Combat && _cryptRun) want = "drone";
        else if (_loop && _scene == Scene.Combat) want = "wind";
        _sfx.SetAmbient(want, want == "drone" ? 0.2f : 0.13f);

        if (_dive != null && !_dive.Ended)
        {
            float c = _dive.Clock;
            foreach (float mark in new[] { 30f, 10f })
                if (_lastDiveClock > mark && c <= mark)
                    _sfx.Play("bell", mark <= 10f ? 1f : 0.8f, jitter: false);
            _lastDiveClock = c;
        }
    }

    /// <summary>
    /// Pump the Gum HUD layer: visible only during watched tithe combat (the surface it skins),
    /// with the fight state pushed into its named elements every frame. See Ui/GumHud.cs.
    /// </summary>
    private void UpdateGumHud(GameTime gameTime)
    {
        _gum.Update(gameTime);
        // The Emberwick combat HUD (from the user's Claude Design project) supersedes the Gum
        // band in fights; Gum stays wired for future non-combat screens.
        _gum.SetVisible(false);
    }

    /// <summary>Archetype id -> readable mob name for rollovers.</summary>
    private static string MobDisplayName(string archetype) =>
        archetype.Replace('_', ' ').ToUpperInvariant();

    private void DrawHoverUnitInfo()
    {
        if (!_engine.Field.InBounds(_hover)) return;
        var f = _engine.FighterAt(_hover);
        if (f == null || !f.IsAlive) return;
        DrawUnitPlate(f, Math.Min(_mouse.X + 16, ScreenW - 190), Math.Max(4, _mouse.Y - 44));
    }

    /// <summary>One status effect as a short readable line ("POISON 6 (2T)").</summary>
    private static string StatusLine(StatusEffect st)
    {
        string name = st.Kind switch
        {
            StatusKind.DamageBuff => $"POWER +{st.Magnitude}%",
            StatusKind.Shield => $"SHIELD {st.Magnitude}",
            StatusKind.Poison => $"POISON {st.Magnitude}",
            StatusKind.Regen => $"REGEN {st.Magnitude}",
            StatusKind.MpDrain => $"MP DRAIN {st.Magnitude}",
            StatusKind.Rooted => "ROOTED",
            StatusKind.Stabilized => "STABILIZED",
            StatusKind.Reflect => $"REFLECT {st.Magnitude}%",
            _ => st.Kind.ToString().ToUpperInvariant(),
        };
        return $"{name} ({st.Remaining}T)";
    }

    /// <summary>The 1.29 rollover plate: name + level, health, points, active effects.</summary>
    private void DrawUnitPlate(Fighter f, int x, int y)
    {
        var lines = new List<(string text, Color color)>
        {
            ($"{f.Name.ToUpperInvariant()}  LVL {f.Level}", Palette.Text),
            ($"{f.Hp} / {f.MaxHp} HP", new Color(150, 214, 130)),
            ($"{f.BaseAp} AP   {f.BaseMp} MP", new Color(214, 196, 120)),
        };
        foreach (var st in f.Statuses.Where(st => st.Kind != StatusKind.None))
            lines.Add((StatusLine(st), new Color(196, 150, 214)));

        int w = lines.Max(l => _font.Measure(l.text, 1)) + 16;
        int h = 8 + lines.Count * 13;
        var r = new Rectangle(Math.Min(x, ScreenW - w - 4), Math.Max(4, Math.Min(y, HudTop - h - 4)), w, h);
        _prim.FillRect(_sb, r, new Color(12, 13, 17, 235));
        _prim.StrokeRect(_sb, r, 1, f.Team == Team.Player ? new Color(214, 60, 40) : new Color(84, 108, 214));
        for (int i = 0; i < lines.Count; i++)
            _font.Draw(_sb, lines[i].text, r.X + 8, r.Y + 5 + i * 13, 1, lines[i].color);
    }

    private void DrawHud()
    {
        if (_tithe) { DrawTitheHud(); return; }

        var hero = Hero;

        // Top-left status.
        _font.Draw(_sb, $"ROUND {_engine.Round}", 16, 12, 2, Palette.Text);
        _font.Draw(_sb, $"{_engine.Current.Name.ToUpperInvariant()}'S TURN", 16, 32, 2,
            _engine.Current.Team == Team.Player ? Palette.HpFill : Palette.EnemyColor);
        _font.Draw(_sb, "CLICK MOVE   1-4 SPELL   SPACE END   R RESTART", 16, HudTop - 22, 1, Palette.TextDim);

        DrawTurnTimer();

        DrawTurnTimeline();

        // Combat log (right side of playfield).
        int ly = 92;
        foreach (var line in _log)
        {
            _font.Draw(_sb, Trunc(line, 44), 940, ly, 1, Palette.TextDim);
            ly += 12;
        }

        // HUD panel.
        _prim.FillRect(_sb, new Rectangle(0, HudTop, ScreenW, ScreenH - HudTop), Palette.HudPanel);

        if (hero != null && hero.IsAlive) DrawPointPips(hero);
        DrawSpellBar(hero);
        DrawEndTurnButton();
        DrawHoverUnitInfo();
    }

    private void DrawTitheHud()
    {
        string place = _loop ? (_cryptRun ? "THE CRYPT" : "THE GRAVEYARD")
            : _boss ? "THE SEXTON'S COURT" : "THE GRAVEYARD";
        if (!_gum.Active)
        {
            _font.Draw(_sb, $"ROUND {_engine.Round}   {place}", 16, 12, 2, Palette.Text);
            _font.Draw(_sb, $"WATCHING — {_engine.Current.Name.ToUpperInvariant()}", 16, 32, 2,
                _engine.Current.Team == Team.Player ? Palette.HpFill : Palette.EnemyColor);
        }
        // Only advertise keys that work here: R/B restart or swap the STANDALONE fight and would
        // mislead during a campaign fight, where the dive owns the flow.
        _font.Draw(_sb, _loop ? "1/2/3 = SPEED   ·   M = SOUND" : "1/2/3 = SPEED   ·   R = NEW FIGHT   ·   B = SEXTON   ·   M = SOUND",
            16, HudTop - 22, 1, Palette.TextDim);

        // Playback speed, top-centre where the piloted mode shows the turn clock.
        _font.DrawCentered(_sb, $"> SPEED {_speed:0}X", ScreenW / 2, 16, 2, Palette.Text);

        DrawTurnTimeline();

        DrawEmberwickLog();

        DrawEmberwickBand();
        DrawHoverUnitInfo();
    }

    /// <summary>The Emberwick combat chat: slate card, turn headers, element-tinted lines.</summary>
    private void DrawEmberwickLog()
    {
        var panel = new Rectangle(930, 84, 344, 250);
        _ew.Panel(_sb, panel, sunken: true, radius: 10);
        _ew.HeaderStrip(_sb, new Rectangle(panel.X + 2, panel.Y + 2, panel.Width - 4, 22));
        _font.DrawCentered(_sb, "THE FIGHT", panel.Center.X, panel.Y + 9, 1, Ew.Ink);
        int ly = panel.Y + 32;
        foreach (var line in _log)
        {
            if (ly > panel.Bottom - 16) break;
            string t = line.TrimStart();
            Color col =
                t.StartsWith("ROUND") ? Ew.Ink
                : t.Contains("FIRE DAMAGE") ? Ew.Ember
                : t.Contains("WATER DAMAGE") ? Ew.Brook
                : t.Contains("AIR DAMAGE") ? Ew.Gale
                : t.Contains("EARTH DAMAGE") ? Ew.Loam
                : t.Contains("DEFEATED") || t.Contains("DIES") ? Ew.Danger
                : t.Contains("CASTS") ? Ew.AccentBright
                : Ew.InkSoft;
            int indent = t.StartsWith("ROUND") ? 0 : 6;
            _font.Draw(_sb, Trunc(line, 40), panel.X + 12 + indent, ly, 1, col);
            ly += 13;
        }
    }

    /// <summary>
    /// The Emberwick combat band (Claude Design import): slate panel, the active unit's
    /// Vim heart with Spark/Stride gems, crew HP wells left, and the actor's spell wells right.
    /// </summary>
    private void DrawEmberwickBand()
    {
        _ew.Panel(_sb, new Rectangle(-6, HudTop + 2, ScreenW + 12, ScreenH - HudTop + 16));

        var cur = _engine.Current;

        // Vitals cluster, Dofus-style bottom centre: heart = HP, star = AP, diamond = MP.
        var heartC = new Vector2(ScreenW / 2f, HudTop + 82);
        _ew.Badge(_sb, EwChrome.Gem.Heart, heartC, 104, Ew.Hp, Ew.HpDeep,
            cur.MaxHp <= 0 ? 0f : (float)Math.Clamp(cur.Hp, 0, cur.MaxHp) / cur.MaxHp);
        _font.DrawCentered(_sb, cur.Hp.ToString(), (int)heartC.X, (int)heartC.Y - 14, 2, Color.White);
        _font.DrawCentered(_sb, cur.MaxHp.ToString(), (int)heartC.X, (int)heartC.Y + 6, 1, Ew.Ink * 0.85f);
        _ew.Badge(_sb, EwChrome.Gem.Star, new Vector2(ScreenW / 2f - 92, HudTop + 96), 62, Ew.Ap, Ew.ApDeep);
        _font.DrawCentered(_sb, cur.CurrentAp.ToString(), ScreenW / 2 - 92, HudTop + 90, 2, Color.White);
        _ew.Badge(_sb, EwChrome.Gem.Diamond, new Vector2(ScreenW / 2f + 92, HudTop + 96), 58, Ew.Mp, Ew.MpDeep);
        _font.DrawCentered(_sb, cur.CurrentMp.ToString(), ScreenW / 2 + 92, HudTop + 90, 2, Color.White);
        _font.DrawCentered(_sb, cur.Name.ToUpperInvariant(), ScreenW / 2, HudTop + 12, 1,
            cur.Team == Team.Player ? Ew.AccentBright : Ew.Danger);

        // Crew HP wells, left column.
        _font.Draw(_sb, "YOUR CREW", 20, HudTop + 14, 1, Ew.InkSoft);
        int y = HudTop + 32;
        foreach (var f in _engine.Fighters.Where(x => x.Team == Team.Player && !x.IsSummon))
        {
            _font.Draw(_sb, Trunc(f.Name.ToUpperInvariant(), 12), 20, y + 3, 1, f.IsAlive ? Ew.Ink : Ew.InkMuted);
            var well = new Rectangle(150, y, 190, 18);
            _ew.Well(_sb, well);
            int fill = (int)(184 * Math.Clamp(f.MaxHp <= 0 ? 0 : (float)Math.Max(0, f.Hp) / f.MaxHp, 0f, 1f));
            if (fill > 0 && f.IsAlive)
                _ew.GradientV(_sb, new Rectangle(well.X + 3, well.Y + 3, fill, 12), Ew.Hp, Ew.HpDeep);
            _font.Draw(_sb, f.IsAlive ? $"{f.Hp}/{f.MaxHp}" : "DOWN", 348, y + 3, 1,
                f.IsAlive ? Ew.InkSoft : Ew.Danger);
            y += 26;
        }

        // The actor's spells as slot wells, right side (element letter + AP cost).
        _font.Draw(_sb, $"{Trunc(cur.Name.ToUpperInvariant(), 14)}'S KIT", 934, HudTop + 14, 1, Ew.InkSoft);
        int sx = 934;
        foreach (var spell in cur.Spells.Take(6))
        {
            var well = new Rectangle(sx, HudTop + 32, 46, 46);
            _ew.Well(_sb, well);
            var dmg = spell.Effects.FirstOrDefault(e => e.Kind == EffectKind.Damage);
            var col = dmg != null ? EwChrome.ElementColor(dmg.Element) : Ew.Moon;
            _font.DrawCentered(_sb, spell.Name[..1].ToUpperInvariant(), well.Center.X, well.Y + 12, 2, col);
            _font.DrawCentered(_sb, $"{spell.ApCost}", well.Center.X, well.Bottom - 14, 1, Ew.InkSoft);
            sx += 52;
        }
    }

    /// <summary>The crew panel: each member's token colour, class, HP bar and fate.</summary>
    private void DrawCrewRoster()
    {
        var crew = _engine.Fighters.Where(f => f.Team == Team.Player).ToList();
        int x = 16, y = HudTop + 12;
        _font.Draw(_sb, "YOUR CREW", x, y, 1, Palette.TextDim);
        y += 16;
        foreach (var f in crew)
        {
            var col = TitheTokenColor(f.Archetype);
            _prim.DiscAt(_sb, new Vector2(x + 8, y + 8), 9, new Color(18, 18, 22));
            _prim.DiscAt(_sb, new Vector2(x + 8, y + 8), 7, col);
            _font.DrawCentered(_sb, TitheGlyph(f.Archetype), x + 8, y + 3, 1, Color.White);

            string role = f.IsMercenary ? "MERC" : "AVATAR";
            _font.Draw(_sb, $"{f.Name.ToUpperInvariant()}  {role}", x + 24, y + 1, 1,
                f.IsAlive ? Palette.Text : Palette.TextDim);

            // HP bar.
            const int bw = 150;
            int by = y + 14;
            _prim.FillRect(_sb, new Rectangle(x + 24, by, bw, 6), Palette.HpBack);
            int fill = (int)MathF.Round(bw * Math.Clamp((float)f.Hp / f.MaxHp, 0f, 1f));
            _prim.FillRect(_sb, new Rectangle(x + 24, by, fill, 6), Palette.HpFill);

            string fate = !f.IsAlive
                ? (_aftermath?.Units.FirstOrDefault(u => u.Id == f.Id) is { Died: true } ? "DEAD" : "WOUNDED")
                : $"{f.Hp}/{f.MaxHp}";
            _font.Draw(_sb, fate, x + 24 + bw + 8, y + 8, 1,
                !f.IsAlive ? new Color(214, 96, 88) : Palette.TextDim);
            y += 34;
        }
    }

    /// <summary>
    /// The turn-order timeline: every fighter in initiative order, left to right, with the
    /// active one highlighted and underlined. Dead fighters grey out. This is the visible
    /// face of the turn system — you can always read whose turn it is and who is next.
    /// </summary>
    private void DrawTurnTimeline()
    {
        Fighter? hoveredCard = null;
        // The dead leave the order 1.29-style — but not until their death has replayed.
        var order = _engine.Fighters.Where(f => f.IsAlive || _anim.StillShown(f.Id)).ToList();
        const int cardW = 96, cardH = 46, gap = 8;
        int totalW = order.Count * cardW + (order.Count - 1) * gap;
        int x0 = ScreenW - 20 - totalW;
        int y0 = 8;

        _font.Draw(_sb, "TURN ORDER", x0, y0, 1, Palette.TextDim);
        int cy = y0 + 12;

        for (int i = 0; i < order.Count; i++)
        {
            var f = order[i];
            var r = new Rectangle(x0 + i * (cardW + gap), cy, cardW, cardH);
            bool current = f == _engine.Current;

            _ew.Panel(_sb, r, sunken: !current, radius: 8);
            if (current) _prim.FillRect(_sb, new Rectangle(r.X + 4, r.Bottom - 4, r.Width - 8, 3), Ew.Accent);

            // Treat a unit whose death hasn't replayed yet as alive — no spoilers on the card.
            bool shownAlive = f.IsAlive || _anim.StillShown(f.Id);
            var token = shownAlive
                ? (_tithe ? TitheTokenColor(f.Archetype)
                          : f.PlayerControlled ? Palette.HeroColor : Palette.CreatureColor(f.Name))
                : new Color(58, 60, 66);
            var mid = new Vector2(r.X + 17, r.Y + cardH / 2f);
            _prim.DiscAt(_sb, mid, 11, new Color(18, 18, 22));
            _prim.DiscAt(_sb, mid, 9, token);

            _font.Draw(_sb, Trunc(f.Name.ToUpperInvariant(), 7), r.X + 32, r.Y + 9, 1,
                shownAlive ? Ew.Ink : Ew.InkMuted);
            _font.Draw(_sb, shownAlive ? $"{(int)MathF.Max(1, _anim.DisplayHp(f))} HP" : "DEAD",
                r.X + 32, r.Y + 25, 1,
                shownAlive ? (f.Team == Team.Player ? Ew.AccentBright : Ew.Danger) : Ew.InkMuted);

            if (current)
                _prim.FillRect(_sb, new Rectangle(r.X, r.Bottom + 2, cardW, 3), Palette.CurrentRing);

            if (r.Contains(new Point(_mouse.X, _mouse.Y)))
            {
                hoveredCard = f;
                if (f.IsAlive) DrawUnitPlate(f, r.X, r.Bottom + 8);
            }
        }
        _timelineHover = hoveredCard; // world pass reads this next frame to ring the cell
    }

    /// <summary>The fighter whose timeline card is under the mouse (1-frame latency is fine).</summary>
    private Fighter? _timelineHover;

    private void DrawTurnTimer()
    {
        bool playerTurn = _engine.Current.PlayerControlled;
        const int barW = 240, barH = 16;
        int bx = ScreenW / 2 - barW / 2, by = 14;

        _prim.FillRect(_sb, new Rectangle(bx, by, barW, barH), Palette.HpBack);
        float f = playerTurn ? Math.Clamp(_turnClock / TurnSeconds, 0f, 1f) : 1f;
        Color fill = !playerTurn ? new Color(70, 74, 84)
            : f > 0.5f ? Palette.HpFill
            : f > 0.25f ? new Color(230, 200, 70)
            : new Color(224, 80, 64);
        _prim.FillRect(_sb, new Rectangle(bx, by, (int)(barW * f), barH), fill);
        _prim.StrokeRect(_sb, new Rectangle(bx, by, barW, barH), 1, new Color(80, 86, 98));

        string label = playerTurn ? $"YOUR TURN - {(int)MathF.Ceiling(Math.Max(0f, _turnClock))}S" : "ENEMY TURN";
        _font.DrawCentered(_sb, label, ScreenW / 2, by + barH + 6, 1, Palette.Text);
    }

    private void DrawPointPips(Fighter hero)
    {
        _font.Draw(_sb, "AP", 16, 608, 1, Palette.TextDim);
        for (int i = 0; i < hero.BaseAp; i++)
            _prim.FillRect(_sb, new Rectangle(34 + i * 12, 606, 9, 9),
                i < hero.CurrentAp ? Palette.ApPip : Palette.PipEmpty);

        _font.Draw(_sb, "MP", 160, 608, 1, Palette.TextDim);
        for (int i = 0; i < hero.BaseMp; i++)
            _prim.FillRect(_sb, new Rectangle(178 + i * 12, 606, 9, 9),
                i < hero.CurrentMp ? Palette.MpPip : Palette.PipEmpty);
    }

    private void DrawSpellBar(Fighter? hero)
    {
        for (int i = 0; i < _spellButtons.Length; i++)
        {
            var spell = HeroSpells[i];
            var r = _spellButtons[i];
            bool selected = _selectedSpell == i;
            bool usable = hero != null && hero.IsAlive && hero.Team == Team.Player &&
                          hero.CurrentAp >= spell.ApCost && hero.HasCastsLeft(spell) &&
                          !hero.IsOnCooldown(spell, _engine.Round);

            _prim.FillRect(_sb, r, selected ? Palette.HudPanelLight : Palette.HudPanel);
            _prim.StrokeRect(_sb, r, 2, selected ? Palette.CurrentRing :
                usable ? new Color(80, 86, 98) : new Color(46, 48, 54));

            var iconColor = ElementColor(spell);
            _prim.DiscAt(_sb, new Vector2(r.X + 26, r.Y + 26), 12, usable ? iconColor : new Color(60, 62, 68));
            _font.Draw(_sb, (i + 1).ToString(), r.X + 8, r.Y + 8, 1, Palette.TextDim);

            var nameColor = usable ? Palette.Text : Palette.TextDim;
            _font.Draw(_sb, Trunc(spell.Name.ToUpperInvariant(), 12), r.X + 10, r.Y + 50, 1, nameColor);
            _font.Draw(_sb, $"{spell.ApCost} AP", r.X + 10, r.Y + 64, 1, Palette.ApPip);
            string range = spell.MaxRange == 0 ? "SELF"
                : spell.MinRange == spell.MaxRange ? $"RNG {spell.MaxRange}"
                : $"RNG {spell.MinRange}-{spell.MaxRange}";
            _font.Draw(_sb, range, r.X + 10, r.Y + 78, 1, Palette.TextDim);
            _font.DrawRight(_sb, spell.Cooldown > 0 ? "CD" : "", r.Right - 10, r.Y + 8, 1, Palette.EnemyColor);
        }
    }

    private void DrawEndTurnButton()
    {
        var r = _endTurnButton;
        bool hover = r.Contains(new Point(_mouse.X, _mouse.Y));
        bool playerTurn = _engine.Current.PlayerControlled;
        _prim.FillRect(_sb, r, hover && playerTurn ? Palette.HudPanelLight : Palette.HudPanel);
        _prim.StrokeRect(_sb, r, 2, playerTurn ? Palette.HpFill : new Color(46, 48, 54));
        _font.DrawCentered(_sb, "END", r.Center.X, r.Y + 28, 3, playerTurn ? Palette.Text : Palette.TextDim);
        _font.DrawCentered(_sb, "TURN", r.Center.X, r.Y + 52, 3, playerTurn ? Palette.Text : Palette.TextDim);
        _font.DrawCentered(_sb, "(SPACE)", r.Center.X, r.Y + 82, 1, Palette.TextDim);
    }

    private void DrawEndOverlay()
    {
        _prim.FillRect(_sb, new Rectangle(0, 0, ScreenW, ScreenH), new Color(0, 0, 0, 160));
        bool win = _engine.Outcome == FightOutcome.Victory;

        if (_tithe)
        {
            // TITHE aftermath: the win is rarely free. Show each crew member's fate and XP.
            _font.DrawCentered(_sb, win ? "THE PACK FALLS" : "THE CREW FALLS", ScreenW / 2, 150, 6,
                win ? Palette.HpFill : Palette.HeroColor);
            _font.DrawCentered(_sb, win ? "you are dragged back toward the Lychgate" : "CAMPAIGN OVER",
                ScreenW / 2, 210, 2, Palette.TextDim);

            int y = 280;
            if (_aftermath != null)
            {
                _font.DrawCentered(_sb, $"XP FROM KILLS: {_aftermath.XpPool}", ScreenW / 2, y, 2, Palette.Text);
                y += 40;
                foreach (var u in _aftermath.Units)
                {
                    string fate = u.Died ? (u.Mercenary ? "DEAD (mercenary lost)" : "DEAD (avatar fell)")
                        : u.Wounded ? "WOUNDED  (-1 PA / -1 PM)"
                        : "unhurt";
                    var col = u.Died ? new Color(214, 96, 88)
                        : u.Wounded ? new Color(230, 200, 70) : Palette.HpFill;
                    _font.DrawCentered(_sb, $"{u.Name.ToUpperInvariant()}   +{u.XpGained} XP   {fate}",
                        ScreenW / 2, y, 2, col);
                    y += 30;
                }
                string loot = _aftermath.Drops.Count > 0
                    ? "ESSENCES DROPPED: " + string.Join(", ", _aftermath.Drops).ToUpperInvariant()
                    : "NO ESSENCES DROPPED";
                _font.DrawCentered(_sb, loot, ScreenW / 2, y + 8, 1,
                    _aftermath.Drops.Count > 0 ? new Color(200, 170, 240) : Palette.TextDim);
                y += 30;
            }
            _font.DrawCentered(_sb, "PRESS R TO DIVE AGAIN   ·   B: FACE THE SEXTON", ScreenW / 2, y + 20, 1, Palette.Text);
            return;
        }

        string title = win ? "VICTORY!" : "DEFEAT";
        _font.DrawCentered(_sb, title, ScreenW / 2, 280, 8, win ? Palette.HpFill : Palette.HeroColor);
        _font.DrawCentered(_sb, "PRESS R TO FIGHT AGAIN", ScreenW / 2, 380, 2, Palette.Text);
    }

    private static Color ElementColor(SpellDef spell)
    {
        var dmg = spell.Effects.FirstOrDefault(e => e.Kind == EffectKind.Damage);
        if (spell.Effects.Any(e => e.Kind == EffectKind.Teleport)) return new Color(150, 210, 240);
        return dmg?.Element switch
        {
            Element.Fire => new Color(224, 96, 72),
            Element.Water => new Color(84, 150, 224),
            Element.Air => new Color(120, 210, 130),
            Element.Earth => new Color(176, 132, 90),
            _ => new Color(200, 200, 206),
        };
    }

    // ===== M2 scene rendering ========================================================

    private IEnumerable<CellCoord> PlaneCellsByDepth(MapData map)
    {
        var list = new List<CellCoord>();
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                list.Add(new CellCoord(x, y));
        return list.OrderBy(c => c.X + c.Y);
    }

    /// <summary>Draw a scene map's floor tiles (reuses the combat tile look, off a MapData).</summary>
    private void DrawPlaneTiles(MapData map)
    {
        foreach (var c in PlaneCellsByDepth(map)) DrawTacticalCell(c, map.Tile(c.X, c.Y), PixFamNow());
    }

    // ----- City ---------------------------------------------------------------------

    private void DrawCity()
    {
        _sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: _camera.View);
        DrawPlaneTiles(_cityMap);
        foreach (var cell in new[] { TitheCell, TempleCell, HireCell, LychgateCell })
            if (cell == _hover) _prim.DiamondAt(_sb, _proj.CellCenter(cell), Palette.CurrentRing * 0.30f);
        DrawBuilding(TitheCell, new Color(214, 176, 84), "T", "TITHE-KEEPER", "chest_open");
        DrawBuilding(TempleCell, new Color(186, 150, 220), "+", "TEMPLE SISTER", "crown");
        DrawBuilding(HireCell, new Color(150, 190, 140), "H", "HIRING POST", "barrel");
        DrawLychgate(LychgateCell);
        _sb.End();

        _sb.Begin(samplerState: SamplerState.PointClamp);
        _font.Draw(_sb, "CLICK A BUILDING TO TRADE   ·   E: STASH & KIT   ·   CLICK THE LYCHGATE TO DIVE", 16, 44, 1, Palette.TextDim);
        DrawCampaignHud();
        if (_openNpc >= 0 && !_equipOpen) DrawNpcPanel(_openNpc);
        if (_equipOpen) DrawEquipPanel();
        if (_campaign.Over) DrawGameOver();
        _sb.End();
    }

    private void DrawBuilding(CellCoord c, Color col, string glyph, string label, string? pixProp = null)
    {
        var center = _proj.CellCenter(c);
        var outline = new Color(16, 16, 20);
        _prim.DiscAt(_sb, center + new Vector2(0, 2), 14, Palette.Shadow);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 14, (int)center.Y - 34, 28, 36), outline);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 12, (int)center.Y - 32, 24, 32), col);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 12, (int)center.Y - 32, 24, 7), col * 1.25f);
        _font.DrawCentered(_sb, glyph, (int)center.X, (int)center.Y - 22, 2, Color.White);
        _font.DrawCentered(_sb, label, (int)center.X, (int)center.Y - 48, 1, Palette.Text);
        if (c == _hover)
            _font.DrawCentered(_sb, "CLICK TO TRADE", (int)center.X, (int)center.Y - 60, 1, new Color(232, 202, 92));
    }

    private void DrawLychgate(CellCoord c)
    {
        var center = _proj.CellCenter(c);
        var col = new Color(120, 120, 140);
        _prim.DiscAt(_sb, center + new Vector2(0, 2), 18, Palette.Shadow);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 18, (int)center.Y - 42, 8, 42), col);
        _prim.FillRect(_sb, new Rectangle((int)center.X + 10, (int)center.Y - 42, 8, 42), col);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 20, (int)center.Y - 48, 40, 8), col);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 10, (int)center.Y - 40, 20, 40), new Color(8, 8, 12));
        _font.DrawCentered(_sb, "LYCHGATE", (int)center.X, (int)center.Y - 62, 1, new Color(200, 200, 220));
        if (c == _hover)
            _font.DrawCentered(_sb, "CLICK TO DIVE", (int)center.X, (int)center.Y - 74, 1, new Color(232, 202, 92));
    }

    private void DrawNpcPanel(int npc)
    {
        var r = new Rectangle(330, 296, 620, 336); // tall enough for the Temple's five services
        UiPanelBg(r);
        string[] titles = { "THE TITHE-KEEPER", "THE TEMPLE SISTER", "THE HIRING POST" };
        _font.DrawCentered(_sb, titles[npc], r.Center.X, r.Y + 14, 2, UiSkinned ? UiInk : Palette.Text);

        var acts = NpcActions(npc);
        for (int i = 0; i < acts.Count; i++)
        {
            var b = PanelButton(i);
            bool hover = b.Contains(new Point(_mouse.X, _mouse.Y));
            if (UiSkinned)
            {
                UiButtonBg(b, hover, acts[i].ok ? Color.White : new Color(148, 148, 144));
                _font.Draw(_sb, acts[i].label, b.X + 14, b.Y + 16, 1, acts[i].ok ? UiInkOnGreen : UiInkDim);
            }
            else
            {
                _prim.FillRect(_sb, b, acts[i].ok ? (hover ? Palette.HudPanelLight : Palette.HudPanel) : new Color(30, 30, 34));
                _prim.StrokeRect(_sb, b, 1, acts[i].ok ? new Color(96, 150, 96) : new Color(60, 60, 66));
                _font.Draw(_sb, acts[i].label, b.X + 14, b.Y + 16, 1, acts[i].ok ? Palette.Text : Palette.TextDim);
            }
        }
        _font.DrawCentered(_sb, "(ESC TO CLOSE)", r.Center.X, r.Bottom - 20, 1, UiSkinned ? UiInkDim : Palette.TextDim);
    }

    private void DrawGameOver()
    {
        _prim.FillRect(_sb, new Rectangle(0, 0, ScreenW, ScreenH), new Color(0, 0, 0, 190));
        _font.DrawCentered(_sb, "THE AVATAR HAS FALLEN", ScreenW / 2, 250, 5, Palette.HeroColor);
        _font.DrawCentered(_sb, "the labyrinth keeps what it takes", ScreenW / 2, 320, 2, Palette.TextDim);
        _font.DrawCentered(_sb, "PRESS R TO BEGIN A NEW CAMPAIGN", ScreenW / 2, 400, 2, Palette.Text);
    }

    // ----- Graveyard ----------------------------------------------------------------

    private void DrawGraveyard()
    {
        _sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: _camera.View);
        DrawPlaneTiles(_graveMap);

        // Path preview + hover highlight.
        foreach (var c in _partyPath)
            _prim.DiscAt(_sb, _proj.CellCenter(c), 4, new Color(232, 222, 140, 150));
        if (_graveField != null && _graveField.InBounds(_hover))
        {
            bool interactive = _hover == CryptCell ||
                (_dive?.Survivor != null && _hover == SurvivorCell) ||
                (_dive != null && _dive.Packs.Any(p => !p.Cleared && _packCells.TryGetValue(p.Def.Id, out var pc) && pc == _hover));
            if (interactive || _graveMap.IsWalkable(_hover))
                _prim.DiamondAt(_sb, _proj.CellCenter(_hover), Palette.CurrentRing * (interactive ? 0.35f : 0.16f));
        }

        foreach (var c in PlaneCellsByDepth(_graveMap))
        {
            var k = _graveMap.Tile(c.X, c.Y);
            if (k is TileKind.Rock or TileKind.Tree) DrawObstacleKind(_proj.CellCenter(c), k);
        }
        if (_dive != null)
            foreach (var p in _dive.Packs)
                if (!p.Cleared && _packCells.TryGetValue(p.Def.Id, out var cell))
                    DrawPackToken(cell, p);
        DrawCrypt(); // after the packs so its label is never buried under a huddle
        if (_dive?.Survivor is { } offer) DrawSurvivorToken(offer);
        DrawPartyToken(_partyWorld);
        _sb.End();

        _sb.Begin(samplerState: SamplerState.PointClamp);
        _font.Draw(_sb, $"CLICK TO MOVE   ·   CLICK A PACK TO FIGHT   ·   REACH THE CRYPT AT LEVEL {CryptLevel}", 16, 44, 1, Palette.TextDim);
        DrawCampaignHud();
        DrawDiveClock(ScreenW / 2, 14, 300, 18);
        if (_yardMsgTimer > 0f)
            _font.DrawCentered(_sb, _yardMsg, ScreenW / 2, 508, 2, new Color(232, 202, 96));
        _sb.End();
    }

    /// <summary>A survivor wandering the yard (Bible §6.12): class and price visible, nature not.</summary>
    private void DrawSurvivorToken(DiveSession.SurvivorOffer offer)
    {
        var center = _proj.CellCenter(SurvivorCell);
        if (Pix)
        {
            _prim.DiscAt(_sb, center + new Vector2(0, 7), 11, new Color(120, 190, 150) * 0.55f);
            DrawPixActorIdle("hero", center, new Color(196, 214, 200));
            _font.DrawCentered(_sb, "?", (int)center.X + 14, (int)center.Y - 44, 2, new Color(232, 220, 140));
        }
        else
        {
            _prim.DiscAt(_sb, center + new Vector2(0, 2), 11, Palette.Shadow);
            _prim.DiscAt(_sb, center, 9, new Color(16, 16, 20));
            _prim.DiscAt(_sb, center, 7, new Color(120, 190, 150));
            _font.DrawCentered(_sb, "?", (int)center.X, (int)center.Y - 5, 1, Color.White);
        }
        _font.DrawCentered(_sb, $"SURVIVOR — {offer.ClassId.ToUpperInvariant()} L{offer.Level} ({offer.Price}g)",
            (int)center.X, (int)center.Y - 30, 1, new Color(150, 210, 170));
    }

    private void DrawCrypt()
    {
        var center = _proj.CellCenter(CryptCell);
        bool locked = (_campaign.Avatar?.Level ?? 1) < CryptLevel;
        var col = _cryptCleared ? new Color(70, 70, 80) : locked ? new Color(96, 84, 108) : new Color(158, 96, 178);
        _prim.DiscAt(_sb, center + new Vector2(0, 2), 18, Palette.Shadow);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 18, (int)center.Y - 42, 36, 44), new Color(14, 14, 18));
        _prim.FillRect(_sb, new Rectangle((int)center.X - 15, (int)center.Y - 38, 30, 38), col * 0.55f);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 9, (int)center.Y - 32, 18, 32), new Color(6, 6, 10));
        _font.DrawCentered(_sb, "THE CRYPT", (int)center.X, (int)center.Y - 56, 1,
            _cryptCleared ? Palette.TextDim : new Color(204, 172, 224));
        string sub = _cryptCleared ? "cleared" : locked ? $"LVL {CryptLevel}+" : "OPEN — THE SEXTON";
        _font.DrawCentered(_sb, sub, (int)center.X, (int)center.Y - 44, 1,
            locked ? new Color(222, 122, 92) : new Color(200, 160, 120));
        if (CryptCell == _hover && !locked && !_cryptCleared)
            _font.DrawCentered(_sb, "CLICK TO DESCEND", (int)center.X, (int)center.Y - 68, 1, new Color(232, 202, 92));
    }

    private void DrawPartyToken(Vector2 center)
    {
        if (Pix)
        {
            // The crew in a loose wedge — hero flanked by soldiers on the overworld.
            DrawPixActorIdle("soldier", center + new Vector2(-15, -4), Color.White);
            DrawPixActorIdle("soldier", center + new Vector2(15, -2), new Color(216, 224, 236));
            DrawPixActorIdle("hero", center + new Vector2(0, 3), Color.White);
            _font.DrawCentered(_sb, "CREW", (int)center.X, (int)center.Y - 52, 1, Palette.Text);
            return;
        }
        var col = new Color(120, 190, 120);
        var outline = new Color(16, 16, 20);
        _prim.DiscAt(_sb, center + new Vector2(0, 2), 12, Palette.Shadow);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 8, (int)center.Y - 22, 16, 22), outline);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 6, (int)center.Y - 20, 12, 20), col);
        var head = new Vector2(center.X, center.Y - 26);
        _prim.DiscAt(_sb, head, 10, outline);
        _prim.DiscAt(_sb, head, 8, col);
        _font.DrawCentered(_sb, "CREW", (int)center.X, (int)center.Y - 44, 1, Palette.Text);
    }

    private void DrawPackToken(CellCoord c, DiveSession.PackState p)
    {
        var center = _proj.CellCenter(c);
        bool afford = _dive!.CanAfford(p);
        int size = p.Def.Comp.Length;
        var col = !afford ? new Color(90, 90, 96)
            : size >= 4 ? new Color(206, 84, 84)
            : size == 3 ? new Color(212, 150, 80) : new Color(200, 190, 110);

        if (c == _hover && afford) _prim.DiamondAt(_sb, center, Palette.CurrentRing * 0.30f);
        if (Pix)
        {
            // A huddle of the dead in the pack leader's skin, greyed when out of reach.
            var (sprite, tint, _) = PixActor(p.Def.Comp[0]);
            var mobTint = afford ? tint : new Color(120, 120, 128);
            int n = Math.Min(size, 3);
            for (int i = 0; i < n; i++)
                DrawPixActorIdle(sprite, center + new Vector2((i - (n - 1) / 2f) * 16, (i % 2) * 6 - 3), mobTint);
        }
        else
        {
            _prim.DiscAt(_sb, center + new Vector2(0, 2), 13, Palette.Shadow);
            for (int i = 0; i < Math.Min(size, 4); i++)
            {
                var o = new Vector2((i - 1.5f) * 7, -6 - (i % 2) * 5);
                _prim.DiscAt(_sb, center + o, 7, new Color(16, 16, 20));
                _prim.DiscAt(_sb, center + o, 5, col);
            }
        }
        _font.DrawCentered(_sb, afford ? $"x{size}" : "TOO FAR", (int)center.X, (int)center.Y - 30, 1,
            afford ? Palette.Text : Palette.TextDim);

        if (c == _hover)
        {
            // Pack rollover: the composition, grouped, plus the grade threat marker.
            var groups = p.Def.Comp.GroupBy(a => a).Select(g => $"{g.Count()}x {MobDisplayName(g.Key)}").ToList();
            if (p.Def.Grade > 1) groups.Add($"GRADE +{p.Def.Grade - 1} (STRONGER)");
            if (p.Def.Hunts) groups.Add("HUNTS THE LIVING");
            int w = groups.Max(t => _font.Measure(t, 1)) + 16;
            var box = new Rectangle((int)center.X - w / 2, (int)center.Y - 46 - groups.Count * 13,
                w, 8 + groups.Count * 13);
            _prim.FillRect(_sb, box, new Color(12, 13, 17, 235));
            _prim.StrokeRect(_sb, box, 1, new Color(84, 108, 214));
            for (int i = 0; i < groups.Count; i++)
                _font.Draw(_sb, groups[i], box.X + 8, box.Y + 5 + i * 13, 1,
                    i < p.Def.Comp.GroupBy(a => a).Count() ? Palette.Text : new Color(214, 150, 96));
        }
        // A hunting pack with the crew in its aggro radius is actively closing — flag it.
        if (p.Def.Hunts && c.DistanceTo(_partyCell) <= HuntAggroRadius)
            _font.DrawCentered(_sb, "!", (int)center.X + 20, (int)center.Y - 44, 3, new Color(224, 80, 64));
    }

    private void DrawDiveClock(int cx, int y, int w, int h)
    {
        if (_dive == null) return;
        float frac = Math.Clamp(_dive.Clock / Math.Max(1, TitheContent.Graveyard.ClockSeconds), 0f, 1f);
        int bx = cx - w / 2;
        _prim.FillRect(_sb, new Rectangle(bx, y, w, h), Palette.HpBack);
        var col = frac > 0.5f ? Palette.HpFill : frac > 0.25f ? new Color(230, 200, 70) : new Color(224, 80, 64);
        _prim.FillRect(_sb, new Rectangle(bx, y, (int)(w * frac), h), col);
        _prim.StrokeRect(_sb, new Rectangle(bx, y, w, h), 1, new Color(80, 86, 98));
        _font.DrawCentered(_sb, $"THE BELL — {(int)MathF.Ceiling(Math.Max(0, _dive.Clock))}S", cx, y + h + 6, 1, Palette.Text);
    }

    // ----- Shared campaign HUD + combat overlays ------------------------------------

    private void DrawCampaignHud()
    {
        UiTitle(_scene == Scene.City ? "THE CITY" : "THE GRAVEYARD", 16, 12, Palette.Text);

        _prim.FillRect(_sb, new Rectangle(0, HudTop, ScreenW, ScreenH - HudTop), Palette.HudPanel);
        _font.Draw(_sb, $"GOLD {_campaign.Gold}", 16, HudTop + 14, 2, new Color(232, 202, 92));
        _font.Draw(_sb, $"BREAD {_campaign.Bread}    DRAUGHTS {_campaign.Draughts}    ESSENCES {_campaign.Essences.Count}",
            16, HudTop + 42, 1, Palette.TextDim);
        int per = TitheContent.Prices.TitheEveryNDives;
        string tithe = _campaign.TitheDue ? $"TITHE DUE: {_campaign.TitheAmount}g"
            : $"tithe in {per - (_campaign.Dives % per)} dive(s)";
        _font.Draw(_sb, tithe, 16, HudTop + 58, 1, _campaign.TitheDue ? new Color(226, 122, 82) : Palette.TextDim);

        int x = 560, y = HudTop + 12;
        _font.Draw(_sb, "CREW", x, y, 1, Palette.TextDim);
        y += 16;
        foreach (var u in _campaign.Crew)
        {
            var col = TitheTokenColor(u.ClassId);
            _prim.DiscAt(_sb, new Vector2(x + 8, y + 7), 8, new Color(18, 18, 22));
            _prim.DiscAt(_sb, new Vector2(x + 8, y + 7), 6, col);
            _font.DrawCentered(_sb, TitheGlyph(u.ClassId), x + 8, y + 2, 1, Color.White);
            int xpNeed = CampaignUnit.XpForNextLevel(u.Level);
            _prim.FillRect(_sb, new Rectangle(x + 24, y + 24, 120, 4), new Color(38, 40, 48));
            _prim.FillRect(_sb, new Rectangle(x + 24, y + 24,
                (int)(120 * Math.Clamp(xpNeed <= 0 ? 1f : (float)u.Xp / xpNeed, 0f, 1f)), 4), new Color(120, 170, 230));
            if (u.StatPoints > 0)
                _font.Draw(_sb, $"+{u.StatPoints} PTS (E)", x + 152, y + 21, 1, new Color(240, 208, 120));
            string tag = u.IsAvatar ? "AVATAR" : "MERC";
            _font.Draw(_sb, $"{u.Name.ToUpperInvariant()}  {tag}  L{u.Level}{(u.Wounded ? "  WOUNDED" : "")}",
                x + 22, y + 3, 1, u.Wounded ? new Color(230, 200, 70) : Palette.Text);
            // Effective Dofus stats (grown by level + gear) — the class's own damage element leads
            // (Fire Cannon reads INT, Air Archer AGI, Earth Bulwark STR), then the utility stats.
            var s = TitheContent.StatsOf(u);
            var elem = TitheContent.ClassElement(u.ClassId);
            string sheet = $"{s.MaxHp} HP  {elem.ToString().ToUpperInvariant()} {TitheContent.DamageStatFor(s, elem)}"
                + $"  AGI {s.Agility}  WIS {s.Wisdom}";
            if (u.IsAvatar)
            {
                int set = TitheContent.SetPiecesEquipped(u, TitheContent.GraveyardSet);
                if (set > 0) sheet += $"   ADV {set}/7";
            }
            if (u.EssenceSlots.Count > 0)
                sheet += "   [" + string.Join(" + ", u.EssenceSlots.Select(e => e.ToUpperInvariant())) + "]";
            if (u.Temperament != Temperament.None)
                sheet += u.Vetted ? $"   ({u.Temperament.ToString().ToUpperInvariant()})" : "   (NATURE?)";
            _font.Draw(_sb, sheet, x + 22, y + 15, 1, Palette.TextDim);
            y += 34;
        }
    }

    private void DrawDiveCombatOverlay()
    {
        _sb.Begin(samplerState: SamplerState.PointClamp);
        if (_dive != null)
        {
            // A slim, always-visible bell bar at the very top so the floor clock reads during a fight.
            float frac = Math.Clamp(_dive.Clock / Math.Max(1, TitheContent.Graveyard.ClockSeconds), 0f, 1f);
            _prim.FillRect(_sb, new Rectangle(0, 0, ScreenW, 5), Palette.HpBack);
            var col = frac > 0.5f ? Palette.HpFill : frac > 0.25f ? new Color(230, 200, 70) : new Color(224, 80, 64);
            _prim.FillRect(_sb, new Rectangle(0, 0, (int)(ScreenW * frac), 5), col);
        }
        if (_cryptRun && !_placing)  // which sealing-door room you're in
            _font.DrawCentered(_sb, $"THE CRYPT  —  {_cryptRooms[_cryptRoom].Name.ToUpperInvariant()}  ({_cryptRoom + 1}/{_cryptRooms.Count})",
                ScreenW / 2, 552, 1, new Color(204, 172, 224));
        if (_jumpedFight && !_combatResolved) // caught in the open — the fight found YOU
            _font.DrawCentered(_sb, "JUMPED — THE PACK FINDS YOU IN THE OPEN", ScreenW / 2, 528, 1, new Color(224, 96, 88));
        if (_combatResolved && _fightReport != null && !_anim.IsBusy) DrawFightReport();
        _sb.End();
    }

    private void DrawFightReport()
    {
        _prim.FillRect(_sb, new Rectangle(0, 0, ScreenW, ScreenH), new Color(0, 0, 0, 155));
        var r = _fightReport!;
        bool win = r.Outcome == FightOutcome.Victory;
        bool bossRoom = _cryptRun && _cryptRooms[_cryptRoom].Boss;

        if (!_reportSounded)
        {
            _reportSounded = true;
            _sfx.Play(win ? "victory" : "defeat", 0.8f, jitter: false);
            if (win && r.Gold > 0) _sfx.Play("coin");
            if (_levelUps.Count > 0) _sfx.Play("levelup", 0.9f, jitter: false);
        }

        int rows = (_dive?.LastResolution ?? _aftermath)?.Units.Count ?? 0;
        int extras = _levelUps.Count + (_fightReport!.Gear.Count > 0 ? 1 : 0)
            + (_fightReport.Drops.Count > 0 ? 1 : 0) + (_fightReport.Lost.Count > 0 ? 1 : 0);
        var panel = new Rectangle(340, 150, 600, Math.Clamp(210 + rows * 20 + extras * 18 + (_levelUps.Count > 0 ? 22 : 0), 260, 460));
        UiPanelBg(panel);
        var ink = UiSkinned ? UiInk : Palette.Text;
        var inkDim = UiSkinned ? UiInkDim : Palette.TextDim;

        string title = !win ? "THE CREW FALLS"
            : bossRoom ? "THE SEXTON FALLS"
            : _cryptRun ? "ROOM CLEARED"
            : _jumpedFight ? "THE AMBUSH IS BEATEN" : "PACK CLEARED";
        _font.DrawCentered(_sb, title, panel.Center.X, panel.Y + 18, 3,
            win ? (UiSkinned ? new Color(52, 108, 54) : Palette.HpFill) : new Color(184, 70, 60));

        int y = panel.Y + 60;
        if (win)
        {
            _font.DrawCentered(_sb, $"+{r.Gold} GOLD      +{r.Xp} XP POOL", panel.Center.X, y, 2, ink); y += 34;

            // Per-unit XP shares with live XP bars — the Dofus end-of-fight window.
            var res = _dive?.LastResolution ?? _aftermath;
            if (res != null)
                foreach (var ur in res.Units)
                {
                    var cu = _campaign.Crew.FirstOrDefault(c => c.Id == ur.Id);
                    _font.Draw(_sb, Trunc(ur.Name.ToUpperInvariant(), 14), panel.X + 28, y, 1, ink);
                    _font.Draw(_sb, ur.Died ? "LOST" : ur.Wounded ? "WOUNDED" : $"+{ur.XpGained} XP",
                        panel.X + 170, y, 1,
                        ur.Died ? new Color(184, 70, 60) : ur.Wounded ? new Color(190, 140, 40) : inkDim);
                    if (cu != null)
                    {
                        int need = CampaignUnit.XpForNextLevel(cu.Level);
                        _prim.FillRect(_sb, new Rectangle(panel.X + 300, y + 2, 180, 8), new Color(60, 56, 50));
                        _prim.FillRect(_sb, new Rectangle(panel.X + 300, y + 2,
                            (int)(180 * Math.Clamp(need <= 0 ? 1f : (float)cu.Xp / need, 0f, 1f)), 8),
                            new Color(120, 170, 230));
                        _font.Draw(_sb, $"LVL {cu.Level}", panel.X + 492, y, 1, inkDim);
                    }
                    y += 20;
                }
            y += 8;

            foreach (var (name, level) in _levelUps)
            {
                _font.DrawCentered(_sb, $"* {name.ToUpperInvariant()} REACHES LEVEL {level}! +5 POINTS +1 SPELL *",
                    panel.Center.X, y, 1, new Color(190, 140, 20)); y += 18;
            }
            if (_levelUps.Count > 0)
            { _font.DrawCentered(_sb, "spend points in the city: E, then the + buttons", panel.Center.X, y, 1, inkDim); y += 22; }

            if (r.Gear.Count > 0)
            { _font.DrawCentered(_sb, "FOUND: " + string.Join(", ", r.Gear.Select(TitheContent.ItemName)).ToUpperInvariant(),
                panel.Center.X, y, 1, new Color(150, 110, 20)); y += 18; }
            if (r.Drops.Count > 0)
            { _font.DrawCentered(_sb, "ESSENCES: " + string.Join(", ", r.Drops).ToUpperInvariant(),
                panel.Center.X, y, 1, new Color(120, 80, 160)); y += 18; }
            if (r.Lost.Count > 0)
            { _font.DrawCentered(_sb, "LOST: " + string.Join(", ", r.Lost).ToUpperInvariant(),
                panel.Center.X, y, 1, new Color(184, 70, 60)); y += 18; }
        }
        else
        {
            _font.DrawCentered(_sb, _campaign.Over ? "CAMPAIGN OVER" : "the bell drags the survivors out",
                panel.Center.X, y, 2, new Color(184, 70, 60)); y += 36;
        }

        string next = _dive!.Ended
            ? (_campaign.Over ? "PRESS SPACE — THE CAMPAIGN IS OVER" : "THE BELL TOLLS — PRESS SPACE TO BE EJECTED")
            : bossRoom ? "THE ALTAR TEARS THE CREW OUT — PRESS SPACE"
            : _cryptRun ? "THE DOOR AHEAD GRINDS OPEN — PRESS SPACE TO PRESS DEEPER"
            : "PRESS SPACE TO PRESS ON";
        _font.DrawCentered(_sb, next, panel.Center.X, panel.Bottom - 30, 1, ink);
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}

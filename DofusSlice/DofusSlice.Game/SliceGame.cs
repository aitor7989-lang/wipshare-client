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
    private int _openNpc = -1;                        // which City building's panel is open
    private bool _equipOpen;                          // the stash & kit screen (E in the City)
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
    private TileSet _tiles = null!;                   // the 8-bit tileset skin (local-only art)
    private bool Pix => _tiles.Loaded;                // tileset present -> top-down pixel look
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
        _tiles = new TileSet(_sprites.Get("tileset"));
        _prim.SquareMode = Pix;
        _prim.SquareSize = TileSet.Cell;

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
        _proj = Pix
            ? IsoProjector.TopDownCentered(map.Width, map.Height, TileSet.Cell,
                new Vector2(ScreenW / 2f, (HudTop / 2f) - 8))
            : IsoProjector.Centered(map.Width, map.Height, TileW, TileH,
                new Vector2(ScreenW / 2f, (HudTop / 2f) - 20));
        _anim = new BattleAnimator(_proj);

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
        if (_tithe) return TitheContent.Arena();
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
                if (PanelButton(i).Contains(m)) { if (acts[i].ok) acts[i].act(); return; }
        }

        if (_hover == TitheCell) _openNpc = 0;
        else if (_hover == TempleCell) _openNpc = 1;
        else if (_hover == HireCell) _openNpc = 2;
        else if (_hover == LychgateCell) StartDive();
        else _openNpc = -1;
    }

    private static Rectangle PanelButton(int i) => new(360, 344 + i * 52, 560, 44);

    // ----- The stash & kit screen (Bible §6.13: manage the stash and equip units) -----

    private static Rectangle EquipRowRect(int i) => new(260, 214 + i * 34, 372, 30);
    private static Rectangle StashRowRect(int i) => new(648, 214 + i * 34, 372, 30);

    /// <summary>Click an equipped row to strip it to the stash; click a stash row to equip it.
    /// Only the avatar re-gears (Bible §6.6.9 — mercs keep the kit they were hired with).</summary>
    private void ClickEquipPanel(Point m)
    {
        var a = _campaign.Avatar;
        if (a == null) return;
        for (int i = 0; i < a.Equipment.Count; i++)
            if (EquipRowRect(i).Contains(m)) { _campaign.Unequip(a, a.Equipment[i]); return; }
        for (int i = 0; i < _campaign.Stash.Count; i++)
            if (StashRowRect(i).Contains(m)) { _campaign.Equip(a, _campaign.Stash[i]); return; }
    }

    private void DrawEquipPanel()
    {
        var a = _campaign.Avatar;
        if (a == null) return;
        var r = new Rectangle(236, 150, 800, 470);
        _prim.FillRect(_sb, r, new Color(22, 24, 30));
        _prim.StrokeRect(_sb, r, 2, Palette.CurrentRing);
        _font.DrawCentered(_sb, "STASH + KIT: THE AVATAR", r.Center.X, r.Y + 14, 2, Palette.Text);
        _font.Draw(_sb, "EQUIPPED  (click to strip)", 260, r.Y + 44, 1, Palette.TextDim);
        _font.Draw(_sb, "STASH  (click to equip)", 648, r.Y + 44, 1, Palette.TextDim);

        var mp = new Point(_mouse.X, _mouse.Y);
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
            _font.Draw(_sb, "— nothing worn —", 268, 220, 1, Palette.TextDim);

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
            _font.Draw(_sb, "— the stash is empty —", 656, 220, 1, Palette.TextDim);

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
                _fightReport = _dive!.ApplyResult(_pendingPack!, _engine);
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
            if (onCell is { Team: Team.Player })
            {
                _selCrew = onCell; // pick up a crew member
            }
            else if (_selCrew != null && _map.PlayerStartCells.Contains(_hover) && onCell is null)
            {
                _selCrew.Pos = _hover; // drop the selected member on a free start cell
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

        if (_loop && _scene == Scene.City) { DrawCity(); base.Draw(gameTime); return; }
        if (_loop && _scene == Scene.Graveyard) { DrawGraveyard(); base.Draw(gameTime); return; }

        DrawCombatScene();
        if (_loop) DrawDiveCombatOverlay();
        base.Draw(gameTime);
    }

    private void DrawCombatScene()
    {
        // World pass — everything on the map moves/zooms/shakes with the camera.
        _sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: _camera.View);
        DrawFloor();
        if (_placing) DrawPlacementCells(); else DrawFloorOverlays();
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
        if (Pix)
        {
            var fam = PixFamNow();
            DrawPixWalls(_engine.Field.Width, _engine.Field.Height, fam);
            foreach (var c in CellsByDepth()) DrawPixCell(c, _engine.Field.TileAt(c), fam);
            return;
        }
        foreach (var c in CellsByDepth())
        {
            var center = _proj.CellCenter(c);
            var kind = _engine.Field.TileAt(c);
            if (kind == TileKind.Void)
            {
                _prim.DiamondAt(_sb, center, new Color(16, 18, 24)); // a pit
                continue;
            }
            if (kind == TileKind.Water)
            {
                DrawWater(c, center);
                continue;
            }

            string spriteName = kind switch
            {
                TileKind.Grass2 => "tile_grass2",
                TileKind.Dirt or TileKind.Path => "tile_dirt",
                _ => "tile_grass",
            };
            var tile = _sprites.Get(spriteName) ?? _sprites.Get("tile_grass");
            if (tile != null)
            {
                _sb.Draw(tile, new Vector2(center.X - TileW / 2f, center.Y - TileH / 2f), Color.White);
            }
            else
            {
                _prim.DiamondAt(_sb, center, FloorColor(kind, c));
                DrawTileOutline(center, Palette.TileEdge);
            }
        }
    }

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

    /// <summary>Variants grow in 2×2 clumps, never lone tiles (TILESET-RULES §2): hash the block.</summary>
    private static int PixBlockHash(CellCoord c) =>
        ((c.X >> 1) * 73856093 ^ (c.Y >> 1) * 19349663) & 0x7fffffff;

    /// <summary>City rug rectangles anchored to the service landmarks (TILESET-RULES §2).</summary>
    private int PixRugAt(CellCoord c)
    {
        if (c.X >= TitheCell.X - 1 && c.X <= TitheCell.X && c.Y >= TitheCell.Y + 1 && c.Y <= TitheCell.Y + 2)
            return Tid.RugGold;
        if (c.X >= TempleCell.X - 1 && c.X <= TempleCell.X && c.Y >= TempleCell.Y + 1 && c.Y <= TempleCell.Y + 2)
            return Tid.RugWeave;
        if (c.X >= HireCell.X - 1 && c.X <= HireCell.X && c.Y >= HireCell.Y + 1 && c.Y <= HireCell.Y + 2)
            return Tid.RugBlue;
        return -1;
    }

    private void DrawPixCell(CellCoord c, TileKind kind, PixFam fam)
    {
        var center = _proj.CellCenter(c);
        if (kind == TileKind.Void)
        {
            _tiles.Draw(_sb, fam == PixFam.City ? Tid.CityVoid : Tid.DunVoid, center);
            return;
        }
        if (kind == TileKind.Water)
        {
            _tiles.Draw(_sb, Tid.Water, center);
            if (PixHash(c) % 4 == 0) _tiles.Draw(_sb, Tid.WaterGlint, center, Color.White * 0.85f);
            return;
        }

        if (fam == PixFam.City)
        {
            int rug = PixRugAt(c);
            if (rug >= 0) { _tiles.Draw(_sb, rug, center); return; }
        }

        if (fam == PixFam.City)
        {
            // Room floors vary per-cell, sparsely and subtly (TILESET-RULES §2) — never in blocks.
            int ch = PixHash(c);
            _tiles.Draw(_sb, ch % 8 == 0 ? Tid.CityClumps[ch % Tid.CityClumps.Length] : Tid.CityFloor, center);
            return;
        }
        int bh = PixBlockHash(c);
        if (fam == PixFam.Crypt)
        {
            _tiles.Draw(_sb, bh % 4 == 0 ? Tid.CryptClumps[bh % Tid.CryptClumps.Length] : Tid.CryptFloor, center);
            return;
        }
        // Yard (TILESET-RULES §2): dead mixed earth per-cell, mossy growth in blocks, rare bramble.
        int tile = bh % 8 == 0 ? Tid.YardBramble
            : bh % 3 == 0 ? Tid.YardMoss[bh % Tid.YardMoss.Length]
            : Tid.YardBase[PixHash(c) % Tid.YardBase.Length];
        _tiles.Draw(_sb, tile, center);
    }

    /// <summary>
    /// The wall sandwich (TILESET-RULES §1): band + decorated face above the playfield, band +
    /// face + skirt below, side columns capped by corners, and a whisper of SE drop shadow.
    /// </summary>
    private void DrawPixWalls(int width, int height, PixFam fam)
    {
        bool city = fam == PixFam.City;
        int band = city ? Tid.CityBand : Tid.DunBand;
        int cornerL = city ? Tid.CityCornerL : Tid.DunCornerL;
        int cornerR = city ? Tid.CityCornerR : Tid.DunCornerR;
        int side = city ? Tid.CitySide : Tid.DunSide;
        int decor = city ? Tid.CityDecor : Tid.DunDecor;
        int skirt = city ? Tid.CitySkirt : Tid.DunSkirt;
        var face = city ? Tid.CityFace : Tid.DunFace;

        int Face(int x) => x % 5 == 2 ? decor : face[x % face.Length]; // decor cadence ~1-in-5

        for (int x = 0; x < width; x++)
        {
            _tiles.Draw(_sb, band, _proj.CellCenter(x, -2));      // north band
            _tiles.Draw(_sb, Face(x), _proj.CellCenter(x, -1));   // north face (decorated)
            _tiles.Draw(_sb, band, _proj.CellCenter(x, height));  // south band
            _tiles.Draw(_sb, Face(x + 3), _proj.CellCenter(x, height + 1)); // south face
            _tiles.Draw(_sb, PixHash(new CellCoord(x, height + 2)) % 5 == 0 ? Tid.DunSkirtAlt : skirt,
                _proj.CellCenter(x, height + 2));                 // skirt
        }
        _tiles.Draw(_sb, cornerL, _proj.CellCenter(-1, -2));
        _tiles.Draw(_sb, cornerR, _proj.CellCenter(width, -2));
        _tiles.Draw(_sb, cornerL, _proj.CellCenter(-1, height));
        _tiles.Draw(_sb, cornerR, _proj.CellCenter(width, height));
        for (int y = -1; y <= height + 2; y++)
        {
            _tiles.Draw(_sb, side, _proj.CellCenter(-1, y));
            _tiles.Draw(_sb, side, _proj.CellCenter(width, y));
            // SE drop shadow: sparse void tiles hugging the right wall (TILESET-RULES §5.2).
            if (PixHash(new CellCoord(width + 1, y)) % 3 != 0)
                _tiles.Draw(_sb, city ? Tid.CityVoid : Tid.DunVoid, _proj.CellCenter(width + 1, y));
        }
    }

    /// <summary>Sprite + idle-frame count for a unit archetype (pairs animate at 2 fps).</summary>
    private static (int idx, int frames) PixSprite(string archetype) => archetype switch
    {
        "archer" => (Tid.FigA, 2), "bulwark" => (Tid.FigB, 2), "cannon" => (Tid.FigC, 2),
        "barrow_husk" => (Tid.FigD, 2), "crypt_warden" => (Tid.FigB, 2),
        "marrow_spitter" => (Tid.Crab, 1), "gravehound" => (Tid.Spider, 2),
        "grave_mite" => (Tid.Mite, 1), "bone_piper" => (Tid.Bird, 2),
        "sexton" => (Tid.FigD, 2),
        _ => (Tid.FigA, 2),
    };

    private void DrawFloorOverlays()
    {
        if (_engine.Outcome != FightOutcome.Ongoing) return;

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
        foreach (var cell in _map.PlayerStartCells)
            if (_engine.FighterAt(cell) is null)
                _prim.DiamondAt(_sb, _proj.CellCenter(cell), Palette.PlacementCell);

        if (_engine.Field.InBounds(_hover) && _hover.Y >= 0)
            DrawTileOutline(_proj.CellCenter(_hover), Color.White);
    }

    private void DrawPlacementHud()
    {
        _font.Draw(_sb, "PLACEMENT", 16, 12, 3, Palette.Text);
        if (_tithe)
        {
            _font.Draw(_sb, "CLICK A CREW MEMBER, THEN A BLUE CELL TO POSITION THEM", 16, 40, 1, Palette.TextDim);
            _font.Draw(_sb, "PLACE THE SQUISHY BACKLINE SAFE FROM THE FLANKING GRAVEHOUNDS", 16, 54, 1, Palette.TextDim);
        }
        else
        {
            _font.Draw(_sb, "CLICK A BLUE CELL TO POSITION YOUR IOP", 16, 40, 1, Palette.TextDim);
            _font.Draw(_sb, "THEN PRESS FIGHT (OR SPACE) TO BEGIN", 16, 54, 1, Palette.TextDim);
        }
        DrawTurnTimeline(); // preview the fighters you'll face

        _prim.FillRect(_sb, new Rectangle(0, HudTop, ScreenW, ScreenH - HudTop), Palette.HudPanel);
        if (_tithe) DrawCrewRoster();
        _font.DrawCentered(_sb, _tithe ? "PLACE YOUR CREW, THEN PRESS FIGHT — THEN WATCH"
                                       : "POSITION YOUR HERO ON A BLUE STARTING CELL, THEN FIGHT",
            ScreenW / 2, _tithe ? HudTop + 14 : HudTop + 60, 2, Palette.Text);

        var r = _endTurnButton;
        bool hover = r.Contains(new Point(_mouse.X, _mouse.Y));
        _prim.FillRect(_sb, r, hover ? Palette.HudPanelLight : Palette.HudPanel);
        _prim.StrokeRect(_sb, r, 2, Palette.HpFill);
        _font.DrawCentered(_sb, "FIGHT!", r.Center.X, r.Y + 34, 4, Palette.Text);
        _font.DrawCentered(_sb, "(SPACE)", r.Center.X, r.Y + 80, 1, Palette.TextDim);
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

        foreach (var f in _engine.Fighters.Where(x => x.IsAlive))
        {
            var fighter = f;
            items.Add((_anim.CenterFor(fighter).Y, 1, () => DrawFighter(fighter)));
        }

        foreach (var it in items.OrderBy(i => i.depth).ThenBy(i => i.tie))
            it.draw();
    }

    private void DrawObstacle(CellCoord c) => DrawObstacleKind(_proj.CellCenter(c), _engine.Field.TileAt(c));

    private void DrawObstacleKind(Vector2 center, TileKind kind)
    {
        if (Pix)
        {
            // A tombstone in the yard, a stone lump in the crypt, an evergreen for trees.
            if (kind == TileKind.Tree) { _tiles.DrawFeet(_sb, Tid.Tree, center, null, 1.4f); return; }
            if (PixFamNow() == PixFam.Crypt) { _tiles.DrawFeet(_sb, Tid.RockBlob, center, null, 1.2f); return; }
            _tiles.DrawFeet(_sb, Tid.Tombstone, center, new Color(206, 206, 218), 1.2f);
            return;
        }
        if (kind == TileKind.Tree)
        {
            var tree = _sprites.Get("tile_tree");
            if (tree != null) { DrawSpriteFeet(tree, center, Color.White, TileH + 40); return; }
            DrawProceduralTree(center);
            return;
        }
        var rock = _sprites.Get("tile_rock");
        if (rock != null)
        {
            DrawSpriteFeet(rock, center, Color.White, TileH + 24);
            return;
        }
        _prim.DiscAt(_sb, center + new Vector2(0, 2), 12, Palette.Shadow);
        _prim.DiscAt(_sb, center + new Vector2(0, -5), 12, Palette.Obstacle);
        _prim.DiscAt(_sb, center + new Vector2(0, -9), 9, Palette.ObstacleTop);
        _prim.DiscAt(_sb, center + new Vector2(-4, -12), 4, new Color(150, 136, 120));
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
            // Team pad under the feet: the figures are black silhouettes (the pack's idiom), so
            // the class/enemy colour lives in the ground pad and the HP bar above.
            var pad = crew ? col : new Color(206, 66, 54);
            _prim.DiscAt(_sb, center + new Vector2(0, TileSet.Cell * 0.30f), TileSet.Cell * 0.30f, pad * 0.8f);
            var (idx, frames) = PixSprite(f.Archetype);
            int frame = frames > 1 ? ((int)(_time * 2) + (f.Id.GetHashCode() & 1)) % frames : 0;
            float sc = f.Archetype == "sexton" ? 1.8f : 1.2f;
            var stint = flash > 0f ? Color.Lerp(Color.White, new Color(255, 90, 90), flash) : Color.White;
            _tiles.DrawFeet(_sb, idx + frame, center, stint, sc);
            float spriteTop = center.Y + TileSet.Cell / 2f - TileSet.Cell * sc;
            DrawHpBar(f, center.X, spriteTop - 10);
            DrawStatusPips(f, center.X, spriteTop - 2);
            return;
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
    }

    private void DrawTitheHud()
    {
        string place = _loop ? (_cryptRun ? "THE CRYPT" : "THE GRAVEYARD")
            : _boss ? "THE SEXTON'S COURT" : "THE GRAVEYARD";
        _font.Draw(_sb, $"ROUND {_engine.Round}   {place}", 16, 12, 2, Palette.Text);
        _font.Draw(_sb, $"WATCHING — {_engine.Current.Name.ToUpperInvariant()}", 16, 32, 2,
            _engine.Current.Team == Team.Player ? Palette.HpFill : Palette.EnemyColor);
        // Only advertise keys that work here: R/B restart or swap the STANDALONE fight and would
        // mislead during a campaign fight, where the dive owns the flow.
        _font.Draw(_sb, _loop ? "1/2/3 = SPEED" : "1/2/3 = SPEED   ·   R = NEW FIGHT   ·   B = SEXTON",
            16, HudTop - 22, 1, Palette.TextDim);

        // Playback speed, top-centre where the piloted mode shows the turn clock.
        _font.DrawCentered(_sb, $"> SPEED {_speed:0}X", ScreenW / 2, 16, 2, Palette.Text);

        DrawTurnTimeline();

        int ly = 92;
        foreach (var line in _log)
        {
            _font.Draw(_sb, Trunc(line, 44), 940, ly, 1, Palette.TextDim);
            ly += 12;
        }

        _prim.FillRect(_sb, new Rectangle(0, HudTop, ScreenW, ScreenH - HudTop), Palette.HudPanel);
        DrawCrewRoster();
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
        var order = _engine.Fighters;
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

            _prim.FillRect(_sb, r, current ? Palette.HudPanelLight : Palette.HudPanel);
            _prim.StrokeRect(_sb, r, current ? 2 : 1,
                current ? Palette.CurrentRing : new Color(60, 64, 72));

            var token = f.IsAlive
                ? (_tithe ? TitheTokenColor(f.Archetype)
                          : f.PlayerControlled ? Palette.HeroColor : Palette.CreatureColor(f.Name))
                : new Color(58, 60, 66);
            var mid = new Vector2(r.X + 17, r.Y + cardH / 2f);
            _prim.DiscAt(_sb, mid, 11, new Color(18, 18, 22));
            _prim.DiscAt(_sb, mid, 9, token);

            _font.Draw(_sb, Trunc(f.Name.ToUpperInvariant(), 7), r.X + 32, r.Y + 9, 1,
                f.IsAlive ? Palette.Text : Palette.TextDim);
            _font.Draw(_sb, f.IsAlive ? $"{f.Hp} HP" : "DEAD", r.X + 32, r.Y + 25, 1,
                f.IsAlive ? (f.Team == Team.Player ? Palette.HpFill : new Color(210, 150, 90))
                          : new Color(200, 90, 80));

            if (current)
                _prim.FillRect(_sb, new Rectangle(r.X, r.Bottom + 2, cardW, 3), Palette.CurrentRing);
        }
    }

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
        if (Pix)
        {
            var fam = PixFamNow();
            DrawPixWalls(map.Width, map.Height, fam);
            foreach (var c in PlaneCellsByDepth(map)) DrawPixCell(c, map.Tile(c.X, c.Y), fam);
            return;
        }
        foreach (var c in PlaneCellsByDepth(map))
        {
            var center = _proj.CellCenter(c);
            var kind = map.Tile(c.X, c.Y);
            if (kind == TileKind.Void) { _prim.DiamondAt(_sb, center, new Color(16, 18, 24)); continue; }
            if (kind == TileKind.Water) { DrawWater(c, center); continue; }

            string sprite = kind switch
            {
                TileKind.Grass2 => "tile_grass2",
                TileKind.Dirt or TileKind.Path => "tile_dirt",
                _ => "tile_grass",
            };
            var tile = _sprites.Get(sprite) ?? _sprites.Get("tile_grass");
            if (tile != null)
                _sb.Draw(tile, new Vector2(center.X - TileW / 2f, center.Y - TileH / 2f), Color.White);
            else { _prim.DiamondAt(_sb, center, FloorColor(kind, c)); DrawTileOutline(center, Palette.TileEdge); }
        }
    }

    // ----- City ---------------------------------------------------------------------

    private void DrawCity()
    {
        _sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: _camera.View);
        DrawPlaneTiles(_cityMap);
        foreach (var cell in new[] { TitheCell, TempleCell, HireCell, LychgateCell })
            if (cell == _hover) _prim.DiamondAt(_sb, _proj.CellCenter(cell), Palette.CurrentRing * 0.30f);
        DrawBuilding(TitheCell, new Color(214, 176, 84), "T", "TITHE-KEEPER", Tid.Chest);
        DrawBuilding(TempleCell, new Color(186, 150, 220), "+", "TEMPLE SISTER", Tid.Statue);
        DrawBuilding(HireCell, new Color(150, 190, 140), "H", "HIRING POST", Tid.FigB);
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

    private void DrawBuilding(CellCoord c, Color col, string glyph, string label, int pixTile = -1)
    {
        var center = _proj.CellCenter(c);
        if (Pix && pixTile >= 0)
        {
            _tiles.DrawFeet(_sb, pixTile, center, null, 1.6f);
            _font.DrawCentered(_sb, label, (int)center.X, (int)center.Y - 48, 1, Palette.Text);
            return;
        }
        var outline = new Color(16, 16, 20);
        _prim.DiscAt(_sb, center + new Vector2(0, 2), 14, Palette.Shadow);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 14, (int)center.Y - 34, 28, 36), outline);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 12, (int)center.Y - 32, 24, 32), col);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 12, (int)center.Y - 32, 24, 7), col * 1.25f);
        _font.DrawCentered(_sb, glyph, (int)center.X, (int)center.Y - 22, 2, Color.White);
        _font.DrawCentered(_sb, label, (int)center.X, (int)center.Y - 48, 1, Palette.Text);
    }

    private void DrawLychgate(CellCoord c)
    {
        var center = _proj.CellCenter(c);
        if (Pix)
        {
            _tiles.DrawFeet(_sb, Tid.Arch, center, new Color(226, 226, 238), 2.2f);
            _tiles.Draw(_sb, Tid.Torch, center + new Vector2(-TileSet.Cell, -6), null, 1f);
            _tiles.Draw(_sb, Tid.Torch, center + new Vector2(TileSet.Cell, -6), null, 1f);
            _font.DrawCentered(_sb, "LYCHGATE", (int)center.X, (int)center.Y - 74, 1, new Color(200, 200, 220));
            return;
        }
        var col = new Color(120, 120, 140);
        _prim.DiscAt(_sb, center + new Vector2(0, 2), 18, Palette.Shadow);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 18, (int)center.Y - 42, 8, 42), col);
        _prim.FillRect(_sb, new Rectangle((int)center.X + 10, (int)center.Y - 42, 8, 42), col);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 20, (int)center.Y - 48, 40, 8), col);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 10, (int)center.Y - 40, 20, 40), new Color(8, 8, 12));
        _font.DrawCentered(_sb, "LYCHGATE", (int)center.X, (int)center.Y - 62, 1, new Color(200, 200, 220));
    }

    private void DrawNpcPanel(int npc)
    {
        var r = new Rectangle(330, 296, 620, 336); // tall enough for the Temple's five services
        _prim.FillRect(_sb, r, new Color(22, 24, 30));
        _prim.StrokeRect(_sb, r, 2, Palette.CurrentRing);
        string[] titles = { "THE TITHE-KEEPER", "THE TEMPLE SISTER", "THE HIRING POST" };
        _font.DrawCentered(_sb, titles[npc], r.Center.X, r.Y + 14, 2, Palette.Text);

        var acts = NpcActions(npc);
        for (int i = 0; i < acts.Count; i++)
        {
            var b = PanelButton(i);
            bool hover = b.Contains(new Point(_mouse.X, _mouse.Y));
            _prim.FillRect(_sb, b, acts[i].ok ? (hover ? Palette.HudPanelLight : Palette.HudPanel) : new Color(30, 30, 34));
            _prim.StrokeRect(_sb, b, 1, acts[i].ok ? new Color(96, 150, 96) : new Color(60, 60, 66));
            _font.Draw(_sb, acts[i].label, b.X + 14, b.Y + 16, 1, acts[i].ok ? Palette.Text : Palette.TextDim);
        }
        _font.DrawCentered(_sb, "(ESC TO CLOSE)", r.Center.X, r.Bottom - 20, 1, Palette.TextDim);
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
        DrawCrypt();
        if (_dive != null)
            foreach (var p in _dive.Packs)
                if (!p.Cleared && _packCells.TryGetValue(p.Def.Id, out var cell))
                    DrawPackToken(cell, p);
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
            _prim.DiscAt(_sb, center + new Vector2(0, 12), 12, new Color(120, 190, 150) * 0.55f);
            _tiles.DrawFeet(_sb, Tid.FigA + (int)(_time * 2) % 2, center, null, 1f);
            _tiles.Draw(_sb, Tid.Question, center + new Vector2(12, -TileSet.Cell), null, 0.8f);
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
        if (Pix)
        {
            _tiles.DrawFeet(_sb, Tid.Arch, center, col * 1.4f, 2.2f);
            if (!_cryptCleared && !locked)
                _tiles.Draw(_sb, Tid.Torch, center + new Vector2(0, -TileSet.Cell * 1.6f), null, 1f);
            _font.DrawCentered(_sb, "THE CRYPT", (int)center.X, (int)center.Y - 74, 1,
                _cryptCleared ? Palette.TextDim : new Color(204, 172, 224));
            string psub = _cryptCleared ? "cleared" : locked ? $"LVL {CryptLevel}+" : "OPEN — THE SEXTON";
            _font.DrawCentered(_sb, psub, (int)center.X, (int)center.Y - 62, 1,
                locked ? new Color(222, 122, 92) : new Color(200, 160, 120));
            return;
        }
        _prim.DiscAt(_sb, center + new Vector2(0, 2), 18, Palette.Shadow);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 18, (int)center.Y - 42, 36, 44), new Color(14, 14, 18));
        _prim.FillRect(_sb, new Rectangle((int)center.X - 15, (int)center.Y - 38, 30, 38), col * 0.55f);
        _prim.FillRect(_sb, new Rectangle((int)center.X - 9, (int)center.Y - 32, 18, 32), new Color(6, 6, 10));
        _font.DrawCentered(_sb, "THE CRYPT", (int)center.X, (int)center.Y - 56, 1,
            _cryptCleared ? Palette.TextDim : new Color(204, 172, 224));
        string sub = _cryptCleared ? "cleared" : locked ? $"LVL {CryptLevel}+" : "OPEN — THE SEXTON";
        _font.DrawCentered(_sb, sub, (int)center.X, (int)center.Y - 44, 1,
            locked ? new Color(222, 122, 92) : new Color(200, 160, 120));
    }

    private void DrawPartyToken(Vector2 center)
    {
        if (Pix)
        {
            int t = (int)(_time * 2);
            _tiles.DrawFeet(_sb, Tid.FigA + t % 2, center + new Vector2(-11, 0), null, 1f);
            _tiles.DrawFeet(_sb, Tid.FigB + (t + 1) % 2, center + new Vector2(11, 2), null, 1f);
            _tiles.DrawFeet(_sb, Tid.FigC + t % 2, center + new Vector2(0, -7), null, 1f);
            _font.DrawCentered(_sb, "CREW", (int)center.X, (int)center.Y - 44, 1, Palette.Text);
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
            var (idx, fr) = PixSprite(p.Def.Comp[0]);
            int n = Math.Min(size, 3);
            var mobTint = afford ? Color.White : new Color(120, 120, 128);
            for (int i = 0; i < n; i++)
            {
                var o = new Vector2((i - (n - 1) / 2f) * 12, (i % 2) * 6 - 2);
                _tiles.DrawFeet(_sb, idx + (fr > 1 ? (i + (int)(_time * 2)) % fr : 0), center + o, mobTint, 1f);
            }
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
        _font.Draw(_sb, _scene == Scene.City ? "THE CITY" : "THE GRAVEYARD", 16, 12, 3, Palette.Text);

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
        string title = !win ? "THE CREW FALLS"
            : bossRoom ? "THE SEXTON FALLS"
            : _cryptRun ? "ROOM CLEARED"
            : _jumpedFight ? "THE AMBUSH IS BEATEN" : "PACK CLEARED";
        _font.DrawCentered(_sb, title, ScreenW / 2, 175, 5, win ? Palette.HpFill : Palette.HeroColor);

        int y = 265;
        if (win)
        {
            _font.DrawCentered(_sb, $"+{r.Gold} GOLD    +{r.Xp} XP", ScreenW / 2, y, 2, Palette.Text); y += 36;
            if (r.Gear.Count > 0)
            { _font.DrawCentered(_sb, "★ FOUND: " + string.Join(", ", r.Gear).ToUpperInvariant() + " ★", ScreenW / 2, y, 1, new Color(240, 208, 120)); y += 24; }
            if (r.Drops.Count > 0)
            { _font.DrawCentered(_sb, "ESSENCES: " + string.Join(", ", r.Drops).ToUpperInvariant(), ScreenW / 2, y, 1, new Color(200, 170, 240)); y += 24; }
            if (r.Wounded.Count > 0)
            { _font.DrawCentered(_sb, "WOUNDED: " + string.Join(", ", r.Wounded).ToUpperInvariant(), ScreenW / 2, y, 1, new Color(230, 200, 70)); y += 24; }
            if (r.Lost.Count > 0)
            { _font.DrawCentered(_sb, "LOST: " + string.Join(", ", r.Lost).ToUpperInvariant(), ScreenW / 2, y, 1, new Color(214, 96, 88)); y += 24; }
        }
        else { _font.DrawCentered(_sb, "CAMPAIGN OVER", ScreenW / 2, y, 2, Palette.HeroColor); y += 36; }

        string next = _dive!.Ended
            ? (_campaign.Over ? "PRESS SPACE — THE CAMPAIGN IS OVER" : "THE BELL TOLLS — PRESS SPACE TO BE EJECTED")
            : bossRoom ? "THE ALTAR TEARS THE CREW OUT — PRESS SPACE"
            : _cryptRun ? "THE DOOR AHEAD GRINDS OPEN — PRESS SPACE TO PRESS DEEPER"
            : "PRESS SPACE TO PRESS ON";
        _font.DrawCentered(_sb, next, ScreenW / 2, y + 22, 1, Palette.Text);
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}

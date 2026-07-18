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

    public SliceGame(bool tithe = false, int startSeed = 1, bool boss = false)
    {
        _tithe = tithe;
        _seed = startSeed;
        _boss = boss;
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

        _map = LoadMap();
        _proj = IsoProjector.Centered(_map.Width, _map.Height, TileW, TileH,
            new Vector2(ScreenW / 2f, (HudTop / 2f) - 20));
        _anim = new BattleAnimator(_proj);

        // Camera views the play area above the HUD; clamp to the map's world bounds.
        _camera = new Camera2D(ScreenW, HudTop);
        var corners = new[]
        {
            _proj.CellCenter(0, 0), _proj.CellCenter(_map.Width - 1, 0),
            _proj.CellCenter(0, _map.Height - 1),
            _proj.CellCenter(_map.Width - 1, _map.Height - 1),
        };
        var min = new Vector2(corners.Min(c => c.X) - TileW, corners.Min(c => c.Y) - TileH * 2f);
        var max = new Vector2(corners.Max(c => c.X) + TileW, corners.Max(c => c.Y) + TileH);
        _camera.SetBounds(min, max);
        _camera.Center = (min + max) / 2f;

        StartFight();
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

    // ----- Update -------------------------------------------------------------------

    protected override void Update(GameTime gameTime)
    {
        _prevMouse = _mouse; _mouse = Mouse.GetState();
        _prevKeys = _keys; _keys = Keyboard.GetState();

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
            // Hold the end screen until the final death/hit animation has played out.
            if (_engine.Outcome != FightOutcome.Ongoing && !_anim.IsBusy) DrawEndOverlay();
        }
        _sb.End();

        base.Draw(gameTime);
    }

    private IEnumerable<CellCoord> CellsByDepth() =>
        _engine.Field.AllCells().OrderBy(c => c.X + c.Y);

    private static bool IsObstacle(Battlefield f, CellCoord c) =>
        !f.IsWalkable(c) && f.BlocksLineOfSight(c);

    /// <summary>The flat ground: each cell's tile by kind (sprite, or a procedural fallback).</summary>
    private void DrawFloor()
    {
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

    private void DrawObstacle(CellCoord c)
    {
        var center = _proj.CellCenter(c);
        if (_engine.Field.TileAt(c) == TileKind.Tree)
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
        _font.Draw(_sb, $"ROUND {_engine.Round}   {(_boss ? "THE SEXTON'S COURT" : "THE GRAVEYARD")}", 16, 12, 2, Palette.Text);
        _font.Draw(_sb, $"WATCHING — {_engine.Current.Name.ToUpperInvariant()}", 16, 32, 2,
            _engine.Current.Team == Team.Player ? Palette.HpFill : Palette.EnemyColor);
        _font.Draw(_sb, "1/2/3 = SPEED   ·   R = NEW FIGHT   ·   B = SEXTON", 16, HudTop - 22, 1, Palette.TextDim);

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

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}

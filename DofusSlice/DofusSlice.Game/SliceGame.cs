using DofusSlice.Core.AI;
using DofusSlice.Core.Combat;
using DofusSlice.Core.Content;
using DofusSlice.Core.Grid;
using DofusSlice.Core.Spells;
using DofusSlice.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DofusSlice.Game;

/// <summary>
/// The playable slice: a Dofus-style isometric tactical fight. This class is only
/// presentation + input — every rule lives in <see cref="CombatEngine"/>. It renders the
/// grid, lets the player drive the Iop with mouse/keyboard, and auto-runs the mob turns.
/// </summary>
public sealed class SliceGame : Microsoft.Xna.Framework.Game
{
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

    private CombatEngine _engine = null!;
    private int _seed = 1;
    private readonly List<string> _log = new();

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

    private readonly Rectangle[] _spellButtons = new Rectangle[4];
    private Rectangle _endTurnButton;

    public SliceGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = ScreenW,
            PreferredBackBufferHeight = ScreenH,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "Dofus Slice — Incarnam Combat (Iop)";
    }

    private IReadOnlyList<SpellDef> HeroSpells => SpellLibrary.IopSpells;
    private Fighter? Hero => _engine.Fighters.FirstOrDefault(f => f.Team == Team.Player);

    protected override void Initialize()
    {
        for (int i = 0; i < 4; i++) _spellButtons[i] = new Rectangle(16 + i * 168, 636, 156, 104);
        _endTurnButton = new Rectangle(1080, 636, 184, 104);
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _sb = new SpriteBatch(GraphicsDevice);
        _prim = new Primitives(GraphicsDevice, TileW, TileH, 64);
        _font = new PixelFont(_prim.Pixel);
        _proj = IsoProjector.Centered(Encounter.Width, Encounter.Height, TileW, TileH,
            new Vector2(ScreenW / 2f, (HudTop / 2f) - 20));
        StartFight();
    }

    private void StartFight()
    {
        _engine = Encounter.CreateIncarnamSandbox(new SystemRng(_seed));
        _log.Clear();
        _engine.Logged += line =>
        {
            _log.Add(line);
            if (_log.Count > 8) _log.RemoveAt(0);
        };
        _engine.Start();
        _selectedSpell = -1;
        _enemyTimer = 0f;
    }

    // ----- Update -------------------------------------------------------------------

    protected override void Update(GameTime gameTime)
    {
        _prevMouse = _mouse; _mouse = Mouse.GetState();
        _prevKeys = _keys; _keys = Keyboard.GetState();

        if (Pressed(Keys.R)) { _seed++; StartFight(); return; }

        _hover = _proj.ScreenToCell(new Vector2(_mouse.X, _mouse.Y));

        if (_engine.Outcome != FightOutcome.Ongoing) { base.Update(gameTime); return; }

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // A change of active fighter starts a fresh turn clock.
        if (_engine.Current.Id != _turnOwner)
        {
            _turnOwner = _engine.Current.Id;
            _turnClock = TurnSeconds;
            _enemyTimer = 0f;
        }

        if (_engine.Current.Team == Team.Player)
            UpdatePlayerTurn(dt);
        else
            UpdateEnemyTurn(dt);

        base.Update(gameTime);
    }

    private void UpdateEnemyTurn(float dt)
    {
        _enemyTimer += dt;
        if (_enemyTimer < EnemyStepDelay) return;
        _enemyTimer = 0f;
        MobBrain.TakeTurn(_engine, _engine.Current);
        _engine.EndTurn();
    }

    private void UpdatePlayerTurn(float dt)
    {
        var hero = Hero;
        if (hero == null) return;
        _moveRange = _engine.MovementRange(hero);

        // Turn timer: auto-end when it runs out.
        _turnClock -= dt;
        if (_turnClock <= 0f) { EndPlayerTurn(); return; }

        // Spell hotkeys.
        if (Pressed(Keys.D1)) ToggleSpell(0);
        if (Pressed(Keys.D2)) ToggleSpell(1);
        if (Pressed(Keys.D3)) ToggleSpell(2);
        if (Pressed(Keys.D4)) ToggleSpell(3);
        if (Pressed(Keys.Escape)) _selectedSpell = -1;
        if (Pressed(Keys.Space)) { EndPlayerTurn(); return; }

        if (RightClicked()) _selectedSpell = -1;

        if (!LeftClicked()) return;
        var m = new Point(_mouse.X, _mouse.Y);

        for (int i = 0; i < 4; i++)
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
        _sb.Begin(samplerState: SamplerState.PointClamp);

        DrawTiles();
        DrawOverlays();
        DrawFighters();
        DrawHud();
        if (_engine.Outcome != FightOutcome.Ongoing) DrawEndOverlay();

        _sb.End();
        base.Draw(gameTime);
    }

    private IEnumerable<CellCoord> CellsByDepth() =>
        _engine.Field.AllCells().OrderBy(c => c.X + c.Y);

    private void DrawTiles()
    {
        foreach (var c in CellsByDepth())
        {
            var center = _proj.CellCenter(c);
            bool obstacle = !_engine.Field.IsWalkable(c) && _engine.Field.BlocksLineOfSight(c);
            Color baseColor = ((c.X + c.Y) % 2 == 0) ? Palette.TileA : Palette.TileB;
            _prim.DiamondAt(_sb, center, obstacle ? Palette.Obstacle : baseColor);
            DrawTileOutline(center, Palette.TileEdge);
            if (obstacle)
            {
                _prim.DiscAt(_sb, center + new Vector2(0, -6), 11, Palette.ObstacleTop);
                _prim.DiscAt(_sb, center + new Vector2(-4, -9), 5, new Color(140, 126, 110));
            }
        }
    }

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

    private void DrawOverlays()
    {
        if (_engine.Outcome != FightOutcome.Ongoing) return;
        var hero = Hero;
        bool playerTurn = _engine.Current.Team == Team.Player && hero != null;

        if (playerTurn && _selectedSpell < 0)
            foreach (var cell in _moveRange.Keys)
                _prim.DiamondAt(_sb, _proj.CellCenter(cell), Palette.MoveRange);

        if (playerTurn && _selectedSpell >= 0)
        {
            var spell = HeroSpells[_selectedSpell];

            // Dim wash over the spell's whole reach, brighter on cells that are legal targets.
            foreach (var cell in _engine.SpellReachCells(hero!, spell))
                _prim.DiamondAt(_sb, _proj.CellCenter(cell), Palette.CastReach);

            var castable = _engine.CastableCells(hero!, spell);
            foreach (var cell in castable)
                _prim.DiamondAt(_sb, _proj.CellCenter(cell), Palette.CastRange);

            if (castable.Contains(_hover))
                foreach (var cell in _engine.AreaCells(spell, _hover))
                    _prim.DiamondAt(_sb, _proj.CellCenter(cell), Palette.Aoe);
        }

        if (_engine.Field.InBounds(_hover) && _hover.Y >= 0)
            DrawTileOutline(_proj.CellCenter(_hover), Color.White);
    }

    private void DrawFighters()
    {
        foreach (var f in _engine.Fighters.Where(f => f.IsAlive).OrderBy(f => f.Pos.X + f.Pos.Y))
        {
            var center = _proj.CellCenter(f.Pos);
            var head = center + new Vector2(0, -16);

            _prim.DiscAt(_sb, center + new Vector2(0, 2), 13, Palette.Shadow);

            if (f == _engine.Current)
                _prim.DiscAt(_sb, head, 17, Palette.CurrentRing);

            var body = f.Team == Team.Player ? Palette.HeroColor : Palette.CreatureColor(f.Name);
            _prim.DiscAt(_sb, head, 15, new Color(20, 20, 24));
            _prim.DiscAt(_sb, head, 13, body);

            // HP bar.
            int barW = 30;
            var barPos = new Point((int)center.X - barW / 2, (int)(center.Y - 44));
            _prim.FillRect(_sb, new Rectangle(barPos.X, barPos.Y, barW, 5), Palette.HpBack);
            int fill = (int)MathF.Round(barW * f.Hp / (float)f.MaxHp);
            _prim.FillRect(_sb, new Rectangle(barPos.X, barPos.Y, fill, 5),
                f.Team == Team.Player ? Palette.HpFill : new Color(210, 96, 88));
            _font.DrawCentered(_sb, f.Hp.ToString(), (int)center.X, barPos.Y - 9, 1, Palette.Text);
        }
    }

    // ----- HUD ----------------------------------------------------------------------

    private void DrawHud()
    {
        var hero = Hero;

        // Top-left status.
        _font.Draw(_sb, $"ROUND {_engine.Round}", 16, 12, 2, Palette.Text);
        _font.Draw(_sb, $"{_engine.Current.Name.ToUpperInvariant()}'S TURN", 16, 32, 2,
            _engine.Current.Team == Team.Player ? Palette.HpFill : Palette.EnemyColor);
        _font.Draw(_sb, "CLICK MOVE   1-4 SPELL   SPACE END   R RESTART", 16, HudTop - 22, 1, Palette.TextDim);

        DrawTurnTimer();

        // Turn order (top-right).
        int tx = ScreenW - 20;
        foreach (var f in _engine.Fighters.Reverse())
        {
            var c = new Vector2(tx - 16, 26);
            _prim.DiscAt(_sb, c, 12, f == _engine.Current ? Palette.CurrentRing : new Color(20, 20, 24));
            _prim.DiscAt(_sb, c, 10, f.IsAlive
                ? (f.Team == Team.Player ? Palette.HeroColor : Palette.CreatureColor(f.Name))
                : new Color(50, 50, 54));
            _font.DrawCentered(_sb, f.IsAlive ? f.Hp.ToString() : "X", (int)c.X, 42, 1, Palette.Text);
            tx -= 40;
        }

        // Combat log (right side of playfield).
        int ly = 70;
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

    private void DrawTurnTimer()
    {
        bool playerTurn = _engine.Current.Team == Team.Player;
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
        for (int i = 0; i < 4 && i < HeroSpells.Count; i++)
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
            string range = spell.MinRange == spell.MaxRange ? $"RNG {spell.MaxRange}" : $"RNG {spell.MinRange}-{spell.MaxRange}";
            _font.Draw(_sb, range, r.X + 10, r.Y + 78, 1, Palette.TextDim);
            _font.DrawRight(_sb, spell.Cooldown > 0 ? "CD" : "", r.Right - 10, r.Y + 8, 1, Palette.EnemyColor);
        }
    }

    private void DrawEndTurnButton()
    {
        var r = _endTurnButton;
        bool hover = r.Contains(new Point(_mouse.X, _mouse.Y));
        bool playerTurn = _engine.Current.Team == Team.Player;
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

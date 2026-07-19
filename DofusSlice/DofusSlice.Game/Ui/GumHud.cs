using Gum.Managers;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using MonoGameGum;
using RenderingLibrary;

namespace DofusSlice.Game.Ui;

/// <summary>
/// The Gum-driven HUD layer: loads ui/TitheHud.gumx (authored in the visual Gum editor),
/// shows its CombatHud screen during watched fights, and pushes live data into elements
/// looked up BY NAME each frame — so editor hot-reloads (built into Gum) rebind for free.
/// When the project file or any expected element is missing, <see cref="Active"/> stays
/// false and the game keeps its hand-drawn HUD: the editor file is a skin, never a dependency.
/// </summary>
public sealed class GumHud
{
    private GraphicalUiElement? _screen;
    private bool _initialized;

    public GumHud(Microsoft.Xna.Framework.Game game)
    {
        try
        {
            // Prefer the SOURCE ui/ folder (what the Gum editor edits) so hot reload sees
            // saves instantly during development; published builds use the copied one.
            string rel = GumProjectEmitter.ProjectRelativePath;
            string path = new[]
                {
                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", rel)),
                    Path.Combine(AppContext.BaseDirectory, rel),
                }
                .FirstOrDefault(File.Exists) ?? "";
            if (path.Length == 0) return;

            var project = GumService.Default.Initialize(game, path);
            _initialized = true;
            _screen = project?.GetScreenSave("CombatHud")?.ToGraphicalUiElement(SystemManagers.Default);
            _screen?.AddToRoot();
            if (_screen != null) _screen.Visible = false;

            // The Gum editor saves -> the running game reloads. Re-instantiate our screen from
            // the freshly loaded project; per-frame name lookups rebind everything else.
            GumService.Default.EnableHotReload(path);
            GumService.Default.HotReloadCompleted += () =>
            {
                bool wasVisible = _screen?.Visible ?? false;
                _screen?.RemoveFromManagers();
                _screen = ObjectFinder.Self.GumProjectSave?.GetScreenSave("CombatHud")
                    ?.ToGraphicalUiElement(SystemManagers.Default);
                _screen?.AddToRoot();
                if (_screen != null) _screen.Visible = wasVisible;
            };
        }
        catch
        {
            _screen = null; // any Gum failure -> hand-drawn HUD, never a crash
        }
    }

    /// <summary>The Gum combat HUD is loaded and healthy — the hand-drawn band should yield.</summary>
    public bool Active => _screen != null;

    public void SetVisible(bool visible)
    {
        if (_screen != null) _screen.Visible = visible;
    }

    /// <summary>Push the watched-fight state into the named elements (crew capped at 4 rows).</summary>
    public void BindCombat(string round, string turn, Color turnColor,
        IReadOnlyList<(string name, int hp, int maxHp, bool alive)> crew)
    {
        if (_screen == null) return;
        SetText("RoundText", round);
        SetText("TurnText", turn);
        Set("TurnText", "Red", (int)turnColor.R); Set("TurnText", "Green", (int)turnColor.G);
        Set("TurnText", "Blue", (int)turnColor.B);
        for (int i = 0; i < 4; i++)
        {
            bool has = i < crew.Count;
            foreach (var el in new[] { $"CrewName{i}", $"CrewBarBack{i}", $"CrewBarFill{i}", $"CrewHp{i}" })
                Set(el, "Visible", has);
            if (!has) continue;
            var (name, hp, maxHp, alive) = crew[i];
            SetText($"CrewName{i}", name.ToUpperInvariant());
            SetText($"CrewHp{i}", alive ? $"{hp}/{maxHp}" : "DOWN");
            float back = Get<float>($"CrewBarBack{i}", e => e.Width, 150f);
            Set($"CrewBarFill{i}", "Width", back * Math.Clamp(maxHp <= 0 ? 0f : (float)hp / maxHp, 0f, 1f));
            Set($"CrewBarFill{i}", "Green", alive ? 200 : 70);
            Set($"CrewBarFill{i}", "Red", alive ? 96 : 180);
        }
    }

    private void SetText(string element, string value) => Set(element, "Text", value);

    private void Set(string element, string property, object value) =>
        _screen?.GetGraphicalUiElementByName(element)?.SetProperty(property, value);

    private T Get<T>(string element, Func<GraphicalUiElement, T> read, T fallback)
    {
        var el = _screen?.GetGraphicalUiElementByName(element);
        return el != null ? read(el) : fallback;
    }

    public void Update(GameTime gameTime)
    {
        if (!_initialized) return;
        try
        {
            GumService.Default.Update(gameTime);
        }
        catch
        {
            // A half-saved editor file mid-hot-reload must never kill the game; the watcher
            // fires again on the next save and the last good UI stays up meanwhile.
        }
    }

    public void Draw()
    {
        if (_initialized) GumService.Default.Draw();
    }
}

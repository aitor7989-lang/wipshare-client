using Microsoft.Xna.Framework.Graphics;

namespace DofusSlice.Game.Rendering;

/// <summary>
/// Loads optional external PNG sprites from an <c>assets/</c> folder next to the executable
/// and hands them to the renderer, falling back to procedural art when a file is absent.
///
/// This is the seam for real art: drop your own (or locally-kept datamined) PNGs into
/// <c>assets/</c> and they appear automatically. The folder is gitignored, so no third-party
/// art is ever committed or distributed with the project — see <c>assets/README.md</c>.
/// Recognised names: iop, boar, gobball, piou (fighters); tile_grass, tile_rock (map).
/// </summary>
public sealed class SpriteBank
{
    private readonly GraphicsDevice _gd;
    private readonly string _dir;
    private readonly Dictionary<string, Texture2D?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public SpriteBank(GraphicsDevice gd)
    {
        _gd = gd;
        _dir = Path.Combine(AppContext.BaseDirectory, "assets");
    }

    /// <summary>Whether any external art was found (so the UI can note it's using placeholders).</summary>
    public bool AnyLoaded { get; private set; }

    /// <summary>Returns the sprite for <paramref name="name"/>, or null to signal "draw procedurally".</summary>
    public Texture2D? Get(string name)
    {
        if (_cache.TryGetValue(name, out var cached)) return cached;

        Texture2D? tex = null;
        var path = Path.Combine(_dir, name + ".png");
        try
        {
            if (File.Exists(path))
            {
                using var fs = File.OpenRead(path);
                tex = Texture2D.FromStream(_gd, fs);
                AnyLoaded = true;
            }
        }
        catch
        {
            tex = null; // a bad/locked file just falls back to procedural art
        }

        _cache[name] = tex;
        return tex;
    }
}

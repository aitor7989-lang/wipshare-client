using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DofusSlice.Game.Rendering;

/// <summary>Draws a single sheet frame anchored at its feet (bottom-centre), honouring flip.</summary>
public static class SpriteDraw
{
    public static void Feet(SpriteBatch sb, SpriteSheet sheet, Vector2 feet, Color tint,
        float targetHeight, int frame)
    {
        float scale = targetHeight / sheet.FrameHeight;
        var origin = new Vector2(sheet.FrameWidth / 2f, sheet.FrameHeight);
        var fx = sheet.Flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        sb.Draw(sheet.Texture, feet, sheet.Frame(frame), tint, 0f, origin, scale, fx, 0f);
    }
}

/// <summary>One resolved animation strip: a texture sliced into equal horizontal frames.</summary>
public sealed class SpriteSheet
{
    public required Texture2D Texture { get; init; }
    public required int FrameWidth { get; init; }
    public required int FrameHeight { get; init; }
    public required int FrameCount { get; init; }
    /// <summary>Draw mirrored (used to derive SW/NW facings from SE/NE art).</summary>
    public bool Flip { get; init; }

    public Rectangle Frame(int index) =>
        new(Math.Clamp(index, 0, FrameCount - 1) * FrameWidth, 0, FrameWidth, FrameHeight);
}

/// <summary>
/// Loads optional external PNG sprites from an <c>assets/</c> folder next to the executable
/// and hands them to the renderer, falling back to procedural art when a file is absent.
///
/// Supports both single images (<c>iop.png</c>) and directional animation strips named
/// <c>{name}_{state}_{dir}[_{frames}].png</c> — e.g. <c>iop_walk_se_6.png</c> is a 6-frame
/// walk strip facing south-east. Missing SW/NW facings are mirrored from SE/NE. All art is
/// premultiplied on load so 32-bit RGBA (transparent-edged) sprites composite without halos.
///
/// The folder is gitignored: dropped art stays strictly local and is never committed.
/// Recognised names: iop, boar, gobball, piou; states idle/walk/cast/hurt/die; dirs se/sw/ne/nw.
/// </summary>
public sealed class SpriteBank
{
    private readonly GraphicsDevice _gd;
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string path, int frames)> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpriteSheet?> _sheetCache = new(StringComparer.OrdinalIgnoreCase);

    public bool AnyLoaded { get; private set; }

    public SpriteBank(GraphicsDevice gd)
    {
        _gd = gd;
        // Committed default art first, then the (gitignored) user folder overrides it.
        IndexDir(Path.Combine(AppContext.BaseDirectory, "assets-default"));
        IndexDir(Path.Combine(AppContext.BaseDirectory, "assets"));
    }

    private void IndexDir(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var path in Directory.EnumerateFiles(dir, "*.png"))
        {
            var key = Path.GetFileNameWithoutExtension(path);
            // A trailing _<digits> is the frame count; otherwise it's a single-frame image.
            int underscore = key.LastIndexOf('_');
            if (underscore > 0 && int.TryParse(key[(underscore + 1)..], out int frames) && frames > 0)
                _index[key[..underscore]] = (path, frames); // later dir wins (user overrides default)
            else
                _index[key] = (path, 1);
        }
    }

    // ----- Single static image (tiles, fallback token) ------------------------------

    public Texture2D? Get(string name)
    {
        if (_index.TryGetValue(name, out var entry))
            return Load(entry.path);
        return null;
    }

    // ----- Directional animation strips ---------------------------------------------

    public SpriteSheet? GetSheet(string name, string state, string dir)
    {
        string cacheKey = $"{name}|{state}|{dir}";
        if (_sheetCache.TryGetValue(cacheKey, out var cached)) return cached;

        SpriteSheet? result = null;
        foreach (var (baseName, flip) in Candidates(name, state, dir))
        {
            if (!_index.TryGetValue(baseName, out var entry)) continue;
            var tex = Load(entry.path);
            if (tex == null) continue;
            // Never let a mislabelled frame count produce a zero-width frame.
            int count = Math.Clamp(entry.frames, 1, Math.Max(1, tex.Width));
            result = new SpriteSheet
            {
                Texture = tex,
                FrameWidth = tex.Width / count,
                FrameHeight = tex.Height,
                FrameCount = count,
                Flip = flip,
            };
            break;
        }

        _sheetCache[cacheKey] = result;
        return result;
    }

    private static IEnumerable<(string name, bool flip)> Candidates(string name, string state, string dir)
    {
        yield return ($"{name}_{state}_{dir}", false);
        if (dir == "sw") yield return ($"{name}_{state}_se", true);  // mirror SE -> SW
        if (dir == "nw") yield return ($"{name}_{state}_ne", true);  // mirror NE -> NW
        yield return ($"{name}_{state}_se", false);                  // any-facing fallback
        yield return ($"{name}_{state}", false);                     // state, no direction
        yield return ($"{name}", false);                             // single static image
    }

    // ----- Loading + premultiplied-alpha fixup --------------------------------------

    private Texture2D? Load(string path)
    {
        if (_textures.TryGetValue(path, out var cached)) return cached;

        Texture2D? tex = null;
        try
        {
            using var fs = File.OpenRead(path);
            tex = Texture2D.FromStream(_gd, fs);
            Premultiply(tex);
            AnyLoaded = true;
        }
        catch
        {
            tex = null; // bad/locked file -> procedural fallback
        }

        _textures[path] = tex;
        return tex;
    }

    /// <summary>
    /// Texture2D.FromStream loads straight (non-premultiplied) RGBA, but SpriteBatch's default
    /// AlphaBlend expects premultiplied colour. Fix it up so 32-bit alpha edges don't darken.
    /// </summary>
    private static void Premultiply(Texture2D tex)
    {
        var data = new Color[tex.Width * tex.Height];
        tex.GetData(data);
        for (int i = 0; i < data.Length; i++)
        {
            var c = data[i];
            data[i] = new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A);
        }
        tex.SetData(data);
    }
}

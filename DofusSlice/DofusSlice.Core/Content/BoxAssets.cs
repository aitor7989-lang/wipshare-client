using System.Text.Json;

namespace DofusSlice.Core.Content;

/// <summary>
/// The DawnBringer-32 palette, in canonical order. This is the ONE colour table shared by the
/// BOXER editor and the game: the editor exports palette INDICES, never RGB, so recolouring the
/// game means editing this table — not re-exporting every asset. Index order is load-bearing
/// (a cell char maps positionally into it), which is why the table is built from the canonical
/// hex listing verbatim rather than hand-typed byte triples that could silently transpose.
/// </summary>
public static class Db32
{
    /// <summary>The editor's cell encoding: '.' is empty, otherwise one of these 32 chars is the
    /// palette index. Base32 keeps a voxel one character wide, so a layer stays a readable,
    /// diffable grid of text — same reasoning as <see cref="MapLoader"/>'s ASCII maps.</summary>
    public const string Base32Alphabet = "0123456789abcdefghijklmnopqrstuv";

    /// <summary>Indexed exactly like the editor's swatch strip: char <c>Base32Alphabet[i]</c>
    /// in a row string means <c>Colors[i]</c> on screen.</summary>
    public static readonly (byte R, byte G, byte B)[] Colors = Build(
        "000000 222034 45283c 663931 8f563b df7126 d9a066 eec39a fbf236 99e550 6abe30 " +
        "37946e 4b692f 524b24 323c39 3f3f74 306082 5b6ee1 639bff 5fcde4 cbdbfc ffffff " +
        "9badb7 847e87 696a6a 595652 76428a ac3232 d95763 d77bba 8f974a 8a6f30");

    private static (byte R, byte G, byte B)[] Build(string hex) =>
        hex.Split(' ')
           .Select(s => (Convert.ToByte(s[..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..], 16)))
           .ToArray();
}

/// <summary>
/// One named animation of a <see cref="BoxSprite"/>: its own playback rate and its own frame
/// strip. v3 files author several of these ("idle", "walk", …); v1/v2 files carry exactly one
/// anonymous strip, which the parser wraps as the "idle" entry — the same convert-to-one-shape
/// discipline as <see cref="TileLibrary"/>'s v1 conversion, so consumers never branch on the
/// file's vintage.
/// </summary>
public sealed class BoxAnim
{
    /// <summary>Playback rate the artist authored THIS animation at — per-anim so a 2-frame
    /// idle can breathe at 4fps while the walk in the same file cycles at 8.</summary>
    public int Fps { get; }

    /// <summary>One <c>byte[N, N, H]</c> per frame, z bottom-up (layer 0 is the feet).
    /// 0 = empty, otherwise palette index + 1 into <see cref="Db32.Colors"/>.</summary>
    public IReadOnlyList<byte[,,]> Frames { get; }

    internal BoxAnim(int fps, IReadOnlyList<byte[,,]> frames) { Fps = fps; Frames = frames; }
}

/// <summary>
/// A box character exported by BOXER (kind "char"): an n×n×h voxel prism with one or more
/// named animations (v3 "anims"), or a single anonymous strip in older exports. Voxels are
/// stored as <c>byte[x, y, z]</c> where 0 is empty and any other value is palette index + 1 —
/// the +1 shift exists so a freshly allocated array IS an empty frame, and the lenient rules
/// ("missing rows are empty cells") fall out of simply not writing anything.
///
/// Parsing is deliberately forgiving about SHAPE (short rows, missing layers, unknown fields:
/// hand-edited exports and older editors must keep loading) but strict about MEANING: a wrong
/// kind, an out-of-range footprint or an unknown cell char is a real authoring error, and
/// silently guessing would push the bug downstream to a renderer that can only draw garbage.
/// Those throw <see cref="FormatException"/> with a message that names the exact spot.
/// </summary>
public sealed class BoxSprite
{
    public string Name { get; }

    /// <summary>Footprint: every layer is N×N.</summary>
    public int N { get; }

    /// <summary>Height in voxels: every frame is N×N×H.</summary>
    public int H { get; }

    /// <summary>Every animation by name, in file order semantics: v3 files author these
    /// directly; v1/v2 files parse into a single "idle" entry, so this is never empty and
    /// consumers need no per-version logic.</summary>
    public IReadOnlyDictionary<string, BoxAnim> Anims { get; }

    // The anim Fps/Frames forward to: "idle" when the file has one, else the first anim in
    // file order. Resolved once at parse time so the two forwards below cannot disagree.
    private readonly BoxAnim _default;

    /// <summary>Playback rate of the default animation ("idle" if present, else the first).
    /// Kept as a forward into <see cref="Anims"/> rather than deleted: existing sim code and
    /// tests predate named animations and read Fps/Frames directly, and for a single-strip
    /// file (all v1/v2 art) the forward IS the whole truth.</summary>
    public int Fps => _default.Fps;

    /// <summary>Frames of the default animation — same forward rationale as <see cref="Fps"/>.
    /// 0 = empty, otherwise palette index + 1 into <see cref="Db32.Colors"/>.</summary>
    public IReadOnlyList<byte[,,]> Frames => _default.Frames;

    private BoxSprite(string name, int n, int h, IReadOnlyDictionary<string, BoxAnim> anims, BoxAnim @default)
    {
        Name = name; N = n; H = h; Anims = anims; _default = @default;
    }

    public static BoxSprite Parse(string json)
    {
        using var doc = ParseDocument(json, "char");
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("char: root is not a JSON object");

        // v1 exports predate the "frames" array (one implicit frame under "layers"), v2 predates
        // named animations ("anims"); all still load so old art never has to be re-exported.
        // Anything newer than we understand is refused OUT LOUD — a v4 file probably means new
        // semantics we would misread. A 'v' that is not an integer at all gets the same loud
        // treatment: GetInt32 on a string or array leaks an InvalidOperationException, which
        // reads as a parser bug rather than a bad file.
        if (root.TryGetProperty("v", out var vEl))
        {
            if (vEl.ValueKind != JsonValueKind.Number || !vEl.TryGetInt32(out int v))
                throw new FormatException("char: 'v' is not an integer");
            if (v is not (1 or 2 or 3)) throw new FormatException($"char: unsupported version v={v} (expected 1..3)");
        }

        // Same reasoning for 'kind': strict about MEANING, but a null stays tolerated the way a
        // missing property is — only a value that claims to be something else gets refused.
        if (root.TryGetProperty("kind", out var kindEl) && kindEl.ValueKind != JsonValueKind.Null)
        {
            if (kindEl.ValueKind != JsonValueKind.String)
                throw new FormatException("char: 'kind' is not a string");
            if (kindEl.GetString() is string kind && kind != "char")
                throw new FormatException($"char: kind \"{kind}\" is not \"char\" — wrong asset type for this parser");
        }

        string name = root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
            ? nameEl.GetString()! : "";

        int n = RequireInt(root, "char", "n");
        int h = RequireInt(root, "char", "h");
        // 32 is the editor's own hard cap; anything outside it is a corrupt or hostile file, and
        // bounding it here also bounds the n*n*h allocation below.
        if (n is < 1 or > 32) throw new FormatException($"char: n={n} out of range 1..32");
        if (h is < 1 or > 32) throw new FormatException($"char: h={h} out of range 1..32");

        // v3: named animations. Dated by SHAPE, like the tile parser dates undated libraries —
        // "anims" only ever came from a v3 editor, so it wins whenever present, and a v3 file
        // that (illegally) also carries a legacy strip loses nothing we would trust anyway.
        if (root.TryGetProperty("anims", out var animsEl))
        {
            var (byName, first) = ParseAnims(animsEl, n, h);
            return new BoxSprite(name, n, h, byName,
                byName.TryGetValue("idle", out var idle) ? idle : first);
        }

        // v1 files carry no fps; 6 is the editor's default playback rate. The editor clamps its
        // own exports to 1..12 — 60 leaves generous headroom for hand-authored files while still
        // refusing the absurd (an fps in the billions is a corrupt or hostile file, and every
        // renderer divides by it).
        int fps = 6;
        if (root.TryGetProperty("fps", out var fpsEl) && fpsEl.ValueKind == JsonValueKind.Number &&
            !fpsEl.TryGetInt32(out fps))
            throw new FormatException("char: 'fps' is not an integer");
        if (fps is < 1 or > 60) throw new FormatException($"char: fps={fps} out of range 1..60");

        var frames = new List<byte[,,]>();
        if (root.TryGetProperty("frames", out var framesEl))
        {
            if (framesEl.ValueKind != JsonValueKind.Array)
                throw new FormatException("char: 'frames' is not an array");
            // Bound the allocation BEFORE decoding anything: every frame is an n*n*h array, so
            // a file that is a few kilobytes of "[],[]…" can demand gigabytes. 64 frames is far
            // beyond any animation the editor can author; past it the file is not art. (v3 files
            // cap tighter — 16 per anim, see ParseAnims — but this legacy strip keeps its shipped
            // limit: art that loaded yesterday must load today.)
            if (framesEl.GetArrayLength() > 64)
                throw new FormatException($"char: {framesEl.GetArrayLength()} frames (max 64)");
            int fi = 0;
            foreach (var frameEl in framesEl.EnumerateArray())
                frames.Add(ParseFrame(frameEl, $"frame {fi++}", n, h));
        }
        else if (root.TryGetProperty("layers", out var layersEl))
        {
            frames.Add(ParseFrame(layersEl, "frame 0", n, h)); // v1: the whole file is one frame
        }

        // Zero frames is a hard error, not a lenient default: a sprite with nothing to draw is
        // an export bug, and an empty Frames list would NRE at first render instead of at load.
        if (frames.Count == 0) throw new FormatException("char: zero frames (need at least 1)");

        // Legacy single strip -> the "idle" animation, so pre-v3 art is indistinguishable from
        // a one-anim v3 file downstream (see BoxAnim).
        var strip = new BoxAnim(fps, frames);
        return new BoxSprite(name, n, h, new Dictionary<string, BoxAnim> { ["idle"] = strip }, strip);
    }

    /// <summary>The v3 "anims" object: 1..8 entries, each name ^[a-z0-9-]{1,16}$, each value an
    /// object holding an optional fps (1..60, default 6 like the legacy strip) and a mandatory
    /// 1..16-frame strip. Names are strict where the rest of the format is lenient because they
    /// are IDENTIFIERS — game code looks anims up by literal string, so "Idle" silently never
    /// playing is exactly the downstream bug the strict-about-meaning rule exists to prevent.</summary>
    private static (Dictionary<string, BoxAnim> ByName, BoxAnim First) ParseAnims(
        JsonElement animsEl, int n, int h)
    {
        if (animsEl.ValueKind != JsonValueKind.Object)
            throw new FormatException("char: 'anims' is not an object of named animations");

        // Cap the ENTRY count before decoding anything, same discipline as the frame caps: each
        // entry can demand up to 16 n*n*h arrays, so the count is the outer allocation bound.
        int count = 0;
        foreach (var _ in animsEl.EnumerateObject()) count++;
        if (count == 0) throw new FormatException("char: 'anims' has zero animations (need at least 1)");
        if (count > 8) throw new FormatException($"char: 'anims' has {count} animations (max 8)");

        var byName = new Dictionary<string, BoxAnim>(count);
        BoxAnim? first = null;
        int total = 0;
        foreach (var prop in animsEl.EnumerateObject())
        {
            string key = prop.Name;
            if (!IsAnimName(key))
                throw new FormatException(
                    $"char: anim name \"{key}\" does not match [a-z0-9-]{{1,16}}");
            // JSON allows duplicate keys; a dictionary would either throw the wrong exception
            // type or silently keep one strip of two the artist authored. Neither is the
            // one-FormatException failure contract.
            if (byName.ContainsKey(key))
                throw new FormatException($"char: duplicate anim \"{key}\"");
            if (prop.Value.ValueKind != JsonValueKind.Object)
                throw new FormatException($"char: anim \"{key}\" is not a JSON object");

            int fps = 6;
            if (prop.Value.TryGetProperty("fps", out var fpsEl) &&
                fpsEl.ValueKind == JsonValueKind.Number && !fpsEl.TryGetInt32(out fps))
                throw new FormatException($"char: anim \"{key}\" 'fps' is not an integer");
            if (fps is < 1 or > 60)
                throw new FormatException($"char: anim \"{key}\" fps={fps} out of range 1..60");

            // Frames are NOT optional per anim — an anim with nothing to draw is the zero-frames
            // export bug wearing a name. Both caps run BEFORE this anim's frames are decoded:
            // 16 per anim (v3's tighter replacement for the legacy 64-strip cap) and 64 across
            // the whole file, so eight 16-frame anims cannot quadruple the old worst case.
            if (!prop.Value.TryGetProperty("frames", out var framesEl) ||
                framesEl.ValueKind != JsonValueKind.Array)
                throw new FormatException($"char: anim \"{key}\" missing 'frames' array");
            int len = framesEl.GetArrayLength();
            if (len == 0)
                throw new FormatException($"char: anim \"{key}\" has zero frames (need at least 1)");
            if (len > 16)
                throw new FormatException($"char: anim \"{key}\" has {len} frames (max 16 per anim)");
            total += len;
            if (total > 64)
                throw new FormatException($"char: {total} frames across all anims (max 64 total)");

            var frames = new List<byte[,,]>(len);
            int fi = 0;
            foreach (var frameEl in framesEl.EnumerateArray())
                frames.Add(ParseFrame(frameEl, $"anim \"{key}\" frame {fi++}", n, h));

            var anim = new BoxAnim(fps, frames);
            byName.Add(key, anim);
            first ??= anim; // file order, not dictionary order: "first anim" must be stable
        }
        return (byName, first!);
    }

    /// <summary>^[a-z0-9-]{1,16}$, spelled out rather than a Regex: three range checks need no
    /// regex engine, and the error message quotes the pattern anyway.</summary>
    private static bool IsAnimName(string s)
    {
        if (s.Length is < 1 or > 16) return false;
        foreach (char c in s)
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')) return false;
        return true;
    }

    /// <summary>One frame: an array of layers (bottom, z=0, FIRST), each layer an array of row
    /// strings (row y=0 first). Anything missing or short is empty; anything EXTRA beyond n/h is
    /// ignored, so a file whose header shrank still loads its surviving core.
    /// <paramref name="where"/> locates the frame in error messages — "frame 2" for legacy
    /// strips, "anim \"walk\" frame 2" for v3, so the artist knows WHICH strip broke.</summary>
    private static byte[,,] ParseFrame(JsonElement frameEl, string where, int n, int h)
    {
        if (frameEl.ValueKind != JsonValueKind.Array)
            throw new FormatException($"char: {where} is not an array of layers");

        var vox = new byte[n, n, h];
        int z = 0;
        foreach (var layerEl in frameEl.EnumerateArray())
        {
            if (z >= h) break;
            if (layerEl.ValueKind != JsonValueKind.Array)
                throw new FormatException($"char: {where} layer {z} is not an array of rows");
            int y = 0;
            foreach (var rowEl in layerEl.EnumerateArray())
            {
                if (y >= n) break;
                if (rowEl.ValueKind != JsonValueKind.String)
                    throw new FormatException($"char: {where} layer {z} row {y} is not a string");
                string row = rowEl.GetString()!;
                for (int x = 0; x < n && x < row.Length; x++)
                {
                    char c = row[x];
                    if (c == '.') continue;
                    int idx = Db32.Base32Alphabet.IndexOf(c);
                    if (idx < 0)
                        throw new FormatException(
                            $"char: {where} layer {z} row {y} has char '{c}' outside base32");
                    vox[x, y, z] = (byte)(idx + 1); // +1 so 0 stays "empty"
                }
                y++;
            }
            z++;
        }
        return vox;
    }

    internal static int RequireInt(JsonElement root, string what, string prop)
    {
        // TryGetInt32 rather than GetInt32: a fractional number ("n": 4.5) must fail naming the
        // property, not with the framework's context-free "value is not in a supported format".
        if (!root.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.Number ||
            !el.TryGetInt32(out int value))
            throw new FormatException($"{what}: missing or non-integer '{prop}'");
        return value;
    }

    /// <summary>Malformed JSON must surface as the same <see cref="FormatException"/> every other
    /// bad-file case throws — a caller catching load errors should not also need to know which
    /// JSON library sits underneath (JsonReaderException leaked to the game once).</summary>
    internal static JsonDocument ParseDocument(string json, string what)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException e) { throw new FormatException($"{what}: invalid JSON: {e.Message}"); }
    }
}

/// <summary>
/// A 2D pixel overlay animation exported by BOXER (kind "vfx"): slashes, impact flashes and
/// other screen-space effects that stamp OVER the rendered scene rather than living in it as
/// voxels. Pixels are stored as <c>byte[x, y]</c> where 0 is transparent and any other value is
/// palette index + 1 — the same +1 shift as <see cref="BoxSprite"/>, for the same reason: a
/// freshly allocated array IS a fully transparent frame, so the lenient shape rules
/// (short/missing rows) cost no code.
///
/// Same discipline as the other parsers — lenient about SHAPE, strict about MEANING — with ONE
/// deliberate exception: 'kind' is REQUIRED and must be "vfx". The char and tile parsers
/// tolerate a missing kind because files predating the field must keep loading; this format is
/// brand new, no such legacy exists, so an untagged file is far more likely a char or tile
/// export fed to the wrong loader than a hand-authored vfx — refuse it out loud.
/// </summary>
public sealed class VfxSprite
{
    public string Name { get; }

    /// <summary>Frame width in pixels, 8..48: below 8 nothing reads on screen, above 48 the
    /// overlay dwarfs the entities it decorates — and the bound also caps the w*h allocation
    /// per frame, same hostile-file reasoning as <see cref="BoxSprite"/>'s n/h caps.</summary>
    public int W { get; }

    /// <summary>Frame height in pixels, same 8..48 bound (and reasoning) as <see cref="W"/>.</summary>
    public int H { get; }

    /// <summary>Playback rate, 1..24 (default 12): vfx are snappy one-shots, and 24 already
    /// means a full 16-frame effect lives well under a second.</summary>
    public int Fps { get; }

    /// <summary>One <c>byte[W, H]</c> per frame, indexed <c>[x, y]</c> with row y=0 first.
    /// 0 = transparent, otherwise palette index + 1 into <see cref="Db32.Colors"/>.</summary>
    public IReadOnlyList<byte[,]> Frames { get; }

    private VfxSprite(string name, int w, int h, int fps, IReadOnlyList<byte[,]> frames)
    {
        Name = name; W = w; H = h; Fps = fps; Frames = frames;
    }

    public static VfxSprite Parse(string json)
    {
        using var doc = BoxSprite.ParseDocument(json, "vfx");
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("vfx: root is not a JSON object");

        // Same version discipline as the other parsers; only v1 exists so far.
        if (root.TryGetProperty("v", out var vEl))
        {
            if (vEl.ValueKind != JsonValueKind.Number || !vEl.TryGetInt32(out int v))
                throw new FormatException("vfx: 'v' is not an integer");
            if (v != 1) throw new FormatException($"vfx: unsupported version v={v} (expected 1)");
        }

        // REQUIRED, unlike char/tiles — see the class doc: there are no legacy untagged files
        // to tolerate, so missing is as suspicious as wrong.
        if (!root.TryGetProperty("kind", out var kindEl) || kindEl.ValueKind != JsonValueKind.String)
            throw new FormatException("vfx: missing string 'kind' (this format always tags itself \"vfx\")");
        if (kindEl.GetString() is string kind && kind != "vfx")
            throw new FormatException($"vfx: kind \"{kind}\" is not \"vfx\" — wrong asset type for this parser");

        string name = root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
            ? nameEl.GetString()! : "";

        int w = BoxSprite.RequireInt(root, "vfx", "w");
        int h = BoxSprite.RequireInt(root, "vfx", "h");
        if (w is < 8 or > 48) throw new FormatException($"vfx: w={w} out of range 8..48");
        if (h is < 8 or > 48) throw new FormatException($"vfx: h={h} out of range 8..48");

        int fps = 12;
        if (root.TryGetProperty("fps", out var fpsEl) && fpsEl.ValueKind == JsonValueKind.Number &&
            !fpsEl.TryGetInt32(out fps))
            throw new FormatException("vfx: 'fps' is not an integer");
        if (fps is < 1 or > 24) throw new FormatException($"vfx: fps={fps} out of range 1..24");

        var frames = new List<byte[,]>();
        if (root.TryGetProperty("frames", out var framesEl))
        {
            if (framesEl.ValueKind != JsonValueKind.Array)
                throw new FormatException("vfx: 'frames' is not an array");
            // Cap BEFORE decoding anything, same reasoning as the char frame flood guard: each
            // frame is a w*h array, and 16 frames is already a lavish overlay effect.
            if (framesEl.GetArrayLength() > 16)
                throw new FormatException($"vfx: {framesEl.GetArrayLength()} frames (max 16)");
            int fi = 0;
            foreach (var frameEl in framesEl.EnumerateArray())
                frames.Add(ParseFrame(frameEl, fi++, w, h));
        }
        if (frames.Count == 0) throw new FormatException("vfx: zero frames (need at least 1)");

        return new VfxSprite(name, w, h, fps, frames);
    }

    /// <summary>One frame: an array of h row strings of length w, y=0 first. Short or missing
    /// rows are transparent, extra rows/columns are ignored — the same lenient shape rules as
    /// every other grid in this file — but an unknown cell char throws naming frame, row and
    /// char, because it means the file was authored against a different palette.</summary>
    private static byte[,] ParseFrame(JsonElement frameEl, int fi, int w, int h)
    {
        if (frameEl.ValueKind != JsonValueKind.Array)
            throw new FormatException($"vfx: frame {fi} is not an array of rows");

        var px = new byte[w, h];
        int y = 0;
        foreach (var rowEl in frameEl.EnumerateArray())
        {
            if (y >= h) break;
            if (rowEl.ValueKind != JsonValueKind.String)
                throw new FormatException($"vfx: frame {fi} row {y} is not a string");
            string row = rowEl.GetString()!;
            for (int x = 0; x < w && x < row.Length; x++)
            {
                char c = row[x];
                if (c == '.') continue;
                int idx = Db32.Base32Alphabet.IndexOf(c);
                if (idx < 0)
                    throw new FormatException($"vfx: frame {fi} row {y} has char '{c}' outside base32");
                px[x, y] = (byte)(idx + 1); // +1 so 0 stays "transparent"
            }
            y++;
        }
        return px;
    }
}

/// <summary>The rendering context a tile face is authored for. A face never says WHERE it goes
/// — the map does — only what kind of cell it is allowed to dress (see the consumer rule on
/// <see cref="TileLibrary"/>).</summary>
public enum TileRole { Floor, Worn, Edge, Wall, Water }

/// <summary>
/// One authored tile face: a 16×8 shade mask plus the metadata the renderer picks by. Grids are
/// indexed <c>[col, row]</c> and hold marks 0..3 (0 = no mark; see <see cref="TileLibrary.ShadeK"/>).
/// </summary>
public sealed class TileFace
{
    public string Name { get; }
    public TileRole Role { get; }

    /// <summary>Relative pick weight among faces of the same role, 1..9. Authored, not
    /// computed: the artist decides that the boring pebbles show three times as often as the
    /// showpiece crack.</summary>
    public int Weight { get; }

    /// <summary>Whether the renderer may also stamp this face horizontally flipped — a free
    /// second variant for symmetric-ish art, opt-in because text and directional marks read
    /// wrong mirrored.</summary>
    public bool Mirror { get; }

    /// <summary>The shade mask, <c>[col, row]</c>, always 16×8 (short files are zero-padded).</summary>
    public byte[,] Grid { get; }

    internal TileFace(string name, TileRole role, int weight, bool mirror, byte[,] grid)
    {
        Name = name; Role = role; Weight = weight; Mirror = mirror; Grid = grid;
    }
}

/// <summary>
/// The BOXER tile library: shade OVERLAYS for ground tiles, not pixels. Each face is a 16×8
/// mask of marks — 0 no mark, 1 dark, 2 darker, 3 light — that MULTIPLY whatever base colour
/// the tile already has (<see cref="ShadeK"/>). The editor ships shape, the game keeps colour,
/// so one library reskins with the biome instead of needing art per palette.
///
/// CONSUMER RULE — the game-side renderer must derive a cell's context exactly as the tools do,
/// so both ends dress the same map identically: worn tile → <see cref="TileRole.Worn"/>; water
/// tile → <see cref="TileRole.Water"/>; else any 4-neighbour void → <see cref="TileRole.Edge"/>;
/// else any 4-neighbour rock → <see cref="TileRole.Wall"/>; else <see cref="TileRole.Floor"/>.
/// Pick among the faces of that role, weighted by <see cref="TileFace.Weight"/>. Edge and Wall
/// FALL BACK to the Floor faces when their role has no faces; Worn and Water do NOT fall back —
/// a library without a worn face means "no trail", not a random floor face down the path. The
/// <see cref="Plain"/> gate (leave that % of cells untextured) applies only to Floor/Edge/Wall
/// contexts, never to Worn or Water.
///
/// v2 files (the current editor export) carry a flat role-tagged "tiles" list; v1 files (four
/// fixed floor slots + one worn grid) still load, converted into the same shape, so shipped
/// libraries never need re-exporting. Same discipline as <see cref="BoxSprite"/>: lenient about
/// SHAPE, strict about MEANING.
/// </summary>
public sealed class TileLibrary
{
    /// <summary>Grid dimensions, fixed by the export format: one iso tile face is 16×8.</summary>
    public const int Cols = 16;
    public const int Rows = 8;

    /// <summary>Brightness multiplier per mark value. Slot 0 is a sentinel — "no mark" means
    /// "leave the base colour alone", not "multiply by something"; a renderer must skip 0 cells
    /// rather than index this slot, and the 0f makes forgetting that visibly, blackly wrong.</summary>
    public static readonly float[] ShadeK = { 0f, 0.85f, 0.72f, 1.08f };

    /// <summary>Percent (0..95) of Floor/Edge/Wall cells left untextured — the breathing room
    /// between marks that keeps a map from reading as noise. Capped below 100 because a fully
    /// plain library is an authoring accident: it would render as if no library loaded at all.</summary>
    public int Plain { get; }

    /// <summary>Every authored face, in file order. May legitimately be empty (a library under
    /// construction); the consumer rule above says which absences are meaningful.</summary>
    public IReadOnlyList<TileFace> Tiles { get; }

    private TileLibrary(int plain, IReadOnlyList<TileFace> tiles)
    {
        Plain = plain; Tiles = tiles;
    }

    public static TileLibrary Parse(string json)
    {
        using var doc = BoxSprite.ParseDocument(json, "tiles");
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("tiles: root is not a JSON object");

        // Same version discipline as the char parser: a non-integer 'v' is a bad file, not a
        // parser crash, and anything newer than we understand is refused out loud. A file with
        // no 'v' at all is dated by its shape — a "tiles" list only ever came from a v2 editor,
        // everything else predates versioned exports — so a hand-edit that dropped the header
        // still loads as what it plainly is.
        int v = root.TryGetProperty("tiles", out _) ? 2 : 1;
        if (root.TryGetProperty("v", out var vEl))
        {
            if (vEl.ValueKind != JsonValueKind.Number || !vEl.TryGetInt32(out v))
                throw new FormatException("tiles: 'v' is not an integer");
            if (v is not (1 or 2)) throw new FormatException($"tiles: unsupported version v={v} (expected 1 or 2)");
        }

        // Same 'kind' rule as the char parser: missing or null is tolerated, a claim to be some
        // OTHER asset type is not — feeding a char export to the tile loader is a real mistake.
        if (root.TryGetProperty("kind", out var kindEl) && kindEl.ValueKind != JsonValueKind.Null)
        {
            if (kindEl.ValueKind != JsonValueKind.String)
                throw new FormatException("tiles: 'kind' is not a string");
            if (kindEl.GetString() is string kind && kind != "tiles")
                throw new FormatException($"tiles: kind \"{kind}\" is not \"tiles\" — wrong asset type for this parser");
        }

        return v == 1 ? ParseV1(root) : ParseV2(root);
    }

    private static TileLibrary ParseV2(JsonElement root)
    {
        // 45 is the editor's default plain gate; files predating the slider load unchanged.
        int plain = 45;
        if (root.TryGetProperty("plain", out var plainEl))
        {
            if (plainEl.ValueKind != JsonValueKind.Number || !plainEl.TryGetInt32(out plain))
                throw new FormatException("tiles: 'plain' is not an integer");
            if (plain is < 0 or > 95) throw new FormatException($"tiles: plain={plain} out of range 0..95");
        }

        var tiles = new List<TileFace>();
        if (root.TryGetProperty("tiles", out var tilesEl))
        {
            if (tilesEl.ValueKind != JsonValueKind.Array)
                throw new FormatException("tiles: 'tiles' is not an array");
            // Bound the allocation BEFORE decoding anything, same reasoning as the char frame
            // cap: each face is a 16×8 array, and 64 is far beyond any library the editor can
            // author — past it the file is not art.
            if (tilesEl.GetArrayLength() > 64)
                throw new FormatException($"tiles: {tilesEl.GetArrayLength()} tiles (max 64)");
            int i = 0;
            foreach (var tileEl in tilesEl.EnumerateArray())
                tiles.Add(ParseTile(tileEl, i++));
        }
        return new TileLibrary(plain, tiles);
    }

    private static TileFace ParseTile(JsonElement el, int i)
    {
        if (el.ValueKind != JsonValueKind.Object)
            throw new FormatException($"tiles: tile {i} is not a JSON object");

        // Names exist for error messages and artist sanity, not identity, so a missing one gets
        // a positional stand-in rather than a rejection.
        string name = el.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
            ? nameEl.GetString()! : $"tile-{i}";

        // Role is the ONE field with no safe default: it decides where the face may appear, and
        // guessing Floor would stamp wall art across open ground. Unknown values are named in
        // the error — "lava" probably means a newer editor, and the artist should hear that.
        if (!el.TryGetProperty("role", out var roleEl) || roleEl.ValueKind != JsonValueKind.String)
            throw new FormatException($"tiles: tile {i} (\"{name}\") missing string 'role'");
        var role = roleEl.GetString()! switch
        {
            "floor" => TileRole.Floor, "worn" => TileRole.Worn, "edge" => TileRole.Edge,
            "wall" => TileRole.Wall, "water" => TileRole.Water,
            var r => throw new FormatException(
                $"tiles: tile {i} (\"{name}\") role \"{r}\" is not one of floor|worn|edge|wall|water"),
        };

        // Weight 1..9 mirrors the editor's own slider; 0 would make a face unpickable (why
        // export it?) and a huge weight starves every other face — both authoring errors.
        int weight = 1;
        if (el.TryGetProperty("weight", out var wEl) &&
            (wEl.ValueKind != JsonValueKind.Number || !wEl.TryGetInt32(out weight)))
            throw new FormatException($"tiles: tile {i} (\"{name}\") 'weight' is not an integer");
        if (weight is < 1 or > 9)
            throw new FormatException($"tiles: tile {i} (\"{name}\") weight={weight} out of range 1..9");

        bool mirror = false;
        if (el.TryGetProperty("mirror", out var mEl) && mEl.ValueKind != JsonValueKind.Null)
            mirror = mEl.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new FormatException($"tiles: tile {i} (\"{name}\") 'mirror' is not a boolean"),
            };

        // A missing grid is a legal all-zero face (the artist reserved the slot); a present but
        // malformed one throws inside ParseGrid, same rules as v1.
        var grid = new byte[Cols, Rows];
        if (el.TryGetProperty("grid", out var gridEl) && gridEl.ValueKind != JsonValueKind.Null)
            ParseGrid(gridEl, $"tile {i} (\"{name}\") grid", grid);

        return new TileFace(name, role, weight, mirror, grid);
    }

    /// <summary>v1 files carried four fixed floor slots and one worn grid; convert them to the
    /// flat face list so consumers only ever see one shape. Empty (all-zero) grids are DROPPED
    /// rather than kept: under v1 an unmarked slot meant "draw the plain tile", and the v2 way
    /// to say that is the <see cref="Plain"/> gate, not a face that shades nothing. Names keep
    /// the SLOT index (floor-0, floor-2, …) so a face keeps its name when a neighbour empties.
    /// Floor faces get mirror=true — v1 renderers always flipped variants freely — and worn
    /// becomes the single "trail" face, unmirrored, exactly as v1 stamped it.</summary>
    private static TileLibrary ParseV1(JsonElement root)
    {
        var tiles = new List<TileFace>();
        if (root.TryGetProperty("floor", out var floorEl))
        {
            if (floorEl.ValueKind != JsonValueKind.Array)
                throw new FormatException("tiles: 'floor' is not an array");
            int i = 0;
            foreach (var gridEl in floorEl.EnumerateArray())
            {
                if (i >= 4) break; // extra variants: ignore, same leniency as short rows
                var grid = new byte[Cols, Rows];
                if (gridEl.ValueKind != JsonValueKind.Null) ParseGrid(gridEl, $"floor[{i}]", grid);
                if (!IsEmpty(grid)) tiles.Add(new TileFace($"floor-{i}", TileRole.Floor, 1, true, grid));
                i++;
            }
        }

        var worn = new byte[Cols, Rows];
        if (root.TryGetProperty("worn", out var wornEl) && wornEl.ValueKind != JsonValueKind.Null)
            ParseGrid(wornEl, "worn", worn);
        if (!IsEmpty(worn)) tiles.Add(new TileFace("trail", TileRole.Worn, 1, false, worn));

        return new TileLibrary(45, tiles);
    }

    private static bool IsEmpty(byte[,] grid)
    {
        foreach (byte b in grid) if (b != 0) return false;
        return true;
    }

    /// <summary>Rows lenient (missing/short = no mark), chars strict: an unknown shade char means
    /// the file was authored against a different shade table, and guessing a factor would tint
    /// every tile on screen subtly wrong — better one loud load error.</summary>
    private static void ParseGrid(JsonElement gridEl, string what, byte[,] grid)
    {
        if (gridEl.ValueKind != JsonValueKind.Array)
            throw new FormatException($"tiles: {what} is not an array of rows");
        int y = 0;
        foreach (var rowEl in gridEl.EnumerateArray())
        {
            if (y >= Rows) break;
            if (rowEl.ValueKind != JsonValueKind.String)
                throw new FormatException($"tiles: {what} row {y} is not a string");
            string row = rowEl.GetString()!;
            for (int x = 0; x < Cols && x < row.Length; x++)
            {
                byte mark = row[x] switch
                {
                    '.' => 0, '1' => 1, '2' => 2, '3' => 3,
                    var c => throw new FormatException(
                        $"tiles: {what} row {y} has char '{c}' outside shade set '.123'"),
                };
                grid[x, y] = mark;
            }
            y++;
        }
    }
}

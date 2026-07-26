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
/// A box character exported by BOXER (kind "char"): an n×n×h voxel prism with 1+ animation
/// frames. Voxels are stored as <c>byte[x, y, z]</c> where 0 is empty and any other value is
/// palette index + 1 — the +1 shift exists so a freshly allocated array IS an empty frame, and
/// the lenient rules ("missing rows are empty cells") fall out of simply not writing anything.
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

    /// <summary>Playback rate the artist authored the animation at. The game honours it rather
    /// than imposing a global rate, so a 2-frame idle and a 6-frame run can coexist.</summary>
    public int Fps { get; }

    /// <summary>One <c>byte[N, N, H]</c> per animation frame, z bottom-up (layer 0 is the feet).
    /// 0 = empty, otherwise palette index + 1 into <see cref="Db32.Colors"/>.</summary>
    public IReadOnlyList<byte[,,]> Frames { get; }

    private BoxSprite(string name, int n, int h, int fps, IReadOnlyList<byte[,,]> frames)
    {
        Name = name; N = n; H = h; Fps = fps; Frames = frames;
    }

    public static BoxSprite Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("char: root is not a JSON object");

        // v1 exports predate the "frames" array (one implicit frame under "layers"); both still
        // load so old art never has to be re-exported. Anything newer than we understand is
        // refused OUT LOUD — a v3 file probably means new semantics we would misread.
        if (root.TryGetProperty("v", out var vEl))
        {
            int v = vEl.GetInt32();
            if (v is not (1 or 2)) throw new FormatException($"char: unsupported version v={v} (expected 1 or 2)");
        }

        if (root.TryGetProperty("kind", out var kindEl) && kindEl.GetString() is string kind && kind != "char")
            throw new FormatException($"char: kind \"{kind}\" is not \"char\" — wrong asset type for this parser");

        string name = root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
            ? nameEl.GetString()! : "";

        int n = RequireInt(root, "n");
        int h = RequireInt(root, "h");
        // 32 is the editor's own hard cap; anything outside it is a corrupt or hostile file, and
        // bounding it here also bounds the n*n*h allocation below.
        if (n is < 1 or > 32) throw new FormatException($"char: n={n} out of range 1..32");
        if (h is < 1 or > 32) throw new FormatException($"char: h={h} out of range 1..32");

        // v1 files carry no fps; 6 is the editor's default playback rate.
        int fps = root.TryGetProperty("fps", out var fpsEl) && fpsEl.ValueKind == JsonValueKind.Number
            ? fpsEl.GetInt32() : 6;
        if (fps < 1) throw new FormatException($"char: fps={fps} must be >= 1");

        var frames = new List<byte[,,]>();
        if (root.TryGetProperty("frames", out var framesEl))
        {
            if (framesEl.ValueKind != JsonValueKind.Array)
                throw new FormatException("char: 'frames' is not an array");
            int fi = 0;
            foreach (var frameEl in framesEl.EnumerateArray())
                frames.Add(ParseFrame(frameEl, fi++, n, h));
        }
        else if (root.TryGetProperty("layers", out var layersEl))
        {
            frames.Add(ParseFrame(layersEl, 0, n, h)); // v1: the whole file is one frame
        }

        // Zero frames is a hard error, not a lenient default: a sprite with nothing to draw is
        // an export bug, and an empty Frames list would NRE at first render instead of at load.
        if (frames.Count == 0) throw new FormatException("char: zero frames (need at least 1)");

        return new BoxSprite(name, n, h, fps, frames);
    }

    /// <summary>One frame: an array of layers (bottom, z=0, FIRST), each layer an array of row
    /// strings (row y=0 first). Anything missing or short is empty; anything EXTRA beyond n/h is
    /// ignored, so a file whose header shrank still loads its surviving core.</summary>
    private static byte[,,] ParseFrame(JsonElement frameEl, int fi, int n, int h)
    {
        if (frameEl.ValueKind != JsonValueKind.Array)
            throw new FormatException($"char: frame {fi} is not an array of layers");

        var vox = new byte[n, n, h];
        int z = 0;
        foreach (var layerEl in frameEl.EnumerateArray())
        {
            if (z >= h) break;
            if (layerEl.ValueKind != JsonValueKind.Array)
                throw new FormatException($"char: frame {fi} layer {z} is not an array of rows");
            int y = 0;
            foreach (var rowEl in layerEl.EnumerateArray())
            {
                if (y >= n) break;
                if (rowEl.ValueKind != JsonValueKind.String)
                    throw new FormatException($"char: frame {fi} layer {z} row {y} is not a string");
                string row = rowEl.GetString()!;
                for (int x = 0; x < n && x < row.Length; x++)
                {
                    char c = row[x];
                    if (c == '.') continue;
                    int idx = Db32.Base32Alphabet.IndexOf(c);
                    if (idx < 0)
                        throw new FormatException(
                            $"char: frame {fi} layer {z} row {y} has char '{c}' outside base32");
                    vox[x, y, z] = (byte)(idx + 1); // +1 so 0 stays "empty"
                }
                y++;
            }
            z++;
        }
        return vox;
    }

    private static int RequireInt(JsonElement root, string prop)
    {
        if (!root.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.Number)
            throw new FormatException($"char: missing or non-numeric '{prop}'");
        return el.GetInt32();
    }
}

/// <summary>
/// The BOXER tile library (v 1): shade OVERLAYS for ground tiles, not pixels. Each grid is a
/// 16×8 mask of marks — 0 no mark, 1 dark, 2 darker, 3 light — that MULTIPLY whatever base
/// colour the tile already has (<see cref="ShadeK"/>). The editor ships shape, the game keeps
/// colour, so one library reskins with the biome instead of needing art per palette.
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

    /// <summary>ALWAYS length 4 — the editor's four floor variant slots. Unused slots are
    /// all-zero grids rather than nulls, so a renderer can index by variant without a null
    /// check and an unmarked slot simply draws the plain tile.</summary>
    public IReadOnlyList<byte[,]> Floor { get; }

    /// <summary>The worn overlay stamped along the walked path. Indexed [col, row], 16×8.</summary>
    public byte[,] Worn { get; }

    private TileLibrary(IReadOnlyList<byte[,]> floor, byte[,] worn)
    {
        Floor = floor; Worn = worn;
    }

    public static TileLibrary Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("tiles: root is not a JSON object");

        if (root.TryGetProperty("v", out var vEl) && vEl.GetInt32() is int v && v != 1)
            throw new FormatException($"tiles: unsupported version v={v} (expected 1)");

        // All four slots exist up front (see Floor's doc comment); parsing fills what the file has.
        var floor = new byte[4][,];
        for (int i = 0; i < 4; i++) floor[i] = new byte[Cols, Rows];

        if (root.TryGetProperty("floor", out var floorEl))
        {
            if (floorEl.ValueKind != JsonValueKind.Array)
                throw new FormatException("tiles: 'floor' is not an array");
            int i = 0;
            foreach (var gridEl in floorEl.EnumerateArray())
            {
                if (i >= 4) break; // extra variants: ignore, same leniency as short rows
                if (gridEl.ValueKind != JsonValueKind.Null) ParseGrid(gridEl, $"floor[{i}]", floor[i]);
                i++;
            }
        }

        var worn = new byte[Cols, Rows];
        if (root.TryGetProperty("worn", out var wornEl) && wornEl.ValueKind != JsonValueKind.Null)
            ParseGrid(wornEl, "worn", worn);

        return new TileLibrary(floor, worn);
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

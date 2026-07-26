using System.Text.Json;
using DofusSlice.Core.Content;

namespace DofusSlice.Sim;

/// <summary>
/// Deterministic self-tests for the BOXER export formats (<see cref="BoxSprite"/>,
/// <see cref="TileLibrary"/>). Run with: `sim assets`. The fixtures below are LITERAL editor
/// exports, embedded rather than loaded from disk, so this test proves the browser editor and
/// the game agree on the wire format without either end being installed — the contract lives
/// here, in one greppable file, and a format drift fails in CI instead of on someone's screen.
/// </summary>
public static class AssetsTest
{
    private static int _pass, _fail;

    public static int Run()
    {
        KnightV2();
        LegacyV1Layers();
        Tiles();
        Rejections();
        RoundTrip();
        Palette();

        Console.WriteLine($"\n{_pass} passed, {_fail} failed.");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>A v2 export: 2 frames that differ by exactly one moved voxel — the smallest
    /// possible animation, so a frame-order or frame-aliasing bug (both frames decoding to the
    /// same array) cannot hide behind identical content.</summary>
    private const string KnightJson = """
    {
      "v": 2, "kind": "char", "name": "knight", "n": 10, "h": 14, "fps": 6,
      "editor": "boxer-0.7",
      "frames": [
        [
          ["..........","..........","...b......","..........","..........","..........","..........","..........","..........",".........."],
          ["..........","..........","..........","..........","..........","..........","..........","..........","..........",".........."],
          ["..........","..........","..........","..........","..........",".....7....","..........","..........","..........",".........."]
        ],
        [
          ["..........","..........","...b......","..........","..........","..........","..........","..........","..........",".........."],
          ["..........","..........","..........","..........","..........","..........","..........","..........","..........",".........."],
          ["..........","..........","..........","..........","..........","......7...","..........","..........","..........",".........."]
        ]
      ]
    }
    """;

    private static void KnightV2()
    {
        var s = BoxSprite.Parse(KnightJson);
        Check("char v2 header", s.Name == "knight" && s.N == 10 && s.H == 14 && s.Fps == 6,
            $"name '{s.Name}', {s.N}x{s.N}x{s.H} @ {s.Fps}fps (unknown 'editor' field ignored)");
        Check("char v2 frame count", s.Frames.Count == 2, $"{s.Frames.Count} frames parsed");

        // 'b' is base32 index 11, stored as 11+1: the +1 shift is the whole empty-cell scheme.
        Check("char voxel value", s.Frames[0][3, 2, 0] == 12,
            $"frame 0 voxel (3,2,0) = {s.Frames[0][3, 2, 0]} (expected 12: 'b' index 11 + 1)");
        bool moved = s.Frames[0][5, 5, 2] == 8 && s.Frames[1][5, 5, 2] == 0 && s.Frames[1][6, 5, 2] == 8;
        Check("char animation", moved,
            $"'7' voxel moved (5,5,2)->(6,5,2): f0[5]={s.Frames[0][5, 5, 2]}, f1[5]={s.Frames[1][5, 5, 2]}, f1[6]={s.Frames[1][6, 5, 2]}");
        Check("char empty voxel", s.Frames[0][0, 0, 13] == 0,
            $"unspecified top layer reads {s.Frames[0][0, 0, 13]} (leniency: missing layers are empty)");
    }

    /// <summary>v1 files (single implicit frame under "layers", no fps) predate the animation
    /// feature; the shipped art from that era must keep loading. The fixture also carries a
    /// short row and a missing layer to exercise the lenient shape rules.</summary>
    private static void LegacyV1Layers()
    {
        var s = BoxSprite.Parse("""
        {"v":1,"name":"stub","n":4,"h":3,"layers":[[".a..","...."],["..b"]]}
        """);
        Check("char v1 layers", s.Frames.Count == 1, $"{s.Frames.Count} frame from legacy 'layers'");
        bool cells = s.Frames[0][1, 0, 0] == 11   // 'a' = index 10 + 1
                  && s.Frames[0][2, 0, 1] == 12   // short row "..b" still places its voxel
                  && s.Frames[0][0, 3, 2] == 0;   // layer 2 absent entirely -> empty
        Check("char v1 leniency", cells,
            $"(1,0,0)={s.Frames[0][1, 0, 0]}, short-row (2,0,1)={s.Frames[0][2, 0, 1]}, missing-layer (0,3,2)={s.Frames[0][0, 3, 2]}");
    }

    private static void Tiles()
    {
        var lib = TileLibrary.Parse("""
        {
          "v": 1,
          "floor": [
            ["3...............", ".2.............."]
          ],
          "worn": ["................", "...1............"]
        }
        """);
        Check("tiles marks", lib.Floor[0][0, 0] == 3 && lib.Floor[0][1, 1] == 2 && lib.Worn[3, 1] == 1,
            $"floor[0](0,0)={lib.Floor[0][0, 0]}, floor[0](1,1)={lib.Floor[0][1, 1]}, worn(3,1)={lib.Worn[3, 1]} (expected 3/2/1)");

        // One variant in the file must still yield four slots, the absent ones all-zero: the
        // renderer indexes by variant and must never null-check.
        bool empties = true;
        for (int i = 1; i < lib.Floor.Count && empties; i++)
            for (int y = 0; y < TileLibrary.Rows && empties; y++)
                for (int x = 0; x < TileLibrary.Cols; x++)
                    if (lib.Floor[i][x, y] != 0) { empties = false; break; }
        Check("tiles slot padding", lib.Floor.Count == 4 && empties && lib.Floor[0][5, 5] == 0,
            $"Floor.Count={lib.Floor.Count}, slots 1..3 all-zero={empties}, unmarked cell reads 0");

        Check("tiles shade table", TileLibrary.ShadeK.Length == 4
                && TileLibrary.ShadeK[1] == 0.85f && TileLibrary.ShadeK[2] == 0.72f && TileLibrary.ShadeK[3] == 1.08f,
            $"ShadeK = [{string.Join(", ", TileLibrary.ShadeK)}]");
    }

    /// <summary>Hard violations must THROW, not limp: each of these files decodes to something —
    /// just the wrong something — so a lenient parser would ship a wrong asset silently.</summary>
    private static void Rejections()
    {
        Throws("reject wrong kind", () => BoxSprite.Parse(
            """{"v":2,"kind":"banana","n":4,"h":4,"frames":[[]]}"""));
        Throws("reject n=0", () => BoxSprite.Parse(
            """{"v":2,"kind":"char","n":0,"h":4,"frames":[[]]}"""));
        Throws("reject bad cell char", () => BoxSprite.Parse(
            """{"v":2,"kind":"char","n":4,"h":4,"frames":[[["..X."]]]}"""));
        Throws("reject zero frames", () => BoxSprite.Parse(
            """{"v":2,"kind":"char","n":4,"h":4,"frames":[]}"""));
        Throws("reject bad shade char", () => TileLibrary.Parse(
            """{"v":1,"floor":[],"worn":["..9"]}"""));
    }

    /// <summary>Serialize frame 0 of the knight back to row strings and compare against the JSON
    /// input. This closes the loop the other tests leave open: spot-checked voxels prove POINTS
    /// of the mapping, the round trip proves the WHOLE grid — any axis swap, row/col transpose or
    /// off-by-one that happens to miss the probed cells still fails here.</summary>
    private static void RoundTrip()
    {
        var s = BoxSprite.Parse(KnightJson);
        const string b32 = "0123456789abcdefghijklmnopqrstuv";

        // The tiny local serializer: voxels -> full h layers of n rows, the editor's own layout.
        var outRows = new string[s.H][];
        for (int z = 0; z < s.H; z++)
        {
            outRows[z] = new string[s.N];
            for (int y = 0; y < s.N; y++)
            {
                var chars = new char[s.N];
                for (int x = 0; x < s.N; x++)
                {
                    byte v = s.Frames[0][x, y, z];
                    chars[x] = v == 0 ? '.' : b32[v - 1];
                }
                outRows[z][y] = new string(chars);
            }
        }

        // Pull the input rows straight from the same JSON literal the parser saw.
        using var doc = JsonDocument.Parse(KnightJson);
        var inFrame = doc.RootElement.GetProperty("frames")[0];

        bool ok = true;
        string detail = "every serialized row matches its input row (absent input layers all '.')";
        for (int z = 0; z < s.H && ok; z++)
            for (int y = 0; y < s.N; y++)
            {
                string expected = z < inFrame.GetArrayLength() && y < inFrame[z].GetArrayLength()
                    ? inFrame[z][y].GetString()! : new string('.', s.N);
                if (outRows[z][y] != expected)
                {
                    ok = false;
                    detail = $"layer {z} row {y}: got \"{outRows[z][y]}\", input \"{expected}\"";
                    break;
                }
            }
        Check("char round trip", ok, detail);
    }

    /// <summary>The palette must be the DB32 the editor previews with — a shifted table would
    /// recolour every asset at once. Pin the ends and one landmark.</summary>
    private static void Palette()
    {
        var c = Db32.Colors;
        Check("DB32 palette", c.Length == 32
                && c[0] == ((byte)0, (byte)0, (byte)0)
                && c[21] == ((byte)255, (byte)255, (byte)255)
                && c[31] == ((byte)0x8a, (byte)0x6f, (byte)0x30),
            $"{c.Length} colours; [0]=black, [21]=white, [31]=#8a6f30");
    }

    private static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
        if (ok) _pass++; else _fail++;
    }

    private static void Throws(string name, Action act)
    {
        try
        {
            act();
            Check(name, false, "no exception thrown (expected FormatException)");
        }
        catch (FormatException ex)
        {
            Check(name, true, $"FormatException: {ex.Message}");
        }
        catch (Exception ex)
        {
            Check(name, false, $"wrong exception type: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

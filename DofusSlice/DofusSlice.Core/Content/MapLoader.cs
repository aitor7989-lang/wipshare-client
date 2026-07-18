using System.Text.Json;
using DofusSlice.Core.Grid;

namespace DofusSlice.Core.Content;

/// <summary>
/// Parses a compact JSON map into <see cref="MapData"/>. The map is an ASCII grid — each
/// character is a tile — which keeps maps human-editable and diffable:
/// <c>.</c> grass, <c>,</c> grass2, <c>d</c> dirt, <c>p</c> path, <c>#</c> rock, <c>o</c> void,
/// <c>P</c> player spawn, and any digit is a mob spawn resolved through the "legend".
/// </summary>
public static class MapLoader
{
    private sealed class Dto
    {
        public string? Name { get; set; }
        public Dictionary<string, string>? Legend { get; set; }
        public List<string>? Rows { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static MapData Parse(string json)
    {
        const int maxDim = 64; // bound work per frame (LoS/pathfinding scan every cell)

        var dto = JsonSerializer.Deserialize<Dto>(json, Options)
                  ?? throw new FormatException("map: invalid JSON");
        var rows = dto.Rows ?? throw new FormatException("map: missing 'rows'");
        if (rows.Count == 0) throw new FormatException("map: empty 'rows'");
        if (rows.Any(r => r is null)) throw new FormatException("map: null row");

        int height = rows.Count;
        int width = Math.Max(1, rows.Max(r => r.Length));
        if (width > maxDim || height > maxDim)
            throw new FormatException($"map: too large ({width}x{height}), max {maxDim}x{maxDim}");

        var tiles = new TileKind[width * height];
        var legend = dto.Legend ?? new Dictionary<string, string>();

        var map = new MapData { Name = dto.Name ?? "", Width = width, Height = height, Tiles = tiles };
        bool hasPlayerSpawn = false;
        var taken = new HashSet<CellCoord>();

        for (int y = 0; y < height; y++)
        {
            string row = rows[y];
            for (int x = 0; x < width; x++)
            {
                char c = x < row.Length ? row[x] : '.';
                var cell = new CellCoord(x, y);
                TileKind kind = TileKind.Grass;
                switch (c)
                {
                    case '.': kind = TileKind.Grass; break;
                    case ',': kind = TileKind.Grass2; break;
                    case 'd': kind = TileKind.Dirt; break;
                    case 'p': kind = TileKind.Path; break;
                    case '#': kind = TileKind.Rock; break;
                    case 'o': kind = TileKind.Void; break;
                    case 'P': // stands on grass; ignore a duplicate 'P'
                        if (!hasPlayerSpawn) { map.PlayerSpawn = cell; hasPlayerSpawn = true; taken.Add(cell); }
                        break;
                    default:
                        // A non-null legend entry on a fresh cell spawns that mob.
                        if (char.IsDigit(c) && legend.TryGetValue(c.ToString(), out var mob)
                            && !string.IsNullOrWhiteSpace(mob) && taken.Add(cell))
                            map.Enemies.Add((mob, cell));
                        break;
                }
                tiles[y * width + x] = kind;
            }
        }

        // Guarantee a valid hero spawn even if the map omitted 'P'.
        if (!hasPlayerSpawn)
        {
            var spawn = map.FirstWalkableCell(taken);
            if (spawn is not CellCoord ok) throw new FormatException("map: no free cell for the player");
            map.PlayerSpawn = ok;
        }

        return map;
    }
}

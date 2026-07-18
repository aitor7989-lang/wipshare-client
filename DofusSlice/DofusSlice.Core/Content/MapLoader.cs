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
        var dto = JsonSerializer.Deserialize<Dto>(json, Options)
                  ?? throw new FormatException("map: invalid JSON");
        var rows = dto.Rows ?? throw new FormatException("map: missing 'rows'");
        if (rows.Count == 0) throw new FormatException("map: empty 'rows'");

        int height = rows.Count;
        int width = rows.Max(r => r.Length);
        var tiles = new TileKind[width * height];
        var legend = dto.Legend ?? new Dictionary<string, string>();

        var map = new MapData { Name = dto.Name ?? "", Width = width, Height = height, Tiles = tiles };

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
                    case 'P': map.PlayerSpawn = cell; break; // stands on grass
                    default:
                        if (char.IsDigit(c) && legend.TryGetValue(c.ToString(), out var mob))
                            map.Enemies.Add((mob, cell));
                        break;
                }
                tiles[y * width + x] = kind;
            }
        }
        return map;
    }
}

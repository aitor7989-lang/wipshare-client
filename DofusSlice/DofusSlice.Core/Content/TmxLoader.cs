using System.Xml.Linq;
using DofusSlice.Core.Grid;

namespace DofusSlice.Core.Content;

/// <summary>
/// Loads an orthogonal <a href="https://www.mapeditor.org/">Tiled</a> <c>.tmx</c> map into our
/// <see cref="MapData"/>. Tiled is the editor these tilesets are built for, so this lets you
/// design maps there. It reads the first CSV tile layer and maps each tile to one of our
/// <see cref="TileKind"/>s; spawns come from an object layer.
///
/// Tile -> kind resolution (first match wins): a tile's Class/Type or a "kind" custom property,
/// else the tileset's name (contains "water" -> Water, "rock"/"wall"/"stone" -> Rock, etc.),
/// else Grass. Spawns: point objects whose Class/Type/Name is "player" or a mob kind
/// ("boar"/"gobball"/"piou"). Reads embedded tilesets directly; external <c>source=".tsx"</c>
/// tilesets are resolved via <paramref name="readExternal"/> when provided.
/// </summary>
public static class TmxLoader
{
    private const int MaxDim = 64;
    private const uint GidMask = 0x1FFFFFFF; // strip Tiled's flip flags

    public static MapData Parse(string tmxXml, Func<string, string>? readExternal = null)
    {
        var map = XDocument.Parse(tmxXml).Root ?? throw new FormatException("tmx: no root");
        if ((string?)map.Attribute("orientation") is string o && o != "orthogonal")
            throw new FormatException($"tmx: unsupported orientation '{o}'");

        int width = (int)map.Attribute("width")!;
        int height = (int)map.Attribute("height")!;
        int tw = (int?)map.Attribute("tilewidth") ?? 32;
        int th = (int?)map.Attribute("tileheight") ?? 32;
        if (width <= 0 || height <= 0 || width > MaxDim || height > MaxDim)
            throw new FormatException($"tmx: bad size {width}x{height} (max {MaxDim})");

        var gidKind = BuildGidKindTable(map, readExternal);

        // First tile layer, CSV encoding.
        var layer = map.Elements("layer").FirstOrDefault() ?? throw new FormatException("tmx: no tile layer");
        var data = layer.Element("data") ?? throw new FormatException("tmx: layer has no data");
        if ((string?)data.Attribute("encoding") is string enc && enc != "csv")
            throw new FormatException($"tmx: unsupported layer encoding '{enc}' (use CSV)");

        var gids = data.Value.Split(',')
            .Select(s => s.Trim()).Where(s => s.Length > 0)
            .Select(s => (int)(uint.Parse(s) & GidMask)).ToList();
        if (gids.Count < width * height) throw new FormatException("tmx: not enough tile data");

        var tiles = new TileKind[width * height];
        for (int i = 0; i < tiles.Length; i++)
        {
            int gid = gids[i];
            tiles[i] = gid == 0 ? TileKind.Grass : gidKind.GetValueOrDefault(gid, TileKind.Grass);
        }

        var result = new MapData
        {
            Name = (string?)map.Attribute("name") ?? "tiled map",
            Width = width, Height = height, Tiles = tiles,
        };

        ReadSpawns(map, tw, th, result, out bool hasPlayer);
        result.FinalizeSpawns(hasPlayer);
        return result;
    }

    private static Dictionary<int, TileKind> BuildGidKindTable(XElement map, Func<string, string>? readExternal)
    {
        var table = new Dictionary<int, TileKind>();
        foreach (var ts in map.Elements("tileset"))
        {
            int firstGid = (int)ts.Attribute("firstgid")!;
            var root = ts;
            if ((string?)ts.Attribute("source") is string src && readExternal != null)
                root = XDocument.Parse(readExternal(src)).Root ?? ts;

            var defaultKind = KindFromName((string?)root.Attribute("name") ?? "");
            int count = (int?)root.Attribute("tilecount") ?? 0;
            for (int id = 0; id < count; id++) table[firstGid + id] = defaultKind;

            foreach (var tile in root.Elements("tile"))
            {
                int id = (int)tile.Attribute("id")!;
                var cls = (string?)tile.Attribute("class") ?? (string?)tile.Attribute("type");
                var prop = tile.Element("properties")?.Elements("property")
                    .FirstOrDefault(p => (string?)p.Attribute("name") == "kind");
                var overrideName = (string?)prop?.Attribute("value") ?? cls;
                if (overrideName != null) table[firstGid + id] = KindFromName(overrideName);
            }
        }
        return table;
    }

    private static void ReadSpawns(XElement map, int tw, int th, MapData result, out bool hasPlayer)
    {
        hasPlayer = false;
        foreach (var obj in map.Elements("objectgroup").Elements("object"))
        {
            var cell = new CellCoord((int)((float?)obj.Attribute("x") ?? 0) / tw,
                                     (int)((float?)obj.Attribute("y") ?? 0) / th);
            if (!result.InBounds(cell)) continue;

            string tag = ((string?)obj.Attribute("class") ?? (string?)obj.Attribute("type")
                          ?? (string?)obj.Attribute("name") ?? "").ToLowerInvariant();
            switch (tag)
            {
                case "player" or "hero" or "start":
                    if (!hasPlayer) { result.PlayerSpawn = cell; hasPlayer = true; }
                    result.PlayerStartCells.Add(cell);
                    break;
                case "boar" or "gobball" or "piou":
                    result.Enemies.Add((tag, cell));
                    break;
            }
        }
    }

    private static TileKind KindFromName(string raw)
    {
        string s = raw.ToLowerInvariant();
        if (s.Contains("water")) return TileKind.Water;
        if (s.Contains("void") || s.Contains("hole") || s.Contains("pit")) return TileKind.Void;
        if (s.Contains("rock") || s.Contains("wall") || s.Contains("stone")) return TileKind.Rock;
        if (s.Contains("dirt")) return TileKind.Dirt;
        if (s.Contains("path") || s.Contains("road")) return TileKind.Path;
        if (s.Contains("grass2") || s.Contains("grass-2")) return TileKind.Grass2;
        return TileKind.Grass;
    }
}

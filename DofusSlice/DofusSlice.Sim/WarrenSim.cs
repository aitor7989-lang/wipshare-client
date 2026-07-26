using DofusSlice.Core.Grid;
using DofusSlice.Core.World;

namespace DofusSlice.Sim;

/// <summary>
/// Headless harness for the WANDERER floor generator: print a floor so a human can look at it,
/// measure many so the grammar is tuned from numbers, and prove every floor can be walked.
/// </summary>
public static class WarrenSim
{
    private static char Glyph(TileKind t) => t switch
    {
        TileKind.Void => ' ',
        TileKind.Rock => '#',
        TileKind.Path => ':',
        TileKind.Water => '~',
        TileKind.Spikes => '"',
        _ => '.',
    };

    private static char Mark(SegmentKind k) => k switch
    {
        SegmentKind.Throat => 'T',
        SegmentKind.Corridor => 'c',
        SegmentKind.Gallery => 'G',
        SegmentKind.Hall => 'H',
        SegmentKind.Fork => 'Y',
        SegmentKind.Cistern => '~',
        _ => 'o',
    };

    /// <summary>Print a seeded floor, optionally as the player would see it: only what a torch of
    /// <paramref name="sight"/> has lit along the way.</summary>
    public static int Dump(int seed, int rows, int sight = 0)
    {
        var floor = new Warren(seed).Generate(Math.Max(60, rows));
        Fog? fog = null;
        if (sight > 0)
        {
            // Walk the link column down the floor with a torch — the reveal the player would get.
            fog = new Fog(floor);
            for (int y = 0; y < floor.Length; y++)
            {
                int x = floor.FirstWalkable(y);
                if (x >= 0) fog.Reveal(x, y, sight);
            }
        }

        Console.WriteLine($"FLOOR  seed={seed}  {floor.Width}x{floor.Length}" +
                          (sight > 0 ? $"  fog: sight {sight}" : ""));
        Console.WriteLine("  '.' floor  ':' worn  '#' rock  '~' water  '\"' spikes  ' ' unseen / the deeps\n");

        var lastRoom = (RoomKind)(-1);
        var lastKind = (SegmentKind)(-1);
        for (int y = 0; y < Math.Min(rows, floor.Length); y++)
        {
            if (floor.RowKind[y] != lastKind || floor.RowRoom[y] != lastRoom)
            {
                string label = floor.RowRoom[y] == RoomKind.None
                    ? floor.RowKind[y].ToString()
                    : $"** {floor.RowRoom[y].ToString().ToUpperInvariant()} **";
                Console.WriteLine($"      {new string('-', floor.Width)}  {label}");
                lastKind = floor.RowKind[y]; lastRoom = floor.RowRoom[y];
            }

            var sb = new System.Text.StringBuilder();
            for (int x = 0; x < floor.Width; x++)
                sb.Append(fog is null || fog.Seen(x, y) ? Glyph(floor.Tiles[x, y]) : ' ');

            Console.WriteLine($"{y,4}  |{sb}|  {Mark(floor.RowKind[y])} {floor.Constriction[y],3}");
        }
        return 0;
    }

    /// <summary>Distribution across many floors. The grammar is a pile of weights, and weights are
    /// exactly the thing that feels right and measures wrong.</summary>
    public static int Stats(int floors, int length)
    {
        var kinds = new Dictionary<SegmentKind, int>();
        var rooms = new Dictionary<RoomKind, int>();
        var buckets = new int[5];
        var tiles = new Dictionary<TileKind, int>();
        int totalRows = 0, throatRuns = 0, shallowRows = 0, shallowTight = 0, deepRows = 0, deepTight = 0;
        // Chasm = void INSIDE the carved band, i.e. a hole you could be shoved into, as opposed to
        // the void beyond the walls. Counted rather than eyeballed, because a feature that fires
        // rarely is indistinguishable from one that never fires when you are reading dumps.
        int chasmRows = 0;

        for (int s = 0; s < floors; s++)
        {
            // Sweep floors 1..10 so the ramp is exercised, not just floor 1.
            var floor = new Warren(s).Generate(length, 1 + s % 10);
            totalRows += floor.Length;

            int lastSeg = -1;
            bool prevSegThroat = false;
            for (int y = 0; y < floor.Length; y++)
            {
                kinds[floor.RowKind[y]] = kinds.GetValueOrDefault(floor.RowKind[y]) + 1;
                buckets[Math.Min(4, floor.Constriction[y] / 20)]++;

                bool tight = floor.Constriction[y] >= 60;
                if (floor.Number <= 3) { shallowRows++; if (tight) shallowTight++; }
                else { deepRows++; if (tight) deepTight++; }

                // By SEGMENT, never by kind: kind-change cannot see two Throats in a row.
                if (floor.RowSegment[y] != lastSeg)
                {
                    if (floor.RowKind[y] == SegmentKind.Throat && prevSegThroat) throatRuns++;
                    prevSegThroat = floor.RowKind[y] == SegmentKind.Throat;
                    lastSeg = floor.RowSegment[y];
                }
                int firstX = floor.FirstWalkable(y), lastX = -1;
                for (int x = floor.Width - 1; x >= 0; x--) if (floor.Walkable(x, y)) { lastX = x; break; }
                bool chasm = false;
                for (int x = 0; x < floor.Width; x++)
                {
                    tiles[floor.Tiles[x, y]] = tiles.GetValueOrDefault(floor.Tiles[x, y]) + 1;
                    if (floor.Tiles[x, y] == TileKind.Void && x > firstX && x < lastX) chasm = true;
                }
                if (chasm) chasmRows++;

                if (y == 0 || floor.RowRoom[y] != floor.RowRoom[y - 1])
                    rooms[floor.RowRoom[y]] = rooms.GetValueOrDefault(floor.RowRoom[y]) + 1;
            }
        }

        Console.WriteLine($"FLOOR STATS  {floors} floors, target length {length}  ({totalRows} rows)\n");
        Console.WriteLine($"  mean floor length     {totalRows / (float)floors:F1} rows");
        Console.WriteLine($"  chained throats       {throatRuns}  ({throatRuns / (float)floors:F2} per floor)");
        Console.WriteLine($"  special rooms/floor   Vault {rooms.GetValueOrDefault(RoomKind.Vault) / (float)floors:F2}" +
                          $"   Shrine {rooms.GetValueOrDefault(RoomKind.Shrine) / (float)floors:F2}" +
                          $"   Warden {rooms.GetValueOrDefault(RoomKind.Warden) / (float)floors:F2}\n");

        int floorCells = tiles.Values.Sum();
        Console.WriteLine("  TILE COMPOSITION");
        foreach (var (t, n) in tiles.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    {t,-8} {n,7}  {n * 100f / floorCells,5:F2}%");
        Console.WriteLine($"    chasm rows (void inside the band): {chasmRows}  " +
                          $"({chasmRows * 100f / totalRows:F1}% of rows)\n");

        Console.WriteLine("  ROW SHARE BY ARCHETYPE");
        foreach (var (k, n) in kinds.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    {k,-9} {n,6}  {n * 100f / totalRows,5:F1}%  {new string('#', n * 40 / totalRows)}");

        Console.WriteLine("\n  CONSTRICTION DISTRIBUTION");
        string[] names = { "  0-19 open", " 20-39", " 40-59", " 60-79", "80-100 tight" };
        for (int i = 0; i < buckets.Length; i++)
            Console.WriteLine($"    {names[i],-14} {buckets[i],6}  {buckets[i] * 100f / totalRows,5:F1}%  " +
                              $"{new string('#', buckets[i] * 40 / totalRows)}");

        float tightAll = (buckets[3] + buckets[4]) * 100f / totalRows;
        Console.WriteLine($"\n  TIGHT GROUND (60+): {tightAll:F1}% overall");
        if (shallowRows > 0) Console.WriteLine($"    floors 1-3    {shallowTight * 100f / shallowRows,5:F1}%");
        if (deepRows > 0) Console.WriteLine($"    floors 4-10   {deepTight * 100f / deepRows,5:F1}%");
        Console.WriteLine(tightAll is >= 15f and <= 35f
            ? "  -> in band (15-35%)."
            : "  -> OUT OF BAND (want 15-35%). Tune Warren.PickKind.");
        return 0;
    }

    /// <summary>
    /// Prove every floor can actually be walked, entry to Warden.
    ///
    /// This replaced a check that every row had SOME walkable cell — which is necessary and
    /// nowhere near sufficient. Movement is orthogonal, so a width-1 row at column 5 above a
    /// width-1 row at column 6 gives two cells that are only diagonally adjacent: the old check
    /// passed and the floor was unwinnable. On a one-way descent that is a dead run, and the
    /// player could not even walk back to see why.
    /// </summary>
    public static int Verify(int floors, int length)
    {
        int fails = 0, worst = int.MaxValue;
        for (int s = 0; s < floors; s++)
        {
            var floor = new Warren(s).Generate(length, 1 + s % 10);

            if (!floor.IsPassable(out int reached))
            {
                Console.WriteLine($"  seed {s}: IMPASSABLE — reached row {reached} of {floor.Length - 1}");
                fails++;
                worst = Math.Min(worst, reached);
            }

            for (int y = 0; y < floor.Length; y++)
                if (floor.FirstWalkable(y) < 0)
                { Console.WriteLine($"  seed {s} row {y}: no walkable cell"); fails++; break; }

            if (floor.RowRoom[^1] != RoomKind.Warden)
            { Console.WriteLine($"  seed {s}: floor does not end in the Warden's chamber"); fails++; }

            // Determinism: same seed, same floor, or the sim is worthless.
            var b = new Warren(s).Generate(length, 1 + s % 10);
            if (b.Length != floor.Length) { Console.WriteLine($"  seed {s}: NOT DETERMINISTIC (length)"); fails++; }
            else
                for (int y = 0; y < floor.Length && fails == 0; y++)
                    for (int x = 0; x < floor.Width; x++)
                        if (floor.Tiles[x, y] != b.Tiles[x, y])
                        { Console.WriteLine($"  seed {s} ({x},{y}): NOT DETERMINISTIC"); fails++; break; }
        }

        Console.WriteLine(fails == 0
            ? $"floor verify: {floors} floors x ~{length} rows — passable, deterministic, Warden-terminated."
            : $"floor verify: {fails} FAILURES (shallowest reach {worst}).");
        return fails == 0 ? 0 : 1;
    }
}

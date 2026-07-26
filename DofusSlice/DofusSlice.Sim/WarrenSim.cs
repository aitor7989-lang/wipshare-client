using DofusSlice.Core.Grid;
using DofusSlice.Core.World;

namespace DofusSlice.Sim;

/// <summary>
/// Headless harness for the WANDERER floor generator: print a floor as a map so a human can look at
/// it, measure many so the grammar is tuned from numbers, and prove every floor can be walked.
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

    /// <summary>Print the floor as a 2D map, trimmed to what was carved.</summary>
    public static int Dump(int seed, int steps, int sight = 0)
    {
        var floor = new Warren(seed).Generate(steps);
        Fog? fog = null;
        if (sight > 0) { fog = new Fog(floor); fog.WalkWithTorch(sight); }

        // Mark the path so turns and rooms are legible on the map itself.
        var marks = new Dictionary<(int, int), char>();
        foreach (var s in floor.Path)
            marks[(s.X, s.Y)] = s.Room switch
            {
                RoomKind.Warden => 'W',
                RoomKind.Vault => 'V',
                RoomKind.Shrine => 'S',
                _ => s.Constriction >= 60 ? 't' : '+',
            };
        var (ex, ey) = floor.Entry;
        marks[(ex, ey)] = 'A';

        int minX = floor.Width, maxX = 0, minY = floor.Height, maxY = 0;
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (floor.Tiles[x, y] != TileKind.Void)
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }

        Console.WriteLine($"FLOOR  seed={seed}  grid {floor.Width}x{floor.Height}  " +
                          $"path {floor.Path.Count} steps  bounds {maxX - minX + 1}x{maxY - minY + 1}" +
                          (sight > 0 ? $"  fog: sight {sight}" : ""));
        Console.WriteLine("  '.' floor  ':' worn  '#' rock  '~' water  '\"' spikes");
        Console.WriteLine("  path: 'A' entry  '+' walked  't' tight  'S' shrine  'V' vault  'W' warden\n");

        for (int y = minY; y <= maxY; y++)
        {
            var sb = new System.Text.StringBuilder();
            for (int x = minX; x <= maxX; x++)
            {
                if (fog is not null && !fog.Seen(x, y)) { sb.Append(' '); continue; }
                sb.Append(marks.TryGetValue((x, y), out char m) ? m : Glyph(floor.Tiles[x, y]));
            }
            Console.WriteLine($"  {sb}");
        }
        return 0;
    }

    /// <summary>Distribution across many floors. Weights are exactly the thing that feels right and
    /// measures wrong.</summary>
    public static int Stats(int floors, int steps)
    {
        var kinds = new Dictionary<SegmentKind, int>();
        var rooms = new Dictionary<RoomKind, int>();
        var tiles = new Dictionary<TileKind, int>();
        var headings = new Dictionary<Heading, int>();
        var buckets = new int[5];
        int totalSteps = 0, throatRuns = 0, turns = 0;
        int shallowSteps = 0, shallowTight = 0, deepSteps = 0, deepTight = 0;

        for (int s = 0; s < floors; s++)
        {
            var floor = new Warren(s).Generate(steps, 1 + s % 10);
            totalSteps += floor.Path.Count;

            int lastSeg = -1;
            bool prevSegThroat = false;
            int lastDir = -1;

            for (int i = 0; i < floor.Path.Count; i++)
            {
                var st = floor.Path[i];
                kinds[st.Kind] = kinds.GetValueOrDefault(st.Kind) + 1;
                headings[st.Dir] = headings.GetValueOrDefault(st.Dir) + 1;
                buckets[Math.Min(4, st.Constriction / 20)]++;

                bool tight = st.Constriction >= 60;
                if (floor.Number <= 3) { shallowSteps++; if (tight) shallowTight++; }
                else { deepSteps++; if (tight) deepTight++; }

                if (lastDir >= 0 && (int)st.Dir != lastDir) turns++;
                lastDir = (int)st.Dir;

                // By SEGMENT, never by kind: kind-change cannot see two Throats in a row.
                if (st.Segment != lastSeg)
                {
                    if (st.Kind == SegmentKind.Throat && prevSegThroat) throatRuns++;
                    prevSegThroat = st.Kind == SegmentKind.Throat;
                    lastSeg = st.Segment;
                }
                if (i == 0 || st.Room != floor.Path[i - 1].Room)
                    rooms[st.Room] = rooms.GetValueOrDefault(st.Room) + 1;
            }

            for (int y = 0; y < floor.Height; y++)
                for (int x = 0; x < floor.Width; x++)
                    if (floor.Tiles[x, y] != TileKind.Void)
                        tiles[floor.Tiles[x, y]] = tiles.GetValueOrDefault(floor.Tiles[x, y]) + 1;
        }

        Console.WriteLine($"FLOOR STATS  {floors} floors, target {steps} steps\n");
        Console.WriteLine($"  mean path length      {totalSteps / (float)floors:F1} steps");
        Console.WriteLine($"  turns per floor       {turns / (float)floors:F1}   <- 0 would mean a strip");
        Console.WriteLine($"  chained throats       {throatRuns / (float)floors:F2} per floor");
        Console.WriteLine($"  special rooms/floor   Vault {rooms.GetValueOrDefault(RoomKind.Vault) / (float)floors:F2}" +
                          $"   Shrine {rooms.GetValueOrDefault(RoomKind.Shrine) / (float)floors:F2}" +
                          $"   Warden {rooms.GetValueOrDefault(RoomKind.Warden) / (float)floors:F2}");
        Console.Write("  heading spread        ");
        foreach (var h in Enum.GetValues<Heading>())
            Console.Write($"{h} {headings.GetValueOrDefault(h) * 100f / totalSteps:F0}%  ");
        Console.WriteLine("  <- all four, or it favours an axis\n");

        int cells = tiles.Values.Sum();
        Console.WriteLine("  TILE COMPOSITION (carved cells only)");
        foreach (var (t, n) in tiles.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    {t,-8} {n,7}  {n * 100f / cells,5:F2}%");

        Console.WriteLine("\n  STEP SHARE BY ARCHETYPE");
        foreach (var (k, n) in kinds.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    {k,-9} {n,6}  {n * 100f / totalSteps,5:F1}%  {new string('#', n * 40 / totalSteps)}");

        Console.WriteLine("\n  CONSTRICTION DISTRIBUTION");
        string[] names = { "  0-19 open", " 20-39", " 40-59", " 60-79", "80-100 tight" };
        for (int i = 0; i < buckets.Length; i++)
            Console.WriteLine($"    {names[i],-14} {buckets[i],6}  {buckets[i] * 100f / totalSteps,5:F1}%  " +
                              $"{new string('#', buckets[i] * 40 / totalSteps)}");

        float tightAll = (buckets[3] + buckets[4]) * 100f / totalSteps;
        Console.WriteLine($"\n  TIGHT GROUND (60+): {tightAll:F1}% overall");
        if (shallowSteps > 0) Console.WriteLine($"    floors 1-3    {shallowTight * 100f / shallowSteps,5:F1}%");
        if (deepSteps > 0) Console.WriteLine($"    floors 4-10   {deepTight * 100f / deepSteps,5:F1}%");
        Console.WriteLine(tightAll is >= 10f and <= 30f
            ? "  -> in band (10-30%)."
            : "  -> OUT OF BAND (want 10-30%). Tune Warren.TightBudget.");
        return 0;
    }

    /// <summary>
    /// Prove every floor can be walked, entry to Warden.
    ///
    /// This replaced a check that every row had SOME walkable cell — necessary and nowhere near
    /// sufficient, since movement is orthogonal and two diagonally-adjacent cells are mutually
    /// unreachable. That check reported clean while 24 floors in 400 were unwinnable.
    /// </summary>
    public static int Verify(int floors, int steps)
    {
        int fails = 0;
        for (int s = 0; s < floors; s++)
        {
            var floor = new Warren(s).Generate(steps, 1 + s % 10);

            if (!floor.IsPassable(out int reached))
            {
                Console.WriteLine($"  seed {s}: IMPASSABLE — reached step {reached} of {floor.Path.Count}");
                fails++;
            }
            if (floor.Path[^1].Room != RoomKind.Warden)
            { Console.WriteLine($"  seed {s}: does not end in the Warden's chamber"); fails++; }

            // Every step adjacent to the last, or "one orthogonal cell per step" is a comment rather
            // than a property — and the structural passability argument rests entirely on it.
            for (int i = 1; i < floor.Path.Count; i++)
            {
                int d = Math.Abs(floor.Path[i].X - floor.Path[i - 1].X) +
                        Math.Abs(floor.Path[i].Y - floor.Path[i - 1].Y);
                if (d > 1)
                { Console.WriteLine($"  seed {s} step {i}: path jumped {d} cells"); fails++; break; }
            }

            var b = new Warren(s).Generate(steps, 1 + s % 10);
            if (b.Path.Count != floor.Path.Count)
            { Console.WriteLine($"  seed {s}: NOT DETERMINISTIC"); fails++; }
        }

        Console.WriteLine(fails == 0
            ? $"floor verify: {floors} floors — passable, contiguous, deterministic, Warden-terminated."
            : $"floor verify: {fails} FAILURES.");
        return fails == 0 ? 0 : 1;
    }
}

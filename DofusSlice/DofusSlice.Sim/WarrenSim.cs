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

        // Terrain and annotation are printed as TWO layers, never merged. Painting 'S'/'V'/'W'
        // over the tile destroys the terrain underneath, which makes the map unreadable as a place
        // — you cannot tell a shrine's floor from its walls — and leaves any consumer unable to
        // render the room as a room rather than as a letter.
        var marks = new Dictionary<(int, int), char>();
        // Path first, then room FOOTPRINTS over the top: a room is an area, and marking only the
        // path cells inside it describes the walk through the room rather than the room.
        foreach (var st in floor.Path)
            if (st.Constriction >= 60) marks[(st.X, st.Y)] = 't';
            else marks[(st.X, st.Y)] = '+';
        foreach (var r in floor.Rooms)
        {
            char c = r.Kind switch { RoomKind.Warden => 'W', RoomKind.Vault => 'V', _ => 'S' };
            for (int y = r.Y; y < r.Y + r.H; y++)
                for (int x = r.X; x < r.X + r.W; x++)
                    if (floor.Tiles[x, y] != TileKind.Void) marks[(x, y)] = c;
        }
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
        Console.WriteLine("  TERRAIN  '.' floor  ':' worn  '#' rock  '~' water  '\"' spikes");
        Console.WriteLine("  MARKS    'A' entry  '+' walked  't' tight  'S' shrine  'V' vault  'W' warden");

        Console.WriteLine("\n[TERRAIN]");
        for (int y = minY; y <= maxY; y++)
        {
            var sb = new System.Text.StringBuilder();
            for (int x = minX; x <= maxX; x++)
                sb.Append(fog is not null && !fog.Seen(x, y) ? ' ' : Glyph(floor.Tiles[x, y]));
            Console.WriteLine($"  {sb}");
        }

        Console.WriteLine("\n[MARKS]");
        for (int y = minY; y <= maxY; y++)
        {
            var sb = new System.Text.StringBuilder();
            for (int x = minX; x <= maxX; x++)
            {
                if (fog is not null && !fog.Seen(x, y)) { sb.Append(' '); continue; }
                sb.Append(marks.TryGetValue((x, y), out char m) ? m : ' ');
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
        var roomSizes = new List<RoomRect>();
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

            }

            // Counted from the ROOM LIST, not from path steps. Shrines and vaults hang off spurs
            // now, so no path step is ever tagged with them — reading the path reported zero rooms
            // for a feature that was working, which is the third time this harness has managed to
            // be blind to the thing it was measuring.
            roomSizes.AddRange(floor.Rooms);
            foreach (var r in floor.Rooms) rooms[r.Kind] = rooms.GetValueOrDefault(r.Kind) + 1;
            for (int y = 0; y < floor.Height; y++)
                for (int x = 0; x < floor.Width; x++)
                    if (floor.Tiles[x, y] != TileKind.Void)
                        tiles[floor.Tiles[x, y]] = tiles.GetValueOrDefault(floor.Tiles[x, y]) + 1;
        }

        Console.WriteLine($"FLOOR STATS  {floors} floors, target {steps} steps\n");
        Console.WriteLine($"  mean path length      {totalSteps / (float)floors:F1} steps");
        Console.WriteLine($"  turns per floor       {turns / (float)floors:F1}   <- 0 would mean a strip");
        Console.WriteLine($"  chained throats       {throatRuns / (float)floors:F2} per floor");
        Console.WriteLine($"  room sizes            " + string.Join("  ",
            new[] { RoomKind.Shrine, RoomKind.Vault, RoomKind.Warden }.Select(k =>
                $"{k} {string.Join("/", roomSizes.Where(r => r.Kind == k).Select(r => r.W).Distinct().OrderBy(v => v))}")));
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
    /// EXPRESSIVE RANGE ANALYSIS (Smith &amp; Whitehead, 2010).
    ///
    /// Every other number this harness prints is a MEAN, and a mean cannot tell the difference
    /// between a generator that explores its space and one that makes the same floor four hundred
    /// times. ERA plots each floor as a point on two metric axes and looks at the DENSITY of the
    /// cloud: a tight blob means one floor with cosmetic variation, a spread cloud means genuine
    /// range, and an empty region means content the generator can never produce.
    ///
    /// The literature's main complaint about ERA is that the choice of axes is usually left
    /// unjustified, so: TIGHTNESS (mean constriction) against WINDINESS (turns per 100 steps).
    /// Those are the two the design's tension actually rests on — how often the ground pins you,
    /// and how much of the floor you cannot see coming.
    /// </summary>
    public static int Era(int floors, int steps)
    {
        const int CW = 46, CH = 18;      // plot cells
        var grid = new int[CH, CW];
        float xMin = 100, xMax = 0, yMin = 999, yMax = 0;
        var pts = new List<(float X, float Y)>();

        for (int s = 0; s < floors; s++)
        {
            var f = new Warren(s).Generate(steps, 1 + s % 10);
            float tight = (float)f.Path.Average(p => p.Constriction);
            int turns = 0;
            for (int i = 1; i < f.Path.Count; i++) if (f.Path[i].Dir != f.Path[i - 1].Dir) turns++;
            float wind = turns * 100f / f.Path.Count;
            pts.Add((tight, wind));
            xMin = Math.Min(xMin, tight); xMax = Math.Max(xMax, tight);
            yMin = Math.Min(yMin, wind); yMax = Math.Max(yMax, wind);
        }

        // Pad the axes so points never land exactly on the frame.
        float xs = Math.Max(1f, xMax - xMin), ys = Math.Max(1f, yMax - yMin);
        xMin -= xs * 0.06f; xMax += xs * 0.06f; yMin -= ys * 0.06f; yMax += ys * 0.06f;

        foreach (var (x, y) in pts)
        {
            int cx = (int)((x - xMin) / (xMax - xMin) * (CW - 1));
            int cy = (int)((1 - (y - yMin) / (yMax - yMin)) * (CH - 1));
            grid[Math.Clamp(cy, 0, CH - 1), Math.Clamp(cx, 0, CW - 1)]++;
        }

        int peak = 0, filled = 0;
        for (int y = 0; y < CH; y++)
            for (int x = 0; x < CW; x++)
            { peak = Math.Max(peak, grid[y, x]); if (grid[y, x] > 0) filled++; }

        const string RAMP = " .:-=+*#%@";
        Console.WriteLine($"EXPRESSIVE RANGE  {floors} floors, {steps} steps, floors 1-10\n");
        Console.WriteLine($"  y = windiness (turns per 100 steps)   {yMax:F1} at top, {yMin:F1} at bottom");
        Console.WriteLine($"  x = tightness (mean constriction)     {xMin:F1} at left, {xMax:F1} at right");
        Console.WriteLine($"  density ramp: '{RAMP.Trim()}'  (peak {peak} floors in one cell)\n");

        for (int y = 0; y < CH; y++)
        {
            var sb = new System.Text.StringBuilder("  |");
            for (int x = 0; x < CW; x++)
            {
                int n = grid[y, x];
                sb.Append(n == 0 ? ' ' : RAMP[Math.Min(RAMP.Length - 1, 1 + n * (RAMP.Length - 2) / Math.Max(1, peak))]);
            }
            Console.WriteLine(sb.Append('|').ToString());
        }
        Console.WriteLine("  +" + new string('-', CW) + "+\n");

        float coverage = filled * 100f / (CW * CH);
        Console.WriteLine($"  cells occupied      {filled}/{CW * CH}  ({coverage:F1}% of the plot)");
        Console.WriteLine($"  tightness spread    {xMax - xMin:F1} points of constriction");
        Console.WriteLine($"  windiness spread    {yMax - yMin:F1} turns per 100 steps");
        // A blob in one corner is the failure this plot exists to expose: it means the knobs move
        // numbers without moving the floors.
        Console.WriteLine(coverage >= 12f
            ? "  -> the cloud has real spread: floors differ structurally, not just cosmetically."
            : "  -> CLUSTERED. The generator is making one floor with variations. Widen the grammar,\n" +
              "     not the tuning: more archetypes or more varied segment lengths.");
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

            // Every special room must be REACHABLE from the entry — passability alone only proves
            // the Warden. Room isolation shaves cells around chambers, and a shave that severed a
            // shrine's spur would leave a sealed treasure the player can see and never enter, with
            // the entry-to-Warden proof still green.
            foreach (var room in floor.Rooms)
            {
                var seenR = new bool[floor.Width, floor.Height];
                var qr = new Queue<(int X, int Y)>();
                var (rx, ry) = floor.Entry;
                seenR[rx, ry] = true; qr.Enqueue((rx, ry));
                bool roomHit = false;
                while (qr.Count > 0 && !roomHit)
                {
                    var (cx2, cy2) = qr.Dequeue();
                    if (room.Contains(cx2, cy2)) { roomHit = true; break; }
                    foreach (var (ddx, ddy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx2 = cx2 + ddx, ny2 = cy2 + ddy;
                        if (nx2 < 0 || ny2 < 0 || nx2 >= floor.Width || ny2 >= floor.Height) continue;
                        if (!floor.Walkable(nx2, ny2) || seenR[nx2, ny2]) continue;
                        seenR[nx2, ny2] = true; qr.Enqueue((nx2, ny2));
                    }
                }
                if (!roomHit)
                { Console.WriteLine($"  seed {s}: {room.Kind} at ({room.X},{room.Y}) UNREACHABLE"); fails++; }
            }

            // No orphan islands: every scrap of material must be 8-way connected to the entry
            // through other material. Room isolation shaves ground back to void, and a shave can
            // set a lobe of floor adrift — ground the fog would reveal and no one could reach.
            {
                var keep = new bool[floor.Width, floor.Height];
                var qi = new Queue<(int X, int Y)>();
                var (ix, iy) = floor.Entry;
                keep[ix, iy] = true; qi.Enqueue((ix, iy));
                while (qi.Count > 0)
                {
                    var (cx3, cy3) = qi.Dequeue();
                    for (int dy3 = -1; dy3 <= 1; dy3++)
                        for (int dx3 = -1; dx3 <= 1; dx3++)
                        {
                            int nx3 = cx3 + dx3, ny3 = cy3 + dy3;
                            if (nx3 < 0 || ny3 < 0 || nx3 >= floor.Width || ny3 >= floor.Height) continue;
                            if (keep[nx3, ny3] || floor.Tiles[nx3, ny3] == TileKind.Void) continue;
                            keep[nx3, ny3] = true; qi.Enqueue((nx3, ny3));
                        }
                }
                int orphans = 0;
                for (int y3 = 0; y3 < floor.Height; y3++)
                    for (int x3 = 0; x3 < floor.Width; x3++)
                        if (floor.Tiles[x3, y3] != TileKind.Void && !keep[x3, y3]) orphans++;
                if (orphans > 0)
                { Console.WriteLine($"  seed {s}: {orphans} orphan island cells adrift in the void"); fails++; }
            }

            // The fog's promise: a full walk with the torch reveals no walkable cell the player
            // could not orthogonally reach. If this fires, Reveal is leaking across the void again.
            {
                var fog = new Fog(floor);
                fog.WalkWithTorch(4);
                var reach = new bool[floor.Width, floor.Height];
                var qf = new Queue<(int X, int Y)>();
                var (fx, fy) = floor.Entry;
                reach[fx, fy] = true; qf.Enqueue((fx, fy));
                while (qf.Count > 0)
                {
                    var (cx4, cy4) = qf.Dequeue();
                    foreach (var (dx4, dy4) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx4 = cx4 + dx4, ny4 = cy4 + dy4;
                        if (nx4 < 0 || ny4 < 0 || nx4 >= floor.Width || ny4 >= floor.Height) continue;
                        if (reach[nx4, ny4] || !floor.Walkable(nx4, ny4)) continue;
                        reach[nx4, ny4] = true; qf.Enqueue((nx4, ny4));
                    }
                }
                int leaks = 0;
                for (int y4 = 0; y4 < floor.Height; y4++)
                    for (int x4 = 0; x4 < floor.Width; x4++)
                        if (floor.Walkable(x4, y4) && fog.Seen(x4, y4) && !reach[x4, y4]) leaks++;
                if (leaks > 0)
                { Console.WriteLine($"  seed {s}: fog revealed {leaks} walkable cells the player cannot reach"); fails++; }
            }

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

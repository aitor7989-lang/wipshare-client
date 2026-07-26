using DofusSlice.Core.Grid;
using DofusSlice.Core.World;

namespace DofusSlice.Sim;

/// <summary>
/// Headless harness for the WANDERER path generator. Two jobs: print a descent so a human can
/// look at it, and measure a long one so the grammar gets tuned from numbers instead of vibes.
/// </summary>
public static class WarrenSim
{
    private static char Glyph(TileKind t) => t switch
    {
        TileKind.Void => ' ',
        TileKind.Rock => '#',
        TileKind.Path => ':',
        _ => '.',
    };

    private static char Mark(SegmentKind k) => k switch
    {
        SegmentKind.Throat => 'T',
        SegmentKind.Corridor => 'c',
        SegmentKind.Gallery => 'G',
        SegmentKind.Hall => 'H',
        SegmentKind.Fork => 'Y',
        _ => 'o',
    };

    /// <summary>Print <paramref name="rows"/> rows of a seeded descent.</summary>
    public static int Dump(int seed, int rows)
    {
        var w = new Warren(seed);
        Console.WriteLine($"WARREN  seed={seed}  rows={rows}  width={Warren.Width}");
        Console.WriteLine("  '.' floor  ':' worn floor  '#' rock  ' ' the deeps");
        Console.WriteLine("  right column: segment kind, then constriction 0-100\n");

        var last = (SegmentKind)(-1);
        for (int d = 0; d < rows; d++)
        {
            var row = w.RowAt(d);
            string line = new(row.Cells.Select(Glyph).ToArray());
            // A rule at every segment boundary, so the grammar's pacing is visible as pacing
            // rather than having to be inferred from the tiles.
            if (row.Kind != last)
            {
                Console.WriteLine($"      {new string('-', Warren.Width)}  {row.Kind}");
                last = row.Kind;
            }
            Console.WriteLine($"{d,4}  |{line}|  {Mark(row.Kind)} {row.Constriction,3}");
        }
        return 0;
    }

    /// <summary>Walk a long descent and report the distribution. This is the tuning instrument:
    /// the grammar is a pile of weights, and weights are exactly the kind of thing that feels
    /// right and measures wrong.</summary>
    public static int Stats(int seed, int rows)
    {
        var w = new Warren(seed);
        var kinds = new Dictionary<SegmentKind, int>();
        var segments = new List<(SegmentKind Kind, int Start)>();
        var buckets = new int[5];          // constriction 0-19, 20-39, ... 80-100
        int peakLive = 0, throatRuns = 0;
        int lastSeg = -1;
        bool prevSegThroat = false;
        // Depth drives the grammar and saturates around 120, so a 4000-row sample is ~97%
        // "deepest difficulty". Reported split, or the headline number describes a regime the
        // player spends almost none of the run in.
        int shallowRows = 0, shallowTight = 0, deepRows = 0, deepTight = 0;

        for (int d = 0; d < rows; d++)
        {
            var row = w.RowAt(d);
            kinds[row.Kind] = kinds.GetValueOrDefault(row.Kind) + 1;
            buckets[Math.Min(4, row.Constriction / 20)]++;

            if (row.SegmentIndex != lastSeg)
            {
                segments.Add((row.Kind, d));
                if (row.Kind == SegmentKind.Throat && prevSegThroat) throatRuns++;
                prevSegThroat = row.Kind == SegmentKind.Throat;
                lastSeg = row.SegmentIndex;
            }

            bool tightRow = row.Constriction >= 60;
            if (d < 120) { shallowRows++; if (tightRow) shallowTight++; }
            else { deepRows++; if (tightRow) deepTight++; }

            // The collapse, exercised: keep a brink trailing the light so LiveRows should sit
            // flat forever. If this climbs, the tail is being held alive somewhere and the
            // one-way premise is decorative rather than real.
            w.Forget(d - 24);
            peakLive = Math.Max(peakLive, w.LiveRows);
        }

        Console.WriteLine($"WARREN STATS  seed={seed}  rows={rows}\n");
        Console.WriteLine($"  segments        {segments.Count}  (mean {rows / (float)segments.Count:F1} rows)");
        Console.WriteLine($"  peak live rows  {peakLive}   <- must stay flat; the collapse is real if it does");
        Console.WriteLine($"  back-to-back throats  {throatRuns}   <- the chained-ambush threat\n");

        Console.WriteLine("  ROW SHARE BY ARCHETYPE");
        foreach (var (k, n) in kinds.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    {k,-9} {n,5}  {n * 100f / rows,5:F1}%  {new string('#', n * 40 / rows)}");

        Console.WriteLine("\n  CONSTRICTION DISTRIBUTION");
        string[] names = { "  0-19 open", " 20-39", " 40-59", " 60-79", "80-100 tight" };
        for (int i = 0; i < buckets.Length; i++)
            Console.WriteLine($"    {names[i],-14} {buckets[i],5}  {buckets[i] * 100f / rows,5:F1}%  " +
                              $"{new string('#', buckets[i] * 40 / rows)}");

        // The design cares about one number above all: how often the ground is tight enough for a
        // chokepoint ambush to mean something. Too rare and the centrepiece beat never fires; too
        // common and it stops being an event.
        float tight = (buckets[3] + buckets[4]) * 100f / rows;
        Console.WriteLine($"\n  TIGHT GROUND (constriction 60+): {tight:F1}% overall");
        if (shallowRows > 0)
            Console.WriteLine($"    depth 0-119   {shallowTight * 100f / shallowRows,5:F1}%  (the ramp)");
        if (deepRows > 0)
            Console.WriteLine($"    depth 120+    {deepTight * 100f / deepRows,5:F1}%  (saturated)");
        Console.WriteLine(tight is >= 15f and <= 35f
            ? "  -> in band (15-35%): chokepoints are frequent enough to threaten, rare enough to matter."
            : "  -> OUT OF BAND (want 15-35%). Tune the grammar weights in Warren.PickKind.");
        return 0;
    }

    /// <summary>Verify the generator holds its invariants over many seeds. Cheap, and it covers
    /// the failure that would be worst to find later: a row you cannot walk through at all, on a
    /// path that by definition has no way around and no way back.</summary>
    public static int Verify(int seeds, int rows)
    {
        int fails = 0;
        for (int s = 0; s < seeds; s++)
        {
            var w = new Warren(s);
            for (int d = 0; d < rows; d++)
            {
                var row = w.RowAt(d);
                if (row.Cells.Length != Warren.Width)
                { Console.WriteLine($"  seed {s} row {d}: width {row.Cells.Length}"); fails++; }
                if (!Enumerable.Range(0, Warren.Width).Any(row.Walkable))
                { Console.WriteLine($"  seed {s} row {d}: IMPASSABLE"); fails++; }
                if (row.Constriction is < 0 or > 100)
                { Console.WriteLine($"  seed {s} row {d}: constriction {row.Constriction}"); fails++; }
            }

            // Determinism: same seed, same ground. The sim is worthless without it.
            var a = new Warren(s); var b = new Warren(s);
            for (int d = 0; d < 40; d++)
                if (!a.RowAt(d).Cells.SequenceEqual(b.RowAt(d).Cells))
                { Console.WriteLine($"  seed {s} row {d}: NOT DETERMINISTIC"); fails++; break; }
        }
        Console.WriteLine(fails == 0
            ? $"warren verify: {seeds} seeds x {rows} rows — all passed."
            : $"warren verify: {fails} FAILURES.");
        return fails == 0 ? 0 : 1;
    }
}

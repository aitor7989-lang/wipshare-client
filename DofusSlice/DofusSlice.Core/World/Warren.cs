using DofusSlice.Core.Combat;
using DofusSlice.Core.Grid;

namespace DofusSlice.Core.World;

/// <summary>The archetypes the grammar strings together. Each one exists because it changes what
/// an ambush in it would mean — that is the bar for adding another.</summary>
public enum SegmentKind
{
    /// <summary>One or two cells wide. The chokepoint: nowhere to spread, nowhere to dodge.</summary>
    Throat,
    /// <summary>Narrow but walkable two abreast. The connective tissue.</summary>
    Corridor,
    /// <summary>Medium and pillared — cover exists, so line of sight is a puzzle.</summary>
    Gallery,
    /// <summary>Wide and empty. Room to kite, room to be surrounded.</summary>
    Hall,
    /// <summary>Splits into two channels around a spine. The only place the path offers a choice.</summary>
    Fork,
    /// <summary>A rubble-choked chamber. Open floor broken by scattered blockers.</summary>
    Cairn,
}

/// <summary>One generated row of the descent.</summary>
public readonly struct WarrenRow
{
    /// <summary>Left-to-right cells. Always <see cref="Warren.Width"/> long.</summary>
    public TileKind[] Cells { get; init; }
    public SegmentKind Kind { get; init; }
    public int Depth { get; init; }

    /// <summary>Which segment this row belongs to, counting from 0. Kind alone cannot identify a
    /// segment: two Throats in a row are two segments, and anything detecting boundaries by
    /// "the kind changed" silently merges them — which is exactly how the chained-ambush metric
    /// managed to report zero while the generator was emitting them.</summary>
    public int SegmentIndex { get; init; }

    /// <summary>0 = wide open, 100 = single-file. THE scalar the ambush placer, the AI and the
    /// sim all read, so that "a tight section" means one number to every system instead of each
    /// re-deriving tightness from tiles in its own slightly different way.</summary>
    public int Constriction { get; init; }

    public bool Walkable(int x) =>
        x >= 0 && x < Cells.Length && Cells[x] is not (TileKind.Void or TileKind.Rock);
}

/// <summary>
/// The path generator for the WANDERER descent (see docs/WANDERER.md).
///
/// It is a LAZY ROW STREAM, not a map. Rows are produced forward on demand and cached; there is
/// no width-and-height world anywhere. That is what makes "the path raises from the deeps as you
/// walk onto it" and "it collapses behind you" cost nothing: the frontier is just the deepest row
/// anyone has asked for, and collapsing is <see cref="Forget"/> dropping the tail. No rolling
/// window, no coordinate rebasing — those belong to the game loop, and keeping them out of here is
/// the whole point of the split.
///
/// Two layers, per the brief:
///   MACRO — a weighted Markov chain over <see cref="SegmentKind"/>, conditioned on the previous
///           kind and on depth. The game design lives here, because a purely organic generator
///           (drunkard's walk, cellular automata) cannot PROMISE a chokepoint, and if a chokepoint
///           cannot be promised then the whole Brace/Push/Charge tension fires at random.
///   MICRO — a meandering centreline and a width that eases toward each segment's target, so a
///           Throat after a Hall reads as a funnel you can see closing a few rows out. That
///           legibility is a design requirement (§9: the player should be reading odds, not
///           guessing), not decoration.
///
/// Deliberately absent: entities, triggers, ambushes, loot, light. This generates GROUND.
/// </summary>
public sealed class Warren
{
    /// <summary>Cells across. Matches the combat board's width so a segment can host a fight
    /// without any coordinate translation later.</summary>
    public const int Width = 13;

    /// <summary>Widest either side may carve. Capped so the band plus one void cell each side
    /// always fits: uncapped, two sides that both jitter wide on a Hall make the carve wider than
    /// the corridor and the centreline's own clamp inverts.</summary>
    private const int MaxHalf = (Width - 3) / 2;

    private readonly IRng _rng;
    private readonly Dictionary<int, WarrenRow> _rows = new();

    // Generation cursor. Rows are only ever produced in increasing depth order, which is what
    // lets the grammar be a simple chain rather than something seekable.
    private int _built = -1;
    private SegmentKind _kind = SegmentKind.Corridor;
    private int _left;                  // rows remaining in the current segment
    private int _centre = Width / 2;
    // Left and right half-widths are tracked SEPARATELY. Symmetric widths can only be odd
    // (2*half+1), which quantises constriction to six possible values and leaves the design's
    // "moderately tight" band empty. Independent sides give every width from 1 to 13.
    private int _halfL = 1, _halfR = 1;
    private int _targetL = 1, _targetR = 1;
    private int _sinceThroat = 99;
    private int _segment = -1;

    public Warren(IRng rng)
    {
        _rng = rng;
        _left = 0;
    }

    public Warren(int seed) : this(new SystemRng(seed)) { }

    /// <summary>The deepest row generated so far.</summary>
    public int Frontier => _built;

    /// <summary>The row at <paramref name="depth"/>, generating forward if needed.</summary>
    public WarrenRow RowAt(int depth)
    {
        EnsureThrough(depth);
        if (_rows.TryGetValue(depth, out var row)) return row;
        // Asked for ground that already collapsed. That is a caller bug — the descent is one-way
        // and nothing should ever look back past the brink — so say so rather than fabricate it.
        throw new ArgumentOutOfRangeException(nameof(depth),
            $"Row {depth} has already collapsed (live from {_rows.Keys.Min()}).");
    }

    /// <summary>Generate forward until <paramref name="depth"/> exists.</summary>
    public void EnsureThrough(int depth)
    {
        while (_built < depth) BuildNextRow();
    }

    /// <summary>Drop everything shallower than <paramref name="depth"/> — the collapse behind.
    /// The brink TRAILS the light by design (§5), so callers should pass something a few rows
    /// back: with no room at all behind him, BREAK has nowhere to buy distance toward and Push
    /// and Charge degenerate into the same move.</summary>
    public void Forget(int depth)
    {
        if (_rows.Count == 0) return;
        foreach (var key in _rows.Keys.Where(k => k < depth).ToList()) _rows.Remove(key);
    }

    /// <summary>Rows currently held in memory. A descent of any length should keep this flat —
    /// if it climbs, something is holding the tail alive and the collapse is decorative.</summary>
    public int LiveRows => _rows.Count;

    // ---- Generation ------------------------------------------------------------------

    private void BuildNextRow()
    {
        int depth = _built + 1;
        if (_left <= 0) StartSegment(depth);

        // Ease toward the segment's target so a Hall->Throat transition reads as a funnel rather
        // than a wall with a hole in it. Two steps per row when the gap is wide, one when it is
        // close: a flat one-step ease took three rows to reach a Throat's width, which is longer
        // than a short Throat lasts, so the funnel ate the chokepoint entirely and the tightest
        // ground in the game never actually appeared.
        _halfL = Math.Min(Ease(_halfL, _targetL), MaxHalf);
        _halfR = Math.Min(Ease(_halfR, _targetR), MaxHalf);

        // Meander. Bounded so the carved band always fits, otherwise the corridor grinds along a
        // wall and every Throat lands in the same column.
        int drift = _rng.Roll(-1, 1);
        _centre = Math.Clamp(_centre + drift, _halfL + 1, Width - _halfR - 2);

        var cells = new TileKind[Width];
        for (int x = 0; x < Width; x++) cells[x] = TileKind.Void;

        int half = Math.Max(_halfL, _halfR);
        if (_kind == SegmentKind.Fork)
        {
            // Two channels around a spine. The spine is Rock, not Void: you cannot walk it, but
            // you CAN see and shoot across it, so the fork is a real tactical read rather than
            // two unrelated tunnels that happen to be adjacent.
            int gap = Math.Max(1, half);
            CarveBand(cells, _centre - gap - 1, Math.Max(1, half - 1), Math.Max(1, half - 1));
            CarveBand(cells, _centre + gap + 1, Math.Max(1, half - 1), Math.Max(1, half - 1));
            int spine = Math.Clamp(_centre, 1, Width - 2);
            cells[spine] = TileKind.Rock;
        }
        else
        {
            CarveBand(cells, _centre, _halfL, _halfR);
        }

        Decorate(cells, depth);

        int walk = cells.Count(c => c is not (TileKind.Void or TileKind.Rock));
        _rows[depth] = new WarrenRow
        {
            Cells = cells,
            Kind = _kind,
            Depth = depth,
            SegmentIndex = _segment,
            Constriction = Constrict(walk),
        };
        _built = depth;
        _left--;
    }

    /// <summary>Step at most 2 toward the target, so wide gaps close in two rows and narrow ones
    /// still ease.</summary>
    private static int Ease(int cur, int target)
    {
        int d = target - cur;
        if (d == 0) return cur;
        int step = Math.Abs(d) >= 2 ? 2 : 1;
        return cur + Math.Sign(d) * step;
    }

    private static void CarveBand(TileKind[] cells, int centre, int halfL, int halfR)
    {
        int lo = Math.Max(0, centre - halfL), hi = Math.Min(cells.Length - 1, centre + halfR);
        for (int x = lo; x <= hi; x++)
            if (cells[x] == TileKind.Void)
                cells[x] = TileKind.Dirt;
    }

    /// <summary>Walkable count to a 0..100 tightness. Non-linear on purpose: the difference
    /// between 1 and 3 cells of width is the difference between "cannot pass him" and "can", and
    /// the difference between 9 and 11 is nothing at all.</summary>
    private static int Constrict(int walkable)
    {
        if (walkable <= 1) return 100;
        float t = Math.Clamp((walkable - 1) / (float)(Width - 1), 0f, 1f);
        return (int)MathF.Round(100f * (1f - MathF.Sqrt(t)));
    }

    private void Decorate(TileKind[] cells, int depth)
    {
        switch (_kind)
        {
            case SegmentKind.Gallery:
                // Regular pillars, phase-shifted by depth so they form colonnades down the run
                // rather than one long wall. Cover that repeats is cover you can plan around.
                for (int x = 1; x < Width - 1; x++)
                    if (cells[x] == TileKind.Dirt && (x + depth / 2) % 4 == 0)
                        cells[x] = TileKind.Rock;
                break;

            case SegmentKind.Cairn:
                for (int x = 0; x < Width; x++)
                    if (cells[x] == TileKind.Dirt && _rng.Roll(0, 100) < 18)
                        cells[x] = TileKind.Rock;
                break;

            case SegmentKind.Hall:
                // A wide floor reads as a slab of one tone; the alternate shade gives the eye
                // something to measure distance against, which matters when the whole game is
                // judging whether a ring will reach you.
                for (int x = 0; x < Width; x++)
                    if (cells[x] == TileKind.Dirt && (x + depth) % 5 == 0)
                        cells[x] = TileKind.Path;
                break;
        }

        // A carved row that somehow closed completely would break the one-way premise outright.
        if (!cells.Any(c => c is not (TileKind.Void or TileKind.Rock)))
            cells[Math.Clamp(_centre, 0, Width - 1)] = TileKind.Dirt;
    }

    // ---- The grammar -----------------------------------------------------------------

    private void StartSegment(int depth)
    {
        var next = PickKind(depth);
        _kind = next;
        _segment++;
        _sinceThroat = next == SegmentKind.Throat ? 0 : _sinceThroat + 1;

        int baseHalf;
        // Throats run 5-9 rows, not 3-6: the funnel consumes the first rows, so a short Throat
        // is all approach and no chokepoint.
        (_left, baseHalf) = next switch
        {
            SegmentKind.Throat => (_rng.Roll(5, 9), 0),
            SegmentKind.Corridor => (_rng.Roll(6, 12), 1),
            SegmentKind.Gallery => (_rng.Roll(8, 14), 3),
            SegmentKind.Hall => (_rng.Roll(6, 10), 5),
            SegmentKind.Fork => (_rng.Roll(5, 9), 2),
            _ => (_rng.Roll(5, 8), 4),
        };

        // Jitter the two sides independently. A Throat stays hard 0/0 — it is the one archetype
        // whose whole job is being exactly as tight as the game can make it.
        if (next == SegmentKind.Throat) { _targetL = _targetR = 0; }
        else
        {
            _targetL = Math.Clamp(baseHalf + _rng.Roll(-1, 1), 0, MaxHalf);
            _targetR = Math.Clamp(baseHalf + _rng.Roll(-1, 1), 0, MaxHalf);
        }
    }

    private SegmentKind PickKind(int depth)
    {
        // Depth 0..1, saturating around depth 120. Drives the tightening of the deeps.
        float d = Math.Clamp(depth / 120f, 0f, 1f);

        Span<int> w = stackalloc int[6];
        w[(int)SegmentKind.Throat] = 10 + (int)(30 * d);
        w[(int)SegmentKind.Corridor] = 30;
        w[(int)SegmentKind.Gallery] = 20 + (int)(10 * d);
        w[(int)SegmentKind.Hall] = 25 - (int)(15 * d);
        w[(int)SegmentKind.Fork] = 12;
        w[(int)SegmentKind.Cairn] = 15;

        // Never the same archetype twice running — repetition is what makes a generated run feel
        // generated. The one exception is Throat: back-to-back chokepoints are exactly the chained
        // ambush the design wants to threaten (you Push out of one ring straight into the next),
        // so it stays possible and grows more likely with depth.
        w[(int)_kind] = _kind == SegmentKind.Throat ? 6 + (int)(20 * d) : 0;

        // A Throat is the design's loaded gun and firing it constantly cheapens it, so enforce a
        // cooling-off period. It must NOT apply when the last segment was itself a Throat, or it
        // silently cancels the chaining rule directly above — which it did, and chained ambushes
        // never once fired in 4000 rows.
        if (_kind != SegmentKind.Throat && _sinceThroat < 2) w[(int)SegmentKind.Throat] = 0;

        // Openness has to precede a chokepoint often enough that the funnel is visible as a
        // change. A Throat opening directly off a Corridor is a much weaker read.
        if (_kind is SegmentKind.Hall or SegmentKind.Cairn) w[(int)SegmentKind.Throat] += 18;

        int total = 0;
        for (int i = 0; i < w.Length; i++) total += w[i];
        int roll = _rng.Roll(0, Math.Max(0, total - 1));
        for (int i = 0; i < w.Length; i++)
        {
            roll -= w[i];
            if (roll < 0) return (SegmentKind)i;
        }
        return SegmentKind.Corridor;
    }
}

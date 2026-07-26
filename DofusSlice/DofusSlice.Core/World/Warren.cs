using DofusSlice.Core.Combat;
using DofusSlice.Core.Grid;

namespace DofusSlice.Core.World;

/// <summary>The archetypes the grammar strings together. Each exists because it changes what an
/// ambush in it would mean — that is the bar for adding another.</summary>
public enum SegmentKind
{
    /// <summary>Two cells across. The chokepoint: nowhere to spread, nowhere to dodge.</summary>
    Throat,
    /// <summary>Narrow but walkable abreast. The connective tissue.</summary>
    Corridor,
    /// <summary>Medium and pillared — cover exists, so line of sight is a puzzle.</summary>
    Gallery,
    /// <summary>Wide and open. Room to kite, room to be surrounded.</summary>
    Hall,
    /// <summary>Splits around a spine you can see and shoot across but not walk.</summary>
    Fork,
    /// <summary>A rubble-choked chamber, and bone spikes: walkable damage.</summary>
    Cairn,
    /// <summary>Flooded. Wide, but half is standing water — a shooting gallery.</summary>
    Cistern,
}

/// <summary>
/// The floor generator for the WANDERER descent (see docs/WANDERER.md).
///
/// The path is a WALK THAT TURNS across a 2D grid, so a floor is a wandering warren rather than a
/// strip. An earlier version hardcoded depth to the Y axis and carved a fixed-width band down it,
/// which meant the corridor could only ever run one direction — a ribbon, not a dungeon. "Depth" is
/// now distance ALONG THE PATH, which is what light, telegraphs and fog actually care about.
///
/// Two layers:
///   MACRO — a weighted Markov chain over <see cref="SegmentKind"/> plus a turn at each segment
///           boundary. The design lives here, because an organic generator cannot PROMISE a
///           chokepoint, and if one cannot be promised the Brace/Push/Charge tension fires at random.
///   MICRO — width eased toward each segment's target, measured across the heading, so a Throat
///           after a Hall reads as a funnel closing a few steps out.
///
/// PASSABILITY IS STRUCTURAL. The walk advances one orthogonal cell per step, so the centre line is
/// connected by construction; the centre cell is forced walkable after decoration. That is a far
/// stronger guarantee than the band-overlap bookkeeping it replaces, which leaked 24 impassable
/// floors in 400.
/// </summary>
public sealed class Warren
{
    public const int GridW = 56;
    public const int GridH = 56;

    /// <summary>Widest either side of the centre may carve.</summary>
    private const int MaxHalf = 5;

    /// <summary>No corridor is narrower than this ACROSS the heading. Single-file ground read as a
    /// pipe rather than a dungeon, and a one-cell corridor cannot tolerate the centre drifting.</summary>
    private const int MinWidth = 2;

    /// <summary>Ceiling on the share of steps that may be tight (constriction 60+, two across), as a
    /// budget against the finished plan rather than something left to emerge from the weights —
    /// weights drift with every other tuning change, and this is the number the whole
    /// Brace/Push/Charge tension is priced against.
    ///
    /// It RAMPS with depth, because a flat cap silently cancelled the per-floor ramp: the weights
    /// produced more chokepoints deeper down and the budget deleted exactly the surplus. A cap and a
    /// curve only conflict if the cap is not itself the curve.</summary>
    private static float TightBudget(int floorNumber) =>
        0.11f + 0.09f * Math.Clamp((floorNumber - 1) / 7f, 0f, 1f);

    private readonly IRng _rng;

    public Warren(IRng rng) => _rng = rng;
    public Warren(int seed) : this(new SystemRng(seed)) { }

    private sealed class Plan
    {
        public SegmentKind Kind;
        public int Steps;
        public RoomKind Room;
        /// <summary>Which decoration pattern this segment wears, rolled once per segment. The old
        /// decoration keyed on the CELL's coordinates (cx+cy), which advances every step — so every
        /// gallery striped diagonally and every cistern's bank zigzagged, one pattern everywhere,
        /// dressed up as variety.</summary>
        public int Pattern;

        /// <summary>Rubble vein state for Cairns: where the current vein sits across the band and
        /// how many more steps it runs. Per-cell rolls put rubble down as salt-and-pepper singles;
        /// a vein that wanders a few steps reads as a formation instead of static.</summary>
        public int VeinT;
        public int VeinLeft;
    }

    private static (int dx, int dy) Delta(Heading h) => h switch
    {
        Heading.North => (0, -1),
        Heading.East => (1, 0),
        Heading.South => (0, 1),
        _ => (-1, 0),
    };

    /// <summary>The axis across the heading — where width is measured.</summary>
    private static (int dx, int dy) Across(Heading h) => h switch
    {
        Heading.North or Heading.South => (1, 0),
        _ => (0, 1),
    };

    private static Heading Left(Heading h) => (Heading)(((int)h + 3) % 4);
    private static Heading Right(Heading h) => (Heading)(((int)h + 1) % 4);

    /// <summary>Generate a complete floor. <paramref name="steps"/> is a target path length.</summary>
    public Floor Generate(int steps = 200, int floorNumber = 1)
    {
        var plan = BuildPlan(steps, floorNumber);
        var tiles = new TileKind[GridW, GridH];
        for (int y = 0; y < GridH; y++)
            for (int x = 0; x < GridW; x++)
                tiles[x, y] = TileKind.Void;

        var rooms = new List<RoomRect>();
        var spurCells = new HashSet<(int, int)>();
        var path = Walk(tiles, plan, rooms);
        AddSpurRooms(tiles, path, rooms, spurCells);
        IsolateRooms(tiles, path, rooms, spurCells);

        // FINAL PASS: force the whole centre line walkable, after every segment has been carved.
        //
        // Doing it per-step is not enough now that the path can TURN, because a turning path can
        // cross its own earlier ground — and a later segment's pillars, water or chasm will happily
        // overwrite an earlier step's centre. That is what took 398 of 400 floors impassable: the
        // walk was contiguous and every centre was forced walkable at the time it was walked, and
        // then later carving quietly severed it behind. The guarantee has to be applied once the
        // floor is finished, not while it is still being written.
        foreach (var st in path)
            if (tiles[st.X, st.Y] is TileKind.Void or TileKind.Rock or TileKind.Water)
                tiles[st.X, st.Y] = TileKind.Dirt;

        TrimNubs(tiles, path, rooms, spurCells);
        CullIslands(tiles, path[0]);
        EnforceWidthAndMeasure(tiles, path, rooms);
        WearTrail(tiles, path);

        return new Floor(GridW, GridH, path, tiles, floorNumber, rooms);
    }

    /// <summary>
    /// Shave the pimples: cells hanging off the mass by at most one orthogonal neighbour.
    /// Bank jitter and band overlaps leave single-cell nubs and one-wide tails along region
    /// edges, and they read as accidents rather than erosion. Two passes so a two-cell tail
    /// goes too. Path, spur and room cells are never touched, and the pass runs BEFORE the
    /// width floor, so anything it narrows is re-carved by the same authority that owns width.
    /// </summary>
    private static void TrimNubs(TileKind[,] tiles, List<PathStep> path, List<RoomRect> rooms,
                                 HashSet<(int, int)> spurCells)
    {
        var keep = new HashSet<(int, int)>(spurCells);
        foreach (var st in path) keep.Add((st.X, st.Y));

        for (int pass = 0; pass < 2; pass++)
        {
            var cut = new List<(int, int)>();
            for (int y = 1; y < GridH - 1; y++)
                for (int x = 1; x < GridW - 1; x++)
                {
                    if (tiles[x, y] == TileKind.Void || keep.Contains((x, y))) continue;
                    if (rooms.Any(r => r.Contains(x, y))) continue;
                    int n = 0;
                    if (tiles[x - 1, y] != TileKind.Void) n++;
                    if (tiles[x + 1, y] != TileKind.Void) n++;
                    if (tiles[x, y - 1] != TileKind.Void) n++;
                    if (tiles[x, y + 1] != TileKind.Void) n++;
                    if (n <= 1) cut.Add((x, y));
                }
            foreach (var (x, y) in cut) tiles[x, y] = TileKind.Void;
            if (cut.Count == 0) break;
        }
    }

    /// <summary>The trodden line: worn ground scattered along the walked centre, broken like a
    /// real trail rather than painted solid. Replaces the halls' diagonal stripes — the wear
    /// belongs to the route, and as a side effect it reads as a subtle guide through the floor.</summary>
    private void WearTrail(TileKind[,] tiles, List<PathStep> path)
    {
        foreach (var st in path)
            if (st.Room == RoomKind.None && tiles[st.X, st.Y] == TileKind.Dirt &&
                _rng.Roll(0, 99) < 35)
                tiles[st.X, st.Y] = TileKind.Path;
    }

    // ---- Macro: the plan --------------------------------------------------------------

    private List<Plan> BuildPlan(int target, int floorNumber)
    {
        var plan = new List<Plan>();
        var kind = SegmentKind.Corridor;
        int steps = 0, sinceThroat = 99;

        const int WardenSteps = 9;
        while (steps < target - WardenSteps)
        {
            float d = Math.Clamp((floorNumber - 1) / 8f + steps / (float)target * 0.3f, 0f, 1f);
            kind = PickKind(kind, sinceThroat, d);
            sinceThroat = kind == SegmentKind.Throat ? 0 : sinceThroat + 1;
            int n = SegmentSteps(kind);
            plan.Add(new Plan { Kind = kind, Steps = n });
            steps += n;
        }

        plan.Add(new Plan { Kind = SegmentKind.Hall, Steps = WardenSteps, Room = RoomKind.Warden });
        CapTightGround(plan, floorNumber);
        return plan;
    }

    /// <summary>Demote Throat segments until inside <see cref="TightBudget"/>. Demoting rather than
    /// shortening keeps survivors at full length: a budget spent on many stubby chokepoints buys
    /// tension nowhere, one spent on a few real ones buys it somewhere.</summary>
    private void CapTightGround(List<Plan> plan, int floorNumber)
    {
        int total = plan.Sum(p => p.Steps);
        int allowed = (int)(total * TightBudget(floorNumber));

        var throats = new List<int>();
        for (int i = 0; i < plan.Count; i++)
            if (plan[i].Kind == SegmentKind.Throat) throats.Add(i);

        int tight = throats.Sum(i => plan[i].Steps);
        if (tight <= allowed) return;

        for (int i = throats.Count - 1; i > 0; i--)
        {
            int j = _rng.Roll(0, i);
            (throats[i], throats[j]) = (throats[j], throats[i]);
        }
        foreach (var idx in throats)
        {
            if (tight <= allowed) break;
            tight -= plan[idx].Steps;
            plan[idx].Kind = SegmentKind.Corridor;
        }
    }

    private int SegmentSteps(SegmentKind k) => k switch
    {
        // Throats run long enough to have a core: the funnel consumes the first steps, so a short
        // Throat is all approach and no chokepoint.
        SegmentKind.Throat => _rng.Roll(5, 9),
        SegmentKind.Corridor => _rng.Roll(6, 12),
        SegmentKind.Gallery => _rng.Roll(8, 13),
        SegmentKind.Hall => _rng.Roll(6, 9),
        SegmentKind.Fork => _rng.Roll(5, 8),
        SegmentKind.Cistern => _rng.Roll(7, 10),
        _ => _rng.Roll(5, 8),
    };

    private SegmentKind PickKind(SegmentKind prev, int sinceThroat, float d)
    {
        Span<int> w = stackalloc int[7];
        w[(int)SegmentKind.Throat] = 10 + (int)(30 * d);
        w[(int)SegmentKind.Corridor] = 30;
        w[(int)SegmentKind.Gallery] = 20 + (int)(10 * d);
        w[(int)SegmentKind.Hall] = 25 - (int)(15 * d);
        w[(int)SegmentKind.Fork] = 12;
        w[(int)SegmentKind.Cairn] = 15;
        w[(int)SegmentKind.Cistern] = 10 + (int)(8 * d);

        // Never the same archetype twice running — repetition is what makes a generated run feel
        // generated. The exception is Throat: back-to-back chokepoints are the chained ambush the
        // design wants to threaten, so it stays possible and grows likelier with depth.
        w[(int)prev] = prev == SegmentKind.Throat ? 6 + (int)(20 * d) : 0;

        // Cooling-off, which must NOT apply when the last segment was itself a Throat, or it
        // silently cancels the chaining rule above — it did, and chained ambushes never fired.
        if (prev != SegmentKind.Throat && sinceThroat < 2) w[(int)SegmentKind.Throat] = 0;

        if (prev is SegmentKind.Hall or SegmentKind.Cairn) w[(int)SegmentKind.Throat] += 18;

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

    // ---- Micro: the walk -------------------------------------------------------------

    /// <summary>
    /// Shrines and vaults hang off the main path on a dead-end SPUR, and are RARE.
    ///
    /// They used to be promoted segments, which meant the main walk was carved straight through
    /// them: every floor had one vault and two shrines, and you could not miss them because they
    /// were the corridor. A reward you cannot avoid is not a reward, it is a checkpoint. As a spur
    /// they cost a detour, which makes taking one a decision — and on a clock, a real one.
    ///
    /// The footprint plus a one-cell margin must be untouched rock before it is carved, so the
    /// chamber touches nothing except its own corridor. That is what makes it feel sealed rather
    /// than like a bulge in the warren.
    /// </summary>
    private void AddSpurRooms(TileKind[,] tiles, List<PathStep> path, List<RoomRect> rooms,
                              HashSet<(int, int)> spurCells)
    {
        // Rare, but not so rare the median run never sees one: the Shrine is the only source of
        // abilities in the design, and an identity-defining room that shows up on 6% of floors is
        // the Diablo 3 launch failure rather than scarcity.
        TrySpur(tiles, path, rooms, RoomKind.Shrine, 55, spurCells);
        TrySpur(tiles, path, rooms, RoomKind.Vault, 34, spurCells);
    }

    /// <summary>
    /// Strip the one-cell ring around every special room back to void, sparing only the path and
    /// the spur corridors. The seal is checked when a spur room is PLACED, but the Warden is carved
    /// mid-walk and later segments are free to wander up against its walls — so by the end of
    /// generation a "sealed" chamber could have corridor pressed along two sides. A room with
    /// neighbours is a bulge; a room in rock with one way in is a place.
    ///
    /// Only band EDGES are shaved, never path centres, so passability survives — and the verifier
    /// proves that over 400 floors rather than trusting this comment.
    /// </summary>
    private static void IsolateRooms(TileKind[,] tiles, List<PathStep> path,
                                     List<RoomRect> rooms, HashSet<(int, int)> spurCells)
    {
        var keep = new HashSet<(int, int)>(spurCells);
        foreach (var st in path) keep.Add((st.X, st.Y));

        foreach (var r in rooms)
            for (int y = r.Y - 1; y <= r.Y + r.H; y++)
                for (int x = r.X - 1; x <= r.X + r.W; x++)
                {
                    bool ring = x == r.X - 1 || x == r.X + r.W || y == r.Y - 1 || y == r.Y + r.H;
                    if (!ring || x < 1 || y < 1 || x >= GridW - 1 || y >= GridH - 1) continue;
                    if (keep.Contains((x, y))) continue;
                    tiles[x, y] = TileKind.Void;
                }
    }

    /// <summary>
    /// Return every scrap of material not connected to the entry to the void.
    ///
    /// Sealing a room in rock shaves a ring of ground away, and the shave can sever a lobe of an
    /// earlier segment entirely — a patch of floor adrift in the deeps that fog would reveal and
    /// nobody could ever stand on. Connectivity is 8-way THROUGH ANY MATERIAL, not just walkable
    /// ground: a pond's far bank or the inside of a rock heap is attached to the floor even though
    /// you cannot walk it, and culling those would eat the scenery. Only what floats free goes.
    /// </summary>
    private static void CullIslands(TileKind[,] tiles, PathStep entry)
    {
        var keep = new bool[GridW, GridH];
        var q = new Queue<(int, int)>();
        keep[entry.X, entry.Y] = true;
        q.Enqueue((entry.X, entry.Y));

        while (q.Count > 0)
        {
            var (cx, cy) = q.Dequeue();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= GridW || ny >= GridH) continue;
                    if (keep[nx, ny] || tiles[nx, ny] == TileKind.Void) continue;
                    keep[nx, ny] = true;
                    q.Enqueue((nx, ny));
                }
        }

        for (int y = 0; y < GridH; y++)
            for (int x = 0; x < GridW; x++)
                if (!keep[x, y]) tiles[x, y] = TileKind.Void;
    }

    /// <summary>
    /// The LAST word on width and tightness, spoken over the FINISHED floor.
    ///
    /// Constriction was measured at carve time and went stale: later segments, spur mouths,
    /// isolation shaves and the island cull all edit ground after a step is recorded, so the
    /// number "every other system reads" was wrong on a quarter of steps — and one step in ten
    /// ended up single-file despite the MinWidth invariant. Two duties here:
    ///
    ///   WIDTH FLOOR — a non-Throat step narrower than 3 across (or ANY step narrower than
    ///   MinWidth) gets ground carved back, so tight ground exists only where a Throat put it
    ///   and the plan-level TightBudget prices what the floor actually contains. Cells beside
    ///   special rooms are never touched: widening through a seal would breach the chamber.
    ///
    ///   RE-MEASURE — every step's Constriction is recomputed from the finished tiles as the
    ///   contiguous walkable run across the heading through the centre.
    /// </summary>
    private static void EnforceWidthAndMeasure(TileKind[,] tiles, List<PathStep> path,
                                               List<RoomRect> rooms)
    {
        bool NearRoom(int x, int y) =>
            rooms.Any(r => x >= r.X - 1 && x < r.X + r.W + 1 && y >= r.Y - 1 && y < r.Y + r.H + 1);
        bool Walk(int x, int y) =>
            x >= 1 && x < GridW - 1 && y >= 1 && y < GridH - 1 &&
            tiles[x, y] is not (TileKind.Void or TileKind.Rock or TileKind.Water);

        int Measure(PathStep st, (int, int) across)
        {
            var (ax, ay) = across;
            int walk = 1;
            for (int t = 1; t <= MaxHalf + 1 && Walk(st.X + ax * t, st.Y + ay * t); t++) walk++;
            for (int t = 1; t <= MaxHalf + 1 && Walk(st.X - ax * t, st.Y - ay * t); t++) walk++;
            return walk;
        }

        for (int i = 0; i < path.Count; i++)
        {
            var st = path[i];
            var across = Across(st.Dir);
            var (ax, ay) = across;

            int want = st.Kind == SegmentKind.Throat || st.Room != RoomKind.None ? MinWidth : 3;
            int walk = Measure(st, across);
            bool grew = true;
            while (walk < want && grew)
            {
                grew = false;
                foreach (int sign in new[] { 1, -1 })
                {
                    if (walk >= want) break;
                    // Extend the CONTIGUOUS run: carve exactly the first blocked cell past the
                    // walkable ground on this side. Skipping a guarded cell and carving beyond
                    // it plants ground with no connection to anything — an orphan island made
                    // by the very pass that runs after the island cull.
                    int t = 1;
                    while (t <= MaxHalf && Walk(st.X + ax * sign * t, st.Y + ay * sign * t)) t++;
                    int x = st.X + ax * sign * t, y = st.Y + ay * sign * t;
                    if (t > MaxHalf || x < 1 || y < 1 || x >= GridW - 1 || y >= GridH - 1 ||
                        NearRoom(x, y)) continue;
                    if (tiles[x, y] is TileKind.Void or TileKind.Rock or TileKind.Water)
                    {
                        tiles[x, y] = TileKind.Dirt;
                        grew = true;
                        walk = Measure(st, across);
                    }
                }
            }

            path[i] = new PathStep
            {
                X = st.X, Y = st.Y, Dir = st.Dir, Kind = st.Kind, Room = st.Room,
                Segment = st.Segment, Constriction = Constrict(Measure(st, across)),
            };
        }
    }

    private void TrySpur(TileKind[,] tiles, List<PathStep> path, List<RoomRect> rooms,
                         RoomKind kind, int percent, HashSet<(int, int)> spurCells)
    {
        if (_rng.Roll(0, 99) >= percent) return;
        int size = RoomSize(kind, _rng);

        // The sealed-margin test is strict by design, so a lazy search finds a home about one time
        // in five and the room becomes accidentally near-impossible rather than deliberately rare.
        // Search properly instead — both sides of the path, several spur lengths — and let the ROLL
        // above set the frequency, which is the only place it should be set.
        for (int attempt = 0; attempt < 30; attempt++)
        {
            var st = path[_rng.Roll(path.Count / 6, path.Count * 5 / 6)];
            foreach (var side in new[] { Left(st.Dir), Right(st.Dir) })
            {
                var (dx, dy) = Delta(side);

                // Cross the corridor's own band FIRST. The old scan demanded Void from k=1,
                // which fails whenever the corridor is wider than nothing — so placement died
                // about four times in five and the rarity ROLL above quietly stopped being
                // what sets shrine/vault frequency (measured: shrines at 46% of intent).
                int k0 = 1;
                while (k0 <= MaxHalf + 1)
                {
                    int bx = st.X + dx * k0, by = st.Y + dy * k0;
                    if (bx < 1 || by < 1 || bx >= GridW - 1 || by >= GridH - 1) { k0 = int.MaxValue; break; }
                    if (tiles[bx, by] == TileKind.Void) break;
                    k0++;
                }
                if (k0 > MaxHalf + 1) continue;

                for (int len = 3; len <= 7; len++)
                {
                    int reach = k0 - 1 + len;
                    int rcx = st.X + dx * (reach + size / 2), rcy = st.Y + dy * (reach + size / 2);
                    if (!AreaVoid(tiles, rcx - size / 2 - 1, rcy - size / 2 - 1, size + 2, size + 2))
                        continue;

                    bool clear = true;
                    for (int k = k0; k <= reach && clear; k++)
                    {
                        int cx = st.X + dx * k, cy = st.Y + dy * k;
                        if (cx < 1 || cy < 1 || cx >= GridW - 1 || cy >= GridH - 1 ||
                            tiles[cx, cy] != TileKind.Void) clear = false;
                    }
                    if (!clear) continue;

                    // Band cells join spurCells too, or IsolateRooms could shave the mouth.
                    for (int k = 1; k <= reach; k++)
                    {
                        tiles[st.X + dx * k, st.Y + dy * k] = TileKind.Dirt;
                        spurCells.Add((st.X + dx * k, st.Y + dy * k));
                    }
                    rooms.Add(CarveRoom(tiles, rcx, rcy, size, kind));
                    return;
                }
            }
        }
    }

    private static bool AreaVoid(TileKind[,] tiles, int x0, int y0, int w, int h)
    {
        if (x0 < 1 || y0 < 1 || x0 + w >= GridW - 1 || y0 + h >= GridH - 1) return false;
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
                if (tiles[x, y] != TileKind.Void) return false;
        return true;
    }

    /// <summary>Special rooms are SQUARE CHAMBERS — 4x4, 6x6 or 8x8 — carved as a block, not a
    /// widened stretch of corridor. A room the corridor merely swells into is not a room; it reads
    /// as a bulge, and a boss arena needs to be a place you arrive in.</summary>
    private static int RoomSize(RoomKind kind, IRng rng) => kind switch
    {
        RoomKind.Warden => 8,
        RoomKind.Vault => 6,
        _ => rng.Roll(0, 1) == 0 ? 4 : 6,
    };

    private static RoomRect CarveRoom(TileKind[,] tiles, int cx, int cy, int size, RoomKind kind)
    {
        int x0 = Math.Clamp(cx - size / 2, 1, GridW - size - 1);
        int y0 = Math.Clamp(cy - size / 2, 1, GridH - size - 1);
        for (int y = y0; y < y0 + size; y++)
            for (int x = x0; x < x0 + size; x++)
                tiles[x, y] = TileKind.Dirt;

        // CHAMFER: cut the corners back to void, one cell on small chambers, a three-cell
        // triangle on the Warden's. A perfect square reads as a stamp; an octagon reads as a
        // dug place. The RoomRect stays the full rectangle — seals, margins and the geometric
        // Warden check all reason about the footprint, not the silhouette.
        int ch = size >= 8 ? 2 : 1;
        for (int i = 0; i < ch; i++)
            for (int j = 0; j < ch - i; j++)
            {
                tiles[x0 + i, y0 + j] = TileKind.Void;
                tiles[x0 + size - 1 - i, y0 + j] = TileKind.Void;
                tiles[x0 + i, y0 + size - 1 - j] = TileKind.Void;
                tiles[x0 + size - 1 - i, y0 + size - 1 - j] = TileKind.Void;
            }

        // DRESSING, by what the room is for. The Warden arena gets four pillar stubs clear of
        // the centre lanes — cover to fight around, not an empty box. The path's final force-
        // walk pass runs after all carving, so a pillar can never sever the way through.
        if (kind == RoomKind.Warden && size >= 8)
        {
            tiles[x0 + 2, y0 + 2] = TileKind.Rock;
            tiles[x0 + size - 3, y0 + 2] = TileKind.Rock;
            tiles[x0 + 2, y0 + size - 3] = TileKind.Rock;
            tiles[x0 + size - 3, y0 + size - 3] = TileKind.Rock;
        }

        return new RoomRect(x0, y0, size, size, kind);
    }

    private List<PathStep> Walk(TileKind[,] tiles, List<Plan> plan, List<RoomRect> rooms)
    {
        var path = new List<PathStep>();
        int cx = GridW / 2, cy = GridH / 2;
        var dir = (Heading)_rng.Roll(0, 3);
        int halfL = 1, halfR = 1;
        int seg = -1;

        // Bank jitter: each side carries a lazy ±1 random walk on top of the eased half-width.
        // Width targets are the DESIGN (a hall is wide, a throat is tight); the jitter is the
        // EROSION — without it every segment carves a ruler-edged rectangle band.
        int jL = 0, jR = 0;
        // Staircase turns: when the heading changes, the first few steps alternate old and new
        // heading, so a corner is a diagonal shoulder rather than a drafted right angle.
        var oldDir = dir;
        int blend = 0;
        // How long the walk has held one heading, ACROSS segments. Straight is favoured per
        // roll, so two straight segments could chain into a 25-cell ruler run — the cap is here.
        int held = 0;
        var lastStep = dir;

        foreach (var p in plan)
        {
            seg++;
            p.Pattern = _rng.Roll(0, 2);
            if (seg > 0)
            {
                var nd = Turn(dir, cx, cy, p.Steps, held);
                // No stair into a special room: it is carved whole along the heading, and the
                // path must cross it straight or the exit lands outside the chamber.
                blend = nd != dir && p.Room == RoomKind.None ? _rng.Roll(3, 6) : 0;
                oldDir = dir;
                dir = nd;
            }

            // A special room is carved whole, centred far enough along the heading that the path
            // enters one side and crosses to the far interior column. Steps = size, NOT size + 2:
            // the extra two steps marched the walk straight out the far wall, which put the floor
            // EXIT outside the Warden's chamber on 19 floors in 20 — "the boss is always the way
            // out" was true in the plan and false on the ground.
            if (p.Room != RoomKind.None)
            {
                int size = RoomSize(p.Room, _rng);
                var (rdx, rdy) = Delta(dir);
                rooms.Add(CarveRoom(tiles, cx + rdx * (size / 2), cy + rdy * (size / 2), size, p.Room));
                p.Steps = size;
            }

            int baseHalf = TargetHalf(p);
            int targetL, targetR;
            if (p.Kind == SegmentKind.Throat && p.Room == RoomKind.None)
            {
                // Two across, not one: asymmetric halves so the pair is 0+1, with the wide side
                // rolled per segment so throats do not all hug the same wall.
                bool wideLeft = _rng.Roll(0, 1) == 0;
                targetL = wideLeft ? 1 : 0;
                targetR = wideLeft ? 0 : 1;
            }
            else
            {
                targetL = Math.Clamp(baseHalf + _rng.Roll(-1, 1), 0, MaxHalf);
                targetR = Math.Clamp(baseHalf + _rng.Roll(-1, 1), 0, MaxHalf);
            }

            for (int i = 0; i < p.Steps; i++)
            {
                halfL = Ease(halfL, targetL);
                halfR = Ease(halfR, targetR);

                // The stair: odd steps of the blend run on the OLD heading. Still one orthogonal
                // cell per step, so connectivity-by-construction holds through the shoulder.
                var stepDir = dir;
                if (blend > 0)
                {
                    if ((i & 1) == 1) stepDir = oldDir;
                    blend--;
                }

                // Steer off the rim rather than clipping into it, or a floor runs off the grid and
                // gets silently truncated against the boundary.
                if (!Room(cx, cy, stepDir, 3))
                {
                    var alt = _rng.Roll(0, 1) == 0 ? Left(stepDir) : Right(stepDir);
                    stepDir = Room(cx, cy, alt, 3) ? alt
                        : Room(cx, cy, Left(Left(alt)), 3) ? Left(Left(alt)) : alt;
                    dir = stepDir;
                    blend = 0;
                }

                // Ragged banks: lazy ±1 random walk per side. Not in a Throat (its exact width IS
                // the archetype) and not in a chamber (a room's wall is a wall, not a coastline).
                bool exact = p.Kind == SegmentKind.Throat || p.Room != RoomKind.None;
                if (!exact && _rng.Roll(0, 99) < 30) jL = Math.Clamp(jL + _rng.Roll(-1, 1), -1, 1);
                if (!exact && _rng.Roll(0, 99) < 30) jR = Math.Clamp(jR + _rng.Roll(-1, 1), -1, 1);
                int effL = exact ? halfL : Math.Max(0, halfL + jL);
                int effR = exact ? halfR : Math.Max(0, halfR + jR);

                CarveAcross(tiles, cx, cy, stepDir, effL, effR, p, i, out int walk);

                path.Add(new PathStep
                {
                    X = cx, Y = cy, Dir = stepDir, Kind = p.Kind, Room = p.Room,
                    Segment = seg, Constriction = Constrict(walk),
                });

                held = stepDir == lastStep ? held + 1 : 1;
                lastStep = stepDir;

                // Advance one orthogonal cell — this is what makes the centre line connected by
                // construction, turns included.
                var (dx, dy) = Delta(stepDir);
                cx = Math.Clamp(cx + dx, 1, GridW - 2);
                cy = Math.Clamp(cy + dy, 1, GridH - 2);
            }
        }
        return path;
    }

    /// <summary>Is there room to run <paramref name="reach"/> cells this way before the rim?</summary>
    private static bool Room(int cx, int cy, Heading h, int reach)
    {
        var (dx, dy) = Delta(h);
        int nx = cx + dx * reach, ny = cy + dy * reach;
        return nx is >= 6 and < GridW - 6 && ny is >= 6 and < GridH - 6;
    }

    /// <summary>Pick the next heading. Straight is favoured so the warren has runs rather than a
    /// constant zigzag; reversal is never offered, because doubling back over ground just walked
    /// makes a floor read as a scribble. A heading HELD too long stops being offered at all —
    /// straight-favouring rolls chained across segments into 25-cell ruler runs otherwise.</summary>
    private Heading Turn(Heading dir, int cx, int cy, int steps, int held)
    {
        int roll = _rng.Roll(0, 99);
        var next = held > 12 ? (_rng.Roll(0, 1) == 0 ? Left(dir) : Right(dir))
                 : roll < 46 ? dir : roll < 73 ? Left(dir) : Right(dir);
        int reach = Math.Min(steps, 8);
        if (!Room(cx, cy, next, reach))
        {
            var a = Left(dir); var b = Right(dir);
            next = Room(cx, cy, a, reach) ? a : Room(cx, cy, b, reach) ? b : Left(Left(dir));
        }
        return next;
    }

    private void CarveAcross(TileKind[,] tiles, int cx, int cy, Heading dir,
                             int halfL, int halfR, Plan p, int step, out int walkable)
    {
        var (ax, ay) = Across(dir);
        int lo = -halfL, hi = halfR;

        // The width floor, applied across the heading.
        while (hi - lo + 1 < MinWidth)
        {
            if (_rng.Roll(0, 1) == 0) lo--; else hi++;
        }

        for (int t = lo; t <= hi; t++)
        {
            int x = cx + ax * t, y = cy + ay * t;
            if (x < 1 || x >= GridW - 1 || y < 1 || y >= GridH - 1) continue;
            if (tiles[x, y] == TileKind.Void) tiles[x, y] = TileKind.Dirt;
        }

        Decorate(tiles, cx, cy, dir, lo, hi, p, step);

        // The centre is always walkable, after decoration — a pillar or a spine landing on it would
        // otherwise sever the only guaranteed route.
        tiles[cx, cy] = TileKind.Dirt;

        walkable = 0;
        for (int t = lo; t <= hi; t++)
        {
            int x = cx + ax * t, y = cy + ay * t;
            if (x >= 0 && x < GridW && y >= 0 && y < GridH &&
                tiles[x, y] is not (TileKind.Void or TileKind.Rock or TileKind.Water))
                walkable++;
        }
    }

    private static int TargetHalf(Plan p) => p.Room switch
    {
        // Special rooms are chambers: wide, so there is room to fight on your own terms.
        RoomKind.Warden => MaxHalf,
        RoomKind.Vault or RoomKind.Shrine => 4,
        _ => p.Kind switch
        {
            SegmentKind.Throat => 0,
            SegmentKind.Corridor => 1,
            SegmentKind.Gallery => 3,
            SegmentKind.Hall => 4,
            SegmentKind.Fork => 2,
            SegmentKind.Cistern => 4,
            _ => 3,
        },
    };

    /// <summary>Step at most 2 toward the target. A flat one-step ease took three steps to reach a
    /// Throat's width — longer than a short Throat lasts — so the funnel ate the chokepoint and the
    /// tightest ground never existed.</summary>
    private static int Ease(int cur, int target)
    {
        int d = target - cur;
        return d == 0 ? cur : cur + Math.Sign(d) * (Math.Abs(d) >= 2 ? 2 : 1);
    }

    /// <summary>Walkable count across the heading to a 0..100 tightness, non-linear on purpose: 2
    /// cells versus 4 is the difference between "cannot pass him" and "can"; 9 versus 11 is not.</summary>
    private static int Constrict(int walkable)
    {
        if (walkable <= 1) return 100;
        float t = Math.Clamp((walkable - 1) / 11f, 0f, 1f);
        return (int)MathF.Round(100f * (1f - MathF.Sqrt(t)));
    }

    private void Decorate(TileKind[,] tiles, int cx, int cy, Heading dir, int lo, int hi,
                          Plan p, int step)
    {
        if (p.Room != RoomKind.None) return;
        var (ax, ay) = Across(dir);

        void Put(int t, TileKind k)
        {
            int x = cx + ax * t, y = cy + ay * t;
            if (x > 0 && x < GridW - 1 && y > 0 && y < GridH - 1) tiles[x, y] = k;
        }

        switch (p.Kind)
        {
            case SegmentKind.Gallery:
                // Three colonnade patterns, chosen per segment. Keyed on STEP and OFFSET — never on
                // the cell's coordinates, which advance every step and turn any modulo into a
                // diagonal stripe regardless of what it was meant to be.
                switch (p.Pattern)
                {
                    case 0: // twin rows of pillars down the flanks
                        if (step % 3 == 1) { Put(lo + 1, TileKind.Rock); Put(hi - 1, TileKind.Rock); }
                        break;
                    case 1: // a staggered checker through the middle
                        for (int t = lo + 1; t < hi; t++)
                            if (t != 0 && (t - lo) % 2 == step % 2 && step % 2 == 0)
                                Put(t, TileKind.Rock);
                        break;
                    default: // broken colonnade: paired pillars with gaps where they collapsed
                        if (step % 4 == 1 && _rng.Roll(0, 100) < 70) Put(lo + 1, TileKind.Rock);
                        if (step % 4 == 3 && _rng.Roll(0, 100) < 70) Put(hi - 1, TileKind.Rock);
                        break;
                }
                break;

            case SegmentKind.Cairn:
                switch (p.Pattern)
                {
                    case 0: // a rubble VEIN that wanders, plus sparse bone. Per-cell rolls put
                            // rubble down as confetti — one cell here, one there, no mass. A vein
                            // holds a position across steps and drifts, so the rubble reads as a
                            // collapsed seam running through the chamber.
                        if (p.VeinLeft > 0)
                        {
                            p.VeinT = Math.Clamp(p.VeinT + _rng.Roll(-1, 1), lo + 1, hi - 1);
                            if (p.VeinT != 0) Put(p.VeinT, TileKind.Rock);
                            if (_rng.Roll(0, 99) < 40 && p.VeinT + 1 != 0 && p.VeinT + 1 < hi)
                                Put(p.VeinT + 1, TileKind.Rock);
                            p.VeinLeft--;
                        }
                        else if (_rng.Roll(0, 99) < 30 && hi - lo >= 2)
                        {
                            p.VeinT = _rng.Roll(lo + 1, hi - 1);
                            p.VeinLeft = _rng.Roll(2, 5);
                        }
                        for (int t = lo; t <= hi; t++)
                            if (t != 0 && _rng.Roll(0, 100) < 6) Put(t, TileKind.Spikes);
                        break;
                    case 1: // a collapsed heap against one wall, thinning toward the middle
                        for (int t = lo; t <= hi; t++)
                        {
                            if (t == 0) continue;
                            int fromWall = Math.Min(t - lo, hi - t);
                            if (_rng.Roll(0, 100) < 34 - fromWall * 12) Put(t, TileKind.Rock);
                        }
                        break;
                    default: // a spike field with rubble islands
                        for (int t = lo; t <= hi; t++)
                        {
                            if (t == 0) continue;
                            if (_rng.Roll(0, 100) < 16) Put(t, TileKind.Spikes);
                            else if (step % 3 == 0 && _rng.Roll(0, 100) < 10) Put(t, TileKind.Rock);
                        }
                        break;
                }
                break;

            case SegmentKind.Cistern:
            {
                // A POOL, not a stripe. The water is an ellipse centred midway along the segment:
                // (across/ra)^2 + (along/rl)^2 <= 1, so the bank curves — it swells to full width
                // at the middle and pinches out at both ends. The old bank was "everything past
                // half-width on one side", a straight line the segment carried for its whole run.
                float mid = (p.Steps - 1) / 2f;
                float rl = Math.Max(2f, p.Steps / 2f - 0.5f);
                // The pool hugs one side (rolled per segment) so there is always a dry bank.
                bool nearSide = p.Pattern % 2 == 0;
                float centreT = nearSide ? lo + (hi - lo) * 0.3f : lo + (hi - lo) * 0.7f;
                float ra = Math.Max(1.5f, (hi - lo) * 0.42f);
                for (int t = lo; t <= hi; t++)
                {
                    if (t == 0) continue;
                    float u = (t - centreT) / ra, v = (step - mid) / rl;
                    if (u * u + v * v <= 1f) Put(t, TileKind.Water);
                }
                break;
            }

            case SegmentKind.Fork:
                // A spine you can SEE AND SHOOT ACROSS but not walk — WATER, not rock: rock
                // blocks line of sight, which contradicted the archetype's entire identity.
                // One cell off the centre line, strictly interior, so both branches walk.
                if (hi - lo >= 2)
                {
                    int s = p.Pattern % 2 == 0 ? -1 : 1;
                    if (s <= lo) s = lo + 1;
                    if (s >= hi) s = hi - 1;
                    if (s != 0) Put(s, TileKind.Water);
                }
                break;

            case SegmentKind.Hall:
                // (The old worn-tile stripes are gone: keyed on step they striped every hall
                // diagonally. Worn ground now traces the walked centre line — see WearTrail.)

                // CHASM, strictly inside with floor on both sides. Placed at the edge it is not a
                // chasm at all — indistinguishable from the wall being one cell nearer, with nothing
                // to be shoved across. That version fired on 22% of wide halls and made zero holes.
                if (hi - lo >= 6 && _rng.Roll(0, 100) < 26)
                {
                    int g = _rng.Roll(lo + 2, hi - 2);
                    if (g != 0) Put(g, TileKind.Void);
                }
                break;
        }
    }
}

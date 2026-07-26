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
        /// <summary>Does this segment raise a parapet along its flanks? Rolled per segment so
        /// walls come and go along the descent — everywhere reads as a fortress, nowhere reads
        /// as a cave, and neither is a warren.</summary>
        public bool Walled;
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
        WallRooms(tiles, rooms);

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

        return new Floor(GridW, GridH, path, tiles, floorNumber, rooms);
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
                for (int len = 3; len <= 7; len++)
                {
                    int rcx = st.X + dx * (len + size / 2), rcy = st.Y + dy * (len + size / 2);
                    if (!AreaVoid(tiles, rcx - size / 2 - 1, rcy - size / 2 - 1, size + 2, size + 2))
                        continue;

                    bool clear = true;
                    for (int k = 1; k <= len && clear; k++)
                    {
                        int cx = st.X + dx * k, cy = st.Y + dy * k;
                        if (cx < 1 || cy < 1 || cx >= GridW - 1 || cy >= GridH - 1 ||
                            tiles[cx, cy] != TileKind.Void) clear = false;
                    }
                    if (!clear) continue;

                    for (int k = 1; k <= len; k++)
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

    /// <summary>Raise a parapet on the void ring around a chamber — after isolation, so the
    /// shave cannot delete it. The Warden and every Vault are always walled (an arena and a
    /// treasury read as built places); a Shrine only sometimes, so some stand open to the dark.
    /// Only void becomes rock: the doorway is Dirt and survives untouched.</summary>
    private void WallRooms(TileKind[,] tiles, List<RoomRect> rooms)
    {
        foreach (var r in rooms)
        {
            if (r.Kind == RoomKind.Shrine && _rng.Roll(0, 99) < 40) continue;
            for (int y = r.Y - 1; y <= r.Y + r.H; y++)
                for (int x = r.X - 1; x <= r.X + r.W; x++)
                {
                    bool ring = x == r.X - 1 || x == r.X + r.W || y == r.Y - 1 || y == r.Y + r.H;
                    if (!ring || x < 1 || y < 1 || x >= GridW - 1 || y >= GridH - 1) continue;
                    if (tiles[x, y] == TileKind.Void) tiles[x, y] = TileKind.Rock;
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
        return new RoomRect(x0, y0, size, size, kind);
    }

    private List<PathStep> Walk(TileKind[,] tiles, List<Plan> plan, List<RoomRect> rooms)
    {
        var path = new List<PathStep>();
        int cx = GridW / 2, cy = GridH / 2;
        var dir = (Heading)_rng.Roll(0, 3);
        int halfL = 1, halfR = 1;
        int seg = -1;

        foreach (var p in plan)
        {
            seg++;
            p.Pattern = _rng.Roll(0, 2);
            // Wide, roomy archetypes carry walls most often — a parapet on a Throat would just
            // deepen a slot that is already the tightest thing in the game, so never there.
            p.Walled = p.Kind switch
            {
                SegmentKind.Hall => _rng.Roll(0, 99) < 55,
                SegmentKind.Cistern => _rng.Roll(0, 99) < 45,
                SegmentKind.Gallery => _rng.Roll(0, 99) < 30,
                SegmentKind.Cairn => _rng.Roll(0, 99) < 20,
                SegmentKind.Corridor => _rng.Roll(0, 99) < 12,
                _ => false,
            };
            if (seg > 0) dir = Turn(dir, cx, cy, p.Steps);

            // A special room is carved whole, centred far enough along the heading that the path
            // enters one side and leaves the other rather than clipping a corner.
            if (p.Room != RoomKind.None)
            {
                int size = RoomSize(p.Room, _rng);
                var (rdx, rdy) = Delta(dir);
                rooms.Add(CarveRoom(tiles, cx + rdx * (size / 2), cy + rdy * (size / 2), size, p.Room));
                p.Steps = size + 2;
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

                // Steer off the rim rather than clipping into it, or a floor runs off the grid and
                // gets silently truncated against the boundary.
                if (!Room(cx, cy, dir, 3))
                {
                    var alt = _rng.Roll(0, 1) == 0 ? Left(dir) : Right(dir);
                    dir = Room(cx, cy, alt, 3) ? alt
                        : Room(cx, cy, Left(Left(alt)), 3) ? Left(Left(alt)) : alt;
                }

                CarveAcross(tiles, cx, cy, dir, halfL, halfR, p, i, out int walk);

                path.Add(new PathStep
                {
                    X = cx, Y = cy, Dir = dir, Kind = p.Kind, Room = p.Room,
                    Segment = seg, Constriction = Constrict(walk),
                });

                // Advance one orthogonal cell — this is what makes the centre line connected by
                // construction, turns included.
                var (dx, dy) = Delta(dir);
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
    /// makes a floor read as a scribble.</summary>
    private Heading Turn(Heading dir, int cx, int cy, int steps)
    {
        int roll = _rng.Roll(0, 99);
        var next = roll < 46 ? dir : roll < 73 ? Left(dir) : Right(dir);
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

        // The parapet: one cell of rock just outside each flank, raised only where the band
        // meets untouched void — an existing corridor is never bricked over, so walls cannot
        // change what is reachable, only what is visible and what light can cross.
        if (p.Walled)
        {
            foreach (var t in new[] { lo - 1, hi + 1 })
            {
                int x = cx + ax * t, y = cy + ay * t;
                if (x > 0 && x < GridW - 1 && y > 0 && y < GridH - 1 &&
                    tiles[x, y] == TileKind.Void)
                    tiles[x, y] = TileKind.Rock;
            }
        }

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
                    case 0: // scattered rubble and bone
                        for (int t = lo; t <= hi; t++)
                        {
                            if (t == 0) continue;
                            int roll = _rng.Roll(0, 100);
                            if (roll < 13) Put(t, TileKind.Rock);
                            else if (roll < 20) Put(t, TileKind.Spikes);
                        }
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
                // A spine you can see and shoot across but not walk, offset off the centre line.
                if (hi - lo >= 2) Put(lo + 1 == 0 ? lo + 2 : lo + 1, TileKind.Rock);
                break;

            case SegmentKind.Hall:
                for (int t = lo; t <= hi; t++)
                    if (t != 0 && (t - lo + step) % 5 == 0) Put(t, TileKind.Path);

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

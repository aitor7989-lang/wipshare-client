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
    /// <summary>Flooded. Wide, but half of it is standing water you can see and shoot across and
    /// cannot walk — a shooting gallery where the geometry favours whoever has range.</summary>
    Cistern,
}

/// <summary>
/// The floor generator for the WANDERER descent (see docs/WANDERER.md).
///
/// Generates a WHOLE FLOOR up front. An earlier version was a lazy row stream, to make "the path
/// raises from the deeps" literal — but a generator that only knows the last row cannot put a boss
/// at the end, cannot space shrines apart, and cannot prove the floor is walkable, because all
/// three are statements about the floor as a whole. Fog of war does that job better anyway: the
/// floor exists, and what the player has seen is a mask over it (<see cref="Fog"/>).
///
/// Two layers:
///   MACRO — a weighted Markov chain over <see cref="SegmentKind"/>, conditioned on the previous
///           kind and on depth, with special rooms placed against the finished plan. The design
///           lives here, because a purely organic generator cannot PROMISE a chokepoint, and if a
///           chokepoint cannot be promised then the Brace/Push/Charge tension fires at random.
///   MICRO — a meandering centreline and a width that eases toward each segment's target, so a
///           Throat after a Hall reads as a funnel you can see closing a few rows out.
///
/// PASSABILITY IS GUARANTEED BY CONSTRUCTION, not by generate-and-retry: every row shares a link
/// column with the row above, and that column is forced walkable after decoration. See
/// <see cref="Floor.IsPassable"/> for why the obvious check is not sufficient.
/// </summary>
public sealed class Warren
{
    /// <summary>Cells across. Matches the combat board's width so a segment can host a fight
    /// without any coordinate translation.</summary>
    public const int Width = 13;

    /// <summary>Widest either side may carve — capped so the band plus a void margin always fits.
    /// Uncapped, two sides that both jitter wide make the carve wider than the corridor and the
    /// centreline's own clamp inverts.</summary>
    private const int MaxHalf = (Width - 3) / 2;

    /// <summary>No row is ever narrower than this. Single-file ground made the descent feel like a
    /// pipe, and it is where every passability failure came from — two cells still cannot be walked
    /// abreast comfortably, so a Throat is still a chokepoint, just not a straw.</summary>
    private const int MinWidth = 2;

    /// <summary>Ceiling on the share of rows that may be tight (constriction 60+, i.e. two cells
    /// wide), as a BUDGET against the finished plan rather than something left to emerge from the
    /// weights — weights drift with every other tuning change, and this is the number the whole
    /// Brace/Push/Charge tension is priced against, so it should not move by accident.
    ///
    /// It RAMPS with depth. A flat cap silently cancelled the per-floor difficulty ramp: the
    /// weights dutifully produced more chokepoints deeper down and the budget deleted exactly the
    /// surplus, leaving floor 10 (17.3% tight) indistinguishable from floor 1 (16.8%). A cap and a
    /// curve are not in conflict as long as the cap is the curve.</summary>
    private static float TightBudget(int floorNumber) =>
        0.11f + 0.09f * Math.Clamp((floorNumber - 1) / 7f, 0f, 1f);

    private readonly IRng _rng;

    public Warren(IRng rng) => _rng = rng;
    public Warren(int seed) : this(new SystemRng(seed)) { }

    private sealed class Plan
    {
        public SegmentKind Kind;
        public int Rows;
        public RoomKind Room;
    }

    /// <summary>Generate a complete floor. <paramref name="length"/> is a target; the floor is a
    /// whole number of segments plus the Warden's chamber, so it lands slightly over.</summary>
    public Floor Generate(int length = 200, int floorNumber = 1)
    {
        var plan = BuildPlan(length, floorNumber);
        var floor = new Floor(Width, plan.Sum(p => p.Rows), Width / 2, floorNumber);
        Carve(floor, plan);
        return floor;
    }

    // ---- Macro: the segment plan ------------------------------------------------------

    private List<Plan> BuildPlan(int length, int floorNumber)
    {
        var plan = new List<Plan>();
        var kind = SegmentKind.Corridor;
        int rows = 0, sinceThroat = 99;

        const int WardenRows = 11;
        while (rows < length - WardenRows)
        {
            // The ramp is driven mainly by WHICH FLOOR this is, not by depth within it. Keyed to
            // row depth alone it barely moved across a 200-row floor (23.7% tight to 24.6%) because
            // it was written for one endless descent; with floors, "deeper" means further down the
            // dungeon, and a floor should have its own mild internal build instead.
            float d = Math.Clamp((floorNumber - 1) / 8f + rows / (float)length * 0.3f, 0f, 1f);
            kind = PickKind(kind, sinceThroat, d);
            sinceThroat = kind == SegmentKind.Throat ? 0 : sinceThroat + 1;
            int n = SegmentRows(kind);
            plan.Add(new Plan { Kind = kind, Rows = n });
            rows += n;
        }

        // The Warden is always the last chamber and always the way out. This is the whole reason
        // the generator produces a floor instead of a stream.
        plan.Add(new Plan { Kind = SegmentKind.Hall, Rows = WardenRows, Room = RoomKind.Warden });

        CapTightGround(plan, floorNumber);
        PlaceRooms(plan);
        return plan;
    }

    /// <summary>Demote Throat segments until they are inside <see cref="TightBudget"/>. Demoting
    /// (rather than shortening) keeps every Throat that survives at full length: a budget spent on
    /// many stubby chokepoints buys tension nowhere, and one spent on a few real ones buys it
    /// somewhere.</summary>
    private void CapTightGround(List<Plan> plan, int floorNumber)
    {
        int total = plan.Sum(p => p.Rows);
        int allowed = (int)(total * TightBudget(floorNumber));

        var throats = new List<int>();
        for (int i = 0; i < plan.Count; i++)
            if (plan[i].Kind == SegmentKind.Throat) throats.Add(i);

        int tight = throats.Sum(i => plan[i].Rows);
        if (tight <= allowed) return;

        // Shuffle so the survivors are spread down the floor instead of all being the early ones.
        for (int i = throats.Count - 1; i > 0; i--)
        {
            int j = _rng.Roll(0, i);
            (throats[i], throats[j]) = (throats[j], throats[i]);
        }

        foreach (var idx in throats)
        {
            if (tight <= allowed) break;
            tight -= plan[idx].Rows;
            plan[idx].Kind = SegmentKind.Corridor;
        }
    }

    /// <summary>Promote wide interior segments to special rooms, spaced apart. Wide on purpose: a
    /// Shrine whose harvest condition needs manoeuvring room, or a Vault you cannot move in, turns
    /// a designed encounter into a terrain accident.</summary>
    private void PlaceRooms(List<Plan> plan)
    {
        var eligible = new List<int>();
        for (int i = 2; i < plan.Count - 2; i++)
            if (plan[i].Room == RoomKind.None &&
                plan[i].Kind is SegmentKind.Cairn or SegmentKind.Hall or SegmentKind.Gallery)
                eligible.Add(i);

        for (int i = eligible.Count - 1; i > 0; i--)
        {
            int j = _rng.Roll(0, i);
            (eligible[i], eligible[j]) = (eligible[j], eligible[i]);
        }

        // Spacing rule: two special rooms never abut, because back-to-back rewards read as one
        // lucky room rather than two decisions.
        var taken = new List<int>();
        foreach (var idx in eligible)
        {
            if (taken.Count >= 3) break;
            if (taken.Any(t => Math.Abs(t - idx) < 3)) continue;
            taken.Add(idx);
        }

        for (int i = 0; i < taken.Count; i++)
            plan[taken[i]].Room = i == 0 ? RoomKind.Vault : RoomKind.Shrine;
    }

    private int SegmentRows(SegmentKind k) => k switch
    {
        // Throats run 5-9 rows: the funnel consumes the first rows, so a short Throat would be all
        // approach and no chokepoint.
        SegmentKind.Throat => _rng.Roll(5, 9),
        SegmentKind.Corridor => _rng.Roll(6, 12),
        SegmentKind.Gallery => _rng.Roll(8, 14),
        SegmentKind.Hall => _rng.Roll(6, 10),
        SegmentKind.Fork => _rng.Roll(5, 9),
        SegmentKind.Cistern => _rng.Roll(7, 11),
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
        // generated. The exception is Throat: back-to-back chokepoints are exactly the chained
        // ambush the design wants to threaten, so it stays possible and grows likelier with depth.
        w[(int)prev] = prev == SegmentKind.Throat ? 6 + (int)(20 * d) : 0;

        // Cooling-off, which must NOT apply when the last segment was itself a Throat, or it
        // silently cancels the chaining rule above — it did, and chained ambushes never fired.
        if (prev != SegmentKind.Throat && sinceThroat < 2) w[(int)SegmentKind.Throat] = 0;

        // Openness should precede a chokepoint often enough that the funnel reads as a change.
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

    // ---- Micro: carving --------------------------------------------------------------

    private void Carve(Floor floor, List<Plan> plan)
    {
        int centre = floor.EntryX, halfL = 1, halfR = 1;
        int prevLo = centre, prevHi = centre, prevLink = centre;
        int y = 0;

        int segIndex = -1;
        foreach (var seg in plan)
        {
            segIndex++;
            int baseHalf = TargetHalf(seg);
            int targetL, targetR;
            if (seg.Kind == SegmentKind.Throat && seg.Room == RoomKind.None)
            {
                // Two cells, not one: asymmetric halves so the pair is 0+1 rather than 0+0. Which
                // side is the wide one is rolled per segment, so throats do not all hug one wall.
                bool leftWide = _rng.Roll(0, 1) == 0;
                targetL = leftWide ? 1 : 0;
                targetR = leftWide ? 0 : 1;
            }
            else
            {
                targetL = Math.Clamp(baseHalf + _rng.Roll(-1, 1), 0, MaxHalf);
                targetR = Math.Clamp(baseHalf + _rng.Roll(-1, 1), 0, MaxHalf);
            }

            for (int i = 0; i < seg.Rows && y < floor.Length; i++, y++)
            {
                halfL = Math.Min(Ease(halfL, targetL), MaxHalf);
                halfR = Math.Min(Ease(halfR, targetR), MaxHalf);

                centre = Math.Clamp(centre + _rng.Roll(-1, 1), halfL + 1, Width - halfR - 2);

                int lo = Math.Max(0, centre - halfL);
                int hi = Math.Min(Width - 1, centre + halfR);

                // The width floor. Single-file ground read as a pipe rather than a dungeon, and it
                // was the source of every passability failure — a one-cell row cannot tolerate the
                // centreline drifting at all.
                while (hi - lo + 1 < MinWidth)
                {
                    if (lo > 0 && (hi >= Width - 1 || _rng.Roll(0, 1) == 0)) lo--;
                    else if (hi < Width - 1) hi++;
                    else break;
                }

                // PASSABILITY, guaranteed here rather than checked afterwards. If this row's band
                // does not overlap the row above, stretch it until it does. Without this, a width-1
                // Throat row whose centre drifted by one leaves two cells that are only DIAGONALLY
                // adjacent — and movement is orthogonal, so the floor is unwinnable while every row
                // still passes a naive "this row has a walkable cell" check.
                if (y > 0)
                {
                    if (hi < prevLo) hi = prevLo;
                    else if (lo > prevHi) lo = prevHi;
                }

                for (int x = 0; x < Width; x++) floor.Tiles[x, y] = TileKind.Void;
                for (int x = lo; x <= hi; x++) floor.Tiles[x, y] = TileKind.Dirt;

                if (seg.Kind == SegmentKind.Fork && seg.Room == RoomKind.None && hi - lo >= 2)
                {
                    // A rock spine, not void: you cannot walk it but you CAN see and shoot across
                    // it, so a fork is a tactical read rather than two unrelated tunnels.
                    int spine = Math.Clamp(centre, lo + 1, hi - 1);
                    floor.Tiles[spine, y] = TileKind.Rock;
                }

                floor.RowKind[y] = seg.Kind;
                floor.RowRoom[y] = seg.Room;
                floor.RowSegment[y] = segIndex;
                Decorate(floor, y, seg, lo, hi);

                // THE LINK SPINE. Pick a column inside the overlap with the row above, then force
                // it walkable after decoration — a Gallery pillar or a Fork spine landing on the
                // shared column would otherwise sever the floor.
                //
                // Forcing the two endpoint cells is NOT enough, which cost 24 impassable floors in
                // 400. When the link MOVES between rows, the old and new columns both sit in the
                // row above — but a Gallery's pillars split that row into separate runs, so two
                // walkable cells in one row can belong to different components. So force the whole
                // SPAN between them, and do it in row y-1, where the span is guaranteed to lie
                // inside that row's carved band rather than spurring out into the void.
                int link;
                if (y == 0)
                {
                    link = Math.Clamp(prevLink, lo, hi);
                }
                else
                {
                    int a = Math.Max(lo, prevLo), b = Math.Min(hi, prevHi);
                    link = Math.Clamp(prevLink, a, b);
                    for (int x = Math.Min(prevLink, link); x <= Math.Max(prevLink, link); x++)
                        floor.Tiles[x, y - 1] = TileKind.Dirt;
                }
                floor.Tiles[link, y] = TileKind.Dirt;

                int walk = 0;
                for (int x = 0; x < Width; x++) if (floor.Walkable(x, y)) walk++;
                floor.Constriction[y] = Constrict(walk);

                prevLo = lo; prevHi = hi; prevLink = link;
            }
        }

        floor.ExitX = prevLink;
    }

    private static int TargetHalf(Plan seg) => seg.Room switch
    {
        // Special rooms are chambers: wide, so there is room to fight on your own terms.
        RoomKind.Warden => MaxHalf,
        RoomKind.Vault or RoomKind.Shrine => 4,
        _ => seg.Kind switch
        {
            SegmentKind.Throat => 0,
            SegmentKind.Corridor => 1,
            SegmentKind.Gallery => 3,
            SegmentKind.Hall => 5,
            SegmentKind.Fork => 2,
            SegmentKind.Cistern => 5,
            _ => 4,
        },
    };

    /// <summary>Step at most 2 toward the target, so wide gaps close in two rows and narrow ones
    /// still ease. A flat one-step ease took three rows to reach a Throat's width — longer than a
    /// short Throat lasts — so the funnel ate the chokepoint and the tightest ground never existed.
    /// </summary>
    private static int Ease(int cur, int target)
    {
        int d = target - cur;
        if (d == 0) return cur;
        return cur + Math.Sign(d) * (Math.Abs(d) >= 2 ? 2 : 1);
    }

    /// <summary>Walkable count to a 0..100 tightness, non-linear on purpose: 1 cell versus 3 is the
    /// difference between "cannot pass him" and "can"; 9 versus 11 is nothing.</summary>
    private static int Constrict(int walkable)
    {
        if (walkable <= 1) return 100;
        float t = Math.Clamp((walkable - 1) / (float)(Width - 1), 0f, 1f);
        return (int)MathF.Round(100f * (1f - MathF.Sqrt(t)));
    }

    private void Decorate(Floor floor, int y, Plan seg, int lo, int hi)
    {
        // Special rooms stay clear: a boss arena full of rubble is a terrain accident, not an
        // encounter.
        if (seg.Room != RoomKind.None) return;

        // ALCOVES — a shallow dead-end pocket off one wall, in narrow ground only. Somewhere to be
        // that is not "forward", which is the one thing a corridor otherwise never offers: a cell
        // out of a firing line, or a place a pursuer has to commit to entering.
        if (seg.Kind is SegmentKind.Corridor or SegmentKind.Throat && _rng.Roll(0, 100) < 9)
        {
            if (lo > 1 && _rng.Roll(0, 1) == 0) floor.Tiles[lo - 1, y] = TileKind.Dirt;
            else if (hi < Width - 2) floor.Tiles[hi + 1, y] = TileKind.Dirt;
        }

        switch (seg.Kind)
        {
            case SegmentKind.Gallery:
                // Colonnades, phase-shifted by depth so pillars form rows down the run rather than
                // one long wall. Cover that repeats is cover you can plan around.
                for (int x = lo + 1; x < hi; x++)
                    if ((x + y / 2) % 4 == 0) floor.Tiles[x, y] = TileKind.Rock;
                break;

            case SegmentKind.Cairn:
                for (int x = lo; x <= hi; x++)
                {
                    int roll = _rng.Roll(0, 100);
                    // Bone spikes are WALKABLE damage, so unlike rubble they do not shrink the room
                    // — they price a route instead of closing it, which is a different decision.
                    if (roll < 14) floor.Tiles[x, y] = TileKind.Rock;
                    else if (roll < 21) floor.Tiles[x, y] = TileKind.Spikes;
                }
                break;

            case SegmentKind.Cistern:
                // Standing water down one side: see across it, shoot across it, never walk it. The
                // wide room stops being wide for the purposes of closing distance, which is what
                // makes it a gallery rather than just another Hall.
                bool leftBank = (y / 3) % 2 == 0;
                int edge = leftBank ? lo + (hi - lo) / 2 : hi - (hi - lo) / 2;
                for (int x = lo; x <= hi; x++)
                    if (leftBank ? x <= edge : x >= edge) floor.Tiles[x, y] = TileKind.Water;
                break;

            case SegmentKind.Hall:
                // A wide floor reads as one slab; the worn shade gives the eye something to judge
                // distance against, which matters when the game is judging whether a ring reaches.
                for (int x = lo; x <= hi; x++)
                    if ((x + y) % 5 == 0) floor.Tiles[x, y] = TileKind.Path;

                // CHASM. A hall wide enough to be safe is a hall with no opinion, and this
                // project already has void-death and a mob that shoves — so a hole here makes
                // pushback lethal in halls and nowhere else.
                //
                // It must sit STRICTLY INSIDE the band with floor on both sides. Voiding an edge
                // cell, which is what this did first, is not a chasm at all: it is indistinguishable
                // from the wall being one cell nearer, and there is nothing to be shoved across. It
                // fired on 22% of wide hall rows and produced exactly zero holes.
                if (hi - lo >= 6 && _rng.Roll(0, 100) < 26)
                {
                    int gx = _rng.Roll(lo + 2, hi - 2);
                    floor.Tiles[gx, y] = TileKind.Void;
                    if (hi - lo >= 8 && gx + 1 <= hi - 2) floor.Tiles[gx + 1, y] = TileKind.Void;
                }
                break;
        }
    }
}

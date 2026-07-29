namespace DofusSlice.Core.Grid;

public static class Pathfinding
{
    /// <summary>
    /// Dijkstra/BFS flood from <paramref name="start"/> outward, returning every cell
    /// reachable within <paramref name="maxSteps"/> movement points and the step cost to
    /// reach it. Movement is orthogonal, one MP per cell; non-walkable and occupied cells
    /// are impassable. The start cell is excluded from the result.
    /// </summary>
    public static Dictionary<CellCoord, int> ReachableCells(Battlefield field, CellCoord start,
        int maxSteps, Func<CellCoord, bool> occupied)
    {
        var dist = new Dictionary<CellCoord, int> { [start] = 0 };
        var frontier = new Queue<CellCoord>();
        frontier.Enqueue(start);

        while (frontier.Count > 0)
        {
            var cur = frontier.Dequeue();
            int d = dist[cur];
            if (d >= maxSteps) continue;

            foreach (var n in field.Orthogonal(cur))
            {
                if (dist.ContainsKey(n)) continue;
                if (!field.IsWalkable(n)) continue;
                if (occupied(n)) continue;
                dist[n] = d + 1;
                frontier.Enqueue(n);
            }
        }

        dist.Remove(start);
        return dist;
    }

    /// <summary>
    /// A* shortest orthogonal path from start to goal, inclusive of both ends.
    /// Returns null when the goal is unreachable. The goal cell itself is allowed to be
    /// occupied only if <paramref name="allowOccupiedGoal"/> is set (used when we want to
    /// path *adjacent* to a target rather than onto it).
    /// </summary>
    public static List<CellCoord>? FindPath(Battlefield field, CellCoord start, CellCoord goal,
        Func<CellCoord, bool> occupied, bool allowOccupiedGoal = false,
        Func<CellCoord, int>? stepDanger = null)
    {
        if (start == goal) return new List<CellCoord> { start };

        // Steps dominate (each costs 1000); per-cell danger from stepDanger breaks ties,
        // so among equally-short routes the walker picks the one that isn't on fire —
        // without ever paying extra MP to detour.
        var open = new PriorityQueue<CellCoord, int>();
        var cameFrom = new Dictionary<CellCoord, CellCoord>();
        var gScore = new Dictionary<CellCoord, int> { [start] = 0 };
        open.Enqueue(start, start.DistanceTo(goal) * 1000);

        while (open.Count > 0)
        {
            var cur = open.Dequeue();
            if (cur == goal) return Reconstruct(cameFrom, cur);

            foreach (var n in field.Orthogonal(cur))
            {
                bool isGoal = n == goal;
                if (!field.IsWalkable(n) && !isGoal) continue;
                if (occupied(n) && !(isGoal && allowOccupiedGoal)) continue;

                int tentative = gScore[cur] + 1000 + Math.Clamp(stepDanger?.Invoke(n) ?? 0, 0, 999);
                if (gScore.TryGetValue(n, out int existing) && tentative >= existing) continue;

                cameFrom[n] = cur;
                gScore[n] = tentative;
                open.Enqueue(n, tentative + n.DistanceTo(goal) * 1000);
            }
        }

        return null;
    }

    /// <summary>
    /// Geodesic (walk-around-walls) distance from <paramref name="source"/> to every reachable
    /// walkable cell, ignoring fighter occupancy. Used by the AI to approach a target around
    /// obstacles instead of walking straight into a wall with a naive Manhattan heuristic.
    /// </summary>
    public static Dictionary<CellCoord, int> DistanceField(Battlefield field, CellCoord source)
    {
        var dist = new Dictionary<CellCoord, int> { [source] = 0 };
        var frontier = new Queue<CellCoord>();
        frontier.Enqueue(source);
        while (frontier.Count > 0)
        {
            var cur = frontier.Dequeue();
            int d = dist[cur];
            foreach (var n in field.Orthogonal(cur))
            {
                if (dist.ContainsKey(n) || !field.IsWalkable(n)) continue;
                dist[n] = d + 1;
                frontier.Enqueue(n);
            }
        }
        return dist;
    }

    private static List<CellCoord> Reconstruct(Dictionary<CellCoord, CellCoord> cameFrom, CellCoord cur)
    {
        var path = new List<CellCoord> { cur };
        while (cameFrom.TryGetValue(cur, out var prev))
        {
            cur = prev;
            path.Add(cur);
        }
        path.Reverse();
        return path;
    }
}

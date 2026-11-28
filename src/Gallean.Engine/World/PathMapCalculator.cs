using Gallean.Common;

namespace Gallean.Engine;

internal static class PathMapCalculator
{
    // maxCost bounds the search: cells that cost more than maxCost to reach are left unreachable in the result. Every
    // cell within maxCost gets the same cost and predecessor as in an unbounded search, because cells are settled in
    // cost order and a cell's best predecessor always costs less than the cell itself.
    public static WorldPathMap? Calculate(
        WorldMap map,
        WorldPosition start,
        ImmutableBitmap? fogOfWar,
        ImmutableBitmap blockedCells,
        ImmutableBitmap terminalCells,
        Func<WorldPosition, WorldPosition, int> stepCost,
        int maxCost = int.MaxValue,
        HashSet<int>? stopTargets = null)
    {
        if (blockedCells.Get(start.X, start.Y))
        {
            return null;
        }

        Scratch scratch = Scratch.Take(map.Width * map.Height);

        try
        {
            return Search(map, start, fogOfWar, blockedCells, terminalCells, stepCost, maxCost, scratch, stopTargets);
        }
        finally
        {
            scratch.Release();
        }
    }

    private static WorldPathMap Search(
        WorldMap map,
        WorldPosition start,
        ImmutableBitmap? fogOfWar,
        ImmutableBitmap blockedCells,
        ImmutableBitmap terminalCells,
        Func<WorldPosition, WorldPosition, int> stepCost,
        int maxCost,
        Scratch scratch,
        HashSet<int>? stopTargets = null)
    {
        Span<WorldPosition> neighbors = stackalloc WorldPosition[8];

        int cellCount = map.Width * map.Height;
        int startIndex = GetIndex(map, start);
        List<int> reached = [];

        scratch.Touch(startIndex, reached);
        scratch.Distance[startIndex] = 0;
        scratch.Diagonals[startIndex] = 0;

        PriorityQueue<WorldPosition, long> openSet = new();
        openSet.Enqueue(start, GetPriority(0, startIndex));

        while (openSet.TryDequeue(out WorldPosition current, out long priority))
        {
            if (priority >> 32 > maxCost)
            {
                break;
            }

            int currentIndex = GetIndex(map, current);

            if (scratch.IsClosed(currentIndex))
            {
                continue;
            }

            scratch.Close(currentIndex);

            // Once every target is settled, finish the cells of the same cost and stop (a bounded map up to that cost).
            if (stopTargets is not null && stopTargets.Remove(currentIndex) && stopTargets.Count == 0)
            {
                maxCost = (int)(priority >> 32);
            }

            if (current != start && terminalCells.Get(current.X, current.Y))
            {
                continue;
            }

            int neighborCount = GetNeighbors(map, current, fogOfWar, blockedCells, neighbors);
            for (int i = 0; i < neighborCount; i++)
            {
                WorldPosition neighbor = neighbors[i];
                int neighborIndex = GetIndex(map, neighbor);

                if (scratch.IsClosed(neighborIndex))
                {
                    continue;
                }

                int moveCost = stepCost(current, neighbor);
                if (moveCost >= int.MaxValue)
                {
                    continue;
                }

                scratch.Touch(neighborIndex, reached);

                int tentativeDistance = scratch.Distance[currentIndex] + moveCost;
                int tentativeDiagonals = scratch.Diagonals[currentIndex] + (IsDiagonal(current, neighbor) ? 1 : 0);

                if (tentativeDistance > scratch.Distance[neighborIndex])
                {
                    continue;
                }

                if (tentativeDistance == scratch.Distance[neighborIndex] &&
                    tentativeDiagonals >= scratch.Diagonals[neighborIndex])
                {
                    continue;
                }

                scratch.Predecessors[neighborIndex] = currentIndex;
                scratch.Distance[neighborIndex] = tentativeDistance;
                scratch.Diagonals[neighborIndex] = tentativeDiagonals;
                openSet.Enqueue(neighbor, GetPriority(tentativeDistance, neighborIndex));
            }
        }

        return maxCost < int.MaxValue
            ? BuildBounded(map, start, scratch, reached, maxCost)
            : BuildUnbounded(map, start, scratch, reached, cellCount);
    }

    // Only the cells settled within maxCost: cells reached but not settled may not have their final cost.
    private static WorldPathMap BuildBounded(WorldMap map, WorldPosition start, Scratch scratch, List<int> reached, int maxCost)
    {
        reached.Sort();

        List<int> indexes = new(reached.Count);
        List<int> costs = new(reached.Count);
        List<int> predecessors = new(reached.Count);

        foreach (int index in reached)
        {
            if (scratch.Distance[index] <= maxCost)
            {
                indexes.Add(index);
                costs.Add(scratch.Distance[index]);
                predecessors.Add(scratch.Predecessors[index]);
            }
        }

        return new WorldPathMap(map, start, [.. indexes], [.. costs], [.. predecessors], maxCost);
    }

    private static WorldPathMap BuildUnbounded(WorldMap map, WorldPosition start, Scratch scratch, List<int> reached, int cellCount)
    {
        int[] costs = new int[cellCount];
        int[] predecessors = new int[cellCount];

        Array.Fill(costs, int.MaxValue);
        Array.Fill(predecessors, -1);

        foreach (int index in reached)
        {
            costs[index] = scratch.Distance[index];
            predecessors[index] = scratch.Predecessors[index];
        }

        return new WorldPathMap(map, start, costs, predecessors);
    }

    private static int GetNeighbors(
        WorldMap map,
        WorldPosition position,
        ImmutableBitmap? fogOfWar,
        ImmutableBitmap blockedCells,
        Span<WorldPosition> neighbors)
    {
        int count = 0;

        count = AddNeighbor(map, position with { Y = position.Y - 1 }, fogOfWar, blockedCells, neighbors, count);
        count = AddNeighbor(map, position with { Y = position.Y + 1 }, fogOfWar, blockedCells, neighbors, count);
        count = AddNeighbor(map, position with { X = position.X - 1 }, fogOfWar, blockedCells, neighbors, count);
        count = AddNeighbor(map, position with { X = position.X + 1 }, fogOfWar, blockedCells, neighbors, count);
        count = AddNeighbor(map, new WorldPosition(position.X - 1, position.Y - 1), fogOfWar, blockedCells, neighbors, count);
        count = AddNeighbor(map, new WorldPosition(position.X + 1, position.Y - 1), fogOfWar, blockedCells, neighbors, count);
        count = AddNeighbor(map, new WorldPosition(position.X - 1, position.Y + 1), fogOfWar, blockedCells, neighbors, count);
        count = AddNeighbor(map, new WorldPosition(position.X + 1, position.Y + 1), fogOfWar, blockedCells, neighbors, count);

        return count;
    }

    private static int AddNeighbor(
        WorldMap map,
        WorldPosition candidate,
        ImmutableBitmap? fogOfWar,
        ImmutableBitmap blockedCells,
        Span<WorldPosition> neighbors,
        int count)
    {
        if (!map.IsValidPosition(candidate))
        {
            return count;
        }

        if (fogOfWar is not null && !fogOfWar.Get(candidate.X, candidate.Y))
        {
            return count;
        }

        if (blockedCells.Get(candidate.X, candidate.Y))
        {
            return count;
        }

        neighbors[count] = candidate;

        return count + 1;
    }

    private static long GetPriority(int cost, int index)
    {
        return cost * (1L << 32) + index;
    }

    private static bool IsDiagonal(WorldPosition from, WorldPosition to)
    {
        return from.X != to.X && from.Y != to.Y;
    }

    private static int GetIndex(WorldMap map, WorldPosition position)
    {
        return position.Y * map.Width + position.X;
    }

    // Map-sized search arrays kept per thread and reset by generation instead of filled: a search that reaches a few
    // thousand cells used to allocate and fill four arrays over the whole map (a million cells at 1024x1024).
    private sealed class Scratch
    {
        [ThreadStatic]
        private static Scratch? cached;

        private readonly int[] marks;
        private int generation;
        private bool inUse;

        private Scratch(int cellCount)
        {
            marks = new int[cellCount];
            Distance = new int[cellCount];
            Diagonals = new int[cellCount];
            Predecessors = new int[cellCount];
        }

        public int[] Distance { get; }
        public int[] Diagonals { get; }
        public int[] Predecessors { get; }

        public static Scratch Take(int cellCount)
        {
            Scratch? scratch = cached;

            // A search inside a search (should a step cost ever need one) gets its own arrays.
            if (scratch is null || scratch.inUse || scratch.marks.Length != cellCount)
            {
                scratch = new Scratch(cellCount);

                if (cached is null || !cached.inUse)
                {
                    cached = scratch;
                }
            }

            scratch.inUse = true;

            // marks[i] is 2 * generation once cell i is reached in this search, 2 * generation + 1 once it is closed.
            if (scratch.generation >= (int.MaxValue / 2) - 1)
            {
                Array.Clear(scratch.marks);
                scratch.generation = 0;
            }

            scratch.generation++;

            return scratch;
        }

        public void Release()
        {
            inUse = false;
        }

        // First touch in this search: unreached (infinite cost, no predecessor).
        public void Touch(int index, List<int> reached)
        {
            if (marks[index] >> 1 != generation)
            {
                marks[index] = generation << 1;
                Distance[index] = int.MaxValue;
                Diagonals[index] = int.MaxValue;
                Predecessors[index] = -1;
                reached.Add(index);
            }
        }

        public bool IsClosed(int index)
        {
            return marks[index] == (generation << 1) + 1;
        }

        public void Close(int index)
        {
            marks[index] = (generation << 1) + 1;
        }
    }
}

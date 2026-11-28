using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Gallean.Engine;

// Costs and predecessors of the cells a path search reached. An unbounded search keeps them in arrays over the whole
// map; a bounded search (where can this army go this turn) reaches a few thousand cells, so it keeps only those, sorted
// by cell index: two map-sized arrays per step cost ~8 MB of garbage on a 1024x1024 map.
internal sealed class WorldPathMap
{
    private readonly WorldMap map;
    private readonly WorldPosition start;
    private readonly int[]? cellIndexes;
    private readonly int[] costs;
    private readonly int[] predecessors;
    private readonly int maxCost;

    // costs and predecessors indexed by cell.
    public WorldPathMap(WorldMap map, WorldPosition start, int[] costs, int[] predecessors, int maxCost = int.MaxValue)
        : this(map, start, null, costs, predecessors, maxCost)
    {
    }

    // costs[i] and predecessors[i] belong to cell cellIndexes[i]; cellIndexes is sorted; other cells are unreached.
    public WorldPathMap(WorldMap map, WorldPosition start, int[]? cellIndexes, int[] costs, int[] predecessors, int maxCost)
    {
        this.map = map;
        this.start = start;
        this.cellIndexes = cellIndexes;
        this.costs = costs;
        this.predecessors = predecessors;
        this.maxCost = maxCost;
    }

    public int MaxCost => maxCost;

    // True when the map holds the final answer for the target: an unbounded map always does, a bounded map only for
    // targets it reached within its cost bound.
    public bool IsSettled(WorldPosition target)
    {
        return maxCost == int.MaxValue || GetCost(GetIndex(target)) <= maxCost;
    }

    public bool TryGetPath(WorldPosition target, WorldMovementLimit limit, [NotNullWhen(true)] out WorldPath? path)
    {
        if (GetCost(GetIndex(target)) >= int.MaxValue)
        {
            path = null;
            return false;
        }

        Stack<(WorldPosition From, WorldPosition To, int Cost)> steps = new();
        WorldPosition current = target;

        while (current != start)
        {
            int predecessor = GetPredecessor(GetIndex(current));

            if (predecessor < 0)
            {
                path = null;
                return false;
            }

            WorldPosition previous = GetPosition(predecessor);
            int stepCost = GetCost(GetIndex(current)) - GetCost(GetIndex(previous));
            steps.Push((previous, current, stepCost));
            current = previous;
        }

        path = PathSegmentCalculator.Calculate([.. steps], limit);
        return true;
    }

    public ImmutableArray<WorldPosition> GetReachablePositions(int movementPoints)
    {
        ImmutableArray<WorldPosition>.Builder result = ImmutableArray.CreateBuilder<WorldPosition>();

        for (int slot = 0; slot < costs.Length; slot++)
        {
            int index = cellIndexes is null ? slot : cellIndexes[slot];

            if (predecessors[slot] >= 0 && IsReachable(index, movementPoints))
            {
                result.Add(GetPosition(index));
            }
        }

        return result.ToImmutable();
    }

    // Cost to reach the target, int.MaxValue when unreached.
    public int GetCost(WorldPosition target)
    {
        return GetCost(GetIndex(target));
    }

    private bool IsReachable(int index, int movementPoints)
    {
        int entry = index;

        while (GetPredecessor(entry) >= 0 && GetCost(GetPredecessor(entry)) == GetCost(entry))
        {
            entry = GetPredecessor(entry);
        }

        int previous = GetPredecessor(entry);

        if (previous >= 0)
        {
            return WorldMovementCalculator.CanBeginStep(movementPoints, GetCost(entry) - GetCost(previous), GetCost(previous));
        }
        else
        {
            return true;
        }
    }

    private int GetCost(int index)
    {
        int slot = GetSlot(index);

        return slot >= 0 ? costs[slot] : int.MaxValue;
    }

    private int GetPredecessor(int index)
    {
        int slot = GetSlot(index);

        return slot >= 0 ? predecessors[slot] : -1;
    }

    private int GetSlot(int index)
    {
        return cellIndexes is null ? index : Array.BinarySearch(cellIndexes, index);
    }

    private int GetIndex(WorldPosition position)
    {
        return position.Y * map.Width + position.X;
    }

    private WorldPosition GetPosition(int index)
    {
        return new WorldPosition(index % map.Width, index / map.Width);
    }
}

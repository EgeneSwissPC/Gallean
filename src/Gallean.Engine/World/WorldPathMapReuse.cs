using Gallean.Common;

namespace Gallean.Engine;

// Gallean Upgrade (AI speed): path maps kept across states. Every action makes a new state and so a new WorldPlan with
// an empty path-map cache, and the AI asks again for the maps of the same armies (reinforce planning, attacks,
// exploring): on a 1024x1024 map each one is a search over a million cells. A map from an earlier state is taken over
// when it is provably the map a new search would build: same water and features on the map, start, movement
// profile, fog of war and free entrances, and for every cell whose blocked or terminal flag changed (an army moved,
// a bag appeared) the search could not have come out differently:
// - a cell that became blocked must have been unreached (a reached cell would now be cut off);
// - a cell that became free must have no reached neighbour (else it becomes reachable);
// - a cell that became terminal (stops the search there, like an army) must be nobody's predecessor;
// - a cell that stopped being terminal is expanded now: no neighbour may get a lower cost, or an equal cost with
//   fewer diagonal steps, or an equal one offered before its current predecessor was settled (the search keeps the
//   first of equal offers, in priority order: cost, then cell index).
// Anything else (terrain change, other fog, another layout) is a plain miss.
internal static class WorldPathMapReuse
{
    private const int MaxEntries = 4096;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldMap, Cache> Caches = [];

    // Land spread makes a new map at every turn start, but it only changes the race terrain of land cells, and movement
    // costs only read water and features (WorldMovementCalculator.GetMovementCost). A new map with the same water and
    // features as the last one shares its cache.
    private static WeakReference<WorldMap>? lastMap;

    // Test hooks: GALLEAN_VERIFY_PATH_REUSE=1 rebuilds every reused map and counts differences.
    internal static readonly bool Verify = Environment.GetEnvironmentVariable("GALLEAN_VERIFY_PATH_REUSE") == "1";
    internal static long Reused;
    internal static long Rejected;
    internal static long Mismatches;
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> Reasons = new();

    private static bool Reason(string reason)
    {
        Reasons.AddOrUpdate(reason, 1, (_, n) => n + 1);
        return false;
    }

    private readonly record struct Key(WorldPosition Start, WorldMovementCalculator.MovementProfile Profile, bool HasFog);

    private sealed class Entry(ImmutableBitmap? fog, ImmutableBitmap blocked, ImmutableBitmap terminal, ImmutableBitmap entrances, WorldPathMap? pathMap)
    {
        public ImmutableBitmap? Fog { get; set; } = fog;
        public ImmutableBitmap Blocked { get; set; } = blocked;
        public ImmutableBitmap Terminal { get; set; } = terminal;
        public ImmutableBitmap Entrances { get; set; } = entrances;
        public WorldPathMap? PathMap { get; set; } = pathMap;
    }

    private sealed class Cache
    {
        public Dictionary<Key, Entry> Entries { get; } = [];
    }

    // The inputs a search reads, besides the map cells and the start: stepCost(from, to) depends on the map, the free
    // entrances and the blocked cells (an army on an impassable cell clears it there), nothing else.
    internal readonly record struct Inputs(
        WorldMap Map,
        WorldPosition Start,
        ImmutableBitmap? Fog,
        WorldMovementCalculator.MovementProfile Profile,
        ImmutableBitmap Blocked,
        ImmutableBitmap Terminal,
        ImmutableBitmap Entrances,
        Func<WorldPosition, WorldPosition, int> StepCost);

    // A path map from an earlier state that is valid for these inputs and covers what accept asks for, or null.
    internal static WorldPathMap? TryReuse(in Inputs inputs, Func<WorldPathMap, bool> accept)
    {
        Cache cache = GetCache(inputs.Map);

        lock (cache)
        {
            if (!cache.Entries.TryGetValue(new Key(inputs.Start, inputs.Profile, inputs.Fog is not null), out Entry? entry) ||
                entry.PathMap is null)
            {
                Reason("no entry");
                return null;
            }

            if (!accept(entry.PathMap))
            {
                Reason("too small");
                return null;
            }

            if (!SameBitmap(entry.Fog, inputs.Fog))
            {
                Reason("fog");
                Rejected++;
                return null;
            }

            if (!SameBitmap(entry.Entrances, inputs.Entrances))
            {
                Reason("entrances");
                Rejected++;
                return null;
            }

            if (!IsStillValid(entry, inputs))
            {
                Rejected++;
                return null;
            }

            entry.Fog = inputs.Fog;
            entry.Blocked = inputs.Blocked;
            entry.Terminal = inputs.Terminal;
            entry.Entrances = inputs.Entrances;
            Reused++;

            return entry.PathMap;
        }
    }

    internal static void Store(in Inputs inputs, WorldPathMap? pathMap)
    {
        if (pathMap is null)
        {
            return;
        }

        Cache cache = GetCache(inputs.Map);

        lock (cache)
        {
            if (cache.Entries.Count >= MaxEntries)
            {
                cache.Entries.Clear();
            }

            cache.Entries[new Key(inputs.Start, inputs.Profile, inputs.Fog is not null)] =
                new Entry(inputs.Fog, inputs.Blocked, inputs.Terminal, inputs.Entrances, pathMap);
        }
    }

    // Verification: the reused map against a fresh one, on every cell the reused map settles.
    internal static void Check(WorldPathMap reused, WorldPathMap? fresh)
    {
        if (fresh is null)
        {
            Interlocked.Increment(ref Mismatches);
            return;
        }

        int count = reused.Map.Width * reused.Map.Height;

        for (int index = 0; index < count; index++)
        {
            int cost = reused.GetCostAt(index);

            if (cost <= reused.MaxCost && cost < int.MaxValue)
            {
                if (fresh.GetCostAt(index) != cost || fresh.GetPredecessorAt(index) != reused.GetPredecessorAt(index))
                {
                    Interlocked.Increment(ref Mismatches);
                    return;
                }
            }
            else if (reused.MaxCost == int.MaxValue && fresh.GetCostAt(index) < int.MaxValue)
            {
                Interlocked.Increment(ref Mismatches);
                return;
            }
        }
    }

    private static Cache GetCache(WorldMap map)
    {
        lock (Caches)
        {
            if (Caches.TryGetValue(map, out Cache? cache))
            {
                return cache;
            }

            if (lastMap is not null && lastMap.TryGetTarget(out WorldMap? last) && Caches.TryGetValue(last, out Cache? lastCache) &&
                HasSameMovement(last, map))
            {
                cache = lastCache;
            }
            else
            {
                cache = new Cache();
            }

            Caches.AddOrUpdate(map, cache);
            lastMap = new WeakReference<WorldMap>(map);

            return cache;
        }
    }

    private static bool HasSameMovement(WorldMap a, WorldMap b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
        {
            return false;
        }

        for (int index = 0; index < a.Cells.Length; index++)
        {
            WorldMapCell cellA = a.Cells[index];
            WorldMapCell cellB = b.Cells[index];

            if (cellA.Feature != cellB.Feature ||
                (cellA.Terrain == WorldMapTerrain.Water) != (cellB.Terrain == WorldMapTerrain.Water))
            {
                return false;
            }
        }

        return true;
    }

    private static List<int> EnumerateDifferences(ImmutableBitmap before, ImmutableBitmap after)
    {
        List<int> result = [];

        if (ReferenceEquals(before, after))
        {
            return result;
        }

        ReadOnlySpan<ulong> a = before.GetWords();
        ReadOnlySpan<ulong> b = after.GetWords();

        for (int word = 0; word < a.Length; word++)
        {
            ulong difference = a[word] ^ b[word];

            while (difference != 0)
            {
                int bit = System.Numerics.BitOperations.TrailingZeroCount(difference);
                result.Add((word << 6) + bit);
                difference &= difference - 1;
            }
        }

        return result;
    }

    private static bool SameBitmap(ImmutableBitmap? a, ImmutableBitmap? b)
    {
        return ReferenceEquals(a, b) || (a is not null && b is not null && a.Equals(b));
    }

    private static bool IsStillValid(Entry entry, in Inputs inputs)
    {
        WorldPathMap pathMap = entry.PathMap!;
        WorldMap map = inputs.Map;
        int width = map.Width;
        int startIndex = inputs.Start.Y * width + inputs.Start.X;

        foreach (int index in EnumerateDifferences(entry.Blocked, inputs.Blocked))
        {
            WorldPosition cell = new(index % width, index / width);

            if (inputs.Blocked.Get(cell.X, cell.Y))
            {
                if (pathMap.GetCostAt(index) < int.MaxValue || index == startIndex)
                {
                    return Reason("blocked reached");
                }
            }
            else if (inputs.Fog is null || inputs.Fog.Get(cell.X, cell.Y))
            {
                foreach (WorldPosition neighbor in cell.GetAdjacentPositions())
                {
                    if (map.IsValidPosition(neighbor) && pathMap.GetCostAt(neighbor.Y * width + neighbor.X) < int.MaxValue)
                    {
                        return Reason("freed next to reached");
                    }
                }
            }
        }

        foreach (int index in EnumerateDifferences(entry.Terminal, inputs.Terminal))
        {
            if (index == startIndex)
            {
                continue;
            }

            WorldPosition cell = new(index % width, index / width);

            if (inputs.Blocked.Get(cell.X, cell.Y))
            {
                // Handled above: a blocked cell is never entered, terminal or not.
                continue;
            }

            if (inputs.Terminal.Get(cell.X, cell.Y))
            {
                foreach (WorldPosition neighbor in cell.GetAdjacentPositions())
                {
                    if (map.IsValidPosition(neighbor) && pathMap.GetPredecessorAt(neighbor.Y * width + neighbor.X) == index)
                    {
                        return Reason("new terminal is predecessor");
                    }
                }
            }
            else if (!CanStayUnexpanded(pathMap, inputs, cell, index))
            {
                return Reason("old terminal expands");
            }
        }

        return true;
    }

    // A cell that is no longer terminal now relaxes its neighbours; true when none of them would change.
    private static bool CanStayUnexpanded(WorldPathMap pathMap, in Inputs inputs, WorldPosition cell, int index)
    {
        int cost = pathMap.GetCostAt(index);

        if (cost >= int.MaxValue || cost > pathMap.MaxCost)
        {
            return true;
        }

        WorldMap map = inputs.Map;
        int width = map.Width;
        int diagonals = -1;

        foreach (WorldPosition neighbor in cell.GetAdjacentPositions())
        {
            if (!map.IsValidPosition(neighbor) ||
                (inputs.Fog is not null && !inputs.Fog.Get(neighbor.X, neighbor.Y)) ||
                inputs.Blocked.Get(neighbor.X, neighbor.Y))
            {
                continue;
            }

            int neighborIndex = neighbor.Y * width + neighbor.X;
            int neighborCost = pathMap.GetCostAt(neighborIndex);

            // Settled before this cell: the search never relaxes a closed cell.
            if (IsSettledBefore(neighborCost, neighborIndex, cost, index))
            {
                continue;
            }

            int step = inputs.StepCost(cell, neighbor);

            if (step >= int.MaxValue)
            {
                continue;
            }

            int offer = cost + step;

            if (offer > pathMap.MaxCost)
            {
                continue;
            }

            if (offer < neighborCost)
            {
                return false;
            }

            if (offer == neighborCost)
            {
                if (neighborIndex == inputs.Start.Y * width + inputs.Start.X)
                {
                    continue;
                }

                if (diagonals < 0)
                {
                    diagonals = CountDiagonals(pathMap, index, width);
                }

                int offerDiagonals = diagonals + (neighbor.X != cell.X && neighbor.Y != cell.Y ? 1 : 0);
                int predecessor = pathMap.GetPredecessorAt(neighborIndex);
                int neighborDiagonals = CountDiagonals(pathMap, neighborIndex, width);

                if (offerDiagonals < neighborDiagonals)
                {
                    return false;
                }

                if (offerDiagonals == neighborDiagonals &&
                    predecessor >= 0 &&
                    IsSettledBefore(cost, index, pathMap.GetCostAt(predecessor), predecessor))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsSettledBefore(int costA, int indexA, int costB, int indexB)
    {
        return costA < costB || (costA == costB && indexA < indexB);
    }

    private static int CountDiagonals(WorldPathMap pathMap, int index, int width)
    {
        int count = 0;
        int current = index;

        for (int predecessor = pathMap.GetPredecessorAt(current); predecessor >= 0; predecessor = pathMap.GetPredecessorAt(current))
        {
            if (predecessor % width != current % width && predecessor / width != current / width)
            {
                count++;
            }

            current = predecessor;
        }

        return count;
    }
}

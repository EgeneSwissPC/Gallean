using System.Collections.Immutable;
using System.Numerics;
using Gallean.Common;

namespace Gallean.Engine;

// Tuning for auto explore (docs/auto-explore.md). Defaults are the user's start values.
public sealed record AutoExploreSettings(
    int RevealWeight = 10,
    int CostWeight = 3,
    int RoadBonusTiles = 10,
    int SafetyDistance = 3)
{
    public static AutoExploreSettings Default { get; } = new();
}

public abstract record AutoExploreResult
{
    private AutoExploreResult()
    {
    }

    // The next tile to walk to: a seen tile at the edge of the fog.
    public sealed record Target(WorldPosition Position, int RevealedTiles, int PathCost, bool IsRoadIntoFog) : AutoExploreResult;

    // Every tile this army could ever reach is already seen (docs/auto-explore.md decision H: per army).
    public sealed record NothingLeft : AutoExploreResult
    {
        public static NothingLeft Instance { get; } = new();
    }

    // Hidden land is reachable, but no edge of the fog can be used right now (own army in a pass, enemies near every
    // edge, a battle on every path). Try again later; this is never "no more land".
    public sealed record NoTargetNow : AutoExploreResult
    {
        public static NoTargetNow Instance { get; } = new();
    }
}

// Picks where an exploring army goes next. Read only, uses only what the player has seen (plus the game's own path
// check, which counts every army like a normal move).
internal static class AutoExploreCalculator
{
    public static AutoExploreResult FindTarget(
        WorldState state,
        WorldStack stack,
        IEngineContext context,
        WorldPosition? previousTarget,
        AutoExploreSettings settings)
    {
        WorldPlayer player = state.Players[stack.OwnerId];
        ImmutableBitmap seen = player.FogOfWar;
        WorldPlan plan = WorldPlan.For(state);
        WorldMovementCalculator.MovementProfile profile = WorldMovementCalculator.GetMovementProfile(stack, context.Definitions);

        if (!HasReachableHiddenLand(state, plan, seen, stack.Position, profile))
        {
            return AutoExploreResult.NothingLeft.Instance;
        }

        WorldPathMap? pathMap = plan.GetPathMap(stack.Position, seen, profile);

        if (pathMap is null)
        {
            return AutoExploreResult.NoTargetNow.Instance;
        }

        int visionRadius = context.UnitStats.GetWorldStackStats(stack).VisionRadius;
        ImmutableBitmap danger = BuildDangerZone(state, plan, seen, stack, context.Definitions, settings.SafetyDistance);
        StackStats stats = context.UnitStats.GetWorldStackStats(stack);
        WorldMovementLimit limit = new(stack.MovementPoints, stats.MaxMovementPoints, TurnLimit: int.MaxValue);

        // Keep the current target while it is still a usable edge of the fog, so the army doesn't change its mind.
        if (previousTarget is { } kept && previousTarget != stack.Position)
        {
            Candidate? candidate = Evaluate(kept);

            if (candidate is not null && IsSafePath(candidate.Value))
            {
                return ToResult(candidate.Value);
            }
        }

        List<Candidate> candidates = [];

        foreach (WorldPosition position in pathMap.GetReachablePositions(int.MaxValue))
        {
            if (Evaluate(position) is { } candidate)
            {
                candidates.Add(candidate);
            }
        }

        // Best score first; ties go to the lower tile index so results are deterministic.
        candidates.Sort((a, b) => a.Score != b.Score
            ? b.Score.CompareTo(a.Score)
            : GetIndex(state.Map, a.Position).CompareTo(GetIndex(state.Map, b.Position)));

        foreach (Candidate candidate in candidates)
        {
            if (IsSafePath(candidate))
            {
                return ToResult(candidate);
            }
        }

        return AutoExploreResult.NoTargetNow.Instance;

        Candidate? Evaluate(WorldPosition position)
        {
            // Cheap bitmap checks first: this runs for every reachable tile (about 40 000 on the 1024 map).
            if (position == stack.Position ||
                !state.Map.IsValidPosition(position) ||
                !seen.Get(position.X, position.Y) ||
                !HasHiddenNeighbour(state.Map, seen, position) ||
                danger.Get(position.X, position.Y) ||
                !plan.CanPlaceStackAt(position, profile) ||
                plan.GetFortificationAt(position) is not null ||
                plan.GetSiteAt(position) is not null)
            {
                return null;
            }

            int cost = pathMap.GetCost(position);

            if (cost >= int.MaxValue)
            {
                return null;
            }

            int revealed = CountHidden(state.Map, seen, position, visionRadius);
            bool roadIntoFog = state.Map.GetCellAt(position).Feature == WorldMapFeature.Road;
            int bonus = roadIntoFog ? settings.RoadBonusTiles : 0;
            long score = (long)(revealed + bonus) * settings.RevealWeight - (long)cost * settings.CostWeight;

            return new Candidate(position, revealed, cost, roadIntoFog, score);
        }

        bool IsSafePath(Candidate candidate)
        {
            if (!pathMap.TryGetPath(candidate.Position, limit, out WorldPath? path) || path.Positions.IsEmpty)
            {
                return false;
            }

            PathTraversalCalculator.TraversalInfo traversal = PathTraversalCalculator.Calculate(state, stack, path, profile, context.Definitions);

            return traversal.Encounter is null or PathTraversalCalculator.Encounter.Bag;
        }

        static AutoExploreResult ToResult(Candidate candidate)
        {
            return new AutoExploreResult.Target(candidate.Position, candidate.Revealed, candidate.Cost, candidate.RoadIntoFog);
        }
    }

    // Can this army ever reach a hidden tile? Flood fill over seen tiles it can walk on, treating every hidden tile as
    // passable (nothing is known about it). Seen mountains, other impassable terrain and obstacles stop it; a city or
    // site entrance can be entered but not walked through. Armies don't block: they move.
    public static bool HasReachableHiddenLand(
        WorldState state,
        WorldPlan plan,
        ImmutableBitmap seen,
        WorldPosition start,
        WorldMovementCalculator.MovementProfile profile)
    {
        WorldMap map = state.Map;
        bool[] visited = new bool[map.Width * map.Height];
        Queue<WorldPosition> queue = new();

        visited[GetIndex(map, start)] = true;
        queue.Enqueue(start);

        while (queue.TryDequeue(out WorldPosition current))
        {
            if (current != start && IsEntrance(plan, current))
            {
                continue;
            }

            foreach (WorldPosition next in current.GetAdjacentPositions())
            {
                if (!map.IsValidPosition(next))
                {
                    continue;
                }

                int index = GetIndex(map, next);

                if (visited[index])
                {
                    continue;
                }

                visited[index] = true;

                if (!seen.Get(next.X, next.Y))
                {
                    return true;
                }

                if (plan.IsObstacleAt(next) || !WorldMovementCalculator.IsPassable(map, next, profile))
                {
                    continue;
                }

                queue.Enqueue(next);
            }
        }

        return false;
    }

    // Tiles within the safety distance of a known hostile army that moves around: on a seen tile, not in a city, not
    // of the explorer's own subrace (the armies that could intercept a path, see
    // PathTraversalCalculator.FindInterceptingStack).
    private static ImmutableBitmap BuildDangerZone(
        WorldState state,
        WorldPlan plan,
        ImmutableBitmap seen,
        WorldStack stack,
        IGameDefinitionProvider definitions,
        int distance)
    {
        ImmutableBitmap.Builder zone = ImmutableBitmap.Create(state.Map.Width, state.Map.Height).ToBuilder();

        foreach (WorldStack other in state.Stacks.Values)
        {
            WorldPosition position = other.Position;

            if (other.Id != stack.Id &&
                seen.Get(position.X, position.Y) &&
                plan.GetFortificationAt(position) is null &&
                DiplomacyCalculator.IsHostile(state, stack.OwnerId, other.OwnerId) &&
                !StackCalculator.IsSameSubrace(stack, other, definitions))
            {
                for (int y = Math.Max(0, position.Y - distance); y <= Math.Min(state.Map.Height - 1, position.Y + distance); y++)
                {
                    for (int x = Math.Max(0, position.X - distance); x <= Math.Min(state.Map.Width - 1, position.X + distance); x++)
                    {
                        zone.Set(x, y);
                    }
                }
            }
        }

        return zone.ToImmutable();
    }

    private static bool IsEntrance(WorldPlan plan, WorldPosition position)
    {
        return plan.GetFortificationAt(position) is not null || plan.GetSiteEntranceAt(position) is not null;
    }

    private static bool HasHiddenNeighbour(WorldMap map, ImmutableBitmap seen, WorldPosition position)
    {
        foreach (WorldPosition next in position.GetAdjacentPositions())
        {
            if (map.IsValidPosition(next) && !seen.Get(next.X, next.Y))
            {
                return true;
            }
        }

        return false;
    }

    // Hidden tiles inside the square the army would see from there (FogOfWarCalculator reveals a square). Counted row
    // by row on the bitmap words: this runs for every fog-edge tile (about 1 500 on the 1024 map).
    internal static int CountHidden(WorldMap map, ImmutableBitmap seen, WorldPosition center, int radius)
    {
        ReadOnlySpan<ulong> words = seen.GetWords();
        int count = 0;
        int minY = Math.Max(0, center.Y - radius);
        int maxY = Math.Min(map.Height - 1, center.Y + radius);
        int minX = Math.Max(0, center.X - radius);
        int maxX = Math.Min(map.Width - 1, center.X + radius);

        for (int y = minY; y <= maxY; y++)
        {
            int first = (y * map.Width) + minX;
            int last = (y * map.Width) + maxX;
            int seenCount = 0;

            for (int word = first >> 6; word <= last >> 6; word++)
            {
                int from = Math.Max(first, word << 6) & 63;
                int to = Math.Min(last, (word << 6) + 63) & 63;
                ulong mask = (to == 63 ? ulong.MaxValue : (1UL << (to + 1)) - 1) & ~((1UL << from) - 1);
                seenCount += BitOperations.PopCount(words[word] & mask);
            }

            count += (maxX - minX + 1) - seenCount;
        }

        return count;
    }

    private static int GetIndex(WorldMap map, WorldPosition position)
    {
        return position.Y * map.Width + position.X;
    }

    private readonly record struct Candidate(WorldPosition Position, int Revealed, int Cost, bool RoadIntoFog, long Score);
}

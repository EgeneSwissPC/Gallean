using Gallean.Common;

namespace Gallean.Engine;

internal sealed partial class WorldAiPathfinding(IEngineContext context)
{
    public WorldPathMap? GetPathMap(WorldState state, WorldStack stack)
    {
        WorldPlan plan = WorldPlan.For(state);
        WorldPlayer player = state.Players[stack.OwnerId];
        ImmutableBitmap? fogOfWar = stack.Order.Accept(WorldAiFogOfWarVisitor.Instance, player);

        return plan.GetPathMap(stack.Position, fogOfWar, GetMovementProfile(stack));
    }

    // Gallean Upgrade (AI speed): a path map that is only complete for the given targets. The search stops once all of
    // them are settled, so the paths to them are the ones a full search finds; on a 1024 map a full search costs
    // 80-250 ms. Returns null when no target has a position (no path can come out of the map either way).
    public WorldPathMap? GetPathMapToTargets(WorldState state, WorldStack stack, IReadOnlyList<WorldAiTarget> targets)
    {
        List<WorldPosition> positions = [];

        foreach (WorldAiTarget target in targets)
        {
            if (target.Accept(WorldAiTargetPositionVisitor.Instance, state) is WorldPosition position)
            {
                positions.Add(position);
            }
        }

        if (positions.Count == 0)
        {
            return null;
        }

        // Normal orders aim at every enemy, so the search would settle most of the map, and their move action needs the
        // whole map right after; on maps below the large-map size a full search is cheap. Both share one full map.
        if (stack.Order is WorldStackOrder.Normal || state.Map.Width * state.Map.Height < LargeMapRules.MinMapSize * LargeMapRules.MinMapSize)
        {
            return GetPathMap(state, stack);
        }

        WorldPlan plan = WorldPlan.For(state);
        WorldPlayer player = state.Players[stack.OwnerId];
        ImmutableBitmap? fogOfWar = stack.Order.Accept(WorldAiFogOfWarVisitor.Instance, player);

        return plan.GetPathMapToTargets(stack.Position, fogOfWar, GetMovementProfile(stack), positions);
    }

    public WorldPosition? FindMoveTowardCell(WorldState state, WorldStack stack, WorldPathMap pathMap, WorldPosition target)
    {
        WorldMovementLimit movementLimit = new(
            stack.MovementPoints,
            context.UnitStats.GetWorldStackStats(stack).MaxMovementPoints,
            TurnLimit: 1);

        if (!pathMap.TryGetPath(target, movementLimit, out WorldPath? path))
        {
            return null;
        }

        for (int count = path.Segments.Length; count >= 1; count--)
        {
            WorldPosition candidate = path.Positions[count - 1];

            WorldCommandResult commandResult = new WorldAiMoveStackCommand(stack.Id, candidate).Execute(state, context);
            if (!commandResult.Failed)
            {
                PathTraversalCalculator.TraversalInfo traversal = GetTraversalInfo(state, stack, path.Truncate(count));

                if (traversal.Encounter is not PathTraversalCalculator.Encounter.Battle)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public PathTraversalCalculator.TraversalInfo GetTraversalInfo(WorldState state, WorldStack stack, WorldPath path)
    {
        return PathTraversalCalculator.Calculate(state, stack, path, GetMovementProfile(stack), context.Definitions);
    }

    public IReadOnlyList<TargetPath> GetTargetPaths(WorldState state, WorldPathMap pathMap, IEnumerable<WorldAiTarget> targets)
    {
        List<TargetPath> result = [];

        foreach (WorldAiTarget target in targets)
        {
            WorldPosition? position = target.Accept(WorldAiTargetPositionVisitor.Instance, state);

            if (position is not null && pathMap.TryGetPath(position.Value, WorldMovementLimit.Unlimited, out WorldPath? path))
            {
                result.Add(new TargetPath(target, position.Value, path));
            }
        }

        return result;
    }

    private WorldMovementCalculator.MovementProfile GetMovementProfile(WorldStack stack)
    {
        return WorldMovementCalculator.GetMovementProfile(stack, context.Definitions);
    }
}

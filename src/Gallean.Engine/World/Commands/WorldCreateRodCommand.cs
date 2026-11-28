using Gallean.Common;

namespace Gallean.Engine;

public sealed record WorldCreateRodCommand(
    WorldStackId StackId,
    WorldPosition TargetPosition) : IWorldCommand<Empty>
{
    public WorldCommandResult Execute(WorldState state, IEngineContext context)
    {
        WorldPlayerId currentPlayer = state.TurnInfo.CurrentPlayerId;

        if (!WorldCommandHelpers.TryGetCurrentPlayerStack(state, StackId, out WorldStack? stack))
        {
            return WorldCommandResult.Fail(WorldCommandError.StackNotFound);
        }

        (WorldUnit leader, _, _) = stack.GetLeader(context.Definitions);
        LeaderAbility leaderAbilities = context.UnitStats.GetWorldUnitAbilities(leader);

        if (!leaderAbilities.HasFlag(LeaderAbility.Rod))
        {
            return WorldCommandResult.Fail(WorldCommandError.RodLeaderRequired);
        }

        if (!leader.IsAlive)
        {
            return WorldCommandResult.Fail(WorldCommandError.RodLeaderDead);
        }

        if (stack.MovementPoints <= 0)
        {
            return WorldCommandResult.Fail(WorldCommandError.InsufficientMovement);
        }

        WorldPlan plan = WorldPlan.For(state);

        if (plan.GetFortificationAt(stack.Position) is WorldCity or WorldCapital)
        {
            return WorldCommandResult.Fail(WorldCommandError.RodLeaderInCity);
        }

        WorldPlayer player = state.CurrentPlayer;

        if (!ResourceSetCalculator.CanAfford(player.Resources, context.Globals.RodCost))
        {
            return WorldCommandResult.Fail(WorldCommandError.InsufficientResources);
        }

        if (!state.Map.IsValidPosition(TargetPosition))
        {
            return WorldCommandResult.Fail(WorldCommandError.TargetOutOfBounds);
        }

        if (stack.Position.GetDistance(TargetPosition) > context.Globals.RodPlantRange)
        {
            return WorldCommandResult.Fail(WorldCommandError.TargetTooFar);
        }

        if (GetSpotError(state, plan, context.Globals, currentPlayer, TargetPosition) is { } spotError)
        {
            return WorldCommandResult.Fail(spotError);
        }

        WorldRodId rodId = GenerateRodId(state);

        return WorldCommandResult.Success(
            Empty.Value,
            [new WorldRodCreatedEvent(rodId, currentPlayer, TargetPosition), new WorldStackMovementConsumedEvent(StackId, 0)]);
    }

    // Can a rod go on this tile at all (spacing, terrain, free tile, not ally land)? Shared with auto rod
    // (docs/auto-rod.md), which looks for spots before the planter stands next to them.
    internal static WorldCommandError? GetSpotError(WorldState state, WorldPlan plan, GlobalVariables globals, WorldPlayerId playerId, WorldPosition target)
    {
        foreach (WorldRod rod in state.Rods.Values)
        {
            if (rod.Position.GetDistance(target) <= globals.RodSpacingRange)
            {
                return WorldCommandError.RodInvalidLocation;
            }
        }

        WorldMapCell cell = state.Map.GetCellAt(target);

        if (cell.Terrain == WorldMapTerrain.Water || cell.Feature == WorldMapFeature.Mountain)
        {
            return WorldCommandError.TargetUnpassable;
        }

        if (plan.HasStructureAt(target) ||
            plan.GetStackAt(target) is not null ||
            plan.GetBagAt(target) is not null)
        {
            return WorldCommandError.TargetOccupied;
        }

        WorldPlayerId? landOwnerId = plan.GetLandOwner(target);

        if (landOwnerId is not null && landOwnerId != playerId)
        {
            WorldRelation relation = DiplomacyCalculator.GetRelation(state, playerId, landOwnerId.Value);

            if (relation.Alliance)
            {
                return WorldCommandError.RodInvalidLocation;
            }
        }

        return null;
    }

    private static WorldRodId GenerateRodId(WorldState state)
    {
        int index = 1;

        while (state.Rods.ContainsKey(new WorldRodId($"Rod{index:D4}")))
        {
            index++;
        }

        return new WorldRodId($"Rod{index:D4}");
    }
}

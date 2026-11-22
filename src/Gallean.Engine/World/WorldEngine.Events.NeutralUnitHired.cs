using Gallean.Common;

namespace Gallean.Engine;

internal partial class WorldEngine
{
    WorldState IWorldEventVisitor<WorldState, WorldState>.Visit(WorldNeutralUnitHiredEvent ev, WorldState state)
    {
        WorldStack stack = state.Stacks[ev.StackId];
        WorldPlayer player = state.Players[stack.OwnerId];

        WorldStackBuilder stackBuilder = new(Context.Definitions);

        foreach ((WorldUnit unit, GridPosition position) in stack.Units.GetUnits())
        {
            stackBuilder.PlaceUnit(unit, position);
        }

        stackBuilder.PlaceUnit(ev.Unit, ev.Position);

        if (!stackBuilder.TryBuild(out UnitGrid<WorldUnit>? units, out WorldCommandError? error))
        {
            throw new InvalidOperationException($"Unexpected stack composition: {error}.");
        }

        WorldStack updatedStack = stack with
        {
            Units = LeaderEquipmentCalculator.UpdateUnitModifiers(units, stack.Equipment, Context.Definitions)
        };

        return state with
        {
            Players = state.Players.SetItem(player.Id, player with { Resources = ResourceSetCalculator.Subtract(player.Resources, ev.Cost) }),
            Stacks = state.Stacks.SetItem(updatedStack.Id, updatedStack)
        };
    }
}

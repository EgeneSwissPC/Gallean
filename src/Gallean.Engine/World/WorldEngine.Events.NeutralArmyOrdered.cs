namespace Gallean.Engine;

internal partial class WorldEngine
{
    WorldState IWorldEventVisitor<WorldState, WorldState>.Visit(WorldNeutralArmyOrderedEvent ev, WorldState state)
    {
        WorldStack stack = state.Stacks[ev.StackId];

        return state with
        {
            Stacks = state.Stacks.SetItem(stack.Id, stack with { Order = ev.Order, NeutralHomeId = ev.HomeId })
        };
    }
}

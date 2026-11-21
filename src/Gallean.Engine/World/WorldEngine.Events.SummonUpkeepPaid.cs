namespace Gallean.Engine;

internal partial class WorldEngine
{
    WorldState IWorldEventVisitor<WorldState, WorldState>.Visit(WorldSummonUpkeepPaidEvent ev, WorldState state)
    {
        WorldPlayer player = state.Players[ev.PlayerId];
        WorldResourceSet resources = ResourceSetCalculator.Subtract(player.Resources, ev.Resources);

        return state with { Players = state.Players.SetItem(ev.PlayerId, player with { Resources = resources }) };
    }
}

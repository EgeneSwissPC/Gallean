namespace Gallean.Engine;

internal partial class WorldEngine
{
    WorldState IWorldEventVisitor<WorldState, WorldState>.Visit(WorldAiOverrideChangedEvent ev, WorldState state)
    {
        WorldPlayer player = state.Players[ev.PlayerId];

        return state with { Players = state.Players.SetItem(ev.PlayerId, player with { AiOverride = ev.Override }) };
    }
}

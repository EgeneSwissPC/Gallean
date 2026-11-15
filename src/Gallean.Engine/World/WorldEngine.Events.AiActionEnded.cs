namespace Gallean.Engine;

internal partial class WorldEngine
{
    WorldState IWorldEventVisitor<WorldState, WorldState>.Visit(WorldAiActionEndedEvent ev, WorldState state)
    {
        WorldAiMemory memory = ev.Memory;

        // The memory is planned before the action runs, so it still has entries for stacks the action (or an earlier
        // turn) destroyed. Stack ids are never reused, so without pruning these accumulate for the whole scenario.
        if (memory.Stacks.Keys.Any(stackId => !state.Stacks.ContainsKey(stackId)))
        {
            memory = memory with { Stacks = memory.Stacks.RemoveRange(memory.Stacks.Keys.Where(stackId => !state.Stacks.ContainsKey(stackId))) };
        }

        return state with { AiMemories = state.AiMemories.SetItem(ev.PlayerId, memory) };
    }
}

using Gallean.Common;

namespace Gallean.Engine;

// Gallean Upgrade: gives an AI player its own world/battle AI, or (null) makes it follow the
// Settings choice again. Allowed at any time, not only on that player's turn (docs/ai-switching.md).
public sealed record WorldSetAiOverrideCommand(WorldPlayerId PlayerId, WorldAiOverride? Override) : IWorldCommand<Empty>
{
    public WorldCommandResult Execute(WorldState state, IEngineContext context)
    {
        if (!state.Players.TryGetValue(PlayerId, out WorldPlayer? player) || !player.IsAi)
        {
            return WorldCommandResult.Fail(WorldCommandError.AiPlayerRequired);
        }

        WorldAiOverride? normalized = Override is null || Override.IsEmpty ? null : Override;

        if (Equals(player.AiOverride, normalized))
        {
            return WorldCommandResult.Success(Empty.Value, []);
        }

        return WorldCommandResult.Success(Empty.Value, [new WorldAiOverrideChangedEvent(PlayerId, normalized)]);
    }
}

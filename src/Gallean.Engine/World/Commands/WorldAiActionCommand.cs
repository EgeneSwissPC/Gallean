using Gallean.Common;

namespace Gallean.Engine;

internal sealed record WorldAiActionCommand : IWorldCommand<Empty>
{
    // Gallean Upgrade: which AI decides (AiBrainRegistry ID); null = Classic.
    public string? BrainId { get; init; }

    // Gallean Upgrade: keep playing when every player left is an AI (AI tournaments).
    public bool AllowAiOnly { get; init; }

    public WorldCommandResult Execute(WorldState state, IEngineContext context)
    {
        WorldPlayerId playerId = state.TurnInfo.CurrentPlayerId;
        WorldPlayer player = state.CurrentPlayer;

        if (!player.IsAi)
        {
            return WorldCommandResult.Fail(WorldCommandError.NotAiTurn);
        }

        WorldPlan worldPlan = WorldPlan.For(state);

        if (!AllowAiOnly && state.Players.Values.All(candidate => candidate.IsAi || worldPlan.IsPlayerDefeated(candidate.Id)))
        {
            return WorldCommandResult.Fail(WorldCommandError.NoPlayersRemaining);
        }

        WorldAiMemory memory = state.AiMemories.GetValueOrDefault(playerId, WorldAiMemory.Empty);

        // Gallean Upgrade: the AI algorithm is switchable (docs/ai-switching.md).
        WorldAiActionPlan plan = AiBrainRegistry.ResolveWorld(BrainId).DecideAction(context, state, memory);

        WorldCommandResult commandResult = plan.Command.Execute(state, context);
        if (commandResult.Failed)
        {
            return commandResult;
        }

        List<WorldEvent> events =
        [
            new WorldAiActionStartedEvent(plan),
            .. commandResult.Events,
            new WorldAiActionEndedEvent(playerId, plan.Memory)
        ];

        return WorldCommandResult.Success(Empty.Value, events);
    }
}

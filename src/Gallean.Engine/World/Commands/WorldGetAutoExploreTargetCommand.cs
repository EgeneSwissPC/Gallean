using Gallean.Common;
using CommandResult = Gallean.Engine.ICommandResult<Gallean.Engine.WorldCommandError, Gallean.Engine.WorldEvent, Gallean.Engine.AutoExploreResult>;

namespace Gallean.Engine;

// Read only: where an exploring army of the current player goes next (docs/auto-explore.md). PreviousTarget is kept
// while it is still a usable edge of the fog.
public sealed record WorldGetAutoExploreTargetCommand(
    WorldStackId StackId,
    WorldPosition? PreviousTarget = null,
    AutoExploreSettings? Settings = null) : IWorldCommand<AutoExploreResult>
{
    public CommandResult Execute(WorldState state, IEngineContext context)
    {
        if (!WorldCommandHelpers.TryGetCurrentPlayerStack(state, StackId, out WorldStack? stack))
        {
            return CommandResult.Fail(WorldCommandError.StackNotFound);
        }

        AutoExploreResult result = AutoExploreCalculator.FindTarget(state, stack, context, PreviousTarget, Settings ?? AutoExploreSettings.Default);

        return CommandResult.Success(result, []);
    }
}

using Gallean.Common;
using CommandResult = Gallean.Engine.ICommandResult<Gallean.Engine.WorldCommandError, Gallean.Engine.WorldEvent, Gallean.Engine.AutoRodPick>;

namespace Gallean.Engine;

// Read only: the next job of a rod planter of the current player on auto rod (docs/auto-rod.md). SkippedSpots are
// plant spots the player skipped in this run; PreviousExploreTarget is kept while it is still a usable fog edge.
public sealed record WorldGetAutoRodJobCommand(
    WorldStackId StackId,
    IReadOnlySet<WorldPosition>? SkippedSpots = null,
    WorldPosition? PreviousExploreTarget = null,
    AutoRodSettings? Settings = null,
    AutoExploreSettings? ExploreSettings = null) : IWorldCommand<AutoRodPick>
{
    public CommandResult Execute(WorldState state, IEngineContext context)
    {
        if (!WorldCommandHelpers.TryGetCurrentPlayerStack(state, StackId, out WorldStack? stack))
        {
            return CommandResult.Fail(WorldCommandError.StackNotFound);
        }

        AutoRodPick pick = AutoRodCalculator.FindJob(
            state,
            stack,
            context,
            SkippedSpots ?? new HashSet<WorldPosition>(),
            PreviousExploreTarget,
            Settings ?? AutoRodSettings.Default,
            ExploreSettings ?? AutoExploreSettings.Default);

        return CommandResult.Success(pick, []);
    }
}

using Gallean.Common;

namespace Gallean.Engine;

public sealed record BattleAutoResolveCommand : IBattleCommand<Empty>
{
    // Gallean Upgrade: units of this side never flee while the battle is resolved (the
    // "Refuse to flee" battle mode); they keep fighting like in the rest of the auto-resolve.
    public BattleSide? NoRetreatSide { get; init; }

    public BattleCommandResult Execute(BattleState state, IEngineContext context)
    {
        return BattleCommandResult.Success(
            Empty.Value,
            [new BattleAutoResolveStepEvent(state.RoundNumber, NoRetreatSide)]);
    }
}

using Gallean.Common;

namespace Gallean.Engine;

// Gallean Upgrade: CanRetreat = false never flees (the "Refuse to flee" battle mode).
public sealed record BattleAiActionCommand(bool CanUseItems, bool CanRetreat = true) : IBattleCommand<Empty>
{
    public BattleCommandResult Execute(BattleState state, IEngineContext context)
    {
        if (state.Outcome is not null)
        {
            return BattleCommandResult.Fail(BattleCommandError.ActionNotAvailable);
        }

        BattleAiActionPlan plan = new BattleAiPlanner(context, canUseItems: CanUseItems, canRetreat: CanRetreat).DecideAction(state);

        return plan.Command.Execute(state, context);
    }
}

using Gallean.Common;

namespace Gallean.Engine;

// Gallean Upgrade: one turn of the "heal and defend" battle mode. A unit that can heal a
// wounded ally heals the most wounded one; every other unit defends.
public sealed record BattleHealOrDefendCommand(BattleUnitId UnitId) : IBattleCommand<Empty>
{
    public BattleCommandResult Execute(BattleState state, IEngineContext context)
    {
        if (state.Outcome is not null)
        {
            return BattleCommandResult.Fail(BattleCommandError.ActionNotAvailable);
        }

        if (UnitId != state.CurrentUnitId)
        {
            return BattleCommandResult.Fail(BattleCommandError.NotUnitTurn);
        }

        BattleAttackTarget? healTarget = BattleHealModePlanner.FindHealTarget(state, context, UnitId);

        if (healTarget is not null)
        {
            BattleCommandResult heal = new BattleAttackCommand(UnitId, healTarget).Execute(state, context);

            if (!heal.Failed)
            {
                return heal;
            }
        }

        BattleCommandResult defend = new BattleDefendCommand(UnitId).Execute(state, context);

        if (!defend.Failed)
        {
            return defend;
        }

        return new BattleSkipCommand(UnitId).Execute(state, context);
    }
}

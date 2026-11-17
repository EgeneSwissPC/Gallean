using CommandResult = Gallean.Engine.ICommandResult<Gallean.Engine.BattleCommandError, Gallean.Engine.BattleEvent, Gallean.Engine.BattleGetHealModeStatusCommand.Result>;

namespace Gallean.Engine;

// Gallean Upgrade: tells the "heal and defend" battle mode whether healing is still useful
// for one side: someone is wounded and someone who can heal is still standing.
public sealed record BattleGetHealModeStatusCommand(BattleSide Side) : IBattleCommand<BattleGetHealModeStatusCommand.Result>
{
    public sealed record Result(bool IsWounded, bool HasHealer)
    {
        public bool CanKeepHealing => IsWounded && HasHealer;
    }

    public CommandResult Execute(BattleState state, IEngineContext context)
    {
        return CommandResult.Success(
            new Result(
                BattleHealModePlanner.IsWounded(state, context, Side),
                BattleHealModePlanner.HasHealer(state, context, Side)),
            []);
    }
}

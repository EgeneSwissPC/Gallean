using Gallean.Common;

namespace Gallean.Engine;

internal partial class BattleEngine
{
    private sealed class AutoResolveStepRule(IEngineContext context) : BattleEventRule(context)
    {
        private const int RetreatRound = 15;
        private const int DefendRound = 30;
        private const int LimitRound = 45;

        // Gallean Upgrade: a side that refuses to flee keeps fighting past RetreatRound. If the
        // battle is still going after LimitRound (e.g. nobody can hurt the other side), it
        // retreats after all, and the hard limit moves out so that retreat can happen.
        private const int NoRetreatLimitRound = 90;

        private readonly BattleAiPlanner ai = new(context, canUseItems: false);
        private readonly BattleAiPlanner noRetreatAi = new(context, canUseItems: false, canRetreat: false);

        // Gallean Upgrade: each side can use its own battle AI (docs/ai-switching.md).
        private IBattleCommand<Empty> DecideAiAction(BattleState state, string? brainId)
        {
            IBattleAiBrain brain = AiBrainRegistry.ResolveBattle(brainId);

            return brain is ClassicBattleAiBrain
                ? ai.DecideAction(state).Command
                : brain.DecideAction(Context, state, canUseItems: false, canRetreat: true).Command;
        }

        public override EventRuleResult<BattleEvent> Visit(BattleAutoResolveStepEvent ev, BattleState state)
        {
            if (state.Outcome is not null)
            {
                return EventRuleResult<BattleEvent>.Consume();
            }

            if (state.CurrentRound.Type is not BattleRoundType.Recovery &&
                BattleOutcomeCalculator.GetOutcome(state) is not null)
            {
                return EventRuleResult<BattleEvent>.Consume(ev with { });
            }

            int elapsedRounds = state.RoundNumber - ev.StartRoundNumber;
            if (elapsedRounds > (ev.NoRetreatSide is null ? LimitRound : NoRetreatLimitRound))
            {
                throw new InvalidOperationException("Battle auto-resolve failed to complete.");
            }

            BattleUnit currentUnit = state.CurrentUnit;
            IBattleCommand<Empty> command;

            bool refusesToFlee = currentUnit.Side == ev.NoRetreatSide;

            if (refusesToFlee)
            {
                command = elapsedRounds > LimitRound
                    ? new BattleRetreatCommand(currentUnit.Id)
                    : noRetreatAi.DecideAction(state).Command;
            }
            else if (elapsedRounds > RetreatRound && currentUnit.Side == BattleSide.Attacker)
            {
                command = new BattleRetreatCommand(currentUnit.Id);
            }
            else if (elapsedRounds > DefendRound && currentUnit.Side == BattleSide.Defender)
            {
                command = new BattleDefendCommand(currentUnit.Id);
            }
            else
            {
                command = DecideAiAction(state, currentUnit.Side == BattleSide.Attacker ? ev.AttackerAiId : ev.DefenderAiId);
            }

            BattleCommandResult commandResult = command.Execute(state, Context);

            IReadOnlyList<BattleEvent> newEvents;
            if (!commandResult.Failed)
            {
                newEvents = commandResult.Events;
            }
            else
            {
                newEvents = new BattleSkipCommand(currentUnit.Id).Execute(state, Context).Events;
            }

            return EventRuleResult<BattleEvent>.Consume(
            [
                .. newEvents,
                ev with { }
            ]);
        }
    }
}

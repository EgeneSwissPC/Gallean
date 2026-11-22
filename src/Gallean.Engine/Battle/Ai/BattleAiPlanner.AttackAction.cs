using System.Collections.Immutable;

namespace Gallean.Engine;

internal sealed partial class BattleAiPlanner
{
    private sealed partial record AttackAction(IEngineContext Context, int Priority) : AbstractAction(Context, Priority)
    {
        private BattleAiEstimator Estimator { get; } = new(Context);
        private BattleAiTargetProvider TargetProvider { get; } = new(Context);

        // Gallean Upgrade: designed battle AIs (target preferences, protect the leader); Classic = no change.
        public BattleAiProfile Profile { get; init; } = BattleAiProfile.Classic;

        public override BattleAiActionPlan? Plan(BattleState state, BattleUnit unit)
        {
            BattleAttackTarget? bestTarget = null;
            int bestScore = 0;
            BattleAiPreferences? preferences = Profile.HasTargetPreferences || Profile.ProtectLeader ? new BattleAiPreferences(Context, Profile, state, unit) : null;

            foreach ((BattleAttackTarget target, ImmutableArray<UnitAttackDefinition> attacks) in TargetProvider.GetAttackTargets(state, unit))
            {
                int score = ScoreTarget(state, unit, target, attacks);

                if (preferences is not null && score > 0 && target is BattleAttackUnitTarget unitTarget)
                {
                    score = preferences.Apply(score, state.GetUnit(unitTarget.UnitId), attacks[0]);
                }

                if (score > 0 && (bestTarget is null || score > bestScore))
                {
                    bestTarget = target;
                    bestScore = score;
                }
            }

            if (bestTarget is null)
            {
                return null;
            }
            else
            {
                return MakePlan(bestScore, new BattleAttackCommand(unit.Id, bestTarget));
            }
        }

        private int ScoreTarget(
            BattleState state,
            BattleUnit unit,
            BattleAttackTarget target,
            ImmutableArray<UnitAttackDefinition> attacks)
        {
            UnitAttackDefinition primaryAttack = attacks[0];
            UnitAttackTypeInfo primaryAttackInfo = primaryAttack.Type.GetInfo();

            if (primaryAttackInfo is { CanMiss: false, IsWardable: false })
            {
                int score = 0;

                foreach (UnitAttackDefinition attack in attacks)
                {
                    Scorer.Context scorerContext = new(Context, Estimator, state, unit, attack);

                    score += Scorer.Score(target, scorerContext);
                }

                return score;
            }
            else
            {
                Scorer.Context scorerContext = new(Context, Estimator, state, unit, primaryAttack);

                return Scorer.Score(target, scorerContext);
            }
        }
    }
}

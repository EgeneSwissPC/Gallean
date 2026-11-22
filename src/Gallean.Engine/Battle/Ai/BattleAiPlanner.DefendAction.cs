namespace Gallean.Engine;

internal sealed partial class BattleAiPlanner
{
    private sealed record DefendAction(IEngineContext Context, int Priority) : AbstractAction(Context, Priority)
    {
        // Gallean Upgrade: designed battle AIs ("protect the leader": a hurt leader defends).
        public BattleAiProfile Profile { get; init; } = BattleAiProfile.Classic;

        public override BattleAiActionPlan? Plan(BattleState state, BattleUnit unit)
        {
            if (Profile.ProtectLeader && BattleAiPreferences.IsLeader(Context, unit) &&
                unit.Hitpoints * 2 < Context.UnitStats.GetBattleUnitStats(state, unit.Id).MaxHitpoints)
            {
                return MakePlan(MaxScore, new BattleDefendCommand(unit.Id));
            }

            return MakePlan(0, new BattleDefendCommand(unit.Id));
        }
    }
}

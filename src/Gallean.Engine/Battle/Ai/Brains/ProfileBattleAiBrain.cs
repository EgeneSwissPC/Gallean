namespace Gallean.Engine;

// Gallean Upgrade: a battle personality = the Classic battle AI with other knobs (BattleAiProfile).
internal sealed class ProfileBattleAiBrain(AiBrainInfo info, BattleAiProfile profile) : IBattleAiBrain
{
    public AiBrainInfo Info { get; } = info;
    public BattleAiProfile Profile { get; } = profile;

    public BattleAiActionPlan DecideAction(IEngineContext context, BattleState state, bool canUseItems, bool canRetreat)
    {
        return new BattleAiPlanner(context, canUseItems: canUseItems, canRetreat: canRetreat, Profile).DecideAction(state);
    }
}

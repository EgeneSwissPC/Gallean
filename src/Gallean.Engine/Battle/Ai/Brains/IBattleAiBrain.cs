namespace Gallean.Engine;

// Gallean Upgrade: one battle AI algorithm. Picks the action of the unit whose turn it is.
internal interface IBattleAiBrain
{
    AiBrainInfo Info { get; }

    BattleAiActionPlan DecideAction(IEngineContext context, BattleState state, bool canUseItems, bool canRetreat);
}

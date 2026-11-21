namespace Gallean.Engine;

// Gallean Upgrade: the game's own battle AI, unchanged.
internal sealed class ClassicBattleAiBrain : IBattleAiBrain
{
    public static readonly ClassicBattleAiBrain Instance = new();

    private ClassicBattleAiBrain()
    {
    }

    public AiBrainInfo Info { get; } = new(AiBrainRegistry.ClassicId, "Classic", "The original battle AI.");

    public BattleAiActionPlan DecideAction(IEngineContext context, BattleState state, bool canUseItems, bool canRetreat)
    {
        return new BattleAiPlanner(context, canUseItems: canUseItems, canRetreat: canRetreat).DecideAction(state);
    }
}

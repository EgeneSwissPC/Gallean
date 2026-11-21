namespace Gallean.Engine;

public sealed record BattleAutoResolveStepEvent(int StartRoundNumber, BattleSide? NoRetreatSide = null) : BattleEvent
{
    // Gallean Upgrade: the battle AI of each side (AiBrainRegistry ID); null = Classic.
    public string? AttackerAiId { get; init; }
    public string? DefenderAiId { get; init; }

    // Gallean Upgrade: stop after one unit's action instead of resolving the whole battle.
    public bool SingleStep { get; init; }

    public override TResult Accept<TContext, TResult>(IBattleEventVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.Visit(this, context);
    }
}

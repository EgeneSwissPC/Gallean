namespace Gallean.Engine;

// Gallean Upgrade: an AI player's own AI changed (null = follows the Settings choice again).
public sealed record WorldAiOverrideChangedEvent(
    WorldPlayerId PlayerId,
    WorldAiOverride? Override) : WorldEvent
{
    public override TResult Accept<TContext, TResult>(IWorldEventVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.Visit(this, context);
    }
}

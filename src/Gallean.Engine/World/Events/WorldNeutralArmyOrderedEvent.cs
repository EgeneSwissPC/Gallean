namespace Gallean.Engine;

// Gallean Upgrade, neutral armies (docs/town-income.md phase 5): a bought neutral army gets a new order and, after
// capturing a town or losing its village, a new home.
public sealed record WorldNeutralArmyOrderedEvent(
    WorldStackId StackId,
    WorldCanonicalStackOrder Order,
    WorldFortificationId HomeId) : WorldEvent
{
    public override TResult Accept<TContext, TResult>(IWorldEventVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.Visit(this, context);
    }
}

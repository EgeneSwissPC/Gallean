namespace Gallean.Engine;

// Gallean Upgrade, neutral economy (docs/town-income.md phase 4): a neutral side adds a unit to its army waiting at one
// of its villages and pays for it.
public sealed record WorldNeutralUnitHiredEvent(
    WorldStackId StackId,
    WorldUnit Unit,
    GridPosition Position,
    WorldResourceSet Cost) : WorldEvent
{
    public override TResult Accept<TContext, TResult>(IWorldEventVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.Visit(this, context);
    }
}

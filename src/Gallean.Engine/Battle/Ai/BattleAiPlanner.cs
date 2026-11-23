namespace Gallean.Engine;

// Gallean Upgrade: canRetreat = false drops the retreat action (the "Refuse to flee" battle mode).
// Gallean Upgrade: a BattleAiProfile turns the personality's knobs (Classic = original behaviour).
internal sealed partial class BattleAiPlanner(IEngineContext context, bool canUseItems, bool canRetreat = true, BattleAiProfile? profile = null)
{
    private readonly IReadOnlyList<AbstractAction> actions = CreateActions(context, canUseItems, profile ?? BattleAiProfile.Classic)
        .Where(action => (canRetreat && (profile ?? BattleAiProfile.Classic).CanRetreat) || action is not RetreatAction)
        .Where(action => (profile ?? BattleAiProfile.Classic).CanDefend || action is not DefendAction)
        .ToList();

    public BattleAiActionPlan DecideAction(BattleState state)
    {
        BattleUnit unit = state.GetUnit(state.CurrentUnitId);

        if (!unit.IsActive())
        {
            return BattleAiActionPlan.Skip(state.CurrentUnitId);
        }

        BattleAiActionPlan? best = null;

        foreach (AbstractAction action in actions)
        {
            BattleAiActionPlan? plan = action.Plan(state, unit);

            if (plan is not null && (best is null || plan.Value.Score > best.Value.Score))
            {
                best = plan.Value;
            }
        }

        if (best is not null)
        {
            return best.Value;
        }

        return BattleAiActionPlan.Skip(state.CurrentUnitId);
    }

    private static IReadOnlyList<AbstractAction> CreateActions(IEngineContext context, bool canUseItems, BattleAiProfile profile)
    {
        if (canUseItems)
        {
            return
            [
                new RetreatAction(context, Priority: 3) { PowerShare = profile.RetreatPowerShare },
                new HealItemAction(context, Priority: 3),
                new AttackAction(context, Priority: 3) { Profile = profile },
                new WaitAction(context, Priority: 3),
                new DefendAction(context, Priority: 3) { Profile = profile },
                new SkipAction(context, Priority: 1)
            ];
        }
        else
        {
            return
            [
                new RetreatAction(context, Priority: 3) { PowerShare = profile.RetreatPowerShare },
                new AttackAction(context, Priority: 3) { Profile = profile },
                new WaitAction(context, Priority: 3),
                new DefendAction(context, Priority: 3) { Profile = profile },
                new SkipAction(context, Priority: 1)
            ];
        }
    }
}

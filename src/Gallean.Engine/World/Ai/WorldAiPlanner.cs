using System.Collections.Immutable;

namespace Gallean.Engine;

internal sealed partial class WorldAiPlanner(WorldAiContext context)
{
    private readonly WorldAiOrderPlanner orderPlanner = new(context);
    private readonly IReadOnlyList<AbstractStackAction> stackActions = CreateStackActions(context);
    private readonly IReadOnlyList<AbstractEconomyAction> economyActions = CreateEconomyActions(context);

    public WorldAiActionPlan? DecideStackAction(WorldState state, WorldStackId stackId, WorldAiMemory memory)
    {
        WorldStack stack = state.Stacks[stackId];

        WorldStackOrder stackOrder;
        if (stack.Order is WorldStackOrder.Normal)
        {
            stackOrder = memory.GetStackMemory(stack).Order;
        }
        else
        {
            stackOrder = stack.Order;
        }

        ImmutableHashSet<WorldAiTarget> objectiveTargets;
        if (state.TurnInfo.TurnNumber >= GetObjectiveStartTurn(state.Difficulty))
        {
            objectiveTargets = context.Scenario.GetObjectiveTargets(state, stack.OwnerId);
        }
        else
        {
            objectiveTargets = [];
        }

        ImmutableHashSet<WorldAiTarget> blockedTargets = context.Scenario.GetBlockedTargets(state, stack.OwnerId);

        bool isStackVital = context.Scenario.IsStackVital(state, stack);

        (AbstractStackAction Action, WorldAiActionPlan Plan)? best = null;

        foreach (AbstractStackAction action in stackActions)
        {
            WorldAiActionPlan? plan = action.Plan(state, stack, stackOrder, memory, objectiveTargets, blockedTargets, isStackVital);

            context.Trace.EvaluateStackAction(stack, action, plan);

            if (plan is not null && (best is null || plan.Value.Score > best.Value.Plan.Score))
            {
                best = (action, plan.Value);
            }
        }

        if (best is not null && !CanExecutePlan(state, best.Value.Plan))
        {
            best = null;
        }

        // Gallean Upgrade (AI designer): new behaviours may veto or replace the choice (Classic: none).
        if (HasStackBehaviours)
        {
            return ApplyBehaviours(state, stack, best, memory);
        }

        if (best is not null)
        {
            context.Trace.SelectStackAction(stack, best.Value.Action, best.Value.Plan);

            return best.Value.Plan;
        }

        return null;
    }

    public WorldAiMemory DecideStackOrder(WorldState state, WorldStack stack, WorldAiMemory memory)
    {
        if (stack.Order is WorldStackOrder.Normal)
        {
            WorldAiStackMemory stackMemory = memory.GetStackMemory(stack);
            WorldStackOrder stackOrder = orderPlanner.Decide(state, stack, stackMemory.Order);

            return memory.WithStackMemory(stack.Id, stackMemory with { Order = stackOrder });
        }
        else
        {
            return memory;
        }
    }

    public WorldAiActionPlan? DecideEconomyAction(WorldState state, WorldPlayerId playerId, WorldAiMemory memory)
    {
        // Gallean Upgrade (AI designer): a gold reserve is kept out of the economy's reach (Classic: none).
        state = WithoutReserve(state, playerId);

        (AbstractEconomyAction Action, WorldAiActionPlan Plan)? best = null;

        foreach (AbstractEconomyAction action in economyActions)
        {
            WorldAiActionPlan? plan = action.Plan(state, playerId, memory);

            context.Trace.EvaluateEconomyAction(playerId, action, plan);

            if (plan is not null && (best is null || plan.Value.Score > best.Value.Plan.Score))
            {
                best = (action, plan.Value);
            }
        }

        if (best is not null && CanExecutePlan(state, best.Value.Plan))
        {
            context.Trace.SelectEconomyAction(playerId, best.Value.Action, best.Value.Plan);

            return best.Value.Plan;
        }

        return null;
    }

    private bool CanExecutePlan(WorldState state, WorldAiActionPlan plan)
    {
        WorldCommandResult result = plan.Command.Execute(state, context.Engine);

        return !result.Failed;
    }

    private int GetObjectiveStartTurn(WorldDifficulty difficulty)
    {
        return difficulty switch
        {
            WorldDifficulty.Easy => context.Engine.Globals.AiObjectiveTurnEasy,
            WorldDifficulty.Average => context.Engine.Globals.AiObjectiveTurnAverage,
            WorldDifficulty.Hard => context.Engine.Globals.AiObjectiveTurnHard,
            WorldDifficulty.VeryHard => context.Engine.Globals.AiObjectiveTurnVeryHard
        };
    }

    private static IReadOnlyList<AbstractStackAction> CreateStackActions(WorldAiContext context)
    {
        // Gallean Upgrade (AI designer): a profile with Priorities orders and switches off actions.
        System.Collections.Immutable.ImmutableDictionary<WorldAiActionKind, int>? priorities = context.Profile.Priorities;

        if (priorities is not null)
        {
            List<AbstractStackAction> designed = [];

            void Add(WorldAiActionKind kind, Func<int, AbstractStackAction> create)
            {
                if (priorities.TryGetValue(kind, out int priority))
                {
                    designed.Add(create(priority));
                }
            }

            Add(WorldAiActionKind.UseItem, p => new UseItemAction(context, Priority: p));
            Add(WorldAiActionKind.Resurrect, p => new ResurrectAction(context, Priority: p));
            Add(WorldAiActionKind.Heal, p => new HealAction(context, Priority: p));
            Add(WorldAiActionKind.AutoFit, p => new AutoFitAction(context, Priority: p));
            Add(WorldAiActionKind.Attack, p => new AttackAction(context, Priority: p));
            Add(WorldAiActionKind.Thief, p => new ThiefAction(context, Priority: p));
            Add(WorldAiActionKind.Rod, p => new RodAction(context, Priority: p));
            Add(WorldAiActionKind.EnterCity, p => new EnterCityAction(context, Priority: p));
            Add(WorldAiActionKind.TakeBag, p => new TakeBagAction(context, Priority: p));
            Add(WorldAiActionKind.Move, p => new MoveAction(context, Priority: p));
            designed.Add(new ChangeOrderAction(context, Priority: 0));

            return designed;
        }

        return
        [
            new UseItemAction(context, Priority: 6),
            new ResurrectAction(context, Priority: 5),
            new HealAction(context, Priority: 4),
            new AutoFitAction(context, Priority: 3),
            new AttackAction(context, Priority: 2),
            new ThiefAction(context, Priority: 2),
            new RodAction(context, Priority: context.Profile.RodPriority),
            new EnterCityAction(context, Priority: 2),
            new TakeBagAction(context, Priority: 2),
            new MoveAction(context, Priority: 1),
            new ChangeOrderAction(context, Priority: 0)
        ];
    }

    private static IReadOnlyList<AbstractEconomyAction> CreateEconomyActions(WorldAiContext context)
    {
        System.Collections.Immutable.ImmutableDictionary<WorldAiActionKind, int>? priorities = context.Profile.Priorities;

        if (priorities is not null)
        {
            List<AbstractEconomyAction> designed = [];

            void Add(WorldAiActionKind kind, Func<int, AbstractEconomyAction> create)
            {
                if (priorities.TryGetValue(kind, out int priority))
                {
                    designed.Add(create(priority));
                }
            }

            Add(WorldAiActionKind.CastSpell, p => new CastSpellAction(context, Priority: p));
            Add(WorldAiActionKind.ResearchSpell, p => new ResearchSpellAction(context, Priority: p));
            Add(WorldAiActionKind.HireLeader, p => new PurchaseLeaderAction(context, Priority: p));
            Add(WorldAiActionKind.ConstructBuilding, p => new ConstructBuildingAction(context, Priority: p));
            Add(WorldAiActionKind.PurchaseUnit, p => new PurchaseUnitAction(context, Priority: p));

            return designed;
        }

        return
        [
            new CastSpellAction(context, Priority: 4),
            new ResearchSpellAction(context, Priority: 3),
            new PurchaseLeaderAction(context, Priority: 2),
            new ConstructBuildingAction(context, Priority: context.Profile.BuildingPriority),
            new PurchaseUnitAction(context, Priority: context.Profile.UnitPriority)
        ];
    }
}

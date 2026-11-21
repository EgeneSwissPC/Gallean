namespace Gallean.Engine;

// Gallean Upgrade (AI designer, docs/ai-designer.md phase 5): behaviours the Classic AI doesn't have,
// switched on by WorldAiProfile. They act after Classic has chosen an army's action: they can veto it
// (a home guard walking away, an attack before the armies have gathered) and give an army a march when
// it has nothing better to do (back home, towards a friend, towards the enemy capital).
internal sealed partial class WorldAiPlanner
{
    public const int HomeRadius = 4;
    public const int GatherRadius = 4;

    private static readonly WorldAiActionScore BehaviourScore = new(1, 0);

    private bool HasStackBehaviours =>
        context.Profile.KeepArmiesHome > 0 || context.Profile.GatherArmies > 1 || context.Profile.RushEnemyCapital;

    private WorldAiActionPlan? ApplyBehaviours(WorldState state, WorldStack stack, (AbstractStackAction Action, WorldAiActionPlan Plan)? best, WorldAiMemory memory)
    {
        WorldAiProfile profile = context.Profile;

        if (!IsFighter(stack))
        {
            return best?.Plan;
        }

        WorldPlan plan = WorldPlan.For(state);
        WorldCapital? capital = plan.GetCapital(stack.OwnerId);

        // Home guard: stays near the capital.
        if (profile.KeepArmiesHome > 0 && capital is not null && IsHomeGuard(state, stack, capital.Entrance, profile.KeepArmiesHome))
        {
            if (best is { Plan.Command: WorldAiMoveStackCommand move } && move.TargetPosition.GetDistance(capital.Entrance) > HomeRadius)
            {
                best = null;
            }

            if (best is null && stack.Position.GetDistance(capital.Entrance) > HomeRadius)
            {
                return PlanMarch(state, stack, capital.Entrance, memory);
            }

            return best?.Plan;
        }

        // Gather: no attack until enough armies stand together.
        bool gathered = profile.GatherArmies <= 1 || CountArmiesNear(state, stack) >= profile.GatherArmies;

        if (!gathered && best is { Action: AttackAction })
        {
            best = null;
        }

        bool idle = best is null || best.Value.Action is MoveAction or ChangeOrderAction;

        if (idle && !gathered && FindNearestFriend(state, stack) is { } friend)
        {
            return PlanMarch(state, stack, friend, memory) ?? best?.Plan;
        }

        // Rush: march on the nearest known enemy capital.
        if (idle && gathered && profile.RushEnemyCapital && FindEnemyCapital(state, stack) is { } target)
        {
            return PlanMarch(state, stack, target, memory) ?? best?.Plan;
        }

        return best?.Plan;
    }

    private WorldAiActionPlan? PlanMarch(WorldState state, WorldStack stack, WorldPosition target, WorldAiMemory memory)
    {
        WorldPathMap? pathMap = context.Pathfinding.GetPathMap(state, stack);
        WorldPosition? step = pathMap is null ? null : context.Pathfinding.FindMoveTowardCell(state, stack, pathMap, target);

        if (step is null || step.Value == stack.Position)
        {
            return null;
        }

        WorldAiActionPlan march = new(BehaviourScore, new WorldAiMoveStackCommand(stack.Id, step.Value), memory);

        return CanExecutePlan(state, march) ? march : null;
    }

    // Leaders that fight: not rod planters, thieves, summons or illusions.
    private bool IsFighter(WorldStack stack)
    {
        (_, _, UnitDefinition leader) = stack.GetLeader(context.Engine.Definitions);

        return leader.Category is not (UnitCategory.Summon or UnitCategory.Illusion) &&
               leader.LeaderCategory is not (UnitLeaderCategory.Rod or UnitLeaderCategory.Thief);
    }

    private IEnumerable<WorldStack> GetFighters(WorldState state, WorldPlayerId owner)
    {
        return state.Stacks.Values.Where(stack => stack.OwnerId == owner && IsFighter(stack));
    }

    // The N fighting armies nearest the capital entrance (ties by ID, so the choice is stable).
    private bool IsHomeGuard(WorldState state, WorldStack stack, WorldPosition entrance, int count)
    {
        return GetFighters(state, stack.OwnerId)
            .OrderBy(other => other.Position.GetDistance(entrance))
            .ThenBy(other => other.Id.Value, StringComparer.Ordinal)
            .Take(count)
            .Any(other => other.Id == stack.Id);
    }

    private int CountArmiesNear(WorldState state, WorldStack stack)
    {
        return GetFighters(state, stack.OwnerId).Count(other => other.Position.GetDistance(stack.Position) <= GatherRadius);
    }

    private WorldPosition? FindNearestFriend(WorldState state, WorldStack stack)
    {
        return GetFighters(state, stack.OwnerId)
            .Where(other => other.Id != stack.Id && other.Position.GetDistance(stack.Position) > GatherRadius)
            .OrderBy(other => other.Position.GetDistance(stack.Position))
            .ThenBy(other => other.Id.Value, StringComparer.Ordinal)
            .Select(other => (WorldPosition?)other.Position)
            .FirstOrDefault();
    }

    // The nearest hostile capital whose entrance the player has explored.
    private static WorldPosition? FindEnemyCapital(WorldState state, WorldStack stack)
    {
        WorldPlayer player = state.Players[stack.OwnerId];

        return state.Fortifications.Values
            .OfType<WorldCapital>()
            .Where(capital => capital.OwnerId != stack.OwnerId && DiplomacyCalculator.IsHostile(state, stack.OwnerId, capital.OwnerId))
            .Select(capital => capital.Entrance)
            .Where(entrance => player.FogOfWar.Get(entrance.X, entrance.Y))
            .OrderBy(entrance => entrance.GetDistance(stack.Position))
            .Select(entrance => (WorldPosition?)entrance)
            .FirstOrDefault();
    }

    // Gold reserve: economy actions are planned as if that much gold weren't there.
    private WorldState WithoutReserve(WorldState state, WorldPlayerId playerId)
    {
        int reserve = context.Profile.GoldReserve;

        if (reserve <= 0)
        {
            return state;
        }

        WorldPlayer player = state.Players[playerId];
        WorldResourceSet resources = player.Resources with { Gold = Math.Max(0, player.Resources.Gold - reserve) };

        return state with { Players = state.Players.SetItem(playerId, player with { Resources = resources }) };
    }
}

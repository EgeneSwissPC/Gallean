using System.Collections.Immutable;
using Gallean.Common;

namespace Gallean.Engine;

// Gallean Upgrade map rule "neutrals buy units", phase 5 (docs/town-income.md): orders for the armies a neutral side
// bought (WorldStack.NeutralHomeId set), handed out at its turn start right after its purchases. Placed and scripted
// neutral armies keep their own orders.
//   - An army that is not full (leadership left) stays home, or walks back home and waits to be refilled.
//   - A full army hunts hostile armies (other neutral kinds included) within the army radius of its home
//     (DefendLocation around the home village); with none there it goes for a hostile town within the town radius
//     (SecureCity). It only goes when the AI's strength estimate gives it at least the map's margin over a target.
//   - A town it captured becomes its new home; a lost home is replaced by the side's nearest village.
// The orders are set directly, so the scenario-event rule that keeps plain neutrals from SecureCity doesn't apply.
internal static class NeutralArmyPlanner
{
    private const int ReturnRadius = 1;

    public static ScenarioLocationId HomeZoneId(WorldFortificationId homeId)
    {
        return new ScenarioLocationId($"neutral-home:{homeId.Value}");
    }

    public static ImmutableArray<WorldEvent> Plan(WorldState state, WorldPlayer player, IEngineContext context)
    {
        WorldStack[] armies =
        [
            .. state.Stacks.Values
                .Where(stack => stack.OwnerId == player.Id && stack.NeutralHomeId is not null)
                .OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
        ];

        if (armies.Length == 0)
        {
            return [];
        }

        ScenarioDefinition scenario = context.Scenario.Definition;
        WorldAiEstimator estimator = new(context);
        WorldCity[] ownCities = [.. state.Fortifications.Values.OfType<WorldCity>().Where(city => city.OwnerId == player.Id)];
        List<WorldEvent> events = [];

        foreach (WorldStack army in armies)
        {
            WorldFortificationId homeId = army.NeutralHomeId!.Value;

            // A town it was sent to and now owns becomes its home (T2).
            if (army.Order is WorldStackOrder.SecureCity secure &&
                state.Fortifications.GetValueOrDefault(secure.TargetId) is WorldCity captured &&
                captured.OwnerId == player.Id)
            {
                homeId = captured.Id;
            }

            // A lost home: the nearest village the side still owns.
            if (state.Fortifications.GetValueOrDefault(homeId) is not WorldCity home || home.OwnerId != player.Id)
            {
                WorldCity? nearest = ownCities.OrderBy(city => army.Position.GetDistance(city.Entrance)).ThenBy(city => city.Id.Value, StringComparer.Ordinal).FirstOrDefault();

                if (nearest is null)
                {
                    continue;
                }

                home = nearest;
                homeId = nearest.Id;
            }

            WorldCanonicalStackOrder order = Decide(state, player, army, home, scenario, estimator, context);

            if (!Equals(order, army.Order) || homeId != army.NeutralHomeId)
            {
                events.Add(new WorldNeutralArmyOrderedEvent(army.Id, order, homeId));
            }
        }

        return [.. events];
    }

    private static WorldCanonicalStackOrder Decide(
        WorldState state,
        WorldPlayer player,
        WorldStack army,
        WorldCity home,
        ScenarioDefinition scenario,
        WorldAiEstimator estimator,
        IEngineContext context)
    {
        WorldCanonicalStackOrder stayHome = army.Position == home.Entrance
            ? WorldStackOrder.Stand.Instance
            : new WorldStackOrder.MoveToLocation(HomeZoneId(home.Id), home.Entrance, ReturnRadius);

        if (StackCalculator.GetOccupiedLeadership(army.Units, context.Definitions) < StackCalculator.GetTotalLeadership(army.Units, context.Definitions))
        {
            return stayHome;
        }

        WorldPlan plan = WorldPlan.For(state);

        // 1. Hostile armies near home (R1), only beatable ones (S).
        bool huntable = state.Stacks.Values.Any(target =>
            target.OwnerId != player.Id &&
            !target.Concealed &&
            DiplomacyCalculator.IsHostile(state, player.Id, target.OwnerId) &&
            home.Entrance.GetDistance(target.Position) <= scenario.NeutralArmyRadius &&
            plan.GetFortificationAt(target.Position) is null &&
            IsStrongEnough(estimator, WorldCommandHelpers.CreateStackBattleState(state, army, target, context), scenario.NeutralAttackMargin));

        if (huntable)
        {
            return new WorldStackOrder.DefendLocation(HomeZoneId(home.Id), home.Entrance, scenario.NeutralArmyRadius);
        }

        // 2. A hostile town within the town radius: keep the current one while it is still worth it, else the nearest.
        WorldCity[] towns =
        [
            .. state.Fortifications.Values.OfType<WorldCity>()
                .Where(town =>
                    town.OwnerId != player.Id &&
                    DiplomacyCalculator.IsHostile(state, player.Id, town.OwnerId) &&
                    home.Entrance.GetDistance(town.Entrance) <= scenario.NeutralTownRadius)
                .OrderBy(town => army.Position.GetDistance(town.Entrance))
                .ThenBy(town => town.Id.Value, StringComparer.Ordinal)
        ];

        if (army.Order is WorldStackOrder.SecureCity current && towns.FirstOrDefault(town => town.Id == current.TargetId) is { } kept &&
            IsStrongEnough(estimator, WorldCommandHelpers.CreateGarrisonBattleState(state, army, kept, context), scenario.NeutralAttackMargin))
        {
            return army.Order;
        }

        foreach (WorldCity town in towns)
        {
            if (IsStrongEnough(estimator, WorldCommandHelpers.CreateGarrisonBattleState(state, army, town, context), scenario.NeutralAttackMargin))
            {
                return new WorldStackOrder.SecureCity(town.Id);
            }
        }

        return stayHome;
    }

    private static bool IsStrongEnough(WorldAiEstimator estimator, BattleState battle, int margin)
    {
        return estimator.EstimatePower(battle).AtLeast(BattleSide.Attacker, margin);
    }
}

namespace Gallean.Engine;

internal partial class WorldEngine
{
    /// <summary>
    /// Gallean Upgrade map rule "neutrals buy units" (docs/town-income.md phase 4): right after a neutral side's turn
    /// income it buys garrison units, leaders with armies and village upgrades (NeutralEconomyPlanner).
    /// </summary>
    private sealed class TurnResourcesGeneratedNeutralEconomyRule(IEngineContext context) : WorldEventRule(context)
    {
        public override EventRuleResult<WorldEvent> Visit(WorldTurnResourcesGeneratedEvent ev, WorldState state)
        {
            WorldPlayer player = state.Players[ev.PlayerId];

            if (!Context.Scenario.Definition.NeutralBuying || player.Race != WorldRace.Neutral)
            {
                return EventRuleResult<WorldEvent>.Empty;
            }

            return EventRuleResult<WorldEvent>.Append([.. NeutralEconomyPlanner.Plan(state, player, Context), .. NeutralArmyPlanner.Plan(state, player, Context)]);
        }
    }
}

namespace Gallean.Engine;

internal sealed partial class WorldAiPlanner
{
    // Gallean Upgrade, overflow spending phase 3b (docs/ai-overflow-spending.md): while gold is near the cap,
    // upgrade owned cities, lowest level first. Classic never upgrades cities (it never enters spending mode).
    private sealed record UpgradeCityAction(WorldAiContext Context, int Priority) : AbstractEconomyAction(Context, Priority)
    {
        public override WorldAiActionPlan? Plan(WorldState state, WorldPlayerId playerId, WorldAiMemory memory)
        {
            WorldPlayer player = state.Players[playerId];

            if (!WorldAiSpending.IsGoldSpending(Context.Profile, player, Context.Engine.Globals))
            {
                return null;
            }

            IEnumerable<WorldCity> cities = state.Fortifications.Values
                .OfType<WorldCity>()
                .Where(city => city.Owner == playerId)
                .OrderBy(city => city.DefinitionId.Level)
                .ThenBy(city => city.Id.Value, StringComparer.Ordinal);

            foreach (WorldCity city in cities)
            {
                WorldUpgradeCityCommand command = new(city.Id);

                if (!command.Execute(state, Context.Engine).Failed)
                {
                    return MakePlan(MaxScore / 2, command, memory);
                }
            }

            return null;
        }
    }
}

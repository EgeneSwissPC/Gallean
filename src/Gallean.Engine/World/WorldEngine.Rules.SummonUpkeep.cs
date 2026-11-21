namespace Gallean.Engine;

internal partial class WorldEngine
{
    /// <summary>
    /// Gallean Upgrade rule for large maps: right after a player's turn income, every summoned or illusion
    /// army of that player costs its upkeep (the spell's casting cost). Armies are paid in random order
    /// while the player can afford them; the ones that can't be paid are dismissed.
    /// </summary>
    private sealed class TurnResourcesGeneratedSummonUpkeepRule(IEngineContext context) : WorldEventRule(context)
    {
        public override EventRuleResult<WorldEvent> Visit(WorldTurnResourcesGeneratedEvent ev, WorldState state)
        {
            WorldStack[] summons = [.. state.Stacks.Values.Where(stack => stack.OwnerId == ev.PlayerId && stack.SummonUpkeep is not null)];

            if (summons.Length == 0)
            {
                return EventRuleResult<WorldEvent>.Empty;
            }

            Context.Random.Shuffle(summons);

            WorldResourceSet available = state.Players[ev.PlayerId].Resources;
            WorldResourceSet paid = WorldResourceSet.Empty;
            List<WorldEvent> dismissed = [];

            foreach (WorldStack stack in summons)
            {
                WorldResourceSet upkeep = stack.SummonUpkeep!.Value;

                if (ResourceSetCalculator.CanAfford(available, upkeep))
                {
                    available = ResourceSetCalculator.Subtract(available, upkeep);
                    paid = ResourceSetCalculator.Add(paid, upkeep);
                }
                else
                {
                    dismissed.Add(new WorldStackDestroyedEvent(stack.Id, Destroyer: null, DestroyerPlayerId: null));
                }
            }

            List<WorldEvent> events = [];

            if (paid != WorldResourceSet.Empty)
            {
                events.Add(new WorldSummonUpkeepPaidEvent(ev.PlayerId, paid));
            }

            events.AddRange(dismissed);

            return EventRuleResult<WorldEvent>.Append([.. events]);
        }
    }
}

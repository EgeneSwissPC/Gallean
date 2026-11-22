using System.Collections.Immutable;

namespace Gallean.Engine;

// Gallean Upgrade (AI designer): plays a designed map AI. On every decision it finds the active stage (the
// first whose When is true), turns it into a WorldAiProfile and lets the Classic controller play with it.
internal sealed class DesignedWorldAiBrain(DesignedWorldAi design) : IWorldAiBrain
{
    public DesignedWorldAi Design { get; } = design;

    public AiBrainInfo Info { get; } = new(design.Id, design.Name, "Designed in the AI editor.");

    public WorldAiActionPlan DecideAction(IEngineContext context, WorldState state, WorldAiMemory memory, IWorldAiTrace? trace = null)
    {
        (DesignedWorldStage? stage, WorldAiMemory updated, ImmutableSortedSet<WorldFortificationId> owned) = FindStage(state, memory);
        // No stage = Classic, plus overflow spending (every designed AI spends, docs/ai-overflow-spending.md).
        WorldAiProfile profile = stage is null
            ? WorldAiProfile.Classic with { SpendAbovePercent = WorldAiSpending.DefaultSpendAbovePercent, SummonUpkeepPercent = WorldAiSpending.DefaultSummonUpkeepPercent, UseAllSpells = true }
            : DesignedStageEvaluator.ToProfile(stage);

        WorldAiActionPlan plan = new WorldAiController(context, profile).DecideAction(state, updated, trace);

        return plan with { Memory = plan.Memory with { OwnedCities = owned, LostCityTurn = updated.LostCityTurn } };
    }

    // The stage that plays this decision. At the first decision of a turn it notices cities lost since the
    // player's previous turn (the "I lost a city" condition), so the returned memory carries that mark.
    public (DesignedWorldStage? Stage, WorldAiMemory Memory, ImmutableSortedSet<WorldFortificationId> Owned) FindStage(WorldState state, WorldAiMemory memory)
    {
        WorldPlayerId playerId = state.TurnInfo.CurrentPlayerId;
        ImmutableSortedSet<WorldFortificationId> owned = DesignedStageEvaluator.GetOwnedCities(state, playerId);
        bool turnStarted = memory.Turn.TurnNumber != state.TurnInfo.TurnNumber || memory.Turn.PlayerId != playerId;
        int lostCityTurn = memory.LostCityTurn;

        if (turnStarted && memory.OwnedCities is { } before && !before.IsSubsetOf(owned))
        {
            lostCityTurn = state.TurnInfo.TurnNumber;
        }

        WorldAiMemory updated = memory with { LostCityTurn = lostCityTurn };

        return (DesignedStageEvaluator.FindActiveStage(Design, state, playerId, updated), updated, owned);
    }
}

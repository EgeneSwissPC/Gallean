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
        WorldPlayerId playerId = state.TurnInfo.CurrentPlayerId;
        ImmutableSortedSet<WorldFortificationId> owned = DesignedStageEvaluator.GetOwnedCities(state, playerId);
        bool turnStarted = memory.Turn.TurnNumber != state.TurnInfo.TurnNumber || memory.Turn.PlayerId != playerId;
        int lostCityTurn = memory.LostCityTurn;

        if (turnStarted && memory.OwnedCities is { } before && !before.IsSubsetOf(owned))
        {
            lostCityTurn = state.TurnInfo.TurnNumber;
        }

        memory = memory with { LostCityTurn = lostCityTurn };

        DesignedWorldStage? stage = DesignedStageEvaluator.FindActiveStage(Design, state, playerId, memory);
        WorldAiProfile profile = stage is null ? WorldAiProfile.Classic : DesignedStageEvaluator.ToProfile(stage);

        WorldAiActionPlan plan = new WorldAiController(context, profile).DecideAction(state, memory, trace);

        return plan with { Memory = plan.Memory with { OwnedCities = owned, LostCityTurn = lostCityTurn } };
    }
}

namespace Gallean.Engine;

// Gallean Upgrade: one world-map AI algorithm. Picks the next action of the current AI player.
internal interface IWorldAiBrain
{
    AiBrainInfo Info { get; }

    WorldAiActionPlan DecideAction(IEngineContext context, WorldState state, WorldAiMemory memory, IWorldAiTrace? trace = null);
}

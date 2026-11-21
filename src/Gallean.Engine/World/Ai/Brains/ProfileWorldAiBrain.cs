namespace Gallean.Engine;

// Gallean Upgrade: a personality = the Classic world AI with other knobs (WorldAiProfile).
internal sealed class ProfileWorldAiBrain(AiBrainInfo info, WorldAiProfile profile) : IWorldAiBrain
{
    public AiBrainInfo Info { get; } = info;
    public WorldAiProfile Profile { get; } = profile;

    public WorldAiActionPlan DecideAction(IEngineContext context, WorldState state, WorldAiMemory memory, IWorldAiTrace? trace = null)
    {
        return new WorldAiController(context, Profile).DecideAction(state, memory, trace);
    }
}

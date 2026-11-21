namespace Gallean.Engine;

// Gallean Upgrade: the game's own utility AI, unchanged.
internal sealed class ClassicWorldAiBrain : IWorldAiBrain
{
    public static readonly ClassicWorldAiBrain Instance = new();

    private ClassicWorldAiBrain()
    {
    }

    public AiBrainInfo Info { get; } = new(AiBrainRegistry.ClassicId, "Classic", "The original AI: balances economy, armies and exploration.");

    public WorldAiActionPlan DecideAction(IEngineContext context, WorldState state, WorldAiMemory memory, IWorldAiTrace? trace = null)
    {
        return new WorldAiController(context).DecideAction(state, memory, trace);
    }
}

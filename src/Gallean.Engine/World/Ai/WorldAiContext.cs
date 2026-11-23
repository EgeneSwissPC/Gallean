namespace Gallean.Engine;

internal sealed class WorldAiContext(IEngineContext engine, IWorldAiTrace trace, WorldAiProfile? profile = null)
{
    // Gallean Upgrade: the personality's knobs (Classic = original behaviour).
    public WorldAiProfile Profile { get; } = profile ?? WorldAiProfile.Classic;
    public IEngineContext Engine { get; } = engine;
    public IWorldAiTrace Trace { get; } = trace;
    public WorldAiEstimator Estimator { get; } = new(engine);
    public WorldAiPathfinding Pathfinding { get; } = new(engine);
    public WorldAiScenario Scenario { get; } = new(engine);
    public WorldAiSpellcasting Spellcasting { get; } = new(engine);
}

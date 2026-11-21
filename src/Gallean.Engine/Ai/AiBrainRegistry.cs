namespace Gallean.Engine;

// Gallean Upgrade: the selectable world and battle AIs (docs/ai-switching.md).
// Unknown or missing IDs fall back to Classic, so old settings and saves keep working.
public static class AiBrainRegistry
{
    public const string ClassicId = "classic";

    private static readonly IWorldAiBrain[] worldBrains = [ClassicWorldAiBrain.Instance];
    private static readonly IBattleAiBrain[] battleBrains = [ClassicBattleAiBrain.Instance];

    // Brains that tests register to check that a selection reaches the AI. Never listed.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IWorldAiBrain> testWorldBrains = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IBattleAiBrain> testBattleBrains = new();

    public static IReadOnlyList<AiBrainInfo> WorldBrains { get; } = [.. worldBrains.Select(brain => brain.Info)];

    public static IReadOnlyList<AiBrainInfo> BattleBrains { get; } = [.. battleBrains.Select(brain => brain.Info)];

    internal static IWorldAiBrain ResolveWorld(string? id)
    {
        if (id is not null && testWorldBrains.TryGetValue(id, out IWorldAiBrain? testBrain))
        {
            return testBrain;
        }

        return worldBrains.FirstOrDefault(brain => brain.Info.Id == id) ?? ClassicWorldAiBrain.Instance;
    }

    internal static IBattleAiBrain ResolveBattle(string? id)
    {
        if (id is not null && testBattleBrains.TryGetValue(id, out IBattleAiBrain? testBrain))
        {
            return testBrain;
        }

        return battleBrains.FirstOrDefault(brain => brain.Info.Id == id) ?? ClassicBattleAiBrain.Instance;
    }

    internal static void RegisterForTests(IWorldAiBrain brain)
    {
        testWorldBrains[brain.Info.Id] = brain;
    }

    internal static void RegisterForTests(IBattleAiBrain brain)
    {
        testBattleBrains[brain.Info.Id] = brain;
    }
}

namespace Gallean.Engine;

// Gallean Upgrade: the selectable world and battle AIs (docs/ai-switching.md).
// Unknown or missing IDs fall back to Classic, so old settings and saves keep working.
public static class AiBrainRegistry
{
    public const string ClassicId = "classic";

    private static readonly IWorldAiBrain[] worldBrains = [ClassicWorldAiBrain.Instance, .. AiPersonalities.CreateWorldBrains()];
    private static readonly IBattleAiBrain[] battleBrains = [ClassicBattleAiBrain.Instance, .. AiPersonalities.CreateBattleBrains()];

    // Brains that tests register to check that a selection reaches the AI. Never listed.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IWorldAiBrain> testWorldBrains = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IBattleAiBrain> testBattleBrains = new();

    // Gallean Upgrade (AI designer): designed map AIs, replaced as a whole when the files change.
    private static volatile IWorldAiBrain[] designedWorldBrains = [];

    public static IReadOnlyList<AiBrainInfo> WorldBrains => [.. worldBrains.Select(brain => brain.Info), .. designedWorldBrains.Select(brain => brain.Info)];

    public static void SetDesignedWorldAis(IEnumerable<DesignedWorldAi> designs)
    {
        designedWorldBrains = [.. designs.Select(design => (IWorldAiBrain)new DesignedWorldAiBrain(design))];
    }

    // Gallean Upgrade (battle AI designer): designed battle AIs, replaced as a whole when the files change.
    private static volatile IBattleAiBrain[] designedBattleBrains = [];

    public static IReadOnlyList<AiBrainInfo> BattleBrains => [.. battleBrains.Select(brain => brain.Info), .. designedBattleBrains.Select(brain => brain.Info)];

    public static void SetDesignedBattleAis(IEnumerable<DesignedBattleAi> designs)
    {
        designedBattleBrains = [.. designs.Select(design => (IBattleAiBrain)new DesignedBattleAiBrain(design))];
    }

    internal static IWorldAiBrain ResolveWorld(string? id)
    {
        if (id is not null && testWorldBrains.TryGetValue(id, out IWorldAiBrain? testBrain))
        {
            return testBrain;
        }

        if (id is not null && id.StartsWith(DesignedWorldAi.IdPrefix, StringComparison.Ordinal))
        {
            // A designed AI that isn't installed here (deleted, or from another player's save) plays Classic+.
            return designedWorldBrains.FirstOrDefault(brain => brain.Info.Id == id)
                ?? worldBrains.First(brain => brain.Info.Id == AiPersonalities.ClassicPlusId);
        }

        return worldBrains.FirstOrDefault(brain => brain.Info.Id == id) ?? ClassicWorldAiBrain.Instance;
    }

    internal static IBattleAiBrain ResolveBattle(string? id)
    {
        if (id is not null && testBattleBrains.TryGetValue(id, out IBattleAiBrain? testBrain))
        {
            return testBrain;
        }

        if (id is not null && id.StartsWith(DesignedBattleAi.IdPrefix, StringComparison.Ordinal))
        {
            // A designed battle AI that isn't installed here plays Classic.
            return designedBattleBrains.FirstOrDefault(brain => brain.Info.Id == id) ?? ClassicBattleAiBrain.Instance;
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

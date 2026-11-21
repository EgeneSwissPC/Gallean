namespace Gallean.Engine;

// Gallean Upgrade: the selectable world and battle AIs (docs/ai-switching.md).
// Unknown or missing IDs fall back to Classic, so old settings and saves keep working.
public static class AiBrainRegistry
{
    public const string ClassicId = "classic";

    private static readonly IWorldAiBrain[] worldBrains = [ClassicWorldAiBrain.Instance];
    private static readonly IBattleAiBrain[] battleBrains = [ClassicBattleAiBrain.Instance];

    public static IReadOnlyList<AiBrainInfo> WorldBrains { get; } = [.. worldBrains.Select(brain => brain.Info)];

    public static IReadOnlyList<AiBrainInfo> BattleBrains { get; } = [.. battleBrains.Select(brain => brain.Info)];

    internal static IWorldAiBrain ResolveWorld(string? id)
    {
        return worldBrains.FirstOrDefault(brain => brain.Info.Id == id) ?? ClassicWorldAiBrain.Instance;
    }

    internal static IBattleAiBrain ResolveBattle(string? id)
    {
        return battleBrains.FirstOrDefault(brain => brain.Info.Id == id) ?? ClassicBattleAiBrain.Instance;
    }
}

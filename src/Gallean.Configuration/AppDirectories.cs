namespace Gallean.Configuration;

public static class AppDirectories
{
    private static string LocalApplicationData { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);

    // gallean-upgrade keeps its settings, saves, leaders, logs and asset packs apart from the official game
    // (~/.local/share/Gallean), so both can be installed side by side without sharing data.
    public static string Root { get; } = Path.Combine(LocalApplicationData, "GalleanUpgrade");
    public static string Assets { get; } = Path.Combine(Root, "Assets");
    public static string Downloads { get; } = Path.Combine(Root, "Downloads");
    public static string EnhanceCache { get; } = Path.Combine(Root, "Enhance", "Cache");
    public static string EnhanceModels { get; } = Path.Combine(Root, "Enhance", "Models");
    public static string Leaders { get; } = Path.Combine(Root, "Leaders");
    public static string Libraries { get; } = Path.Combine(Root, "Libraries");
    public static string Logs { get; } = Path.Combine(Root, "Logs");
    public static string Maps { get; } = Path.Combine(Root, "Maps");
    public static string OriginalAssets { get; } = Path.Combine(Root, "OriginalAssets");
    public static string Saves { get; } = Path.Combine(Root, "Saves");

    // Gallean Upgrade (AI editor): designed map AIs, one JSON file each.
    public static string MapAis { get; } = Path.Combine(Root, "AIs", "map");

    // Gallean Upgrade (battle AI designer): designed battle AIs, one JSON file each.
    public static string BattleAis { get; } = Path.Combine(Root, "AIs", "battle");

    // Gallean Upgrade (battle sandbox): army presets, one JSON file each.
    public static string BattleArmies { get; } = Path.Combine(Root, "Battles", "armies");
}

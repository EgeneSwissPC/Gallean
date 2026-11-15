namespace Gallean.Configuration;

public static class AppDirectories
{
    // Without SpecialFolderOption.Create, .NET on Linux returns "" when ~/.local/share does not exist yet (fresh
    // accounts, minimal window managers), which made Root the relative path "Gallean" under the working directory.
    private static string LocalApplicationData { get; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);

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
    public static string OriginalAssets { get; } = Path.Combine(Root, "OriginalAssets");
    public static string Saves { get; } = Path.Combine(Root, "Saves");
}

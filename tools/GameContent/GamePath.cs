namespace GameContent;

// Disciples II installs come from Windows (case-insensitive) file systems, so copies on Linux can use any casing
// (e.g. "IMGS/ISOANIM.FF" or "imgs/isoanim.ff"). Resolve each name against what is actually on disk.
public static class GamePath
{
    private static readonly EnumerationOptions CaseInsensitive = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = false,
        IgnoreInaccessible = true
    };

    public static string Resolve(string directory, string name)
    {
        string exact = Path.Combine(directory, name);

        if (Path.Exists(exact) || !Directory.Exists(directory))
        {
            return exact;
        }

        string? match = Directory.EnumerateFileSystemEntries(directory, name, CaseInsensitive)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();

        return match ?? exact;
    }
}

using Gallean.Configuration;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (AI editor, docs/ai-designer.md): the player's designed map AIs. Reload() reads the
// folder and hands them to AiBrainRegistry, so the Settings drop-downs, the AI players panel and the game
// use them. Tests point Directory at a temporary folder.
internal static class DesignedAiLibrary
{
    public static string Directory { get; set; } = AppDirectories.MapAis;

    public static DesignedWorldAiStore Store => new(Directory);

    public static IReadOnlyList<DesignedWorldAi> Reload()
    {
        IReadOnlyList<DesignedWorldAi> designs = Store.LoadAll();
        AiBrainRegistry.SetDesignedWorldAis(designs);

        return designs;
    }

    // A file name for a new AI from its name: letters and digits, dashes for the rest, unique in the folder.
    public static string MakeFileName(string name, IEnumerable<string> existingIds)
    {
        HashSet<string> taken = [.. existingIds.Select(DesignedWorldAiStore.GetFileName)];
        string slug = new([.. name.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);
        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));

        if (slug.Length == 0)
        {
            slug = "ai";
        }

        if (slug.Length > 40)
        {
            slug = slug[..40].TrimEnd('-');
        }

        string candidate = slug;

        for (int i = 2; taken.Contains(candidate); i++)
        {
            candidate = $"{slug}-{i}";
        }

        return candidate;
    }
}

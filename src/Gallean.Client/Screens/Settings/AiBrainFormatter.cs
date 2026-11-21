using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: translated names and descriptions of the selectable AIs (AiBrainRegistry IDs).
internal static class AiBrainFormatter
{
    public static string FormatName(string id)
    {
        return id switch
        {
            AiBrainRegistry.ClassicId => Tr("Classic"),
            _ => id
        };
    }

    public static string FormatWorldDescription(string id)
    {
        return id switch
        {
            AiBrainRegistry.ClassicId => Tr("The original AI: balances economy, armies and exploration."),
            _ => string.Empty
        };
    }

    public static string FormatBattleDescription(string id)
    {
        return id switch
        {
            AiBrainRegistry.ClassicId => Tr("The original battle AI."),
            _ => string.Empty
        };
    }
}

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
            "classic-plus" => Tr("Classic+"),
            "aggressive" => Tr("Aggressive"),
            "builder" => Tr("Builder"),
            "expansionist" => Tr("Expansionist"),
            "cautious" => Tr("Cautious"),
            "ruthless" => Tr("Ruthless"),
            _ => id
        };
    }

    public static string FormatWorldDescription(string id)
    {
        return id switch
        {
            AiBrainRegistry.ClassicId => Tr("The original AI: balances economy, armies and exploration."),
            "classic-plus" => Tr("The original AI, but it no longer waits in the capital to save up for a leader."),
            "aggressive" => Tr("Attacks with less advantage and sends armies out earlier, before they are full or healed."),
            "builder" => Tr("Attacks only with a clear advantage and keeps its armies full and healed."),
            "expansionist" => Tr("Hires leaders early, plants rods first and sends armies out with fewer units."),
            _ => string.Empty
        };
    }

    public static string FormatBattleDescription(string id)
    {
        return id switch
        {
            AiBrainRegistry.ClassicId => Tr("The original battle AI."),
            "cautious" => Tr("Leaders flee as soon as the battle turns against them."),
            "ruthless" => Tr("Never flees and never defends: every unit attacks."),
            _ => string.Empty
        };
    }
}

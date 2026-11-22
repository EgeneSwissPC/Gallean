namespace Gallean.Engine;

// Gallean Upgrade: overflow spending (docs/ai-overflow-spending.md). When a resource is close to the
// MaxResourceAmount cap, our AIs lift Classic's spending limits until it is back below. Classic has
// SpendAbovePercent 0 and never enters spending mode.
internal static class WorldAiSpending
{
    public const int DefaultSpendAbovePercent = 80;

    public static int GetThreshold(WorldAiProfile profile, GlobalVariables globals)
    {
        return globals.MaxResourceAmount * profile.SpendAbovePercent / 100;
    }

    // True when any mana is at or above the threshold.
    public static bool IsManaSpending(WorldAiProfile profile, WorldPlayer player, GlobalVariables globals)
    {
        if (profile.SpendAbovePercent <= 0)
        {
            return false;
        }

        int threshold = GetThreshold(profile, globals);
        WorldResourceSet resources = player.Resources;

        return resources.DeathMana >= threshold ||
               resources.RuneMana >= threshold ||
               resources.LifeMana >= threshold ||
               resources.InfernalMana >= threshold ||
               resources.GroveMana >= threshold;
    }

    // True when the cost uses at least one mana that is at or above the threshold, so paying it helps.
    public static bool UsesOverflowingMana(WorldAiProfile profile, WorldPlayer player, WorldResourceSet cost, GlobalVariables globals)
    {
        if (profile.SpendAbovePercent <= 0)
        {
            return false;
        }

        int threshold = GetThreshold(profile, globals);
        WorldResourceSet resources = player.Resources;

        return (cost.DeathMana > 0 && resources.DeathMana >= threshold) ||
               (cost.RuneMana > 0 && resources.RuneMana >= threshold) ||
               (cost.LifeMana > 0 && resources.LifeMana >= threshold) ||
               (cost.InfernalMana > 0 && resources.InfernalMana >= threshold) ||
               (cost.GroveMana > 0 && resources.GroveMana >= threshold);
    }
}

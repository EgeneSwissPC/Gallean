namespace Gallean.Engine;

internal static class MageSiteCalculator
{
    public sealed record PurchaseInfo(WorldResourceSet Cost, WorldCommandError? Error);

    public static PurchaseInfo GetPurchaseInfo(
        WorldMageSite site,
        WorldStack stack,
        WorldPlayer player,
        WorldSpellDefinitionId spellId,
        IEngineContext context)
    {
        if (!site.Spells.Contains(spellId))
        {
            return new PurchaseInfo(WorldResourceSet.Empty, WorldCommandError.SpellNotAvailable);
        }

        WorldSpellDefinition definition = context.Definitions.GetSpell(spellId);
        WorldResourceSet cost = PurchaseCalculator.GetPurchaseCost(GetListPrice(definition, player, context), stack, context);

        if (player.Spellbook.Acquired.Contains(spellId))
        {
            return new PurchaseInfo(cost, WorldCommandError.SpellAlreadyKnown);
        }

        if (!ResourceSetCalculator.CanAfford(player.Resources, cost))
        {
            return new PurchaseInfo(cost, WorldCommandError.InsufficientResources);
        }

        return new PurchaseInfo(cost, Error: null);
    }

    // The tower's price before leader discounts: the spell's price, plus the map's markup (Gallean Upgrade map rule)
    // when the spell belongs to another race than the buyer's.
    public static WorldResourceSet GetListPrice(WorldSpellDefinition definition, WorldPlayer player, IEngineContext context)
    {
        int markup = context.Scenario.Definition.ForeignSpellMarkup;

        if (markup <= 0 || definition.Race == player.Race)
        {
            return definition.PurchaseCost;
        }

        WorldResourceSet price = definition.PurchaseCost;

        return new WorldResourceSet(
            AddMarkup(price.Gold, markup),
            AddMarkup(price.DeathMana, markup),
            AddMarkup(price.RuneMana, markup),
            AddMarkup(price.LifeMana, markup),
            AddMarkup(price.InfernalMana, markup),
            AddMarkup(price.GroveMana, markup));
    }

    private static int AddMarkup(int amount, int markup)
    {
        return amount + (int)Math.Round(amount * markup / 100.0, MidpointRounding.AwayFromZero);
    }
}

using Gallean.Common;

namespace Gallean.Engine;

// Gallean Upgrade: overflow spending (docs/ai-overflow-spending.md). When a resource is close to the
// MaxResourceAmount cap, our AIs lift Classic's spending limits until it is back below. Classic has
// SpendAbovePercent 0 and never enters spending mode.
internal static class WorldAiSpending
{
    // User decision: start at 70 % of the cap (6999 of 9999), for gold and mana alike.
    public const int DefaultSpendAbovePercent = 70;

    // docs/ai-unused-features.md, answer D: running summon upkeep may take at most half of a mana's income.
    public const int DefaultSummonUpkeepPercent = 50;
    public const int RichSummonUpkeepPercent = 100;

    // True when casting this summon keeps the running summon upkeep of every mana it costs within the profile's
    // share of that mana's income. Always true for Classic (knob 0) and on maps without summon upkeep.
    // User decision (option B): a mana at or above the spending line may carry upkeep up to 100 % of its income.
    public static bool CanAffordSummonUpkeep(WorldAiProfile profile, WorldState state, WorldPlayer player, WorldResourceSet castingCost, IEngineContext context)
    {
        if (profile.SummonUpkeepPercent <= 0 || !LargeMapRules.AppliesTo(state.Map))
        {
            return true;
        }

        WorldResourceSet upkeep = castingCost;

        foreach (WorldStack stack in state.Stacks.Values)
        {
            if (stack.OwnerId == player.Id && stack.SummonUpkeep is { } running)
            {
                upkeep = ResourceSetCalculator.Add(upkeep, running);
            }
        }

        WorldResourceSet income = PlayerIncomeCalculator.GetIncomeInfo(player, state, context).Total;
        WorldResourceSet have = player.Resources;
        int spendingLine = profile.SpendAbovePercent > 0 ? GetThreshold(profile, context.Globals) : int.MaxValue;

        bool Fits(int cost, int total, int perTurn, int amount)
        {
            int percent = amount >= spendingLine ? Math.Max(profile.SummonUpkeepPercent, RichSummonUpkeepPercent) : profile.SummonUpkeepPercent;

            return cost <= 0 || total * 100 <= perTurn * percent;
        }

        return Fits(castingCost.DeathMana, upkeep.DeathMana, income.DeathMana, have.DeathMana) &&
               Fits(castingCost.RuneMana, upkeep.RuneMana, income.RuneMana, have.RuneMana) &&
               Fits(castingCost.LifeMana, upkeep.LifeMana, income.LifeMana, have.LifeMana) &&
               Fits(castingCost.InfernalMana, upkeep.InfernalMana, income.InfernalMana, have.InfernalMana) &&
               Fits(castingCost.GroveMana, upkeep.GroveMana, income.GroveMana, have.GroveMana);
    }

    public static int GetThreshold(WorldAiProfile profile, GlobalVariables globals)
    {
        return globals.MaxResourceAmount * profile.SpendAbovePercent / 100;
    }

    // Phase 2b: the mana types the race lacks for the cheapest level 3 spell it could research (answer H),
    // for our AIs only (SpendAbovePercent > 0; answer G: always, not only while spending). Empty = nothing needed.
    public static IReadOnlySet<WorldResourceType> GetNeededMana(WorldAiProfile profile, WorldPlayer player, IEngineContext context)
    {
        const int Level = 3;

        if (profile.SpendAbovePercent <= 0)
        {
            return EmptyTypes;
        }

        LordDefinition lord = context.Definitions.GetLord(player.LordId);
        int levelLimit = lord.Category switch
        {
            LordCategory.Warrior => context.Globals.LordWarriorSpellResearchLevelLimit,
            LordCategory.Mage => context.Globals.LordMageSpellResearchLevelLimit,
            LordCategory.Guildmaster => context.Globals.LordGuildmasterSpellResearchLevelLimit
        };

        if (levelLimit < Level)
        {
            return EmptyTypes;
        }

        IReadOnlyDictionary<WorldSpellDefinitionId, WorldResourceSet> costs = context.Definitions.GetSpellResearchCosts(player.Race);
        WorldResourceSet? cheapest = null;
        int cheapestTotal = int.MaxValue;

        foreach (WorldSpellDefinitionId spellId in player.Spellbook.Available.Where(id => !player.Spellbook.Acquired.Contains(id)).Order())
        {
            WorldSpellDefinition spell = context.Definitions.GetSpell(spellId);

            if (spell.Level == Level && (spell.AiType != WorldAiSpellType.None || profile.UseAllSpells) &&
                spell.Effect.Accept(WorldAiSpellEffectTypeVisitor.Instance, Empty.Value) is not null && costs.TryGetValue(spellId, out WorldResourceSet cost))
            {
                int total = cost.DeathMana + cost.RuneMana + cost.LifeMana + cost.InfernalMana + cost.GroveMana;

                if (total < cheapestTotal)
                {
                    cheapest = cost;
                    cheapestTotal = total;
                }
            }
        }

        if (cheapest is not { } need)
        {
            return EmptyTypes;
        }

        WorldResourceSet have = player.Resources;
        HashSet<WorldResourceType> types = [];

        AddIfShort(types, WorldResourceType.DeathMana, need.DeathMana, have.DeathMana);
        AddIfShort(types, WorldResourceType.RuneMana, need.RuneMana, have.RuneMana);
        AddIfShort(types, WorldResourceType.LifeMana, need.LifeMana, have.LifeMana);
        AddIfShort(types, WorldResourceType.InfernalMana, need.InfernalMana, have.InfernalMana);
        AddIfShort(types, WorldResourceType.GroveMana, need.GroveMana, have.GroveMana);

        return types;

        static void AddIfShort(HashSet<WorldResourceType> types, WorldResourceType type, int need, int have)
        {
            if (need > 0 && have < need)
            {
                types.Add(type);
            }
        }
    }

    private static readonly IReadOnlySet<WorldResourceType> EmptyTypes = new HashSet<WorldResourceType>();

    // True when gold is at or above the threshold.
    public static bool IsGoldSpending(WorldAiProfile profile, WorldPlayer player, GlobalVariables globals)
    {
        return profile.SpendAbovePercent > 0 && player.Resources.Gold >= GetThreshold(profile, globals);
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

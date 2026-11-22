using System.Collections.Immutable;

namespace Gallean.Engine;

public sealed record ScenarioDefinition(
    ScenarioId Id,
    ScenarioInfoTexts InfoTexts,
    int SuggestedLevel,
    int MaxLeaderLevel,
    int MaxCityLevel,
    ScenarioId? NextScenarioId)
{
    public static ScenarioDefinition Empty { get; } = new(
        new ScenarioId(string.Empty),
        new ScenarioInfoTexts(
            new ScenarioTextId(string.Empty),
            Description: null,
            new ScenarioTextId(string.Empty),
            Briefing: null,
            new ScenarioTextId(string.Empty),
            new ScenarioTextId(string.Empty),
            Creator: null),
        SuggestedLevel: 0,
        MaxLeaderLevel: int.MaxValue,
        MaxCityLevel: 0,
        NextScenarioId: null);

    // Gallean Upgrade map rule (SRules.json): mage towers charge this many percent more for spells of another race
    // than the buyer's. Spells traded in diplomacy keep their list price, so allies are the cheaper source.
    public int ForeignSpellMarkup { get; init; }

    // Gallean Upgrade map rule (SRules.json TOWN_INCOME): gold per turn a town of level 1-5 brings its owner (a capital
    // pays the level-5 value). Empty = off.
    public ImmutableArray<int> TownIncome { get; init; } = [];

    // Gallean Upgrade map rule (SRules.json NEUTRAL_BUYING, docs/town-income.md phase 4): neutral sides spend their
    // gold on garrisons, leaders with armies and village upgrades at their turn start (NeutralEconomyPlanner).
    public bool NeutralBuying { get; init; }

    // Units per village level a neutral side owns before it upgrades villages (NEUTRAL_UNIT_FACTOR).
    public int NeutralUnitFactor { get; init; } = DefaultNeutralUnitFactor;

    public const int DefaultNeutralUnitFactor = 3;

    // Neutral armies (phase 5, NeutralArmyPlanner): a full bought army hunts hostile armies within NeutralArmyRadius
    // tiles of its home village, else goes for a hostile town within NeutralTownRadius, and only when the AI's
    // strength estimate gives it at least NeutralAttackMargin percent of the target's.
    public int NeutralArmyRadius { get; init; } = DefaultNeutralArmyRadius;

    public int NeutralTownRadius { get; init; } = DefaultNeutralTownRadius;

    public int NeutralAttackMargin { get; init; } = DefaultNeutralAttackMargin;

    public const int DefaultNeutralArmyRadius = 10;
    public const int DefaultNeutralTownRadius = 60;
    public const int DefaultNeutralAttackMargin = 140;
}

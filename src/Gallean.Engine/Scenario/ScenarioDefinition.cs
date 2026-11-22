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
}

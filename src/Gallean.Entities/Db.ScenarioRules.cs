using System.Text.Json.Serialization;

namespace Gallean.Entities;

public static partial class Db
{
    // Gallean Upgrade: per-map rules the original format has no field for (written by the map editor; original
    // scenarios have no SRules.json, so every rule keeps its default there).
    [ScenarioDataFile("SRules.json", PerScenario = true)]
    public record ScenarioRules(
        [property: ScenarioKey, JsonPropertyName("NO_SCEN")] ScenarioId ScenarioId,
        [property: DataKey, JsonPropertyName("RULES_ID")] Id RulesId,
        [property: JsonPropertyName("SPELL_MARKUP")] int ForeignSpellMarkup,
        [property: JsonPropertyName("NEUTRAL_KINDS")] bool NeutralKinds,
        // Town income (docs/town-income.md): gold per turn for town levels 1-5, comma-separated; null = off.
        [property: JsonPropertyName("TOWN_INCOME")] string? TownIncome = null,
        // Neutral economy (docs/town-income.md phase 4): neutral sides buy units, leaders and village upgrades.
        [property: JsonPropertyName("NEUTRAL_BUYING")] bool? NeutralBuying = null,
        // Units per village level a neutral side keeps before it upgrades villages; null = 3.
        [property: JsonPropertyName("NEUTRAL_UNIT_FACTOR")] int? NeutralUnitFactor = null
    );
}

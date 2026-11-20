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
        [property: JsonPropertyName("NEUTRAL_KINDS")] bool NeutralKinds
    );
}

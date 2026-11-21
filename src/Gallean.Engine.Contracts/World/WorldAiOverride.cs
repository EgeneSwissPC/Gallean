using System.Text.Json.Serialization;

namespace Gallean.Engine.Contracts;

/// <summary>
/// Gallean Upgrade: an AI player's own world/battle AI (AiBrainRegistry IDs), overriding the
/// Settings choice for this player only. Null parts follow the Settings choice.
/// </summary>
public sealed record WorldAiOverride(string? WorldAi, string? BattleAi)
{
    [JsonIgnore]
    public bool IsEmpty => WorldAi is null && BattleAi is null;
}

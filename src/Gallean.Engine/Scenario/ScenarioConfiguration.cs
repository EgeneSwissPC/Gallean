using System.Collections.Immutable;
using Gallean.Common;

namespace Gallean.Engine;

public sealed record ScenarioConfiguration(
    ImmutableArray<ScenarioPlayerConfiguration> HumanPlayers,
    WorldDifficulty Difficulty,
    ScenarioLeader? Leader)
{
    // Gallean Upgrade: own AIs for computer-played races, chosen on the race screen.
    public ImmutableDictionary<WorldRace, WorldAiOverride> AiOverrides { get; init; } = ImmutableDictionary<WorldRace, WorldAiOverride>.Empty;

    public bool Equals(ScenarioConfiguration? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return HumanPlayers.SequenceEqual(other.HumanPlayers) &&
               Difficulty == other.Difficulty &&
               Equals(Leader, other.Leader) &&
               AiOverrides.Count == other.AiOverrides.Count &&
               AiOverrides.All(pair => other.AiOverrides.TryGetValue(pair.Key, out WorldAiOverride? value) && Equals(value, pair.Value));
    }

    public override int GetHashCode()
    {
        HashCode hash = new();

        hash.AddSequence(HumanPlayers);
        hash.Add(Difficulty);
        hash.Add(Leader);
        hash.Add(AiOverrides.Count);

        return hash.ToHashCode();
    }
}

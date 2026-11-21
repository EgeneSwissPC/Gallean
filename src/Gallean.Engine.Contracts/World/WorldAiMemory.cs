using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Gallean.Common;

namespace Gallean.Engine.Contracts;

[method: JsonConstructor]
public sealed record WorldAiMemory(
    WorldAiTurnMemory Turn,
    ImmutableSortedDictionary<WorldStackId, WorldAiStackMemory> Stacks)
{
    public WorldAiMemory(WorldAiTurnMemory turn)
        : this(turn, ImmutableSortedDictionary<WorldStackId, WorldAiStackMemory>.Empty)
    {
    }

    // Gallean Upgrade (AI designer): the cities the player owned at its last action, and the turn on which it
    // found one of them lost (the "I lost a city" stage condition). Null/0 for AIs that don't use them.
    public ImmutableSortedSet<WorldFortificationId>? OwnedCities { get; init; }
    public int LostCityTurn { get; init; }

    public static WorldAiMemory Empty { get; } = new(
        new WorldAiTurnMemory(0, default),
        ImmutableSortedDictionary<WorldStackId, WorldAiStackMemory>.Empty);

    public bool Equals(WorldAiMemory? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Turn == other.Turn &&
               Stacks.SequenceEqual(other.Stacks) &&
               LostCityTurn == other.LostCityTurn &&
               (OwnedCities is null ? other.OwnedCities is null : other.OwnedCities is not null && OwnedCities.SetEquals(other.OwnedCities));
    }

    public override int GetHashCode()
    {
        HashCode hash = new();

        hash.Add(Turn);
        hash.AddSequence(Stacks);
        hash.Add(LostCityTurn);

        return hash.ToHashCode();
    }
}

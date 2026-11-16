using System.Collections.Immutable;

namespace Gallean.Engine;

public enum CustomMapPlacementError
{
    OutsideMap,
    Blocked,
    Occupied,
    UnknownOwner,
    InvalidLevel,
    LeaderInGarrison,
    GarrisonFull,
    InvalidSlot,
    TooManyGoods
}

// A gold mine or mana source. It pays whoever owns the land under it (WorldPlan.GetLandOwner).
public sealed record CustomMapResource(int Id, WorldPosition Position, WorldResourceType Type);

// A village (WorldCity) of level 1-5. Owner Neutral means the neutral player. The garrison uses army slots
// (GridPosition.ToIndex(), a large unit in its front slot) but holds no leader, and at most Level units, a large
// unit counting twice (GarrisonCalculator).
public sealed record CustomMapVillage(int Id, WorldPosition TopLeft, int Level, WorldRace Owner, string Name, ImmutableArray<CustomMapUnit?> Garrison)
{
    public const int Size = FortificationCalculator.CityFootprint;
    public const int MinLevel = 1;
    public const int MaxLevel = 5;

    public WorldFootprint Footprint => new(TopLeft, Size, Size);

    // The tile an army stationed in the village stands on (FortificationCalculator.GetEntrance).
    public WorldPosition Entrance => new(TopLeft.X + Size - 1, TopLeft.Y + Size - 1);

    public static ImmutableArray<CustomMapUnit?> EmptyGarrison => [.. new CustomMapUnit?[GridPosition.Count]];

    public bool Equals(CustomMapVillage? other)
    {
        return other is not null &&
               Id == other.Id &&
               TopLeft == other.TopLeft &&
               Level == other.Level &&
               Owner == other.Owner &&
               Name == other.Name &&
               Garrison.SequenceEqual(other.Garrison);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();

        hash.Add(Id);
        hash.Add(TopLeft);
        hash.Add(Level);
        hash.Add(Owner);
        hash.Add(Name);

        foreach (CustomMapUnit? unit in Garrison)
        {
            hash.Add(unit);
        }

        return hash.ToHashCode();
    }

    public static CustomMapPlacementError? ValidateGarrison(IReadOnlyList<CustomMapUnit?> slots, int level)
    {
        if (level is < MinLevel or > MaxLevel)
        {
            return CustomMapPlacementError.InvalidLevel;
        }

        return ValidateUnits(slots, level);
    }

    // Garrison rules shared by villages and ruin guards: valid slots, no leaders, at most capacity units with large
    // units counting twice.
    public static CustomMapPlacementError? ValidateUnits(IReadOnlyList<CustomMapUnit?> slots, int capacity)
    {
        if (slots.Count != GridPosition.Count)
        {
            return CustomMapPlacementError.InvalidSlot;
        }

        for (int index = 0; index < GridPosition.Count; index++)
        {
            if (slots[index] is not { } unit)
            {
                continue;
            }

            GridPosition position = GridPosition.FromIndex(index);

            if (unit.IsLarge && (position.IsBackLine || slots[GridPosition.AtBackLine(position.Flank).ToIndex()] is not null))
            {
                return CustomMapPlacementError.InvalidSlot;
            }

            if (unit.IsLeader)
            {
                return CustomMapPlacementError.LeaderInGarrison;
            }
        }

        return CustomMapStack.GetOccupiedLeadership(slots) > capacity ? CustomMapPlacementError.GarrisonFull : null;
    }
}

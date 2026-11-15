using System.Collections.Immutable;

namespace Gallean.Engine;

public enum CustomMapStackError
{
    OutsideMap,
    Blocked,
    Occupied,
    UnknownOwner,
    NoLeader,
    MultipleLeaders,
    LeadershipExceeded,
    InvalidSlot
}

public sealed record CustomMapUnit(UnitDefinitionId TypeId, int Level, int Hitpoints, bool IsLarge, int Leadership, int MovementPoints)
{
    public bool IsLeader => Leadership > 0;

    public static CustomMapUnit From(UnitDefinition definition)
    {
        return new CustomMapUnit(definition.Id, definition.Level, definition.MaxHitpoints, definition.IsLarge, definition.Leadership ?? 0, definition.MovementPoints ?? 20);
    }
}

// An army placed on the map. Slots are indexed by GridPosition.ToIndex(); a large unit sits in its front slot and
// keeps the back slot of the same flank empty. Owner Neutral means the neutral player.
public sealed record CustomMapStack(int Id, WorldPosition Position, WorldRace Owner, ImmutableArray<CustomMapUnit?> Slots)
{
    public CustomMapUnit Leader => Slots.First(unit => unit is { IsLeader: true })!;

    public bool Equals(CustomMapStack? other)
    {
        return other is not null &&
               Id == other.Id &&
               Position == other.Position &&
               Owner == other.Owner &&
               Slots.SequenceEqual(other.Slots);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();

        hash.Add(Id);
        hash.Add(Position);
        hash.Add(Owner);

        foreach (CustomMapUnit? unit in Slots)
        {
            hash.Add(unit);
        }

        return hash.ToHashCode();
    }

    // Same rules as WorldStackBuilder: exactly one leader, and units (large ones count twice) within the leader's
    // leadership.
    public static CustomMapStackError? ValidateSlots(IReadOnlyList<CustomMapUnit?> slots)
    {
        if (slots.Count != GridPosition.Count)
        {
            return CustomMapStackError.InvalidSlot;
        }

        int leaders = 0;
        int occupied = 0;
        int leadership = 0;

        for (int index = 0; index < GridPosition.Count; index++)
        {
            if (slots[index] is not { } unit)
            {
                continue;
            }

            GridPosition position = GridPosition.FromIndex(index);

            if (unit.IsLarge && (position.IsBackLine || slots[GridPosition.AtBackLine(position.Flank).ToIndex()] is not null))
            {
                return CustomMapStackError.InvalidSlot;
            }

            occupied += unit.IsLarge ? GridPosition.LineCount : 1;

            if (unit.IsLeader)
            {
                leaders++;
                leadership = unit.IsLarge ? Math.Max(unit.Leadership, GridPosition.LineCount) : unit.Leadership;
            }
        }

        if (leaders == 0)
        {
            return CustomMapStackError.NoLeader;
        }

        if (leaders > 1)
        {
            return CustomMapStackError.MultipleLeaders;
        }

        return occupied > leadership ? CustomMapStackError.LeadershipExceeded : null;
    }
}

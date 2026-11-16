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

    // The slot holding the unit shown at position (a large unit sits in the front slot of its row).
    public static GridPosition GetUnitSlot(IReadOnlyList<CustomMapUnit?> slots, GridPosition position)
    {
        GridPosition front = GridPosition.AtFrontLine(position.Flank);

        return position.IsBackLine && slots[front.ToIndex()] is { IsLarge: true } ? front : position;
    }

    // Moves the unit at from to to, like rearranging a party in the game: a small unit moves into a free slot or
    // swaps with a small unit; when a large unit is involved the two rows swap places.
    public static CustomMapUnit?[] MoveUnit(IReadOnlyList<CustomMapUnit?> slots, GridPosition from, GridPosition to)
    {
        CustomMapUnit?[] result = [.. slots];
        GridPosition source = GetUnitSlot(slots, from);
        GridPosition target = GetUnitSlot(slots, to);
        CustomMapUnit? moved = slots[source.ToIndex()];
        CustomMapUnit? other = slots[target.ToIndex()];

        if (moved is null || source == target)
        {
            return result;
        }

        if (moved.IsLarge || other is { IsLarge: true })
        {
            if (source.Flank != target.Flank)
            {
                foreach (int line in new[] { GridPosition.FrontLine, GridPosition.BackLine })
                {
                    int a = new GridPosition(line, source.Flank).ToIndex();
                    int b = new GridPosition(line, target.Flank).ToIndex();
                    (result[a], result[b]) = (slots[b], slots[a]);
                }
            }

            return result;
        }

        result[target.ToIndex()] = moved;
        result[source.ToIndex()] = other;

        return result;
    }

    // Places a unit at position; a large unit takes the whole row and needs both of its slots free.
    public static CustomMapUnit?[]? TryPlaceUnit(IReadOnlyList<CustomMapUnit?> slots, GridPosition position, CustomMapUnit unit)
    {
        CustomMapUnit?[] result = [.. slots];

        if (unit.IsLarge)
        {
            GridPosition front = GridPosition.AtFrontLine(position.Flank);
            GridPosition back = GridPosition.AtBackLine(position.Flank);

            if (slots[front.ToIndex()] is not null || slots[back.ToIndex()] is not null)
            {
                return null;
            }

            result[front.ToIndex()] = unit;
            return result;
        }

        GridPosition slot = GetUnitSlot(slots, position);

        if (slots[slot.ToIndex()] is not null)
        {
            return null;
        }

        result[slot.ToIndex()] = unit;
        return result;
    }

    public static int GetOccupiedLeadership(IReadOnlyList<CustomMapUnit?> slots)
    {
        return slots.Sum(unit => unit is null ? 0 : unit.IsLarge ? GridPosition.LineCount : 1);
    }

    public static int GetLeadership(IReadOnlyList<CustomMapUnit?> slots)
    {
        CustomMapUnit? leader = slots.FirstOrDefault(unit => unit is { IsLeader: true });

        return leader is null ? 0 : leader.IsLarge ? Math.Max(leader.Leadership, GridPosition.LineCount) : leader.Leadership;
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

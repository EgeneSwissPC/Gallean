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
    InvalidSlot,
    VillageOwner,
    InvalidLevel,
    Guardian,

    // A sea creature leads the army (Mermaid, Sea Serpent, Kraken): it lives in open water only, so its tile and the
    // eight around it must be water.
    OpenWater
}

// A unit placed in an army, garrison or ruin. Level starts at the unit's own level and can go up to MaxLevel; hit
// points and movement are the unit's full values at that level. A guardian only ever stands in its capital. A
// water-only unit (the sea creatures' leaders) moves on open water only when it leads an army.
public sealed record CustomMapUnit(UnitDefinitionId TypeId, int Level, int Hitpoints, bool IsLarge, int Leadership, int MovementPoints, bool IsGuardian = false, bool IsWaterOnly = false)
{
    // The highest level used in the original scenarios.
    public const int MaxLevel = 99;

    // Guardians have leadership in the data but never lead an army.
    public bool IsLeader => Leadership > 0 && !IsGuardian;

    public static CustomMapUnit From(UnitDefinition definition)
    {
        return new CustomMapUnit(
            definition.Id,
            definition.Level,
            definition.MaxHitpoints,
            definition.IsLarge,
            definition.Leadership ?? 0,
            definition.MovementPoints ?? 20,
            definition.Category == UnitCategory.Guardian,
            definition.IsWaterOnly);
    }

    // The unit at a level from its own level up to MaxLevel, grown like a unit levelling up in the game (dynamic
    // upgrades): full hit points and, for leaders, movement at that level.
    public static CustomMapUnit From(UnitDefinition definition, int level, IGameDefinitionProvider definitions)
    {
        int clamped = Math.Clamp(level, definition.Level, MaxLevel);

        if (clamped == definition.Level)
        {
            return From(definition);
        }

        GlobalVariables globals = new();
        UnitStats stats = UnitStatsCalculator.GetStats(definition, clamped, [], definitions, globals);
        int movement = definition.MovementPoints is null
            ? 20
            : UnitStatsCalculator.GetMaxMovementPoints(definition, clamped, [], definitions, globals);

        return From(definition) with { Level = clamped, Hitpoints = stats.MaxHitpoints, MovementPoints = movement };
    }
}

// An army placed on the map. Slots are indexed by GridPosition.ToIndex(); a large unit sits in its front slot and
// keeps the back slot of the same flank empty. Owner Neutral means the neutral player.
public sealed record CustomMapStack(int Id, WorldPosition Position, WorldRace Owner, ImmutableArray<CustomMapUnit?> Slots)
{
    public CustomMapUnit Leader => Slots.First(unit => unit is { IsLeader: true })!;

    // Led by a sea creature: the game moves it on open water only (WorldMovementCalculator).
    public bool IsSeaArmy => IsLedBySeaCreature(Slots);

    public static bool IsLedBySeaCreature(IReadOnlyList<CustomMapUnit?> slots)
    {
        return slots.Any(unit => unit is { IsLeader: true, IsWaterOnly: true });
    }

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

            if (unit.Level is < 1 or > CustomMapUnit.MaxLevel)
            {
                return CustomMapStackError.InvalidLevel;
            }

            if (unit.IsGuardian)
            {
                return CustomMapStackError.Guardian;
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

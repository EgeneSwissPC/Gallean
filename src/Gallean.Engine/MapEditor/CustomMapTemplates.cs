using System.Collections.Immutable;
using Gallean.Common;

namespace Gallean.Engine;

// Army templates: armies that are not on the map until an event creates them ("create [template] at [zone]"), like the
// original maps' SStkTemp rows. A template has an owner race (Neutral for monsters), the same six slots as an army
// (one leader, leadership rules) and the order the created army gets. Events can also point at every army made from a
// template ("remove the raiders", "the raiders attack").
public sealed partial class CustomMap
{
    public CustomMapArmyTemplate? GetTemplate(int id)
    {
        return templates.FirstOrDefault(template => template.Id == id);
    }

    // Adds a template with a new ID (the given ID is ignored) and returns it.
    public CustomMapArmyTemplate AddTemplate(CustomMapArmyTemplate template)
    {
        int id = templates.Count == 0 ? 1 : templates.Max(existing => existing.Id) + 1;
        CustomMapArmyTemplate added = template with { Id = id, Name = string.IsNullOrWhiteSpace(template.Name) ? $"Army template {id}" : template.Name.Trim() };
        templates.Add(added);

        return added;
    }

    public bool UpdateTemplate(CustomMapArmyTemplate template)
    {
        int index = templates.FindIndex(existing => existing.Id == template.Id);

        if (index < 0)
        {
            return false;
        }

        templates[index] = template;

        return true;
    }

    public bool RemoveTemplate(int id)
    {
        return templates.RemoveAll(template => template.Id == id) > 0;
    }

    // Why a template can't be saved (the event using it then is left out too): null = fine.
    public CustomMapStackError? GetTemplateError(CustomMapArmyTemplate template)
    {
        if (template.Owner != WorldRace.Neutral && players.All(player => player.Race != template.Owner))
        {
            return CustomMapStackError.UnknownOwner;
        }

        return CustomMapStack.ValidateSlots(template.Slots);
    }

    // Saved only with a valid army and an order whose target exists (the game's loader refuses a missing target).
    public bool IsTemplateSaveable(CustomMapArmyTemplate template)
    {
        return GetTemplateError(template) is null && OrderTargetExists(template.Order);
    }

    private bool OrderTargetExists(CustomMapArmyOrder order)
    {
        return order.Kind switch
        {
            CustomMapOrderKind.MoveToZone or CustomMapOrderKind.DefendZone => GetLocation(order.LocationId) is not null,
            CustomMapOrderKind.DefendCity => order.City is { } city && CityExists(city),
            CustomMapOrderKind.AttackArmy => stacks.Any(stack => stack.Id == order.ArmyId),
            _ => true
        };
    }

    private bool ArmyRefExists(CustomMapArmyRef army, bool triggererAllowed)
    {
        return army switch
        {
            CustomMapArmyRef.Army placed => stacks.Any(stack => stack.Id == placed.Id),
            CustomMapArmyRef.Template template => GetTemplate(template.Id) is { } found && IsTemplateSaveable(found),
            CustomMapArmyRef.Triggerer => triggererAllowed,
            _ => false
        };
    }
}

public enum CustomMapOrderKind
{
    Normal,
    Stand,
    Guard,
    Roam,
    Berserk,
    Explore,
    MoveToZone,
    DefendZone,
    DefendCity,
    AttackArmy
}

// An army's order, as the game's WorldStackOrder: the target field used depends on the kind.
public sealed record CustomMapArmyOrder(CustomMapOrderKind Kind, int LocationId = 0, CustomMapCity? City = null, int ArmyId = 0)
{
    public static CustomMapArmyOrder Normal { get; } = new(CustomMapOrderKind.Normal);
}

// An army template. Slots are indexed like CustomMapStack's (GridPosition.ToIndex()).
public sealed record CustomMapArmyTemplate(int Id, string Name, WorldRace Owner, ImmutableArray<CustomMapUnit?> Slots, CustomMapArmyOrder Order)
{
    public bool Equals(CustomMapArmyTemplate? other)
    {
        return other is not null && Id == other.Id && Name == other.Name && Owner == other.Owner && Slots.SequenceEqual(other.Slots) && Order == other.Order;
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Id);
        hash.Add(Name);
        hash.Add(Owner);
        hash.AddSequence(Slots);
        hash.Add(Order);

        return hash.ToHashCode();
    }
}

// What an army effect acts on: a placed army, every army made from a template, or the army that fired the event.
public abstract record CustomMapArmyRef
{
    public sealed record Army(int Id) : CustomMapArmyRef;

    public sealed record Template(int Id) : CustomMapArmyRef;

    public sealed record Triggerer : CustomMapArmyRef;
}

// Creates an army from the template on a free tile of the zone.
public sealed record CustomMapCreateArmyEffect(int TemplateId, int LocationId) : CustomMapEffect;

public sealed record CustomMapRemoveArmyEffect(CustomMapArmyRef Army) : CustomMapEffect;

// Moves the army (or the one that fired the event) to a free tile of the zone at once.
public sealed record CustomMapMoveArmyEffect(CustomMapArmyRef Army, int LocationId) : CustomMapEffect;

// Moves the army next to the army that fired the event.
public sealed record CustomMapMoveBesideEffect(CustomMapArmyRef Army) : CustomMapEffect;

// The army changes sides: to a race (its first player) or, NewOwner null, to the player who fired the event.
public sealed record CustomMapChangeOwnerEffect(CustomMapArmyRef Army, WorldRace? NewOwner) : CustomMapEffect;

public sealed record CustomMapChangeOrderEffect(CustomMapArmyRef Army, CustomMapArmyOrder Order) : CustomMapEffect;

// The army attacks the army that fired the event (a battle starts; the event chain stops there).
public sealed record CustomMapBattleEffect(CustomMapArmyRef Army) : CustomMapEffect;

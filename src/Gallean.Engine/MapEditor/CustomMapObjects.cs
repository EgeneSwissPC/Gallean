using System.Collections.Immutable;

namespace Gallean.Engine;

public enum CustomMapSiteType
{
    Merchant,
    MageTower,
    Mercenaries,
    Trainer
}

public sealed record CustomMapMerchantItem(ItemDefinitionId Item, int Count);

public sealed record CustomMapMercenary(UnitDefinitionId Unit, int Level);

// A ruin (WorldRuin): its guards are fought for its gold and item. Guards follow the garrison rules (no leaders,
// large units count twice) with room for six.
public sealed record CustomMapRuin(int Id, WorldPosition TopLeft, int Variant, string Name, int Gold, ItemDefinitionId? Item, ImmutableArray<CustomMapUnit?> Guards)
{
    public const int Size = FortificationCalculator.RuinFootprint;
    public const int VariantCount = 11;
    public const int GuardCapacity = 6;
    public const int MaxGold = 9999;

    public WorldFootprint Footprint => new(TopLeft, Size, Size);

    public bool Equals(CustomMapRuin? other)
    {
        return other is not null &&
               Id == other.Id &&
               TopLeft == other.TopLeft &&
               Variant == other.Variant &&
               Name == other.Name &&
               Gold == other.Gold &&
               Item == other.Item &&
               Guards.SequenceEqual(other.Guards);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();

        hash.Add(Id);
        hash.Add(TopLeft);
        hash.Add(Variant);
        hash.Add(Name);
        hash.Add(Gold);
        hash.Add(Item);

        foreach (CustomMapUnit? unit in Guards)
        {
            hash.Add(unit);
        }

        return hash.ToHashCode();
    }
}

// A treasure bag lying on one tile, on land or water.
public sealed record CustomMapBag(int Id, WorldPosition Position, int Variant, ImmutableArray<ItemDefinitionId> Items)
{
    public const int VariantCount = 8;
    public const int MaxItems = 16;

    public bool Equals(CustomMapBag? other)
    {
        return other is not null &&
               Id == other.Id &&
               Position == other.Position &&
               Variant == other.Variant &&
               Items.SequenceEqual(other.Items);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();

        hash.Add(Id);
        hash.Add(Position);
        hash.Add(Variant);

        foreach (ItemDefinitionId item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}

// A landmark (WorldLandmark): decoration from the game's GLmark list (cemeteries, houses, statues, rocks...).
// Like in the game it blocks movement on all its tiles. It may stand on land or water (the game then draws its
// water sprite); landmarks of the mountain type also make their tiles count as mountains when the map loads.
public sealed record CustomMapLandmark(int Id, WorldPosition TopLeft, WorldLandmarkDefinitionId Type, int Width, int Height)
{
    public WorldFootprint Footprint => new(TopLeft, Width, Height);
}

// A merchant (Items), mage tower (Spells), mercenary camp (Units) or trainer (nothing for sale).
public sealed record CustomMapSite(
    int Id,
    WorldPosition TopLeft,
    CustomMapSiteType Type,
    int Variant,
    string Name,
    string Greeting,
    ImmutableArray<CustomMapMerchantItem> Items,
    ImmutableArray<WorldSpellDefinitionId> Spells,
    ImmutableArray<CustomMapMercenary> Units)
{
    public const int Size = FortificationCalculator.SiteFootprint;
    public const int MaxGoods = 30;

    public WorldFootprint Footprint => new(TopLeft, Size, Size);

    public static int GetVariantCount(CustomMapSiteType type)
    {
        return type switch
        {
            CustomMapSiteType.Merchant => 8,
            CustomMapSiteType.Mercenaries => 5,
            _ => 4
        };
    }

    public bool Equals(CustomMapSite? other)
    {
        return other is not null &&
               Id == other.Id &&
               TopLeft == other.TopLeft &&
               Type == other.Type &&
               Variant == other.Variant &&
               Name == other.Name &&
               Greeting == other.Greeting &&
               Items.SequenceEqual(other.Items) &&
               Spells.SequenceEqual(other.Spells) &&
               Units.SequenceEqual(other.Units);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();

        hash.Add(Id);
        hash.Add(TopLeft);
        hash.Add(Type);
        hash.Add(Variant);
        hash.Add(Name);
        hash.Add(Greeting);
        hash.Add(Items.Length);
        hash.Add(Spells.Length);
        hash.Add(Units.Length);

        return hash.ToHashCode();
    }
}

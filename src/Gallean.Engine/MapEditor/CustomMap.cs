using System.Collections.Immutable;
using Gallean.Common;

namespace Gallean.Engine;

public enum CustomMapBrush
{
    Land,
    Water,
    Forest,
    Mountain,
    Clear
}

public sealed record CustomMapPlayer(WorldRace Race, WorldPosition? Capital);

public sealed record CustomMapMountain(int Id, WorldPosition TopLeft, int Size, int Variant)
{
    public WorldFootprint Footprint => new(TopLeft, Size, Size);
}

// The map editor's document: terrain, mountains, one player per race with its capital, armies, villages, resources,
// ruins, treasure bags and sites. It is saved as a normal scenario (CustomMapWriter) and read back through the
// regular scenario loader (CustomMapStore). Capitals, villages, ruins and sites are structures: their tiles are
// protected from brushes and they never overlap. An army may stand on a village's entrance: it is stationed there.
public sealed class CustomMap
{
    public const int MinSize = 16;
    public const int MaxSize = 1024;
    public const int CapitalSize = FortificationCalculator.CapitalFootprint;
    public const int ForestVariantCount = 25;

    private static readonly ImmutableArray<int> MountainSizes = [1, 2, 3, 5];

    private readonly WorldMapCell[] cells;
    private readonly List<CustomMapMountain> mountains;
    private readonly List<CustomMapPlayer> players;
    private readonly List<CustomMapStack> stacks;
    private readonly List<CustomMapVillage> villages;
    private readonly List<CustomMapResource> resources;
    private readonly List<CustomMapRuin> ruins;
    private readonly List<CustomMapBag> bags;
    private readonly List<CustomMapSite> sites;

    public CustomMap(
        string id,
        string name,
        string description,
        int size,
        IEnumerable<WorldMapCell> cells,
        IEnumerable<CustomMapMountain> mountains,
        IEnumerable<CustomMapPlayer> players,
        IEnumerable<CustomMapStack>? stacks = null,
        IEnumerable<CustomMapVillage>? villages = null,
        IEnumerable<CustomMapResource>? resources = null,
        IEnumerable<CustomMapRuin>? ruins = null,
        IEnumerable<CustomMapBag>? bags = null,
        IEnumerable<CustomMapSite>? sites = null)
    {
        if (size is < MinSize or > MaxSize)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, $"Map size must be between {MinSize} and {MaxSize}.");
        }

        Id = id;
        Name = name;
        Description = description;
        Size = size;
        this.cells = [.. cells];
        this.mountains = [.. mountains];
        this.players = [.. players];
        this.stacks = [.. stacks ?? []];
        this.villages = [.. villages ?? []];
        this.resources = [.. resources ?? []];
        this.ruins = [.. ruins ?? []];
        this.bags = [.. bags ?? []];
        this.sites = [.. sites ?? []];

        if (this.cells.Length != size * size)
        {
            throw new ArgumentException($"Expected {size * size} cells.", nameof(cells));
        }
    }

    public string Id { get; }
    public string Name { get; set; }
    public string Description { get; set; }
    public int Size { get; }
    public IReadOnlyList<WorldMapCell> Cells => cells;
    public IReadOnlyList<CustomMapMountain> Mountains => mountains;
    public IReadOnlyList<CustomMapPlayer> Players => players;
    public IReadOnlyList<CustomMapStack> Stacks => stacks;
    public IReadOnlyList<CustomMapVillage> Villages => villages;
    public IReadOnlyList<CustomMapResource> Resources => resources;
    public IReadOnlyList<CustomMapRuin> Ruins => ruins;
    public IReadOnlyList<CustomMapBag> Bags => bags;
    public IReadOnlyList<CustomMapSite> Sites => sites;

    public static CustomMap Create(string id, string name, int size, WorldMapTerrain baseTerrain, IEnumerable<WorldRace> races)
    {
        List<CustomMapPlayer> players = [];

        foreach (WorldRace race in races)
        {
            if (!WorldHelpers.IsRacePlayable(race))
            {
                throw new ArgumentException($"Race '{race}' is not playable.", nameof(races));
            }

            if (players.Any(player => player.Race == race))
            {
                throw new ArgumentException($"Race '{race}' was added twice.", nameof(races));
            }

            players.Add(new CustomMapPlayer(race, null));
        }

        if (players.Count == 0)
        {
            throw new ArgumentException("A map needs at least one playable race.", nameof(races));
        }

        WorldMapCell cell = new(baseTerrain, WorldMapFeature.None, new WorldObjectVariant(0));

        return new CustomMap(id, name, string.Empty, size, Enumerable.Repeat(cell, size * size), [], players);
    }

    public CustomMap Clone()
    {
        return new CustomMap(Id, Name, Description, Size, cells, mountains, players, stacks, villages, resources, ruins, bags, sites);
    }

    public WorldMapCell GetCell(WorldPosition position)
    {
        return cells[(position.Y * Size) + position.X];
    }

    public bool IsValidPosition(WorldPosition position)
    {
        return position.X >= 0 && position.Y >= 0 && position.X < Size && position.Y < Size;
    }

    public WorldMap ToWorldMap()
    {
        return new WorldMap(Size, Size, [.. cells]);
    }

    public static IEnumerable<WorldPosition> GetBrushArea(WorldPosition center, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if ((dx * dx) + (dy * dy) <= (radius * radius) + radius)
                {
                    yield return new WorldPosition(center.X + dx, center.Y + dy);
                }
            }
        }
    }

    // Applies a brush around center. Capitals and villages are protected: tiles under them are never changed.
    // Resources keep their tile free of water, forest and mountains, but the land under them can be repainted (that
    // decides who earns from them). Returns true when anything changed.
    public bool Paint(WorldPosition center, int radius, CustomMapBrush brush, WorldMapTerrain landTerrain, Random random)
    {
        if (brush == CustomMapBrush.Mountain)
        {
            return PlaceMountains(center, radius, random);
        }

        bool changed = false;

        foreach (WorldPosition position in GetBrushArea(center, radius))
        {
            if (!IsValidPosition(position) || IsUnderStructure(position) ||
                (brush == CustomMapBrush.Water && (GetStackAt(position) is not null || GetResourceAt(position) is not null)) ||
                (brush == CustomMapBrush.Forest && (GetResourceAt(position) is not null || GetBagAt(position) is not null)))
            {
                continue;
            }

            if (brush is CustomMapBrush.Water or CustomMapBrush.Clear)
            {
                changed |= RemoveMountainsAt(position);
            }

            int index = (position.Y * Size) + position.X;
            WorldMapCell cell = cells[index];
            WorldMapCell updated = brush switch
            {
                CustomMapBrush.Land when cell.Terrain == WorldMapTerrain.Water => new WorldMapCell(landTerrain, WorldMapFeature.None, new WorldObjectVariant(0)),
                CustomMapBrush.Land => cell with { Terrain = landTerrain },
                CustomMapBrush.Water => new WorldMapCell(WorldMapTerrain.Water, WorldMapFeature.None, new WorldObjectVariant(0)),
                CustomMapBrush.Forest when cell.Terrain != WorldMapTerrain.Water && cell.Feature != WorldMapFeature.Mountain && cell.Feature != WorldMapFeature.Forest =>
                    cell with { Feature = WorldMapFeature.Forest, FeatureVariant = new WorldObjectVariant(random.Next(ForestVariantCount)) },
                CustomMapBrush.Clear when cell.Feature == WorldMapFeature.Forest => cell with { Feature = WorldMapFeature.None, FeatureVariant = new WorldObjectVariant(0) },
                _ => cell
            };

            if (updated != cells[index])
            {
                cells[index] = updated;
                changed = true;
            }
        }

        return changed;
    }

    // Places the capital of a race with its top-left corner at topLeft. The footprint must be inside the map and
    // must not overlap another capital; terrain under it becomes the race's land and loses forests and mountains.
    public bool PlaceCapital(WorldRace race, WorldPosition topLeft)
    {
        int playerIndex = players.FindIndex(player => player.Race == race);

        if (playerIndex < 0)
        {
            return false;
        }

        WorldFootprint footprint = new(topLeft, CapitalSize, CapitalSize);

        if (!IsValidPosition(footprint.TopLeft) || !IsValidPosition(new WorldPosition(topLeft.X + CapitalSize - 1, topLeft.Y + CapitalSize - 1)))
        {
            return false;
        }

        if (stacks.Any(stack => footprint.Contains(stack.Position)) ||
            resources.Any(resource => footprint.Contains(resource.Position)) ||
            bags.Any(bag => footprint.Contains(bag.Position)) ||
            villages.Any(village => Overlaps(village.Footprint, footprint)) ||
            ruins.Any(ruin => Overlaps(ruin.Footprint, footprint)) ||
            sites.Any(site => Overlaps(site.Footprint, footprint)))
        {
            return false;
        }

        foreach (CustomMapPlayer other in players)
        {
            if (other.Race != race && other.Capital is { } capital && Overlaps(new WorldFootprint(capital, CapitalSize, CapitalSize), footprint))
            {
                return false;
            }
        }

        players[playerIndex] = players[playerIndex] with { Capital = topLeft };
        WorldMapTerrain terrain = WorldHelpers.GetNativeTerrain(race);

        foreach (WorldPosition position in footprint.EnumerateTiles())
        {
            RemoveMountainsAt(position);
            cells[(position.Y * Size) + position.X] = new WorldMapCell(terrain, WorldMapFeature.None, new WorldObjectVariant(0));
        }

        return true;
    }

    public CustomMapStack? GetStackAt(WorldPosition position)
    {
        return stacks.FirstOrDefault(stack => stack.Position == position);
    }

    public bool IsValidOwner(WorldRace owner)
    {
        return owner == WorldRace.Neutral || players.Any(player => player.Race == owner);
    }

    // Places a new army; returns its ID or the reason it cannot stand there.
    public (CustomMapStack? Stack, CustomMapStackError? Error) PlaceStack(WorldPosition position, WorldRace owner, IReadOnlyList<CustomMapUnit?> slots)
    {
        if (CheckStack(position, owner, slots, ignoreId: null) is { } error)
        {
            return (null, error);
        }

        int id = stacks.Count == 0 ? 1 : stacks.Max(stack => stack.Id) + 1;
        CustomMapStack stack = new(id, position, owner, [.. slots]);
        stacks.Add(stack);

        return (stack, null);
    }

    public CustomMapStackError? UpdateStack(int id, WorldRace owner, IReadOnlyList<CustomMapUnit?> slots)
    {
        int index = stacks.FindIndex(stack => stack.Id == id);

        if (index < 0)
        {
            return CustomMapStackError.OutsideMap;
        }

        if (CheckStack(stacks[index].Position, owner, slots, ignoreId: id) is { } error)
        {
            return error;
        }

        stacks[index] = stacks[index] with { Owner = owner, Slots = [.. slots] };

        return null;
    }

    public bool RemoveStack(int id)
    {
        return stacks.RemoveAll(stack => stack.Id == id) > 0;
    }

    private CustomMapStackError? CheckStack(WorldPosition position, WorldRace owner, IReadOnlyList<CustomMapUnit?> slots, int? ignoreId)
    {
        if (!IsValidPosition(position))
        {
            return CustomMapStackError.OutsideMap;
        }

        WorldMapCell cell = GetCell(position);

        // A village's entrance takes one army of the village's owner: it is stationed in the village.
        CustomMapVillage? village = GetVillageAt(position);
        bool stationed = village is not null && village.Entrance == position;

        if (cell.Terrain == WorldMapTerrain.Water || cell.Feature == WorldMapFeature.Mountain ||
            (IsUnderStructure(position) && !stationed) || GetResourceAt(position) is not null || GetBagAt(position) is not null)
        {
            return CustomMapStackError.Blocked;
        }

        if (stacks.Any(stack => stack.Position == position && stack.Id != ignoreId))
        {
            return CustomMapStackError.Occupied;
        }

        if (!IsValidOwner(owner))
        {
            return CustomMapStackError.UnknownOwner;
        }

        if (stationed && owner != village!.Owner)
        {
            return CustomMapStackError.VillageOwner;
        }

        return CustomMapStack.ValidateSlots(slots);
    }

    public CustomMapResource? GetResourceAt(WorldPosition position)
    {
        return resources.FirstOrDefault(resource => resource.Position == position);
    }

    public CustomMapVillage? GetVillageAt(WorldPosition position)
    {
        return villages.FirstOrDefault(village => village.Footprint.Contains(position));
    }

    // Places a resource, or changes the type of the one already there. Its tile must be free land; a forest on it
    // is cleared.
    public CustomMapPlacementError? PlaceResource(WorldPosition position, WorldResourceType type)
    {
        if (!IsValidPosition(position))
        {
            return CustomMapPlacementError.OutsideMap;
        }

        int existing = resources.FindIndex(resource => resource.Position == position);

        if (existing >= 0)
        {
            resources[existing] = resources[existing] with { Type = type };
            return null;
        }

        WorldMapCell cell = GetCell(position);

        if (cell.Terrain == WorldMapTerrain.Water || cell.Feature == WorldMapFeature.Mountain || IsUnderStructure(position))
        {
            return CustomMapPlacementError.Blocked;
        }

        if (GetStackAt(position) is not null || GetBagAt(position) is not null)
        {
            return CustomMapPlacementError.Occupied;
        }

        int id = resources.Count == 0 ? 1 : resources.Max(resource => resource.Id) + 1;
        resources.Add(new CustomMapResource(id, position, type));
        cells[(position.Y * Size) + position.X] = cell with { Feature = WorldMapFeature.None, FeatureVariant = new WorldObjectVariant(0) };

        return null;
    }

    public bool RemoveResource(WorldPosition position)
    {
        return resources.RemoveAll(resource => resource.Position == position) > 0;
    }

    // Places a village with its top-left corner at topLeft. Like a capital, the land under it becomes the owner's
    // and loses forests and mountains; it must not overlap a capital, another village, an army or a resource.
    public (CustomMapVillage? Village, CustomMapPlacementError? Error) PlaceVillage(WorldPosition topLeft, int level, WorldRace owner, string name)
    {
        WorldFootprint footprint = new(topLeft, CustomMapVillage.Size, CustomMapVillage.Size);

        if (CheckStructureSite(footprint) is { } error)
        {
            return (null, error);
        }

        if (!IsValidOwner(owner))
        {
            return (null, CustomMapPlacementError.UnknownOwner);
        }

        if (level is < CustomMapVillage.MinLevel or > CustomMapVillage.MaxLevel)
        {
            return (null, CustomMapPlacementError.InvalidLevel);
        }

        int id = villages.Count == 0 ? 1 : villages.Max(village => village.Id) + 1;
        CustomMapVillage placed = new(id, topLeft, level, owner, name, CustomMapVillage.EmptyGarrison);
        villages.Add(placed);
        ClaimLand(footprint, owner);

        return (placed, null);
    }

    public CustomMapPlacementError? UpdateVillage(int id, int level, WorldRace owner, string name, IReadOnlyList<CustomMapUnit?> garrison)
    {
        int index = villages.FindIndex(village => village.Id == id);

        if (index < 0)
        {
            return CustomMapPlacementError.OutsideMap;
        }

        if (!IsValidOwner(owner))
        {
            return CustomMapPlacementError.UnknownOwner;
        }

        if (CustomMapVillage.ValidateGarrison(garrison, level) is { } error)
        {
            return error;
        }

        villages[index] = villages[index] with { Level = level, Owner = owner, Name = name, Garrison = [.. garrison] };
        ClaimLand(villages[index].Footprint, owner);

        // The army stationed in the village serves its new owner.
        int stationed = stacks.FindIndex(stack => stack.Position == villages[index].Entrance);

        if (stationed >= 0)
        {
            stacks[stationed] = stacks[stationed] with { Owner = owner };
        }

        return null;
    }

    // Removes a village together with the army stationed in it.
    public bool RemoveVillage(int id)
    {
        if (villages.Find(village => village.Id == id) is not { } village)
        {
            return false;
        }

        villages.Remove(village);
        stacks.RemoveAll(stack => stack.Position == village.Entrance);

        return true;
    }

    public CustomMapStack? GetStationedStack(CustomMapVillage village)
    {
        return GetStackAt(village.Entrance);
    }

    public CustomMapRuin? GetRuinAt(WorldPosition position)
    {
        return ruins.FirstOrDefault(ruin => ruin.Footprint.Contains(position));
    }

    public CustomMapSite? GetSiteAt(WorldPosition position)
    {
        return sites.FirstOrDefault(site => site.Footprint.Contains(position));
    }

    public CustomMapBag? GetBagAt(WorldPosition position)
    {
        return bags.FirstOrDefault(bag => bag.Position == position);
    }

    // Places a ruin with its top-left corner at topLeft. Its tiles become free land (forests, mountains and water
    // are cleared); it starts unguarded with no reward.
    public (CustomMapRuin? Ruin, CustomMapPlacementError? Error) PlaceRuin(WorldPosition topLeft, int variant, string name)
    {
        WorldFootprint footprint = new(topLeft, CustomMapRuin.Size, CustomMapRuin.Size);

        if (CheckStructureSite(footprint) is { } error)
        {
            return (null, error);
        }

        int id = ruins.Count == 0 ? 1 : ruins.Max(ruin => ruin.Id) + 1;
        CustomMapRuin placed = new(id, topLeft, variant.Mod(CustomMapRuin.VariantCount), name, Gold: 0, Item: null, CustomMapVillage.EmptyGarrison);
        ruins.Add(placed);
        ClearLand(footprint);

        return (placed, null);
    }

    public CustomMapPlacementError? UpdateRuin(int id, int variant, string name, int gold, ItemDefinitionId? item, IReadOnlyList<CustomMapUnit?> guards)
    {
        int index = ruins.FindIndex(ruin => ruin.Id == id);

        if (index < 0)
        {
            return CustomMapPlacementError.OutsideMap;
        }

        if (CustomMapVillage.ValidateUnits(guards, CustomMapRuin.GuardCapacity) is { } error)
        {
            return error;
        }

        ruins[index] = ruins[index] with
        {
            Variant = variant.Mod(CustomMapRuin.VariantCount),
            Name = name,
            Gold = Math.Clamp(gold, 0, CustomMapRuin.MaxGold),
            Item = item,
            Guards = [.. guards]
        };

        return null;
    }

    public bool RemoveRuin(int id)
    {
        return ruins.RemoveAll(ruin => ruin.Id == id) > 0;
    }

    // Places a site with its top-left corner at topLeft; like a ruin, its tiles become free land.
    public (CustomMapSite? Site, CustomMapPlacementError? Error) PlaceSite(WorldPosition topLeft, CustomMapSiteType type, int variant, string name, string greeting)
    {
        WorldFootprint footprint = new(topLeft, CustomMapSite.Size, CustomMapSite.Size);

        if (CheckStructureSite(footprint) is { } error)
        {
            return (null, error);
        }

        int id = sites.Count == 0 ? 1 : sites.Max(site => site.Id) + 1;
        CustomMapSite placed = new(id, topLeft, type, variant.Mod(CustomMapSite.GetVariantCount(type)), name, greeting, [], [], []);
        sites.Add(placed);
        ClearLand(footprint);

        return (placed, null);
    }

    // Updates a site; only the goods of its type are kept (items for merchants, spells for mage towers, units for
    // mercenary camps).
    public CustomMapPlacementError? UpdateSite(
        int id,
        int variant,
        string name,
        string greeting,
        IReadOnlyList<CustomMapMerchantItem> items,
        IReadOnlyList<WorldSpellDefinitionId> spells,
        IReadOnlyList<CustomMapMercenary> units)
    {
        int index = sites.FindIndex(site => site.Id == id);

        if (index < 0)
        {
            return CustomMapPlacementError.OutsideMap;
        }

        CustomMapSiteType type = sites[index].Type;
        ImmutableArray<CustomMapMerchantItem> keptItems = type == CustomMapSiteType.Merchant ? [.. items.Where(item => item.Count > 0)] : [];
        ImmutableArray<WorldSpellDefinitionId> keptSpells = type == CustomMapSiteType.MageTower ? [.. spells.Distinct()] : [];
        ImmutableArray<CustomMapMercenary> keptUnits = type == CustomMapSiteType.Mercenaries ? [.. units] : [];

        if (keptItems.Length > CustomMapSite.MaxGoods || keptSpells.Length > CustomMapSite.MaxGoods || keptUnits.Length > CustomMapSite.MaxGoods)
        {
            return CustomMapPlacementError.TooManyGoods;
        }

        sites[index] = sites[index] with
        {
            Variant = variant.Mod(CustomMapSite.GetVariantCount(type)),
            Name = name,
            Greeting = greeting,
            Items = keptItems,
            Spells = keptSpells,
            Units = keptUnits
        };

        return null;
    }

    public bool RemoveSite(int id)
    {
        return sites.RemoveAll(site => site.Id == id) > 0;
    }

    // Places a bag on land or water; a forest under it is cleared.
    public (CustomMapBag? Bag, CustomMapPlacementError? Error) PlaceBag(WorldPosition position, int variant)
    {
        if (!IsValidPosition(position))
        {
            return (null, CustomMapPlacementError.OutsideMap);
        }

        WorldMapCell cell = GetCell(position);

        if (cell.Feature == WorldMapFeature.Mountain || IsUnderStructure(position))
        {
            return (null, CustomMapPlacementError.Blocked);
        }

        if (GetStackAt(position) is not null || GetResourceAt(position) is not null || GetBagAt(position) is not null)
        {
            return (null, CustomMapPlacementError.Occupied);
        }

        int id = bags.Count == 0 ? 1 : bags.Max(bag => bag.Id) + 1;
        CustomMapBag placed = new(id, position, variant.Mod(CustomMapBag.VariantCount), []);
        bags.Add(placed);

        if (cell.Feature == WorldMapFeature.Forest)
        {
            cells[(position.Y * Size) + position.X] = cell with { Feature = WorldMapFeature.None, FeatureVariant = new WorldObjectVariant(0) };
        }

        return (placed, null);
    }

    public CustomMapPlacementError? UpdateBag(int id, int variant, IReadOnlyList<ItemDefinitionId> items)
    {
        int index = bags.FindIndex(bag => bag.Id == id);

        if (index < 0)
        {
            return CustomMapPlacementError.OutsideMap;
        }

        if (items.Count > CustomMapBag.MaxItems)
        {
            return CustomMapPlacementError.TooManyGoods;
        }

        bags[index] = bags[index] with { Variant = variant.Mod(CustomMapBag.VariantCount), Items = [.. items] };

        return null;
    }

    public bool RemoveBag(int id)
    {
        return bags.RemoveAll(bag => bag.Id == id) > 0;
    }

    // A new structure must fit inside the map, must not overlap another structure and must not cover an army, a
    // resource or a bag.
    private CustomMapPlacementError? CheckStructureSite(WorldFootprint footprint)
    {
        if (!IsValidPosition(footprint.TopLeft) ||
            !IsValidPosition(new WorldPosition(footprint.TopLeft.X + footprint.Width - 1, footprint.TopLeft.Y + footprint.Height - 1)))
        {
            return CustomMapPlacementError.OutsideMap;
        }

        if (GetStructureFootprints().Any(other => Overlaps(other, footprint)))
        {
            return CustomMapPlacementError.Blocked;
        }

        if (stacks.Any(stack => footprint.Contains(stack.Position)) ||
            resources.Any(resource => footprint.Contains(resource.Position)) ||
            bags.Any(bag => footprint.Contains(bag.Position)))
        {
            return CustomMapPlacementError.Occupied;
        }

        return null;
    }

    private IEnumerable<WorldFootprint> GetStructureFootprints()
    {
        foreach (CustomMapPlayer player in players)
        {
            if (player.Capital is { } capital)
            {
                yield return new WorldFootprint(capital, CapitalSize, CapitalSize);
            }
        }

        foreach (CustomMapVillage village in villages)
        {
            yield return village.Footprint;
        }

        foreach (CustomMapRuin ruin in ruins)
        {
            yield return ruin.Footprint;
        }

        foreach (CustomMapSite site in sites)
        {
            yield return site.Footprint;
        }
    }

    // Ruins and sites stand on free land: forests and mountains go, water becomes neutral land.
    private void ClearLand(WorldFootprint footprint)
    {
        foreach (WorldPosition position in footprint.EnumerateTiles())
        {
            RemoveMountainsAt(position);
            int index = (position.Y * Size) + position.X;
            WorldMapTerrain terrain = cells[index].Terrain == WorldMapTerrain.Water ? WorldMapTerrain.Neutral : cells[index].Terrain;
            cells[index] = new WorldMapCell(terrain, WorldMapFeature.None, new WorldObjectVariant(0));
        }
    }

    private void ClaimLand(WorldFootprint footprint, WorldRace owner)
    {
        WorldMapTerrain terrain = owner == WorldRace.Neutral ? WorldMapTerrain.Neutral : WorldHelpers.GetNativeTerrain(owner);

        foreach (WorldPosition position in footprint.EnumerateTiles())
        {
            RemoveMountainsAt(position);
            cells[(position.Y * Size) + position.X] = new WorldMapCell(terrain, WorldMapFeature.None, new WorldObjectVariant(0));
        }
    }

    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        foreach (CustomMapPlayer player in players)
        {
            if (player.Capital is null)
            {
                errors.Add($"Place a capital for {player.Race}.");
            }
        }

        return errors;
    }

    private bool PlaceMountains(WorldPosition center, int radius, Random random)
    {
        int brushSize = (radius * 2) + 1;
        int mountainSize = MountainSizes.Where(size => size <= Math.Min(brushSize, 5)).Max();
        int span = brushSize <= 5 ? mountainSize : brushSize;
        int start = -(span / 2);
        bool changed = false;

        for (int y = start; y + mountainSize <= start + span; y += mountainSize)
        {
            for (int x = start; x + mountainSize <= start + span; x += mountainSize)
            {
                WorldPosition topLeft = new(center.X + x, center.Y + y);
                changed |= TryPlaceMountain(topLeft, mountainSize, random);
            }
        }

        return changed;
    }

    private bool TryPlaceMountain(WorldPosition topLeft, int size, Random random)
    {
        WorldFootprint footprint = new(topLeft, size, size);

        foreach (WorldPosition position in footprint.EnumerateTiles())
        {
            if (!IsValidPosition(position) || IsUnderStructure(position) || GetStackAt(position) is not null ||
                GetResourceAt(position) is not null || GetBagAt(position) is not null)
            {
                return false;
            }

            WorldMapCell cell = GetCell(position);

            if (cell.Terrain == WorldMapTerrain.Water || cell.Feature == WorldMapFeature.Mountain)
            {
                return false;
            }
        }

        int id = mountains.Count == 0 ? 1 : mountains.Max(mountain => mountain.Id) + 1;
        int variant = random.Next(AssetMountainVariantCount(size));
        mountains.Add(new CustomMapMountain(id, topLeft, size, variant));

        foreach (WorldPosition position in footprint.EnumerateTiles())
        {
            int index = (position.Y * Size) + position.X;
            cells[index] = cells[index] with { Feature = WorldMapFeature.Mountain, FeatureVariant = new WorldObjectVariant(0) };
        }

        return true;
    }

    private bool RemoveMountainsAt(WorldPosition position)
    {
        bool changed = false;

        for (int i = mountains.Count - 1; i >= 0; i--)
        {
            CustomMapMountain mountain = mountains[i];

            if (mountain.Footprint.Contains(position))
            {
                mountains.RemoveAt(i);

                foreach (WorldPosition covered in mountain.Footprint.EnumerateTiles())
                {
                    if (IsValidPosition(covered))
                    {
                        int index = (covered.Y * Size) + covered.X;
                        cells[index] = cells[index] with { Feature = WorldMapFeature.None, FeatureVariant = new WorldObjectVariant(0) };
                    }
                }

                changed = true;
            }
        }

        return changed;
    }

    private bool IsUnderStructure(WorldPosition position)
    {
        return GetStructureFootprints().Any(footprint => footprint.Contains(position));
    }

    private static bool Overlaps(WorldFootprint a, WorldFootprint b)
    {
        return a.TopLeft.X < b.TopLeft.X + b.Width && b.TopLeft.X < a.TopLeft.X + a.Width &&
               a.TopLeft.Y < b.TopLeft.Y + b.Height && b.TopLeft.Y < a.TopLeft.Y + a.Height;
    }

    // Sprite variants per mountain size in the asset pack (AssetSpriteKey.Mountain wraps larger values around).
    private static int AssetMountainVariantCount(int size)
    {
        return size switch
        {
            1 => 11,
            2 => 10,
            3 => 9,
            _ => 8
        };
    }
}

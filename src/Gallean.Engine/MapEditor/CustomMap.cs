using System.Collections.Immutable;

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

// The map editor's document: terrain, mountains, one player per race with its capital, armies, villages and
// resources. It is saved as a normal scenario (CustomMapWriter) and read back through the regular scenario loader
// (CustomMapStore).
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
        IEnumerable<CustomMapResource>? resources = null)
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
        return new CustomMap(Id, Name, Description, Size, cells, mountains, players, stacks, villages, resources);
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
            if (!IsValidPosition(position) || IsUnderFortification(position) ||
                (brush == CustomMapBrush.Water && (GetStackAt(position) is not null || GetResourceAt(position) is not null)) ||
                (brush == CustomMapBrush.Forest && GetResourceAt(position) is not null))
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
            villages.Any(village => Overlaps(village.Footprint, footprint)))
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

        if (cell.Terrain == WorldMapTerrain.Water || cell.Feature == WorldMapFeature.Mountain || IsUnderFortification(position) || GetResourceAt(position) is not null)
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

        if (cell.Terrain == WorldMapTerrain.Water || cell.Feature == WorldMapFeature.Mountain || IsUnderFortification(position))
        {
            return CustomMapPlacementError.Blocked;
        }

        if (GetStackAt(position) is not null)
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

        if (CheckVillageSite(footprint, ignoreId: null) is { } error)
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

        return null;
    }

    public bool RemoveVillage(int id)
    {
        return villages.RemoveAll(village => village.Id == id) > 0;
    }

    private CustomMapPlacementError? CheckVillageSite(WorldFootprint footprint, int? ignoreId)
    {
        if (!IsValidPosition(footprint.TopLeft) ||
            !IsValidPosition(new WorldPosition(footprint.TopLeft.X + footprint.Width - 1, footprint.TopLeft.Y + footprint.Height - 1)))
        {
            return CustomMapPlacementError.OutsideMap;
        }

        bool overlapsCapital = players.Any(player => player.Capital is { } capital && Overlaps(new WorldFootprint(capital, CapitalSize, CapitalSize), footprint));
        bool overlapsVillage = villages.Any(village => village.Id != ignoreId && Overlaps(village.Footprint, footprint));

        if (overlapsCapital || overlapsVillage)
        {
            return CustomMapPlacementError.Blocked;
        }

        if (stacks.Any(stack => footprint.Contains(stack.Position)) || resources.Any(resource => footprint.Contains(resource.Position)))
        {
            return CustomMapPlacementError.Occupied;
        }

        return null;
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
            if (!IsValidPosition(position) || IsUnderFortification(position) || GetStackAt(position) is not null || GetResourceAt(position) is not null)
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

    private bool IsUnderFortification(WorldPosition position)
    {
        foreach (CustomMapPlayer player in players)
        {
            if (player.Capital is { } capital && new WorldFootprint(capital, CapitalSize, CapitalSize).Contains(position))
            {
                return true;
            }
        }

        return GetVillageAt(position) is not null;
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

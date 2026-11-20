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

// A playable race and its capital. The capital's garrison holds the race's guardian and up to GarrisonCapacity
// units in all (large ones count twice); it stays empty until set in the editor, and the writer then puts the
// guardian in alone. An army on the capital's entrance is stationed in it, like in a village. StartingBank is the
// gold and mana the race starts with (the scenario's player bank; in the game a human player gets it scaled by
// difficulty, a computer player as it is).
public sealed record CustomMapPlayer(WorldRace Race, WorldPosition? Capital)
{
    // GlobalVariables.CapitalCityLevel.
    public const int GarrisonCapacity = 6;

    public const int DefaultStartingGold = 500;
    public const int DefaultStartingMana = 1500;

    // GlobalVariables.MaxResourceAmount: the game clamps every amount to it.
    public const int MaxStartingAmount = 9999;

    public ImmutableArray<CustomMapUnit?> Garrison { get; init; } = [];

    public WorldResourceSet StartingBank { get; init; } = GetDefaultStartingBank(Race);

    // 500 gold and 1500 of the race's own mana, as every custom map had before banks could be set.
    public static WorldResourceSet GetDefaultStartingBank(WorldRace race)
    {
        return WorldHelpers.IsRacePlayable(race)
            ? With(new WorldResourceSet(DefaultStartingGold, 0, 0, 0, 0, 0), GetOwnMana(race), DefaultStartingMana)
            : WorldResourceSet.Empty;
    }

    // The mana a race's own buildings and spells use.
    public static WorldResourceType GetOwnMana(WorldRace race)
    {
        return race switch
        {
            WorldRace.Human => WorldResourceType.LifeMana,
            WorldRace.Undead => WorldResourceType.DeathMana,
            WorldRace.Heretic => WorldResourceType.InfernalMana,
            WorldRace.Dwarf => WorldResourceType.RuneMana,
            WorldRace.Elf => WorldResourceType.GroveMana,
            _ => throw new ArgumentOutOfRangeException(nameof(race), race, "Not a playable race.")
        };
    }

    public static int Get(WorldResourceSet bank, WorldResourceType type)
    {
        return type switch
        {
            WorldResourceType.Gold => bank.Gold,
            WorldResourceType.DeathMana => bank.DeathMana,
            WorldResourceType.RuneMana => bank.RuneMana,
            WorldResourceType.LifeMana => bank.LifeMana,
            WorldResourceType.InfernalMana => bank.InfernalMana,
            WorldResourceType.GroveMana => bank.GroveMana,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    public static WorldResourceSet With(WorldResourceSet bank, WorldResourceType type, int amount)
    {
        return type switch
        {
            WorldResourceType.Gold => bank with { Gold = amount },
            WorldResourceType.DeathMana => bank with { DeathMana = amount },
            WorldResourceType.RuneMana => bank with { RuneMana = amount },
            WorldResourceType.LifeMana => bank with { LifeMana = amount },
            WorldResourceType.InfernalMana => bank with { InfernalMana = amount },
            WorldResourceType.GroveMana => bank with { GroveMana = amount },
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    public WorldFootprint? Footprint => Capital is { } capital ? new WorldFootprint(capital, CustomMap.CapitalSize, CustomMap.CapitalSize) : null;

    public WorldPosition? Entrance => Capital is { } capital
        ? new WorldPosition(capital.X + CustomMap.CapitalSize - 1, capital.Y + CustomMap.CapitalSize - 1)
        : null;

    public bool Equals(CustomMapPlayer? other)
    {
        return other is not null && Race == other.Race && Capital == other.Capital && StartingBank == other.StartingBank && Garrison.SequenceEqual(other.Garrison);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();

        hash.Add(Race);
        hash.Add(Capital);
        hash.Add(StartingBank);

        foreach (CustomMapUnit? unit in Garrison)
        {
            hash.Add(unit);
        }

        return hash.ToHashCode();
    }
}

public sealed record CustomMapMountain(int Id, WorldPosition TopLeft, int Size, int Variant)
{
    public WorldFootprint Footprint => new(TopLeft, Size, Size);
}

// The map editor's document: terrain, mountains, one player per race with its capital, armies, villages, resources,
// ruins, treasure bags, sites and landmarks. It is saved as a normal scenario (CustomMapWriter) and read back through
// the regular scenario loader (CustomMapStore). Capitals, villages, ruins and sites are structures: their tiles are
// protected from brushes and they never overlap. An army may stand on the entrance of a village or capital: it is
// stationed there and belongs to the owner. Landmarks overlap nothing either; land and water can be painted under
// them, forests and mountains can't. Rods (CustomMapRods.cs) stand alone on a tile like resources.
public sealed partial class CustomMap
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
    private readonly List<CustomMapLandmark> landmarks;
    private readonly List<CustomMapRod> rods;
    private readonly List<CustomMapLocation> locations;
    private readonly List<CustomMapEvent> events;
    private readonly List<CustomMapArmyTemplate> templates;
    private readonly List<CustomMapVariable> variables;

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
        IEnumerable<CustomMapSite>? sites = null,
        IEnumerable<CustomMapLandmark>? landmarks = null,
        IEnumerable<CustomMapRod>? rods = null,
        IEnumerable<CustomMapLocation>? locations = null,
        IEnumerable<CustomMapEvent>? events = null,
        IEnumerable<CustomMapArmyTemplate>? templates = null,
        IEnumerable<CustomMapVariable>? variables = null)
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
        this.landmarks = [.. landmarks ?? []];
        this.rods = [.. rods ?? []];
        this.locations = [.. locations ?? []];
        this.events = [.. events ?? []];
        this.templates = [.. templates ?? []];
        this.variables = [.. variables ?? []];

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
    public IReadOnlyList<CustomMapLandmark> Landmarks => landmarks;
    public IReadOnlyList<CustomMapRod> Rods => rods;
    public IReadOnlyList<CustomMapLocation> Locations => locations;
    public IReadOnlyList<CustomMapEvent> Events => events;
    public IReadOnlyList<CustomMapArmyTemplate> Templates => templates;
    public IReadOnlyList<CustomMapVariable> Variables => variables;

    // Map rule (Gallean Upgrade, saved in SRules.json): how many percent more mage towers charge for another race's
    // spells. 0 = the game's normal prices.
    public int ForeignSpellMarkup { get; private set; }

    // Map rule (SRules.json): every neutral kind (greenskins, sea creatures, barbarians, ...) is its own neutral side,
    // so only neutrals of one kind help each other and different kinds are hostile. Off = one neutral side.
    public bool NeutralKinds { get; set; }

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
        return new CustomMap(Id, Name, Description, Size, cells, mountains, players, stacks, villages, resources, ruins, bags, sites, landmarks, rods, locations, events, templates, variables)
        {
            ForeignSpellMarkup = ForeignSpellMarkup,
            NeutralKinds = NeutralKinds
        };
    }

    public const int MaxSpellMarkup = 500;

    // Sets the mage tower markup for other races' spells (0-500 %). Returns false when nothing changed.
    public bool SetForeignSpellMarkup(int percent)
    {
        int clamped = Math.Clamp(percent, 0, MaxSpellMarkup);

        if (clamped == ForeignSpellMarkup)
        {
            return false;
        }

        ForeignSpellMarkup = clamped;

        return true;
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
    // decides who earns from them). Landmarks keep forests and mountains off their tiles. Returns true when anything
    // changed.
    public bool Paint(WorldPosition center, int radius, CustomMapBrush brush, WorldMapTerrain landTerrain, Random random)
    {
        if (brush == CustomMapBrush.Mountain)
        {
            return PlaceMountains(center, radius, random);
        }

        bool changed = false;

        foreach (WorldPosition position in GetBrushArea(center, radius))
        {
            if (!IsValidPosition(position) || GetBrushBlock(position, brush) != CustomMapTileReason.None)
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

    // Lays (road = true) or lifts a road on one tile. Roads go on open or forest land (the forest is cleared); never
    // on water, mountains, structures or landmarks. Land brushes keep a road, water and forest brushes replace it.
    // Returns true when the tile changed.
    public bool SetRoad(WorldPosition position, bool road)
    {
        if (!IsValidPosition(position))
        {
            return false;
        }

        int index = (position.Y * Size) + position.X;
        WorldMapCell cell = cells[index];

        if (!road)
        {
            if (cell.Feature != WorldMapFeature.Road)
            {
                return false;
            }

            cells[index] = cell with { Feature = WorldMapFeature.None, FeatureVariant = new WorldObjectVariant(0) };

            return true;
        }

        if (cell.Feature == WorldMapFeature.Road || GetRoadBlock(position) != CustomMapTileReason.None)
        {
            return false;
        }

        cells[index] = cell with { Feature = WorldMapFeature.Road, FeatureVariant = new WorldObjectVariant(0) };

        return true;
    }

    // Why a brush leaves a tile alone: structures for every brush, armies, resources and rods for Water, resources,
    // bags and landmarks for Forest. (Mountains have their own rule, GetMountainBlock.)
    private CustomMapTileReason GetBrushBlock(WorldPosition position, CustomMapBrush brush)
    {
        if (IsUnderStructure(position))
        {
            return CustomMapTileReason.Structure;
        }

        if (brush == CustomMapBrush.Water)
        {
            if (GetStackAt(position) is not null)
            {
                return CustomMapTileReason.Army;
            }

            if (GetResourceAt(position) is not null)
            {
                return CustomMapTileReason.Resource;
            }

            if (GetRodAt(position) is not null)
            {
                return CustomMapTileReason.Rod;
            }
        }

        if (brush == CustomMapBrush.Forest)
        {
            if (GetResourceAt(position) is not null)
            {
                return CustomMapTileReason.Resource;
            }

            if (GetBagAt(position) is not null)
            {
                return CustomMapTileReason.Bag;
            }

            if (GetLandmarkAt(position) is not null)
            {
                return CustomMapTileReason.Landmark;
            }
        }

        return CustomMapTileReason.None;
    }

    // What Paint would do, without painting: the brush tiles (Ok), the ones it leaves alone (Blocked), and for Water
    // and Clear the mountains it removes as a whole (Changes, on all their tiles). For Mountains: the mountain
    // blocks it would create (Ok) and the tiles that stop a block (Blocked).
    public CustomMapPlacementPreview PreviewPaint(WorldPosition center, int radius, CustomMapBrush brush)
    {
        if (brush == CustomMapBrush.Mountain)
        {
            return PreviewMountains(center, radius);
        }

        Dictionary<WorldPosition, CustomMapPreviewTile> tiles = [];
        List<CustomMapMountain> removed = [];

        foreach (WorldPosition position in GetBrushArea(center, radius))
        {
            if (!IsValidPosition(position))
            {
                continue;
            }

            CustomMapTileReason block = GetBrushBlock(position, brush);

            if (block != CustomMapTileReason.None)
            {
                tiles[position] = new CustomMapPreviewTile(position, CustomMapTileStatus.Blocked, block);
                continue;
            }

            tiles[position] = new CustomMapPreviewTile(position, CustomMapTileStatus.Ok, CustomMapTileReason.None);

            if (brush is CustomMapBrush.Water or CustomMapBrush.Clear)
            {
                removed.AddRange(mountains.Where(mountain => mountain.Footprint.Contains(position) && !removed.Contains(mountain)));
            }
        }

        AddRemovedMountains(tiles, removed);

        return new CustomMapPlacementPreview(null, [.. tiles.Values], null)
        {
            RemovedMountains = [.. removed.Select(mountain => mountain.Footprint)]
        };
    }

    private CustomMapPlacementPreview PreviewMountains(WorldPosition center, int radius)
    {
        List<CustomMapPreviewTile> tiles = [];
        List<WorldFootprint> created = [];

        foreach (WorldFootprint block in GetMountainBlocks(center, radius))
        {
            CustomMapPreviewTile[] blocked =
            [
                .. block.EnumerateTiles()
                    .Select(position => new CustomMapPreviewTile(position, CustomMapTileStatus.Blocked, GetMountainBlock(position)))
                    .Where(tile => tile.Reason != CustomMapTileReason.None)
            ];

            if (blocked.Length == 0)
            {
                created.Add(block);
                tiles.AddRange(block.EnumerateTiles().Select(position => new CustomMapPreviewTile(position, CustomMapTileStatus.Ok, CustomMapTileReason.None)));
            }
            else
            {
                tiles.AddRange(blocked.Where(tile => tile.Reason != CustomMapTileReason.OutsideMap));
            }
        }

        return new CustomMapPlacementPreview(null, [.. tiles], null) { NewMountains = [.. created] };
    }

    // Removing a mountain clears all of its tiles, also those outside the brush or footprint.
    private void AddRemovedMountains(Dictionary<WorldPosition, CustomMapPreviewTile> tiles, IEnumerable<CustomMapMountain> removed)
    {
        foreach (CustomMapMountain mountain in removed)
        {
            foreach (WorldPosition position in mountain.Footprint.EnumerateTiles())
            {
                if (IsValidPosition(position) && (!tiles.TryGetValue(position, out CustomMapPreviewTile tile) || tile.Status != CustomMapTileStatus.Blocked))
                {
                    tiles[position] = new CustomMapPreviewTile(position, CustomMapTileStatus.Changes, CustomMapTileReason.RemovesMountain);
                }
            }
        }
    }

    // The top-left corner of a width x height object centred on tile, pushed back inside the map.
    public WorldPosition CenterFootprint(WorldPosition tile, int width, int height)
    {
        return new WorldPosition(
            Math.Clamp(tile.X - (width / 2), 0, Math.Max(0, Size - width)),
            Math.Clamp(tile.Y - (height / 2), 0, Math.Max(0, Size - height)));
    }

    // What placing the race's capital at topLeft would do. Other capitals, villages, ruins, sites and landmarks
    // block it, and so do armies (except its own stationed army, which moves along), resources and bags.
    public CustomMapPlacementPreview PreviewCapital(WorldRace race, WorldPosition topLeft)
    {
        CustomMapPlayer? player = players.Find(candidate => candidate.Race == race);
        CustomMapStack? stationed = player is not null ? GetStationedStack(player) : null;
        WorldFootprint footprint = new(topLeft, CapitalSize, CapitalSize);
        CustomMapPlacementPreview preview = PreviewSite(footprint, SiteKind.Capital, player, stationed);

        if (player is null)
        {
            preview = preview with { Error = CustomMapPlacementError.UnknownOwner };
        }

        return player?.Footprint is { } current && current.TopLeft != topLeft ? preview with { MovesFrom = current } : preview;
    }

    public CustomMapPlacementPreview PreviewVillage(WorldPosition topLeft, int level, WorldRace owner)
    {
        CustomMapPlacementPreview preview = PreviewSite(new WorldFootprint(topLeft, CustomMapVillage.Size, CustomMapVillage.Size), SiteKind.Claim);

        if (preview.Error is null && !IsValidOwner(owner))
        {
            return preview with { Error = CustomMapPlacementError.UnknownOwner };
        }

        if (preview.Error is null && level is < CustomMapVillage.MinLevel or > CustomMapVillage.MaxLevel)
        {
            return preview with { Error = CustomMapPlacementError.InvalidLevel };
        }

        return preview;
    }

    public CustomMapPlacementPreview PreviewRuin(WorldPosition topLeft)
    {
        return PreviewSite(new WorldFootprint(topLeft, CustomMapRuin.Size, CustomMapRuin.Size), SiteKind.Clear);
    }

    public CustomMapPlacementPreview PreviewSite(WorldPosition topLeft)
    {
        return PreviewSite(new WorldFootprint(topLeft, CustomMapSite.Size, CustomMapSite.Size), SiteKind.Clear);
    }

    public CustomMapPlacementPreview PreviewLandmark(WorldPosition topLeft, int width, int height)
    {
        return PreviewSite(new WorldFootprint(topLeft, width, height), SiteKind.Landmark);
    }

    private enum SiteKind
    {
        // A capital: claims the land for its race; everything else like Claim.
        Capital,

        // A village: the land becomes the owner's (water too); forests and mountains go.
        Claim,

        // A ruin or site: forests and mountains go, water becomes land.
        Clear,

        // A landmark: mountains block it, forests go, land and water stay.
        Landmark
    }

    // The shared check behind every structure and landmark: per tile what blocks it or what changes, and the overall
    // error in the order Place* reports it (outside the map, then structures/landmarks/mountains, then armies,
    // resources and bags in the way).
    private CustomMapPlacementPreview PreviewSite(WorldFootprint footprint, SiteKind kind, CustomMapPlayer? capitalOwner = null, CustomMapStack? stationed = null)
    {
        List<WorldFootprint> structures = [.. GetStructureFootprints(ignoreCapitalOf: capitalOwner)];
        Dictionary<WorldPosition, CustomMapPreviewTile> tiles = [];
        List<CustomMapMountain> removed = [];

        foreach (WorldPosition position in footprint.EnumerateTiles())
        {
            CustomMapTileReason block = CustomMapTileReason.None;

            if (!IsValidPosition(position))
            {
                block = CustomMapTileReason.OutsideMap;
            }
            else if (structures.Any(structure => structure.Contains(position)))
            {
                block = CustomMapTileReason.Structure;
            }
            else if (GetLandmarkAt(position) is not null)
            {
                block = CustomMapTileReason.Landmark;
            }
            else if (kind == SiteKind.Landmark && GetCell(position).Feature == WorldMapFeature.Mountain)
            {
                block = CustomMapTileReason.Mountain;
            }
            else if (GetStackAt(position) is { } stack && !ReferenceEquals(stack, stationed))
            {
                block = CustomMapTileReason.Army;
            }
            else if (GetResourceAt(position) is not null)
            {
                block = CustomMapTileReason.Resource;
            }
            else if (GetBagAt(position) is not null)
            {
                block = CustomMapTileReason.Bag;
            }
            else if (GetRodAt(position) is not null)
            {
                block = CustomMapTileReason.Rod;
            }

            if (block != CustomMapTileReason.None)
            {
                tiles[position] = new CustomMapPreviewTile(position, CustomMapTileStatus.Blocked, block);
                continue;
            }

            WorldMapCell cell = GetCell(position);
            CustomMapTileReason change = cell switch
            {
                { Feature: WorldMapFeature.Mountain } => CustomMapTileReason.RemovesMountain,
                { Feature: WorldMapFeature.Forest } => CustomMapTileReason.ClearsForest,
                { Terrain: WorldMapTerrain.Water } when kind != SiteKind.Landmark => CustomMapTileReason.WaterToLand,
                _ => CustomMapTileReason.None
            };

            tiles[position] = new CustomMapPreviewTile(position, change == CustomMapTileReason.None ? CustomMapTileStatus.Ok : CustomMapTileStatus.Changes, change);

            if (change == CustomMapTileReason.RemovesMountain)
            {
                removed.AddRange(mountains.Where(mountain => mountain.Footprint.Contains(position) && !removed.Contains(mountain)));
            }
        }

        AddRemovedMountains(tiles, removed);

        ImmutableArray<CustomMapPreviewTile> result = [.. tiles.Values];
        CustomMapPlacementError? error = null;

        if (result.Any(tile => tile.Reason == CustomMapTileReason.OutsideMap))
        {
            error = CustomMapPlacementError.OutsideMap;
        }
        else if (result.Any(tile => tile.Reason is CustomMapTileReason.Structure or CustomMapTileReason.Landmark or CustomMapTileReason.Mountain))
        {
            error = CustomMapPlacementError.Blocked;
        }
        else if (result.Any(tile => tile.Reason is CustomMapTileReason.Army or CustomMapTileReason.Resource or CustomMapTileReason.Bag or CustomMapTileReason.Rod))
        {
            error = CustomMapPlacementError.Occupied;
        }

        return new CustomMapPlacementPreview(footprint, result, error) { RemovedMountains = [.. removed.Select(mountain => mountain.Footprint)] };
    }

    // Places the capital of a race with its top-left corner at topLeft (rules: PreviewCapital); terrain under it
    // becomes the race's land and loses forests and mountains. Moving a capital takes its garrison and stationed
    // army along.
    public CustomMapPlacementError? PlaceCapital(WorldRace race, WorldPosition topLeft)
    {
        if (PreviewCapital(race, topLeft).Error is { } error)
        {
            return error;
        }

        int playerIndex = players.FindIndex(player => player.Race == race);
        CustomMapStack? stationed = GetStationedStack(players[playerIndex]);
        WorldFootprint footprint = new(topLeft, CapitalSize, CapitalSize);

        players[playerIndex] = players[playerIndex] with { Capital = topLeft };

        if (stationed is not null)
        {
            stacks[stacks.IndexOf(stationed)] = stationed with { Position = players[playerIndex].Entrance!.Value };
        }

        WorldMapTerrain terrain = WorldHelpers.GetNativeTerrain(race);

        foreach (WorldPosition position in footprint.EnumerateTiles())
        {
            RemoveMountainsAt(position);
            cells[(position.Y * Size) + position.X] = new WorldMapCell(terrain, WorldMapFeature.None, new WorldObjectVariant(0));
        }

        return null;
    }

    public CustomMapStack? GetStackAt(WorldPosition position)
    {
        return stacks.FirstOrDefault(stack => stack.Position == position);
    }

    // The player whose capital covers position.
    public CustomMapPlayer? GetCapitalAt(WorldPosition position)
    {
        return players.FirstOrDefault(player => player.Footprint is { } footprint && footprint.Contains(position));
    }

    public CustomMapStack? GetStationedStack(CustomMapPlayer player)
    {
        return player.Entrance is { } entrance ? GetStackAt(entrance) : null;
    }

    public CustomMapPlacementError? UpdateCapital(WorldRace race, IReadOnlyList<CustomMapUnit?> garrison)
    {
        int index = players.FindIndex(player => player.Race == race && player.Capital is not null);

        if (index < 0)
        {
            return CustomMapPlacementError.OutsideMap;
        }

        if (CustomMapVillage.ValidateUnits(garrison, CustomMapPlayer.GarrisonCapacity, capital: true) is { } error)
        {
            return error;
        }

        players[index] = players[index] with { Garrison = [.. garrison] };

        return null;
    }

    // Sets the gold and mana a race starts with; every amount is kept within 0 and MaxStartingAmount. Returns false
    // when the race isn't on the map or nothing changed.
    public bool SetStartingBank(WorldRace race, WorldResourceSet bank)
    {
        int index = players.FindIndex(player => player.Race == race);

        if (index < 0)
        {
            return false;
        }

        WorldResourceSet clamped = new(
            Math.Clamp(bank.Gold, 0, CustomMapPlayer.MaxStartingAmount),
            Math.Clamp(bank.DeathMana, 0, CustomMapPlayer.MaxStartingAmount),
            Math.Clamp(bank.RuneMana, 0, CustomMapPlayer.MaxStartingAmount),
            Math.Clamp(bank.LifeMana, 0, CustomMapPlayer.MaxStartingAmount),
            Math.Clamp(bank.InfernalMana, 0, CustomMapPlayer.MaxStartingAmount),
            Math.Clamp(bank.GroveMana, 0, CustomMapPlayer.MaxStartingAmount));

        if (players[index].StartingBank == clamped)
        {
            return false;
        }

        players[index] = players[index] with { StartingBank = clamped };

        return true;
    }

    // Gives every race the bank of source: the same gold, its own mana as much as source has of source's own mana,
    // and source's own mana as much as source has of the race's own mana (the two amounts swap); other manas stay
    // as source has them. Returns true when any bank changed.
    public bool CopyStartingBankToAll(WorldRace source)
    {
        if (players.Find(player => player.Race == source) is not { } from)
        {
            return false;
        }

        WorldResourceType sourceMana = CustomMapPlayer.GetOwnMana(source);
        bool changed = false;

        foreach (CustomMapPlayer player in players.ToList())
        {
            WorldResourceType ownMana = CustomMapPlayer.GetOwnMana(player.Race);
            WorldResourceSet bank = from.StartingBank;
            int sourceAmount = CustomMapPlayer.Get(bank, sourceMana);
            int ownAmount = CustomMapPlayer.Get(bank, ownMana);
            bank = CustomMapPlayer.With(bank, sourceMana, ownAmount);
            bank = CustomMapPlayer.With(bank, ownMana, sourceAmount);
            changed |= SetStartingBank(player.Race, bank);
        }

        return changed;
    }

    // The race whose land a tile is: it earns from a resource there (like WorldPlan.GetLandOwner). Null for neutral
    // land and water.
    public WorldRace? GetLandOwner(WorldPosition position)
    {
        WorldMapTerrain terrain = GetCell(position).Terrain;

        foreach (CustomMapPlayer player in players)
        {
            if (WorldHelpers.GetNativeTerrain(player.Race) == terrain)
            {
                return player.Race;
            }
        }

        return null;
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
        return CheckStackSite(position, owner, ignoreId).Error ?? CustomMapStack.ValidateSlots(slots);
    }

    // What placing an army of owner on position would do (its units aside): the tile and the reason it can't stand
    // there, the same error PlaceStack returns before it looks at the units.
    public CustomMapPlacementPreview PreviewStack(WorldPosition position, WorldRace owner)
    {
        (CustomMapTileReason reason, CustomMapStackError? error) = CheckStackSite(position, owner, ignoreId: null);
        CustomMapTileStatus status = error is null or CustomMapStackError.UnknownOwner ? CustomMapTileStatus.Ok : CustomMapTileStatus.Blocked;

        return new CustomMapPlacementPreview(new WorldFootprint(position, 1, 1), [new CustomMapPreviewTile(position, status, reason)], null, error);
    }

    private (CustomMapTileReason Reason, CustomMapStackError? Error) CheckStackSite(WorldPosition position, WorldRace owner, int? ignoreId)
    {
        if (!IsValidPosition(position))
        {
            return (CustomMapTileReason.OutsideMap, CustomMapStackError.OutsideMap);
        }

        WorldMapCell cell = GetCell(position);

        // The entrance of a village or capital takes one army of its owner: it is stationed there.
        CustomMapVillage? village = GetVillageAt(position);
        CustomMapPlayer? capital = GetCapitalAt(position);
        bool stationed = (village is not null && village.Entrance == position) || (capital is not null && capital.Entrance == position);
        WorldRace? stationOwner = village?.Owner ?? capital?.Race;

        CustomMapTileReason block = cell.Terrain == WorldMapTerrain.Water ? CustomMapTileReason.Water
            : cell.Feature == WorldMapFeature.Mountain ? CustomMapTileReason.Mountain
            : IsUnderStructure(position) && !stationed ? CustomMapTileReason.Structure
            : GetResourceAt(position) is not null ? CustomMapTileReason.Resource
            : GetBagAt(position) is not null ? CustomMapTileReason.Bag
            : GetLandmarkAt(position) is not null ? CustomMapTileReason.Landmark
            : GetRodAt(position) is not null ? CustomMapTileReason.Rod
            : CustomMapTileReason.None;

        if (block != CustomMapTileReason.None)
        {
            return (block, CustomMapStackError.Blocked);
        }

        if (stacks.Any(stack => stack.Position == position && stack.Id != ignoreId))
        {
            return (CustomMapTileReason.Army, CustomMapStackError.Occupied);
        }

        if (!IsValidOwner(owner))
        {
            return (CustomMapTileReason.None, CustomMapStackError.UnknownOwner);
        }

        if (stationed && owner != stationOwner)
        {
            return (CustomMapTileReason.WrongOwner, CustomMapStackError.VillageOwner);
        }

        return (CustomMapTileReason.None, null);
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

        if (PreviewResource(position).Error is { } error)
        {
            return error;
        }

        WorldMapCell cell = GetCell(position);
        int id = resources.Count == 0 ? 1 : resources.Max(resource => resource.Id) + 1;
        resources.Add(new CustomMapResource(id, position, type));
        cells[(position.Y * Size) + position.X] = cell with { Feature = WorldMapFeature.None, FeatureVariant = new WorldObjectVariant(0) };

        return null;
    }

    // What placing a resource on position would do. A resource already there just changes its type.
    public CustomMapPlacementPreview PreviewResource(WorldPosition position)
    {
        if (!IsValidPosition(position))
        {
            return SingleTile(position, CustomMapTileReason.OutsideMap, CustomMapPlacementError.OutsideMap);
        }

        if (GetResourceAt(position) is not null)
        {
            return SingleTile(position, CustomMapTileReason.None, null);
        }

        WorldMapCell cell = GetCell(position);
        CustomMapTileReason blocked = cell.Terrain == WorldMapTerrain.Water ? CustomMapTileReason.Water
            : cell.Feature == WorldMapFeature.Mountain ? CustomMapTileReason.Mountain
            : IsUnderStructure(position) ? CustomMapTileReason.Structure
            : GetLandmarkAt(position) is not null ? CustomMapTileReason.Landmark
            : CustomMapTileReason.None;

        if (blocked != CustomMapTileReason.None)
        {
            return SingleTile(position, blocked, CustomMapPlacementError.Blocked);
        }

        CustomMapTileReason occupied = GetStackAt(position) is not null ? CustomMapTileReason.Army
            : GetBagAt(position) is not null ? CustomMapTileReason.Bag
            : GetRodAt(position) is not null ? CustomMapTileReason.Rod
            : CustomMapTileReason.None;

        if (occupied != CustomMapTileReason.None)
        {
            return SingleTile(position, occupied, CustomMapPlacementError.Occupied);
        }

        return SingleTile(position, cell.Feature == WorldMapFeature.Forest ? CustomMapTileReason.ClearsForest : CustomMapTileReason.None, null);
    }

    private static CustomMapPlacementPreview SingleTile(WorldPosition position, CustomMapTileReason reason, CustomMapPlacementError? error)
    {
        CustomMapTileStatus status = error is not null ? CustomMapTileStatus.Blocked
            : reason == CustomMapTileReason.None ? CustomMapTileStatus.Ok
            : CustomMapTileStatus.Changes;

        return new CustomMapPlacementPreview(new WorldFootprint(position, 1, 1), [new CustomMapPreviewTile(position, status, reason)], error);
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

        if (PreviewVillage(topLeft, level, owner).Error is { } error)
        {
            return (null, error);
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

        if (PreviewRuin(topLeft).Error is { } error)
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

        if (PreviewSite(topLeft).Error is { } error)
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

        if (keptUnits.Any(unit => unit.Level is < 1 or > CustomMapUnit.MaxLevel))
        {
            return CustomMapPlacementError.InvalidUnitLevel;
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
        if (PreviewBag(position).Error is { } error)
        {
            return (null, error);
        }

        WorldMapCell cell = GetCell(position);

        int id = bags.Count == 0 ? 1 : bags.Max(bag => bag.Id) + 1;
        CustomMapBag placed = new(id, position, variant.Mod(CustomMapBag.VariantCount), []);
        bags.Add(placed);

        if (cell.Feature == WorldMapFeature.Forest)
        {
            cells[(position.Y * Size) + position.X] = cell with { Feature = WorldMapFeature.None, FeatureVariant = new WorldObjectVariant(0) };
        }

        return (placed, null);
    }

    // What dropping a bag on position would do: land or water, no mountain, structure or landmark, nothing else on it.
    public CustomMapPlacementPreview PreviewBag(WorldPosition position)
    {
        if (!IsValidPosition(position))
        {
            return SingleTile(position, CustomMapTileReason.OutsideMap, CustomMapPlacementError.OutsideMap);
        }

        WorldMapCell cell = GetCell(position);
        CustomMapTileReason blocked = cell.Feature == WorldMapFeature.Mountain ? CustomMapTileReason.Mountain
            : IsUnderStructure(position) ? CustomMapTileReason.Structure
            : GetLandmarkAt(position) is not null ? CustomMapTileReason.Landmark
            : CustomMapTileReason.None;

        if (blocked != CustomMapTileReason.None)
        {
            return SingleTile(position, blocked, CustomMapPlacementError.Blocked);
        }

        CustomMapTileReason occupied = GetStackAt(position) is not null ? CustomMapTileReason.Army
            : GetResourceAt(position) is not null ? CustomMapTileReason.Resource
            : GetBagAt(position) is not null ? CustomMapTileReason.Bag
            : GetRodAt(position) is not null ? CustomMapTileReason.Rod
            : CustomMapTileReason.None;

        if (occupied != CustomMapTileReason.None)
        {
            return SingleTile(position, occupied, CustomMapPlacementError.Occupied);
        }

        return SingleTile(position, cell.Feature == WorldMapFeature.Forest ? CustomMapTileReason.ClearsForest : CustomMapTileReason.None, null);
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

    public CustomMapLandmark? GetLandmarkAt(WorldPosition position)
    {
        return landmarks.FirstOrDefault(landmark => landmark.Footprint.Contains(position));
    }

    // Places a landmark with its top-left corner at topLeft. It must fit inside the map, overlap no structure,
    // landmark or mountain and cover no army, resource or bag. Forests under it are cleared; land and water stay.
    public (CustomMapLandmark? Landmark, CustomMapPlacementError? Error) PlaceLandmark(WorldPosition topLeft, WorldLandmarkDefinition definition)
    {
        WorldFootprint footprint = new(topLeft, definition.Width, definition.Height);

        if (PreviewLandmark(topLeft, definition.Width, definition.Height).Error is { } error)
        {
            return (null, error);
        }

        int id = landmarks.Count == 0 ? 1 : landmarks.Max(landmark => landmark.Id) + 1;
        CustomMapLandmark placed = new(id, topLeft, definition.Id, definition.Width, definition.Height, definition.Mountain);
        landmarks.Add(placed);

        foreach (WorldPosition position in footprint.EnumerateTiles())
        {
            int index = (position.Y * Size) + position.X;

            if (cells[index].Feature == WorldMapFeature.Forest)
            {
                cells[index] = cells[index] with { Feature = WorldMapFeature.None, FeatureVariant = new WorldObjectVariant(0) };
            }
        }

        return (placed, null);
    }

    public bool RemoveLandmark(int id)
    {
        return landmarks.RemoveAll(landmark => landmark.Id == id) > 0;
    }

    private IEnumerable<WorldFootprint> GetStructureFootprints(CustomMapPlayer? ignoreCapitalOf = null)
    {
        foreach (CustomMapPlayer player in players)
        {
            if (player.Capital is { } capital && player.Race != ignoreCapitalOf?.Race)
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
        bool changed = false;

        foreach (WorldFootprint block in GetMountainBlocks(center, radius))
        {
            changed |= TryPlaceMountain(block.TopLeft, block.Width, random);
        }

        return changed;
    }

    // The mountain squares a Mountains brush places: one 1x1, 2x2, 3x3 or 5x5 block up to brush size 5, a grid of
    // 5x5 blocks for larger brushes.
    private static IEnumerable<WorldFootprint> GetMountainBlocks(WorldPosition center, int radius)
    {
        int brushSize = (radius * 2) + 1;
        int mountainSize = MountainSizes.Where(size => size <= Math.Min(brushSize, 5)).Max();
        int span = brushSize <= 5 ? mountainSize : brushSize;
        int start = -(span / 2);

        for (int y = start; y + mountainSize <= start + span; y += mountainSize)
        {
            for (int x = start; x + mountainSize <= start + span; x += mountainSize)
            {
                yield return new WorldFootprint(new WorldPosition(center.X + x, center.Y + y), mountainSize, mountainSize);
            }
        }
    }

    // Why a mountain block can't cover a tile.
    private CustomMapTileReason GetMountainBlock(WorldPosition position)
    {
        if (!IsValidPosition(position))
        {
            return CustomMapTileReason.OutsideMap;
        }

        WorldMapCell cell = GetCell(position);

        return IsUnderStructure(position) ? CustomMapTileReason.Structure
            : GetStackAt(position) is not null ? CustomMapTileReason.Army
            : GetResourceAt(position) is not null ? CustomMapTileReason.Resource
            : GetBagAt(position) is not null ? CustomMapTileReason.Bag
            : GetLandmarkAt(position) is not null ? CustomMapTileReason.Landmark
            : GetRodAt(position) is not null ? CustomMapTileReason.Rod
            : cell.Terrain == WorldMapTerrain.Water ? CustomMapTileReason.Water
            : cell.Feature == WorldMapFeature.Mountain ? CustomMapTileReason.Mountain
            : CustomMapTileReason.None;
    }

    private bool TryPlaceMountain(WorldPosition topLeft, int size, Random random)
    {
        WorldFootprint footprint = new(topLeft, size, size);

        if (footprint.EnumerateTiles().Any(position => GetMountainBlock(position) != CustomMapTileReason.None))
        {
            return false;
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

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

// The map editor's document: terrain, mountains and one player per race with its capital. It is saved as a normal
// scenario (CustomMapWriter) and read back through the regular scenario loader (CustomMapStore).
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

    public CustomMap(
        string id,
        string name,
        string description,
        int size,
        IEnumerable<WorldMapCell> cells,
        IEnumerable<CustomMapMountain> mountains,
        IEnumerable<CustomMapPlayer> players)
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
        return new CustomMap(Id, Name, Description, Size, cells, mountains, players);
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

    // Applies a brush around center. Capitals are protected: tiles under them are never changed.
    // Returns true when anything changed.
    public bool Paint(WorldPosition center, int radius, CustomMapBrush brush, WorldMapTerrain landTerrain, Random random)
    {
        if (brush == CustomMapBrush.Mountain)
        {
            return PlaceMountains(center, radius, random);
        }

        bool changed = false;

        foreach (WorldPosition position in GetBrushArea(center, radius))
        {
            if (!IsValidPosition(position) || IsUnderCapital(position))
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
            if (!IsValidPosition(position) || IsUnderCapital(position))
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

    private bool IsUnderCapital(WorldPosition position)
    {
        foreach (CustomMapPlayer player in players)
        {
            if (player.Capital is { } capital && new WorldFootprint(capital, CapitalSize, CapitalSize).Contains(position))
            {
                return true;
            }
        }

        return false;
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

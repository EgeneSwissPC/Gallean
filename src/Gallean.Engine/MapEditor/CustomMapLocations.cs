using System.Globalization;

namespace Gallean.Engine;

// Locations: named square zones (SLoc rows) that events and army orders point at. The game never draws them and they
// block nothing, so they may overlap each other and cover any tile. The area is the centre +- radius, like the game's
// ScenarioLocationCalculator; the radius stays 0-3 like the engine and the original maps. A location that would
// leave the map is moved inwards (its centre clamped) instead of being refused, so placing and moving never fail.
public sealed partial class CustomMap
{
    public const int MaxLocationRadius = 3;

    private const string DefaultLocationNamePrefix = "Location ";

    // The location covering position that a click picks: the selected one if it covers the tile, else the smallest
    // (the most recently placed among equals). Only for picking in the editor: in the game overlapping locations are
    // independent.
    public CustomMapLocation? PickLocation(WorldPosition position, int? selectedId = null)
    {
        if (selectedId is { } id && locations.FirstOrDefault(location => location.Id == id) is { } selected && selected.Contains(position))
        {
            return selected;
        }

        return locations
            .Where(location => location.Contains(position))
            .OrderBy(location => location.Radius)
            .ThenByDescending(location => location.Id)
            .FirstOrDefault();
    }

    // Every location covering position, in placing order.
    public IEnumerable<CustomMapLocation> GetLocationsAt(WorldPosition position)
    {
        return locations.Where(location => location.Contains(position));
    }

    public CustomMapLocation? GetLocation(int id)
    {
        return locations.FirstOrDefault(location => location.Id == id);
    }

    // The nearest centre at which a square of this radius fits on the map.
    public WorldPosition SnapLocationCenter(WorldPosition center, int radius)
    {
        CheckLocationRadius(radius);

        return new WorldPosition(Math.Clamp(center.X, radius, Size - 1 - radius), Math.Clamp(center.Y, radius, Size - 1 - radius));
    }

    // "Location N" with the smallest N no location name uses yet.
    public string GetNextLocationName(string prefix = DefaultLocationNamePrefix)
    {
        HashSet<int> used = [];

        foreach (CustomMapLocation location in locations)
        {
            if (location.Name.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(location.Name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                used.Add(number);
            }
        }

        int next = 1;

        while (used.Contains(next))
        {
            next++;
        }

        return prefix + next.ToString(CultureInfo.InvariantCulture);
    }

    // Places a location centred on center (snapped onto the map). A blank name gives the next default name.
    public CustomMapLocation PlaceLocation(WorldPosition center, int radius, string? name = null)
    {
        WorldPosition snapped = SnapLocationCenter(center, radius);
        int id = locations.Count == 0 ? 1 : locations.Max(location => location.Id) + 1;
        CustomMapLocation placed = new(id, string.IsNullOrWhiteSpace(name) ? GetNextLocationName() : name.Trim(), snapped, radius);
        locations.Add(placed);

        return placed;
    }

    // Moves a location (snapped onto the map); its ID and name stay. Null when there is no such location.
    public CustomMapLocation? MoveLocation(int id, WorldPosition center)
    {
        return Update(id, location => location with { Center = SnapLocationCenter(center, location.Radius) });
    }

    // Changes the radius; the centre moves inwards when the larger square would leave the map.
    public CustomMapLocation? ResizeLocation(int id, int radius)
    {
        CheckLocationRadius(radius);

        return Update(id, location => location with { Radius = radius, Center = SnapLocationCenter(location.Center, radius) });
    }

    // Renames a location; a blank name is ignored (the old name stays).
    public CustomMapLocation? RenameLocation(int id, string name)
    {
        return string.IsNullOrWhiteSpace(name) ? GetLocation(id) : Update(id, location => location with { Name = name.Trim() });
    }

    public bool RemoveLocation(int id)
    {
        return locations.RemoveAll(location => location.Id == id) > 0;
    }

    private CustomMapLocation? Update(int id, Func<CustomMapLocation, CustomMapLocation> change)
    {
        int index = locations.FindIndex(location => location.Id == id);

        if (index < 0)
        {
            return null;
        }

        locations[index] = change(locations[index]);

        return locations[index];
    }

    private static void CheckLocationRadius(int radius)
    {
        if (radius is < 0 or > MaxLocationRadius)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, $"A location radius must be between 0 and {MaxLocationRadius}.");
        }
    }
}

// A named zone: centre and radius 0-3 (radius 0 = 1 tile, 1 = 3x3, 2 = 5x5, 3 = 7x7). Saved as an SLoc row (LO + Id)
// with its name as the Tscen NAME_TXT of that ID.
public sealed record CustomMapLocation(int Id, string Name, WorldPosition Center, int Radius)
{
    public int Side => (2 * Radius) + 1;

    public WorldFootprint Footprint => new(new WorldPosition(Center.X - Radius, Center.Y - Radius), Side, Side);

    public bool Contains(WorldPosition position)
    {
        return position.GetDistance(Center) <= Radius;
    }
}

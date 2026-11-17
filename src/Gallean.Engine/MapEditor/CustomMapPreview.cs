using System.Collections.Immutable;

namespace Gallean.Engine;

public enum CustomMapTileStatus
{
    // The click covers or paints this tile.
    Ok,

    // It fits, but the tile changes in a way you can see (Reason says how).
    Changes,

    // This tile stops the placement, or the brush leaves it alone (Reason says why).
    Blocked
}

public enum CustomMapTileReason
{
    None,

    // Changes.
    ClearsForest,
    RemovesMountain,
    WaterToLand,

    // A rod turns the tile into its race's land.
    ClaimsLand,

    // Blocked.
    OutsideMap,
    Structure,
    Landmark,
    Mountain,
    Water,
    Army,
    Resource,
    Bag,
    WrongOwner,

    // A rod stands here (an object in the way), or a rod is too close for another one.
    Rod
}

public readonly record struct CustomMapPreviewTile(WorldPosition Position, CustomMapTileStatus Status, CustomMapTileReason Reason);

// What a click with an editor tool would do, without doing it: the tiles it covers (each fine, changed or blocking),
// and the same error the matching Place* method returns. Place* methods run the same checks first, so a preview and
// the click it shows never disagree.
public sealed record CustomMapPlacementPreview(
    WorldFootprint? Footprint,
    ImmutableArray<CustomMapPreviewTile> Tiles,
    CustomMapPlacementError? Error,
    CustomMapStackError? StackError = null)
{
    public static readonly CustomMapPlacementPreview Empty = new(null, [], null);

    // Mountains the click removes as a whole (also their tiles outside the footprint or brush).
    public ImmutableArray<WorldFootprint> RemovedMountains { get; init; } = [];

    // Mountain blocks a Mountains brush stroke would create here.
    public ImmutableArray<WorldFootprint> NewMountains { get; init; } = [];

    // Placing a capital again moves it: where it stands now.
    public WorldFootprint? MovesFrom { get; init; }

    public bool Fits => Error is null && StackError is null;

    public int Count(CustomMapTileReason reason)
    {
        return Tiles.Count(tile => tile.Reason == reason);
    }

    public int Count(CustomMapTileStatus status)
    {
        return Tiles.Count(tile => tile.Status == status);
    }

    // The first blocking tile inside the map, for "blocked by a mountain at 23, 41".
    public CustomMapPreviewTile? FirstBlocked =>
        Tiles.Where(tile => tile.Status == CustomMapTileStatus.Blocked && tile.Reason != CustomMapTileReason.OutsideMap)
            .Select(tile => (CustomMapPreviewTile?)tile)
            .FirstOrDefault();
}

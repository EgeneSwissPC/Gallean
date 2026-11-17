using System.Collections.Immutable;

namespace Gallean.Engine;

// Rods: placed like the game's rod planters plant them (WorldCreateRodCommand without the leader, range and cost
// rules) and, like the game on the turn a rod is planted, they turn the first RodTerrainTilesPerTurn tiles around
// them into their race's land (ExpansionCalculator.ExpandRodTerrain). The spread below is a port of that code to the
// editor's map; CustomMapTests.RodPaintAgreesWithGameTest checks it against the game on every tile of a test map.
public sealed partial class CustomMap
{
    private static readonly GlobalVariables RodGlobals = new();

    private static readonly (int X, int Y)[] SpreadDirections = [(0, 1), (0, -1), (1, 0), (-1, 0)];

    // No other rod may stand within this distance (GlobalVariables.RodSpacingRange, Chebyshev distance).
    public static int RodSpacing => RodGlobals.RodSpacingRange;

    // Tiles a rod turns into its race's land when it is planted, and again every turn in the game.
    public static int RodTilesPerTurn => RodGlobals.RodTerrainTilesPerTurn;

    public CustomMapRod? GetRodAt(WorldPosition position)
    {
        return rods.FirstOrDefault(rod => rod.Position == position);
    }

    // A rod closer than RodSpacing to position, if any (the one that stops a new rod there).
    public CustomMapRod? GetRodTooClose(WorldPosition position)
    {
        return rods.FirstOrDefault(rod => rod.Position.GetDistance(position) <= RodSpacing);
    }

    // Why a rod can't stand on this tile (None: it can). Water, mountains, structures, landmarks and a rod nearby
    // block it; an army, resource or bag on the tile is in the way. Forests and roads are fine, as in the game.
    public CustomMapTileReason GetRodBlock(WorldPosition position)
    {
        if (!IsValidPosition(position))
        {
            return CustomMapTileReason.OutsideMap;
        }

        WorldMapCell cell = GetCell(position);

        return cell.Terrain == WorldMapTerrain.Water ? CustomMapTileReason.Water
            : cell.Feature == WorldMapFeature.Mountain ? CustomMapTileReason.Mountain
            : IsUnderStructure(position) ? CustomMapTileReason.Structure
            : GetLandmarkAt(position) is not null ? CustomMapTileReason.Landmark
            : GetRodTooClose(position) is not null ? CustomMapTileReason.Rod
            : GetStackAt(position) is not null ? CustomMapTileReason.Army
            : GetResourceAt(position) is not null ? CustomMapTileReason.Resource
            : GetBagAt(position) is not null ? CustomMapTileReason.Bag
            : CustomMapTileReason.None;
    }

    // What placing a rod of owner on position would do: the rod's tile, and the tiles it turns into the owner's land
    // (Changes, ClaimsLand), including the 3x3 around any resource those tiles touch. When another rod is too
    // close, its tile is listed (blocked) first, so the preview can say where it is.
    public CustomMapPlacementPreview PreviewRod(WorldPosition position, WorldRace owner)
    {
        WorldFootprint footprint = new(position, 1, 1);
        CustomMapTileReason block = GetRodBlock(position);

        if (block == CustomMapTileReason.OutsideMap)
        {
            return SingleTile(position, block, CustomMapPlacementError.OutsideMap);
        }

        if (block == CustomMapTileReason.Rod)
        {
            WorldPosition other = GetRodTooClose(position)!.Position;
            CustomMapPreviewTile here = new(position, CustomMapTileStatus.Blocked, CustomMapTileReason.Rod);
            ImmutableArray<CustomMapPreviewTile> tiles = other == position
                ? [here]
                : [new CustomMapPreviewTile(other, CustomMapTileStatus.Blocked, CustomMapTileReason.Rod), here];

            return new CustomMapPlacementPreview(footprint, tiles, CustomMapPlacementError.Blocked);
        }

        if (block != CustomMapTileReason.None)
        {
            CustomMapPlacementError error = block is CustomMapTileReason.Army or CustomMapTileReason.Resource or CustomMapTileReason.Bag
                ? CustomMapPlacementError.Occupied
                : CustomMapPlacementError.Blocked;

            return SingleTile(position, block, error);
        }

        if (!WorldHelpers.IsRacePlayable(owner) || !players.Any(player => player.Race == owner))
        {
            return new CustomMapPlacementPreview(footprint, [new CustomMapPreviewTile(position, CustomMapTileStatus.Ok, CustomMapTileReason.None)], CustomMapPlacementError.UnknownOwner);
        }

        ImmutableArray<WorldPosition> claimed = GetRodClaim(position, WorldHelpers.GetNativeTerrain(owner));
        List<CustomMapPreviewTile> result = [];

        if (!claimed.Contains(position))
        {
            result.Add(new CustomMapPreviewTile(position, CustomMapTileStatus.Ok, CustomMapTileReason.None));
        }

        result.AddRange(claimed.Select(tile => new CustomMapPreviewTile(tile, CustomMapTileStatus.Changes, CustomMapTileReason.ClaimsLand)));

        return new CustomMapPlacementPreview(footprint, [.. result], null);
    }

    // Places a rod (rules: PreviewRod) and turns the land around it into the owner's, as the game does on the turn
    // a rod is planted.
    public (CustomMapRod? Rod, CustomMapPlacementError? Error) PlaceRod(WorldPosition position, WorldRace owner)
    {
        CustomMapPlacementPreview preview = PreviewRod(position, owner);

        if (preview.Error is { } error)
        {
            return (null, error);
        }

        WorldMapTerrain terrain = WorldHelpers.GetNativeTerrain(owner);

        foreach (CustomMapPreviewTile tile in preview.Tiles.Where(tile => tile.Reason == CustomMapTileReason.ClaimsLand))
        {
            int index = (tile.Position.Y * Size) + tile.Position.X;
            cells[index] = cells[index] with { Terrain = terrain };
        }

        int id = rods.Count == 0 ? 1 : rods.Max(rod => rod.Id) + 1;
        CustomMapRod placed = new(id, position, owner);
        rods.Add(placed);

        return (placed, null);
    }

    // Removes the rod on position. The land it claimed stays, as in the game.
    public bool RemoveRod(WorldPosition position)
    {
        return rods.RemoveAll(rod => rod.Position == position) > 0;
    }

    // The tiles a new rod on anchor turns into terrain, in the order the game converts them (only tiles whose
    // terrain changes). Port of ExpansionCalculator.ExpandAround (source = the new rod, limit RodTilesPerTurn)
    // followed by TerrainTransformCalculator.TransformCells.
    private ImmutableArray<WorldPosition> GetRodClaim(WorldPosition anchor, WorldMapTerrain terrain)
    {
        HashSet<WorldPosition> mountainLandmarkTiles = [.. landmarks.Where(landmark => landmark.Mountain).SelectMany(landmark => landmark.Footprint.EnumerateTiles())];
        bool[] closed = BuildSpreadBlockedCells(mountainLandmarkTiles);
        Queue<WorldPosition> queue = new();
        List<WorldPosition> positions = [];

        closed[(anchor.Y * Size) + anchor.X] = true;
        EnqueueNeighbors(anchor);

        while (queue.Count > 0)
        {
            WorldPosition position = queue.Dequeue();

            if (GetCell(position).Terrain != terrain)
            {
                if (positions.Count < RodTilesPerTurn)
                {
                    positions.Add(position);
                }
                else
                {
                    break;
                }
            }

            EnqueueNeighbors(position);
        }

        return TransformSpreadTiles(positions, terrain, mountainLandmarkTiles);

        void EnqueueNeighbors(WorldPosition position)
        {
            foreach ((int offsetX, int offsetY) in SpreadDirections)
            {
                WorldPosition neighbor = new(position.X + offsetX, position.Y + offsetY);

                if (IsValidPosition(neighbor))
                {
                    int index = (neighbor.Y * Size) + neighbor.X;

                    if (!closed[index])
                    {
                        closed[index] = true;
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }
    }

    // In the game a tile can change owner unless it is water or a mountain; mountain-type landmarks make their tiles
    // mountains there, so they count too.
    private bool IsSpreadable(WorldPosition position, HashSet<WorldPosition> mountainLandmarkTiles)
    {
        WorldMapCell cell = GetCell(position);

        return cell.Terrain != WorldMapTerrain.Water && cell.Feature != WorldMapFeature.Mountain && !mountainLandmarkTiles.Contains(position);
    }

    // Port of ExpansionCalculator.BuildBlockedCells for a new rod: tiles the spread can't enter. Existing rods,
    // resources (and the 3x3 around a resource next to a rod), capitals, villages of playable races and
    // mountain-type landmarks; neutral villages, ruins and sites don't stop it.
    private bool[] BuildSpreadBlockedCells(HashSet<WorldPosition> mountainLandmarkTiles)
    {
        bool[] blocked = new bool[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                blocked[(y * Size) + x] = !IsSpreadable(new WorldPosition(x, y), mountainLandmarkTiles);
            }
        }

        foreach (CustomMapRod rod in rods)
        {
            Block(rod.Position);
        }

        foreach (CustomMapResource resource in resources)
        {
            Block(resource.Position);

            if (rods.Any(rod => rod.Position.GetDistance(resource.Position) <= 1))
            {
                foreach (WorldPosition position in resource.Position.GetAdjacentPositions())
                {
                    Block(position);
                }
            }
        }

        foreach (CustomMapPlayer player in players)
        {
            if (player.Footprint is { } footprint)
            {
                BlockAll(footprint);
            }
        }

        foreach (CustomMapVillage village in villages)
        {
            if (village.Owner != WorldRace.Neutral)
            {
                BlockAll(village.Footprint);
            }
        }

        foreach (WorldPosition position in mountainLandmarkTiles)
        {
            Block(position);
        }

        return blocked;

        void BlockAll(WorldFootprint footprint)
        {
            foreach (WorldPosition position in footprint.EnumerateTiles())
            {
                Block(position);
            }
        }

        void Block(WorldPosition position)
        {
            if (IsValidPosition(position))
            {
                blocked[(position.Y * Size) + position.X] = true;
            }
        }
    }

    // Port of TerrainTransformCalculator.TransformCells: the chosen tiles change, and any resource within 1 tile of a
    // changed tile takes its whole 3x3 along (which can reach further resources). Returns the tiles whose terrain
    // actually changes.
    private ImmutableArray<WorldPosition> TransformSpreadTiles(List<WorldPosition> positions, WorldMapTerrain terrain, HashSet<WorldPosition> mountainLandmarkTiles)
    {
        bool[] visited = new bool[Size * Size];
        Queue<WorldPosition> queue = new(positions);
        List<WorldPosition> changed = [];

        while (queue.Count > 0)
        {
            WorldPosition position = queue.Dequeue();

            if (!IsValidPosition(position))
            {
                continue;
            }

            int index = (position.Y * Size) + position.X;

            if (visited[index])
            {
                continue;
            }

            visited[index] = true;

            if (IsSpreadable(position, mountainLandmarkTiles) && cells[index].Terrain != terrain)
            {
                changed.Add(position);
            }

            foreach (CustomMapResource resource in resources)
            {
                if (resource.Position.GetDistance(position) <= 1)
                {
                    queue.Enqueue(resource.Position);

                    foreach (WorldPosition adjacent in resource.Position.GetAdjacentPositions())
                    {
                        queue.Enqueue(adjacent);
                    }
                }
            }
        }

        return [.. changed];
    }
}

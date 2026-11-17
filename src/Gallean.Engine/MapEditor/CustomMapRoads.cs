using System.Collections.Immutable;

namespace Gallean.Engine;

// The road tool: a road is laid along a path between two tiles, found like an army's path but over the four tile
// edges only, because road pieces join across edges (the renderer picks each tile's piece from its road neighbours).
public sealed partial class CustomMap
{
    // Step costs: walking an existing road is cheapest, so new roads join and follow the ones already there; going
    // through a forest (which the road clears) costs more than open land.
    private const int RoadStepCost = 10;
    private const int OpenStepCost = 20;
    private const int ForestStepCost = 30;

    // Among equally cheap paths, the one closest to the straight line between the ends wins, so a road runs as an
    // even staircase instead of an L. The extra cost per tile is capped well below a step, so it never makes a road
    // longer by more than a little.
    private const int MaxLineCost = 3;

    // Why a road can't go on this tile (None: it can, or a road is already there).
    public CustomMapTileReason GetRoadBlock(WorldPosition position)
    {
        if (!IsValidPosition(position))
        {
            return CustomMapTileReason.OutsideMap;
        }

        WorldMapCell cell = GetCell(position);

        if (cell.Terrain == WorldMapTerrain.Water)
        {
            return CustomMapTileReason.Water;
        }

        if (cell.Feature == WorldMapFeature.Mountain)
        {
            return CustomMapTileReason.Mountain;
        }

        if (IsUnderStructure(position))
        {
            return CustomMapTileReason.Structure;
        }

        if (GetRodAt(position) is not null)
        {
            // Rods block armies, so a road through one would lead nowhere.
            return CustomMapTileReason.Rod;
        }

        return GetLandmarkAt(position) is not null ? CustomMapTileReason.Landmark : CustomMapTileReason.None;
    }

    // The cheapest road from start to end (both included), each tile sharing an edge with the next; null when a road
    // can't reach end. Tiles that already carry a road are part of the path too.
    public ImmutableArray<WorldPosition>? FindRoadPath(WorldPosition start, WorldPosition end)
    {
        if (GetRoadBlock(start) != CustomMapTileReason.None || GetRoadBlock(end) != CustomMapTileReason.None)
        {
            return null;
        }

        if (start == end)
        {
            return [start];
        }

        int count = Size * Size;
        int[] cost = new int[count];
        int[] from = new int[count];
        Array.Fill(cost, int.MaxValue);
        PriorityQueue<int, (int Estimate, int Line)> open = new();
        int startIndex = (start.Y * Size) + start.X;
        int endIndex = (end.Y * Size) + end.X;
        int lineX = end.X - start.X;
        int lineY = end.Y - start.Y;
        double lineLength = Math.Sqrt((lineX * lineX) + (lineY * lineY));

        // Distance from the start-end line in tiles, times 2, capped.
        int LineCost(int x, int y)
        {
            double distance = Math.Abs(((x - start.X) * lineY) - ((y - start.Y) * lineX)) / lineLength;

            return Math.Min(MaxLineCost, (int)(distance * 2));
        }

        int Estimate(int x, int y)
        {
            return (Math.Abs(end.X - x) + Math.Abs(end.Y - y)) * RoadStepCost;
        }

        cost[startIndex] = 0;
        from[startIndex] = -1;
        open.Enqueue(startIndex, (Estimate(start.X, start.Y), 0));
        ReadOnlySpan<(int X, int Y)> steps = [(0, -1), (1, 0), (0, 1), (-1, 0)];

        while (open.TryDequeue(out int index, out (int Estimate, int Line) priority))
        {
            if (index == endIndex)
            {
                break;
            }

            int x = index % Size;
            int y = index / Size;

            if (priority.Estimate - Estimate(x, y) > cost[index])
            {
                continue;
            }

            foreach ((int dx, int dy) in steps)
            {
                WorldPosition next = new(x + dx, y + dy);

                if (GetRoadBlock(next) != CustomMapTileReason.None)
                {
                    continue;
                }

                int nextIndex = (next.Y * Size) + next.X;
                int line = LineCost(next.X, next.Y);
                int stepCost = cells[nextIndex].Feature switch
                {
                    WorldMapFeature.Road => RoadStepCost,
                    WorldMapFeature.Forest => ForestStepCost,
                    _ => OpenStepCost
                } + line;
                int nextCost = cost[index] + stepCost;

                if (nextCost < cost[nextIndex])
                {
                    cost[nextIndex] = nextCost;
                    from[nextIndex] = index;
                    open.Enqueue(nextIndex, (nextCost + Estimate(next.X, next.Y), line));
                }
            }
        }

        if (cost[endIndex] == int.MaxValue)
        {
            return null;
        }

        List<WorldPosition> path = [];

        for (int index = endIndex; index != -1; index = from[index])
        {
            path.Add(new WorldPosition(index % Size, index / Size));
        }

        path.Reverse();

        return [.. path];
    }

    // What laying a road along path would do: new road on open land (Ok), new road through a forest (Changes,
    // ClearsForest), a tile no road can take (Blocked, with the reason). Tiles that already carry a road are left out.
    public CustomMapPlacementPreview PreviewRoad(IReadOnlyList<WorldPosition> path)
    {
        List<CustomMapPreviewTile> tiles = [];
        CustomMapPlacementError? error = null;

        foreach (WorldPosition position in path)
        {
            CustomMapTileReason block = GetRoadBlock(position);

            if (block != CustomMapTileReason.None)
            {
                tiles.Add(new CustomMapPreviewTile(position, CustomMapTileStatus.Blocked, block));
                error = block == CustomMapTileReason.OutsideMap ? CustomMapPlacementError.OutsideMap : CustomMapPlacementError.Blocked;
                continue;
            }

            WorldMapFeature feature = GetCell(position).Feature;

            if (feature == WorldMapFeature.Forest)
            {
                tiles.Add(new CustomMapPreviewTile(position, CustomMapTileStatus.Changes, CustomMapTileReason.ClearsForest));
            }
            else if (feature != WorldMapFeature.Road)
            {
                tiles.Add(new CustomMapPreviewTile(position, CustomMapTileStatus.Ok, CustomMapTileReason.None));
            }
        }

        return new CustomMapPlacementPreview(null, [.. tiles], error);
    }

    // Lays a road on every tile of path (as PreviewRoad shows it); nothing changes when a tile is blocked. Returns the
    // number of new road tiles, or the error.
    public (int Laid, CustomMapPlacementError? Error) LayRoad(IReadOnlyList<WorldPosition> path)
    {
        if (PreviewRoad(path).Error is { } error)
        {
            return (0, error);
        }

        int laid = 0;

        foreach (WorldPosition position in path)
        {
            if (SetRoad(position, true))
            {
                laid++;
            }
        }

        return (laid, null);
    }

    // The map as the game sees it with roads added on the given tiles (the road tool's preview, before a click lays
    // them). Forests under them disappear, as they would when the road is laid.
    public WorldMap ToWorldMap(IEnumerable<WorldPosition> extraRoads)
    {
        WorldMapCell[] copy = [.. cells];

        foreach (WorldPosition position in extraRoads)
        {
            if (GetRoadBlock(position) == CustomMapTileReason.None)
            {
                int index = (position.Y * Size) + position.X;
                copy[index] = copy[index] with { Feature = WorldMapFeature.Road, FeatureVariant = new WorldObjectVariant(0) };
            }
        }

        return new WorldMap(Size, Size, [.. copy]);
    }
}

namespace Gallean.Engine;

internal static class TerrainTransformCalculator
{
    private const int PercentageMax = 100;

    public static bool IsTransformable(WorldMapCell cell)
    {
        return cell.Terrain != WorldMapTerrain.Water &&
               cell.Feature != WorldMapFeature.Mountain;
    }

    public static WorldMap Transform(WorldMap map, WorldState state, IReadOnlyList<WorldPosition> positions, WorldMapTerrain terrain)
    {
        return TransformCells(map, state, positions, terrain);
    }

    public static WorldMap TransformArea(WorldState state, WorldPosition center, int areaSize, WorldMapTerrain terrain)
    {
        WorldMap map = state.Map;
        int left = Math.Max(0, center.X - areaSize / 2);
        int top = Math.Max(0, center.Y - areaSize / 2);
        int right = Math.Min(map.Width, center.X - areaSize / 2 + areaSize);
        int bottom = Math.Min(map.Height, center.Y - areaSize / 2 + areaSize);
        List<WorldPosition> positions = [];

        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                positions.Add(new WorldPosition(x, y));
            }
        }

        return TransformCells(map, state, positions, terrain);
    }

    private static WorldMap TransformCells(
        WorldMap map,
        WorldState state,
        IReadOnlyList<WorldPosition> positions,
        WorldMapTerrain terrain)
    {
        List<int> indexes = GetTransformedIndexes(map, new ResourceNodeIndex(state), positions, index => map.Cells[index]);

        if (indexes.Count > 0)
        {
            map = map.UpdateCells(cells =>
            {
                foreach (int index in indexes)
                {
                    cells[index] = cells[index] with { Terrain = terrain };
                }
            });
        }

        return map;
    }

    // The cells the transform changes: the given positions plus, around every resource node next to one of them, the
    // node and its neighbours (repeated for nodes reached that way), keeping only transformable cells. getCell reads the
    // current cells, so a caller can transform several times into one working copy.
    // Gallean Upgrade (engine speed): visited cells in a set and nodes looked up by position, where the original
    // allocated a map-sized array and looped over every node of the map for every visited cell; same cells.
    internal static List<int> GetTransformedIndexes(
        WorldMap map,
        ResourceNodeIndex nodes,
        IReadOnlyList<WorldPosition> positions,
        Func<int, WorldMapCell> getCell)
    {
        HashSet<int> visitedCells = [];
        Queue<WorldPosition> queue = new(positions);
        List<int> indexes = [];

        while (queue.Count > 0)
        {
            WorldPosition position = queue.Dequeue();

            if (map.IsValidPosition(position))
            {
                int index = GetIndex(map, position);

                if (visitedCells.Add(index))
                {
                    if (IsTransformable(getCell(index)))
                    {
                        indexes.Add(index);
                    }

                    nodes.EnqueueNodesNear(queue, position);
                }
            }
        }

        return indexes;
    }

    internal sealed class ResourceNodeIndex(WorldState state)
    {
        private readonly HashSet<WorldPosition> positions = [.. state.ResourceNodes.Values.Select(node => node.Position)];

        public void EnqueueNodesNear(Queue<WorldPosition> queue, WorldPosition position)
        {
            if (positions.Count == 0)
            {
                return;
            }

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    WorldPosition nodePosition = new(position.X + dx, position.Y + dy);

                    if (positions.Contains(nodePosition))
                    {
                        queue.Enqueue(nodePosition);

                        foreach (WorldPosition adjacent in nodePosition.GetAdjacentPositions())
                        {
                            queue.Enqueue(adjacent);
                        }
                    }
                }
            }
        }
    }

    private static int GetIndex(WorldMap map, WorldPosition position)
    {
        return position.Y * map.Width + position.X;
    }

    public static int GetTransformedPercentage(WorldMap map, WorldMapTerrain terrain)
    {
        int transformable = 0;
        int transformed = 0;

        foreach (WorldMapCell cell in map.Cells)
        {
            if (IsTransformable(cell))
            {
                transformable++;

                if (cell.Terrain == terrain)
                {
                    transformed++;
                }
            }
        }

        if (transformable > 0)
        {
            return transformed * PercentageMax / transformable;
        }
        else
        {
            return 0;
        }
    }
}

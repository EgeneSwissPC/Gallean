using System.Collections.Immutable;

namespace Gallean.Engine;

internal static class ExpansionCalculator
{
    private readonly record struct ExpansionSource(WorldFortificationId? FortificationId, WorldRodId? RodId);

    private static readonly (int X, int Y)[] Directions = [(0, 1), (0, -1), (1, 0), (-1, 0)];

    public static WorldMap ExpandPlayerTerrain(WorldState state, WorldPlayerId playerId, IEngineContext engine)
    {
        WorldPlayer player = state.Players[playerId];

        if (!WorldHelpers.IsRacePlayable(player.Race))
        {
            return state.Map;
        }

        WorldMapTerrain nativeTerrain = WorldHelpers.GetNativeTerrain(player.Race);

        // Gallean Upgrade (engine speed): one expander for all of the player's cities and rods. It builds the
        // source-independent blocked cells once and changes one working copy of the cells, where the original built
        // both per source (a full scan and a full map copy for every city and rod; ~20 % of AI turn time on 1024 maps).
        // Each source still sees the terrain left by the sources before it, so the result is the same.
        Expander expander = new(state, engine);

        foreach (WorldFortification fortification in state.Fortifications.Values)
        {
            if (FortificationCalculator.GetOwner(fortification) == playerId)
            {
                int growth = FortificationCalculator.GetTerrainGrowth(fortification, engine.Globals);

                expander.ExpandAround(
                    new ExpansionSource(fortification.Id, null),
                    FortificationCalculator.GetCenter(fortification.Footprint),
                    nativeTerrain,
                    growth);
            }
        }

        foreach (WorldRod rod in state.Rods.Values)
        {
            if (rod.OwnerId == playerId)
            {
                expander.ExpandAround(
                    new ExpansionSource(null, rod.Id),
                    rod.Position,
                    nativeTerrain,
                    engine.Globals.RodTerrainTilesPerTurn);
            }
        }

        return expander.GetMap();
    }

    public static WorldMap ExpandFortificationTerrain(
        WorldState state,
        IEngineContext engine,
        WorldFortification fortification,
        WorldMapTerrain terrain,
        int tileBudget)
    {
        Expander expander = new(state, engine);

        expander.ExpandAround(
            new ExpansionSource(fortification.Id, null),
            FortificationCalculator.GetCenter(fortification.Footprint),
            terrain,
            tileBudget);

        return expander.GetMap();
    }

    public static WorldMap ExpandRodTerrain(WorldState state, IEngineContext engine, WorldRod rod, WorldMapTerrain terrain)
    {
        Expander expander = new(state, engine);

        expander.ExpandAround(
            new ExpansionSource(null, rod.Id),
            rod.Position,
            terrain,
            engine.Globals.RodTerrainTilesPerTurn);

        return expander.GetMap();
    }

    private sealed class Expander
    {
        private readonly WorldState state;
        private readonly WorldMap map;

        // Cells blocked for every source: not transformable (water, mountains; expansion never changes that), resource
        // nodes and mountain landmarks.
        private readonly bool[] commonBlocked;

        // Resource nodes with at least one rod next to them, and those rods: the node's neighbours are blocked unless
        // the only such rod is the expanding one.
        private readonly List<(WorldResourceNode Node, List<WorldRodId> Rods)> nodesWithRods = [];

        private readonly TerrainTransformCalculator.ResourceNodeIndex nodeIndex;
        private ImmutableArray<WorldMapCell>.Builder? cells;

        public Expander(WorldState state, IEngineContext engine)
        {
            this.state = state;
            map = state.Map;
            nodeIndex = new TerrainTransformCalculator.ResourceNodeIndex(state);
            commonBlocked = new bool[map.Width * map.Height];

            for (int index = 0; index < commonBlocked.Length; index++)
            {
                if (!TerrainTransformCalculator.IsTransformable(map.Cells[index]))
                {
                    commonBlocked[index] = true;
                }
            }

            Dictionary<WorldPosition, List<WorldRodId>> rodsByPosition = [];

            foreach (WorldRod rod in state.Rods.Values)
            {
                if (!rodsByPosition.TryGetValue(rod.Position, out List<WorldRodId>? ids))
                {
                    rodsByPosition[rod.Position] = ids = [];
                }

                ids.Add(rod.Id);
            }

            foreach (WorldResourceNode node in state.ResourceNodes.Values)
            {
                Block(commonBlocked, node.Position);

                List<WorldRodId> near = [];

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (rodsByPosition.TryGetValue(new WorldPosition(node.Position.X + dx, node.Position.Y + dy), out List<WorldRodId>? ids))
                        {
                            near.AddRange(ids);
                        }
                    }
                }

                if (near.Count > 0)
                {
                    nodesWithRods.Add((node, near));
                }
            }

            foreach (WorldLandmark landmark in state.Landmarks.Values)
            {
                if (engine.Definitions.GetLandmark(landmark.DefinitionId).Mountain)
                {
                    foreach (WorldPosition position in landmark.Footprint.EnumerateTiles())
                    {
                        Block(commonBlocked, position);
                    }
                }
            }
        }

        public WorldMap GetMap()
        {
            return cells is null ? map : map with { Cells = cells.MoveToImmutable() };
        }

        public void ExpandAround(ExpansionSource source, WorldPosition anchor, WorldMapTerrain terrain, int cellLimit)
        {
            bool[] closedCells = BuildBlockedCells(source);
            Queue<WorldPosition> queue = new();
            List<WorldPosition> positions = [];

            closedCells[GetIndex(anchor)] = true;
            EnqueueNeighbors(anchor);

            while (queue.Count > 0)
            {
                WorldPosition position = queue.Dequeue();

                if (GetCell(GetIndex(position)).Terrain != terrain)
                {
                    if (positions.Count < cellLimit)
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

            List<int> indexes = TerrainTransformCalculator.GetTransformedIndexes(map, nodeIndex, positions, GetCell);

            if (indexes.Count > 0)
            {
                cells ??= map.Cells.ToBuilder();

                foreach (int index in indexes)
                {
                    cells[index] = cells[index] with { Terrain = terrain };
                }
            }

            void EnqueueNeighbors(WorldPosition position)
            {
                foreach ((int offsetX, int offsetY) in Directions)
                {
                    WorldPosition neighbor = new(position.X + offsetX, position.Y + offsetY);

                    if (map.IsValidPosition(neighbor))
                    {
                        int index = GetIndex(neighbor);

                        if (!closedCells[index])
                        {
                            closedCells[index] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }
        }

        private WorldMapCell GetCell(int index)
        {
            return cells is null ? map.Cells[index] : cells[index];
        }

        private bool[] BuildBlockedCells(ExpansionSource source)
        {
            bool[] blocked = (bool[])commonBlocked.Clone();

            foreach (WorldRod rod in state.Rods.Values)
            {
                if (rod.Id != source.RodId)
                {
                    Block(blocked, rod.Position);
                }
            }

            foreach ((WorldResourceNode node, List<WorldRodId> rods) in nodesWithRods)
            {
                if (rods.Exists(id => id != source.RodId))
                {
                    foreach (WorldPosition position in node.Position.GetAdjacentPositions())
                    {
                        Block(blocked, position);
                    }
                }
            }

            foreach (WorldFortification fortification in state.Fortifications.Values)
            {
                if (fortification.Id != source.FortificationId && HasPlayableOwner(state, fortification))
                {
                    foreach (WorldPosition position in fortification.Footprint.EnumerateTiles())
                    {
                        Block(blocked, position);
                    }
                }
            }

            return blocked;
        }

        private void Block(bool[] blocked, WorldPosition position)
        {
            if (map.IsValidPosition(position))
            {
                blocked[GetIndex(position)] = true;
            }
        }

        private int GetIndex(WorldPosition position)
        {
            return position.Y * map.Width + position.X;
        }
    }

    private static bool HasPlayableOwner(WorldState state, WorldFortification fortification)
    {
        WorldPlayerId? ownerId = FortificationCalculator.GetOwner(fortification);

        if (ownerId is not null)
        {
            return WorldHelpers.IsRacePlayable(state.Players[ownerId.Value].Race);
        }
        else
        {
            return false;
        }
    }
}

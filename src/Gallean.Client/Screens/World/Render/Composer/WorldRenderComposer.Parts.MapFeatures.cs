using System.Runtime.CompilerServices;
using static Gallean.Client.WorldRenderState;

namespace Gallean.Client;

internal sealed partial class WorldRenderComposer
{
    private static class MapFeatures
    {
        // Every forest of a map as its tree object, built once per map: The War of the Ring has 124k forest tiles among a
        // million, and a step that revealed a tile scanned them all and made a new object for each visible one.
        private static readonly ConditionalWeakTable<WorldMap, MapTileFeature[]> Forests = new();

        public readonly record struct Input(WorldMap Map, WorldVisibility Visibility);

        public static IReadOnlyList<MapObject> Build(Input input)
        {
            WorldMap map = input.Map;
            List<MapObject> objects = [];

            foreach (MapTileFeature forest in Forests.GetValue(map, FindForests))
            {
                if (IsObjectVisible(input.Visibility, forest.Position))
                {
                    objects.Add(forest);
                }
            }

            return objects;
        }

        private static MapTileFeature[] FindForests(WorldMap map)
        {
            List<MapTileFeature> forests = [];

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    WorldPosition position = new(x, y);
                    WorldMapCell cell = map.GetCellAt(position);

                    if (cell.Feature == WorldMapFeature.Forest)
                    {
                        forests.Add(new MapTileFeature(
                            position,
                            GetDepth(position, Layer.Trees),
                            new AssetSpriteKey.Tree(cell.Terrain, cell.FeatureVariant.Value)));
                    }
                }
            }

            return [.. forests];
        }
    }
}

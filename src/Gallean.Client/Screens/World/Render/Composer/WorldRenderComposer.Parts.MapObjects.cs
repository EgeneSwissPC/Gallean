using static Gallean.Client.WorldRenderState;

namespace Gallean.Client;

internal sealed partial class WorldRenderComposer
{
    private static class MapObjects
    {
        public readonly record struct Input(
            IReadOnlyList<MapObject> Landmarks,
            IReadOnlyList<MapObject> Mountains,
            IReadOnlyList<MapObject> Sites,
            IReadOnlyList<MapObject> MapFeatures,
            IReadOnlyList<MapObject> ResourceNodes,
            IReadOnlyList<MapObject> Rods,
            IReadOnlyList<MapObject> Tombs,
            IReadOnlyList<MapObject> Bags,
            IReadOnlyList<MapObject> Fortifications);

        public static IReadOnlyList<MapObject> Build(Input input)
        {
            List<MapObject> objects = [];

            objects.AddRange(input.Landmarks);
            objects.AddRange(input.Mountains);
            objects.AddRange(input.Sites);
            objects.AddRange(input.MapFeatures);
            objects.AddRange(input.ResourceNodes);
            objects.AddRange(input.Rods);
            objects.AddRange(input.Tombs);
            objects.AddRange(input.Bags);
            objects.AddRange(input.Fortifications);

            // A stable sort by depth (equal depths keep the order above), on keys instead of LINQ: 120k objects on a
            // well explored 1024x1024 map.
            MapObject[] sorted = new MapObject[objects.Count];
            objects.CopyTo(sorted);
            long[] keys = new long[sorted.Length];

            for (int index = 0; index < sorted.Length; index++)
            {
                keys[index] = ((long)sorted[index].Depth << 32) | (uint)index;
            }

            Array.Sort(keys, sorted);

            return sorted;
        }
    }
}

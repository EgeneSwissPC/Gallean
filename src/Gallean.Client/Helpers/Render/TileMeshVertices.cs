using SkiaSharp;

namespace Gallean.Client;

// Builds the triangle meshes for map tiles (terrain, water, fog, reach). SKVertices indexes are 16-bit, so an indexed
// mesh can address at most 65,536 corners, which a map larger than about 255 x 255 tiles exceeds: the indexes wrapped
// around and joined the wrong corners (missing fog and ground, long black streaks). Larger meshes are therefore
// expanded into a plain triangle list, which has no vertex limit.
internal static class TileMeshVertices
{
    public const int MaxIndexedVertices = ushort.MaxValue + 1;

    public static SKVertices Create(List<SKPoint> positions, List<int> indexes, List<SKColor>? colors = null)
    {
        if (positions.Count <= MaxIndexedVertices)
        {
            ushort[] shortIndexes = new ushort[indexes.Count];

            for (int i = 0; i < indexes.Count; i++)
            {
                shortIndexes[i] = (ushort)indexes[i];
            }

            return SkiaMemoryPressure.CreateVertices([.. positions], colors is null ? null : [.. colors], shortIndexes);
        }

        SKPoint[] expandedPositions = new SKPoint[indexes.Count];
        SKColor[]? expandedColors = colors is null ? null : new SKColor[indexes.Count];

        for (int i = 0; i < indexes.Count; i++)
        {
            expandedPositions[i] = positions[indexes[i]];

            if (expandedColors is not null)
            {
                expandedColors[i] = colors![indexes[i]];
            }
        }

        return SkiaMemoryPressure.CreateVertices(expandedPositions, expandedColors);
    }
}

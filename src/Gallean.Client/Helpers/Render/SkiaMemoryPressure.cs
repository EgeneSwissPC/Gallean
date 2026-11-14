using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Gallean.Client;

internal static class SkiaMemoryPressure
{
    private sealed class Token
    {
        private readonly long bytes;

        public Token(long bytes)
        {
            this.bytes = bytes;
            GC.AddMemoryPressure(bytes);
        }

        public long Bytes => bytes;

        ~Token()
        {
            GC.RemoveMemoryPressure(bytes);
        }
    }

    private static readonly ConditionalWeakTable<SKObject, Token> Tokens = new();

    public static void TrackImage(SKImage image)
    {
        TrackObject(image, (long)image.Width * image.Height * 4);
    }

    public static SKVertices CreateVertices(SKPoint[] positions, SKColor[]? colors = null, ushort[]? indexes = null)
    {
        SKVertices vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, null, colors, indexes);
        long bytes = (positions.LongLength * 8) + ((colors?.LongLength ?? 0) * 4) + ((indexes?.LongLength ?? 0) * 2);

        TrackObject(vertices, bytes);

        return vertices;
    }

    internal static long? GetTrackedBytes(SKObject obj)
    {
        return Tokens.TryGetValue(obj, out Token? token) ? token.Bytes : null;
    }

    private static void TrackObject(SKObject obj, long bytes)
    {
        Tokens.AddOrUpdate(obj, new Token(bytes));
    }
}

using SkiaSharp;

namespace Gallean.Client.Tests;

public sealed class TileMeshVerticesTests
{
    // Builds a grid of square tiles the way the world tile meshes do (shared corners, two triangles per tile),
    // draws it black on white and returns the number of pixels left white.
    private static int CountUncoveredPixels(int size)
    {
        Dictionary<(int X, int Y), int> corners = [];
        List<SKPoint> positions = [];
        List<int> indexes = [];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int top = Corner(x, y);
                int right = Corner(x + 1, y);
                int bottom = Corner(x + 1, y + 1);
                int left = Corner(x, y + 1);

                indexes.AddRange([top, right, bottom, top, bottom, left]);
            }
        }

        using SKVertices vertices = TileMeshVertices.Create(positions, indexes);
        using SKSurface surface = SKSurface.Create(new SKImageInfo(size, size));
        using SKPaint paint = new() { Color = SKColors.Black };

        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawVertices(vertices, SKBlendMode.Dst, paint);

        using SKPixmap pixmap = surface.PeekPixels();
        int uncovered = 0;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (pixmap.GetPixelColor(x, y) != SKColors.Black)
                {
                    uncovered++;
                }
            }
        }

        return uncovered;

        int Corner(int x, int y)
        {
            if (!corners.TryGetValue((x, y), out int index))
            {
                index = positions.Count;
                positions.Add(new SKPoint(x, y));
                corners[(x, y)] = index;
            }

            return index;
        }
    }

    [Test]
    public void SmallMeshTest()
    {
        Assert.That(CountUncoveredPixels(144), Is.Zero);
    }

    [Test]
    public void LargeMeshTest()
    {
        // 300 x 300 tiles have 90,601 corners, more than 16-bit indexes can address.
        Assert.That((300 + 1) * (300 + 1), Is.GreaterThan(TileMeshVertices.MaxIndexedVertices));
        Assert.That(CountUncoveredPixels(300), Is.Zero);
    }
}

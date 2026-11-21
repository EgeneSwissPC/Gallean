using Gallean.Common;

namespace Gallean.Client.Tests;

public sealed class FogWindowMaskTests
{
    [Test]
    [TestCase(70, 40, 1, 1)]
    [TestCase(70, 40, 2, 3)]
    [TestCase(64, 20, 3, 3)]
    [TestCase(13, 9, 2, 1)]
    public void MatchesWindowScanTest(int width, int height, int footprintWidth, int footprintHeight)
    {
        Random random = new(width * 31 + footprintWidth);
        ImmutableBitmap.Builder builder = ImmutableBitmap.Create(width, height).ToBuilder();

        // Sparse explored tiles, so most windows are empty and the ones that aren't hit the window's edges.
        for (int i = 0; i < width * height / 40; i++)
        {
            builder.Set(random.Next(width), random.Next(height));
        }

        ImmutableBitmap fog = builder.ToImmutable();
        FogWindowMask mask = FogWindowMask.For(fog);
        int visible = 0;

        for (int y = 0; y + footprintHeight <= height; y++)
        {
            for (int x = 0; x + footprintWidth <= width; x++)
            {
                WorldFootprint footprint = new(new WorldPosition(x, y), footprintWidth, footprintHeight);
                bool expected = Scan(fog, x, y, footprintWidth, footprintHeight);

                Assert.That(mask.IsAnyExploredAround(footprint), Is.EqualTo(expected), $"footprint at {x},{y}");
                visible += expected ? 1 : 0;
            }
        }

        Assert.That(visible, Is.GreaterThan(0));
        Assert.That(FogWindowMask.For(fog), Is.SameAs(mask));
    }

    // The composer's original check: any explored tile from 7 up and left of the footprint to its far corner.
    private static bool Scan(ImmutableBitmap fog, int left, int top, int footprintWidth, int footprintHeight)
    {
        for (int y = top - FogWindowMask.Padding; y < top + footprintHeight; y++)
        {
            for (int x = left - FogWindowMask.Padding; x < left + footprintWidth; x++)
            {
                if (x >= 0 && y >= 0 && x < fog.Width && y < fog.Height && fog.Get(x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

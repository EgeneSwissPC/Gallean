using SkiaSharp;

namespace Gallean.Client.Tests;

public sealed class SkiaMemoryPressureTests
{
    [Test]
    public void TrackImageTest()
    {
        using SKImage image = SKImage.Create(new SKImageInfo(16, 8));

        SkiaMemoryPressure.TrackImage(image);

        Assert.That(SkiaMemoryPressure.GetTrackedBytes(image), Is.EqualTo(16 * 8 * 4));
    }

    [Test]
    public void CreateVerticesTest()
    {
        SKPoint[] positions = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];
        SKColor[] colors = [SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.White];
        ushort[] indexes = [0, 1, 2, 1, 3, 2];

        using SKVertices vertices = SkiaMemoryPressure.CreateVertices(positions, colors, indexes);

        Assert.That(SkiaMemoryPressure.GetTrackedBytes(vertices), Is.EqualTo((4 * 8) + (4 * 4) + (6 * 2)));
    }

    [Test]
    public void CreateVerticesWithoutColorsTest()
    {
        SKPoint[] positions = [new(0, 0), new(1, 0), new(0, 1)];

        using SKVertices vertices = SkiaMemoryPressure.CreateVertices(positions);

        Assert.That(SkiaMemoryPressure.GetTrackedBytes(vertices), Is.EqualTo(3 * 8));
    }
}

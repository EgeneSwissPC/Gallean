using SkiaSharp;

namespace Gallean.Client.Tests;

public sealed class AtlasTextureTrackerTests
{
    private static AtlasTexture BuildTexture(SKColor color)
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(4, 4));
        surface.Canvas.Clear(color);
        using SKImage source = surface.Snapshot();

        return AtlasTexture.Build(new HashSet<AtlasTexture.RegionKey> { new(source, 4, 4) });
    }

    private static bool IsDisposed(AtlasTexture texture)
    {
        return texture.Image.Handle == IntPtr.Zero;
    }

    [Test]
    public void ReleaseUnusedTest()
    {
        AtlasTextureTracker tracker = new();
        AtlasTexture used = tracker.Add(BuildTexture(SKColors.Red));
        AtlasTexture unused = tracker.Add(BuildTexture(SKColors.Blue));

        tracker.ReleaseUnused([used]);

        Assert.That(IsDisposed(unused), Is.True);
        Assert.That(IsDisposed(used), Is.False);
        Assert.That(tracker.Count, Is.EqualTo(1));

        tracker.ReleaseUnused([]);

        Assert.That(IsDisposed(used), Is.True);
        Assert.That(tracker.Count, Is.Zero);
    }

    [Test]
    public void AddTwiceTest()
    {
        AtlasTextureTracker tracker = new();
        AtlasTexture texture = BuildTexture(SKColors.Red);

        tracker.Add(texture);
        tracker.Add(texture);

        Assert.That(tracker.Count, Is.EqualTo(1));
    }
}

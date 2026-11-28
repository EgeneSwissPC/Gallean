using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

public sealed class AssetPackLoaderTests
{
    [Test]
    public void EnumeratePathsIgnoresExtensionCasingTest()
    {
        using Sandbox sb = Sandbox.Create();
        string lower = sb.WriteTextFile("base.galleanpack", "");
        string mixed = sb.WriteTextFile("Overlay.GalleanPack", "");
        sb.WriteTextFile("notes.txt", "");

        Assert.That(AssetProvider.PackLoader.EnumeratePaths(sb.RootPath).Order(), Is.EqualTo(new[] { lower, mixed }.Order()));
    }

    [Test]
    public void FindsAssetsDirectoryWithAnyCasingTest()
    {
        using Sandbox sb = Sandbox.Create();
        string assets = sb.CreateDirectory("assets");

        Assert.That(AssetProvider.PackLoader.GetAssetsDirectory(sb.RootPath), Is.EqualTo(assets));
    }

    [Test]
    public void PrefersExactAssetsDirectoryTest()
    {
        using Sandbox sb = Sandbox.Create();

        Assert.That(AssetProvider.PackLoader.GetAssetsDirectory(sb.RootPath), Is.EqualTo(Path.Combine(sb.RootPath, "Assets")));
    }

    [Test]
    public void OverlayPacksFromEveryDirectoryTest()
    {
        using Sandbox sb = Sandbox.Create();
        string shipped = sb.CreateDirectory("game/Assets");
        string data = sb.CreateDirectory("data/Assets");
        WritePack(Path.Combine(data, "base.galleanpack"), sparse: false, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "ground/human/0/0");
        WritePack(Path.Combine(shipped, "unit.galleanpack"), sparse: true, new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), "unit-face/g000uu5990");

        IReadOnlyList<AssetPack>? packs = AssetProvider.PackLoader.LoadFrom([shipped, data]);

        try
        {
            Assert.That(packs?.Select(static pack => pack.Manifest.Sparse), Is.EqualTo(new[] { false, true }));
        }
        finally
        {
            foreach (AssetPack pack in packs ?? [])
            {
                pack.Dispose();
            }
        }
    }

    [Test]
    public void OverlayPacksWithoutBasePackLoadNothingTest()
    {
        using Sandbox sb = Sandbox.Create();
        string shipped = sb.CreateDirectory("game/Assets");
        WritePack(Path.Combine(shipped, "unit.galleanpack"), sparse: true, DateTimeOffset.UnixEpoch, "unit-face/g000uu5990");

        Assert.That(AssetProvider.PackLoader.LoadFrom([shipped]), Is.Null);
    }

    private static void WritePack(string path, bool sparse, DateTimeOffset createdAt, string spriteId)
    {
        using AssetPack.Writer writer = AssetPack.Writer.Create(path, sparse, createdAt, "test");
        AssetSpriteMetadata metadata = new(1, 1, PixelWidth: 1, PixelHeight: 1, 0, 0, Additive: false);
        writer.AddSprite(spriteId, new AssetPack.Writer.EncodedFrame(metadata, new AssetPack.Writer.EncodedImage(1, 1, new byte[] { 1 }), []), []);
        writer.Complete();
    }
}

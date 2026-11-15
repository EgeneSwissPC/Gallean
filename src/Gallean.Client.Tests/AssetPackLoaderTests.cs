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
}

namespace Gallean.Configuration.Tests;

public sealed class AppDirectoriesTests
{
    // gallean-upgrade must not share settings, saves or asset packs with the official game in ~/.local/share/Gallean.
    [Test]
    public void RootIsSeparateFromOfficialGameTest()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.That(AppDirectories.Root, Is.EqualTo(Path.Combine(localApplicationData, "GalleanUpgrade")));
        Assert.That(AppDirectories.Saves, Does.StartWith(AppDirectories.Root));
        Assert.That(AppDirectories.Assets, Does.StartWith(AppDirectories.Root));
    }
}

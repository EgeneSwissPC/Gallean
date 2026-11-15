using Gallean.Tests.Common;

namespace GameContent.Tests;

public sealed class GamePathTests
{
    [Test]
    public void ExactMatchTest()
    {
        using Sandbox sb = Sandbox.Create();
        string expected = sb.WriteTextFile("IsoAnim.ff", "");

        Assert.That(GamePath.Resolve(sb.RootPath, "IsoAnim.ff"), Is.EqualTo(expected));
    }

    [Test]
    public void DifferentFileCasingTest()
    {
        using Sandbox sb = Sandbox.Create();
        string expected = sb.WriteTextFile("ISOANIM.FF", "");

        Assert.That(GamePath.Resolve(sb.RootPath, "IsoAnim.ff"), Is.EqualTo(expected));
    }

    [Test]
    public void DifferentDirectoryCasingTest()
    {
        using Sandbox sb = Sandbox.Create();
        string expected = sb.CreateDirectory("imgs");

        Assert.That(GamePath.Resolve(sb.RootPath, "Imgs"), Is.EqualTo(expected));
    }

    [Test]
    public void MissingTest()
    {
        using Sandbox sb = Sandbox.Create();

        Assert.That(GamePath.Resolve(sb.RootPath, "Music"), Is.EqualTo(Path.Combine(sb.RootPath, "Music")));
    }

    [Test]
    public void MissingDirectoryTest()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.That(GamePath.Resolve(root, "Imgs"), Is.EqualTo(Path.Combine(root, "Imgs")));
    }

    [Test]
    public void GameFilesTest()
    {
        using Sandbox sb = Sandbox.Create();
        string images = sb.CreateDirectory("IMGS");
        string interfaces = sb.CreateDirectory("interf");
        string music = sb.CreateDirectory("music");
        string briefing = sb.CreateDirectory("BRIEFING");
        string sounds = sb.CreateDirectory("sounds");
        string scenarioData = sb.CreateDirectory("scendata");

        GameFiles files = new(sb.RootPath);

        Assert.That(files.ImagesRoot, Is.EqualTo(images));
        Assert.That(files.InterfaceRoot, Is.EqualTo(interfaces));
        Assert.That(files.MusicRoot, Is.EqualTo(music));
        Assert.That(files.BriefingRoot, Is.EqualTo(briefing));
        Assert.That(files.SoundsRoot, Is.EqualTo(sounds));
        Assert.That(files.ScenarioDataRoot, Is.EqualTo(scenarioData));
    }
}

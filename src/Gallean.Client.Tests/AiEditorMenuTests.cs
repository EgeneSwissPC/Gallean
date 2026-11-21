using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: the AI editor mode's list screen (docs/ai-designer.md, phase 2).
[NonParallelizable]
public sealed class AiEditorMenuTests
{
    private string previousDirectory = "";
    private Sandbox? sandbox;

    [SetUp]
    public void SetUp()
    {
        sandbox = Sandbox.Create();
        previousDirectory = DesignedAiLibrary.Directory;
        DesignedAiLibrary.Directory = sandbox.GetPath("AIs/map");
    }

    [TearDown]
    public void TearDown()
    {
        DesignedAiLibrary.Directory = previousDirectory;
        AiBrainRegistry.SetDesignedWorldAis([]);
        sandbox?.Dispose();
    }

    private static async Task<(GameViewModel Game, AiEditorMenuScreenViewModel Menu)> OpenAsync()
    {
        GameViewModel gameViewModel = new TestGameViewModelBuilder().Build();
        await gameViewModel.ShowMainMenuCommand.ExecuteAsync(null);
        gameViewModel.ShowAiEditorCommand.Execute(null);

        return (gameViewModel, Assert.IsOfType<AiEditorMenuScreenViewModel>(gameViewModel.RootScreen));
    }

    [Test]
    public async Task CreateDuplicateDeleteTest()
    {
        (GameViewModel game, AiEditorMenuScreenViewModel menu) = await OpenAsync();

        Assert.That(menu.Ais, Is.Empty);
        Assert.That(menu.Sources.Select(option => option.Value), Is.EqualTo(AiBrainRegistry.WorldBrains.Select(info => info.Id)));
        Assert.That(menu.EditCommand.CanExecute(null), Is.False);

        menu.Name = "Raider";
        menu.SelectedSource = menu.Sources.Single(option => option.Value == AiPersonalities.AggressiveId);
        menu.CreateCommand.Execute(null);

        AiEditorItem raider = menu.Ais.Single();
        Assert.That((raider.Id, raider.Name), Is.EqualTo(("custom:raider", "Raider")));
        Assert.That(raider.Details, Does.StartWith("1 stage"));
        Assert.That(menu.SelectedAi, Is.EqualTo(raider));
        Assert.That(File.Exists(Path.Combine(DesignedAiLibrary.Directory, "raider.json")), Is.True);

        // It is a real AI now: the registry, the formatter and a new Settings screen know it.
        Assert.That(AiBrainRegistry.ResolveWorld("custom:raider"), Is.InstanceOf<DesignedWorldAiBrain>());
        Assert.That(AiBrainFormatter.FormatName("custom:raider"), Is.EqualTo("Raider"));
        Assert.That(new SettingsScreenViewModel(game).WorldAis, Does.Contain("custom:raider"));
        // The new source list offers it to start from as well.
        Assert.That(menu.Sources.Select(option => option.Value), Does.Contain("custom:raider"));

        menu.DuplicateCommand.Execute(null);
        Assert.That(menu.Ais.Select(item => item.Name), Is.EqualTo(new[] { "Raider", "Raider (copy)" }));
        Assert.That(menu.SelectedAi!.Id, Is.EqualTo("custom:raider-copy"));
        DesignedWorldAi copy = DesignedAiLibrary.Store.LoadAll().Single(design => design.Id == "custom:raider-copy");
        DesignedWorldAi original = DesignedAiLibrary.Store.LoadAll().Single(design => design.Id == "custom:raider");
        Assert.That(copy.Stages, Is.EqualTo(original.Stages));

        menu.SelectedAi = menu.Ais[0];
        menu.DeleteCommand.Execute(null);
        ConfirmPromptViewModel prompt = Assert.IsOfType<ConfirmPromptViewModel>(game.TopScreen);
        await prompt.ConfirmCommand.ExecuteAsync(null);

        Assert.That(menu.Ais.Select(item => item.Id), Is.EqualTo(new[] { "custom:raider-copy" }));
        Assert.That(AiBrainRegistry.ResolveWorld("custom:raider").Info.Id, Is.EqualTo(AiPersonalities.ClassicPlusId));
    }

    [Test]
    public async Task BackGoesToMainMenuTest()
    {
        (GameViewModel game, AiEditorMenuScreenViewModel menu) = await OpenAsync();

        menu.BackCommand.Execute(null);

        Assert.IsOfType<MainMenuScreenViewModel>(game.RootScreen);
    }

    [TestCase("Raider", new string[0], "raider")]
    [TestCase("Raider", new[] { "custom:raider" }, "raider-2")]
    [TestCase("Raider", new[] { "custom:raider", "custom:raider-2" }, "raider-3")]
    [TestCase("  Злой ИИ! ", new string[0], "ai")]
    [TestCase("Turtle, then strike", new string[0], "turtle-then-strike")]
    public void FileNameTest(string name, string[] existing, string expected)
    {
        Assert.That(DesignedAiLibrary.MakeFileName(name, existing), Is.EqualTo(expected));
    }
}

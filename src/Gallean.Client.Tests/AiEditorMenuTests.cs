using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: the AI editor mode's list screen (docs/ai-designer.md, phase 2).
[NonParallelizable]
public sealed class AiEditorMenuTests
{
    private string previousDirectory = "";
    private string previousBattleDirectory = "";
    private Sandbox? sandbox;

    [SetUp]
    public void SetUp()
    {
        sandbox = Sandbox.Create();
        previousDirectory = DesignedAiLibrary.Directory;
        DesignedAiLibrary.Directory = sandbox.GetPath("AIs/map");
        previousBattleDirectory = DesignedBattleAiLibrary.Directory;
        DesignedBattleAiLibrary.Directory = sandbox.GetPath("AIs/battle");
    }

    [TearDown]
    public void TearDown()
    {
        DesignedAiLibrary.Directory = previousDirectory;
        AiBrainRegistry.SetDesignedWorldAis([]);
        DesignedBattleAiLibrary.Directory = previousBattleDirectory;
        AiBrainRegistry.SetDesignedBattleAis([]);
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
    public async Task BattleAisTabTest()
    {
        (GameViewModel game, AiEditorMenuScreenViewModel menu) = await OpenAsync();
        DesignedAiLibrary.Store.Save(DesignedWorldAi.FromBuiltIn(AiPersonalities.AggressiveId, "raider", "Raider"));

        menu.ShowBattleAisCommand.Execute(null);

        Assert.That(menu.IsBattle, Is.True);
        Assert.That(menu.Ais, Is.Empty, "map AIs are not listed on the battle tab");
        Assert.That(menu.Sources.Select(option => option.Value), Is.EqualTo(AiBrainRegistry.BattleBrains.Select(info => info.Id)));

        menu.Name = "Bold";
        menu.SelectedSource = menu.Sources.Single(option => option.Value == AiPersonalities.RuthlessId);
        menu.CreateCommand.Execute(null);

        AiEditorItem bold = menu.Ais.Single();
        Assert.That((bold.Id, bold.Name), Is.EqualTo(("custom-battle:bold", "Bold")));
        Assert.That(File.Exists(Path.Combine(DesignedBattleAiLibrary.Directory, "bold.json")), Is.True);
        // A new battle AI opens straight in the battle designer; Back returns to the battle tab.
        AiDesignerScreenViewModel designer = Assert.IsOfType<AiDesignerScreenViewModel>(game.RootScreen);
        Assert.That(designer.IsBattle, Is.True);
        designer.BackCommand.Execute(null);
        menu = Assert.IsOfType<AiEditorMenuScreenViewModel>(game.RootScreen);
        Assert.That(menu.IsBattle, Is.True);
        Assert.That(menu.EditCommand.CanExecute(null), Is.True);

        // A copy of Ruthless keeps its knobs, and it is a real battle AI everywhere.
        DesignedBattleAi saved = DesignedBattleAiLibrary.Store.LoadAll().Single();
        Assert.That(saved.Stages.Single().Tactics, Is.EqualTo(DesignedBattleTactics.Classic with { NeverFlee = true, CanDefend = false }));
        Assert.That(AiBrainRegistry.ResolveBattle("custom-battle:bold"), Is.InstanceOf<DesignedBattleAiBrain>());
        Assert.That(AiBrainFormatter.FormatName("custom-battle:bold"), Is.EqualTo("Bold"));
        Assert.That(new SettingsScreenViewModel(game).BattleAis, Does.Contain("custom-battle:bold"));
        Assert.That(new AiPlayersScreenViewModel([], _ => Task.CompletedTask).BattleAis, Does.Contain("custom-battle:bold"));
        Assert.That(new SettingsScreenViewModel(game).WorldAis, Does.Not.Contain("custom-battle:bold"));

        menu.DuplicateCommand.Execute(null);
        Assert.That(menu.Ais.Select(item => item.Id), Is.EqualTo(new[] { "custom-battle:bold", "custom-battle:bold-copy" }));

        menu.SelectedAi = menu.Ais[0];
        menu.DeleteCommand.Execute(null);
        await Assert.IsOfType<ConfirmPromptViewModel>(game.TopScreen).ConfirmCommand.ExecuteAsync(null);
        Assert.That(menu.Ais.Select(item => item.Id), Is.EqualTo(new[] { "custom-battle:bold-copy" }));
        Assert.That(AiBrainRegistry.ResolveBattle("custom-battle:bold").Info.Id, Is.EqualTo(AiBrainRegistry.ClassicId), "a deleted battle AI plays Classic");

        menu.ShowMapAisCommand.Execute(null);
        Assert.That(menu.Ais.Select(item => item.Id), Is.EqualTo(new[] { "custom:raider" }));
        Assert.That(menu.EditCommand.CanExecute(null), Is.True);
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

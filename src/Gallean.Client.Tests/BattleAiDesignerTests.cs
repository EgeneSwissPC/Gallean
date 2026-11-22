using Gallean.Common;
using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: the battle AI designer screen (docs/battle-ai-designer.md phase 9), the map AI designer screen
// working on a DesignedBattleAi.
[NonParallelizable]
public sealed class BattleAiDesignerTests
{
    private string previousDirectory = "";
    private string previousArmies = "";
    private Sandbox? sandbox;

    [SetUp]
    public void SetUp()
    {
        sandbox = Sandbox.Create();
        previousDirectory = DesignedBattleAiLibrary.Directory;
        DesignedBattleAiLibrary.Directory = sandbox.GetPath("AIs/battle");
        previousArmies = BattleSandboxScreenViewModel.ArmiesDirectory;
        BattleSandboxScreenViewModel.ArmiesDirectory = sandbox.GetPath("armies");
    }

    [TearDown]
    public void TearDown()
    {
        DesignedBattleAiLibrary.Directory = previousDirectory;
        BattleSandboxScreenViewModel.ArmiesDirectory = previousArmies;
        AiBrainRegistry.SetDesignedBattleAis([]);
        sandbox?.Dispose();
    }

    // Every condition and every tactic, so the round trip covers them all.
    private static DesignedBattleAi Courier => new("custom-battle:courier", "Careful courier", [
        new DesignedBattleStage("Rich and losing",
            [[new DesignedBattleCondition.CargoAbove(1500), new DesignedBattleCondition.PowerShareBelow(45)], [new DesignedBattleCondition.CarriesQuestItem()], [new DesignedBattleCondition.CargoAbove(3000, IncludeEquipped: true)]],
            DesignedBattleTactics.Classic with { ProtectLeader = true, FleeBelowPercent = 60, LeaderDefendsBelowPercent = 40 }),
        new DesignedBattleStage("Late",
            [[new DesignedBattleCondition.FromRound(4), new DesignedBattleCondition.PowerShareAbove(55), new DesignedBattleCondition.LeaderHitpointsBelow(70)]],
            DesignedBattleTactics.Classic with { HealersFirst = 100, CastersFirst = 150, LeaderFirst = 200, HardestHitterFirst = 50, WoundedFirst = 80, FocusFire = 120, NeverFlee = true, CanDefend = false }),
        DesignedBattleStage.Classic
    ]);

    private async Task<(GameViewModel Game, AiDesignerScreenViewModel Designer)> OpenAsync(DesignedBattleAi design)
    {
        DesignedBattleAiLibrary.Store.Save(design);
        GameViewModel game = new TestGameViewModelBuilder()
            .WithGameLoader(new GameLoader(new RandomProvider(0), sandbox!.GetPath("maps")))
            .Build();
        await game.ShowMainMenuCommand.ExecuteAsync(null);
        game.ShowAiEditorCommand.Execute(null);
        AiEditorMenuScreenViewModel menu = Assert.IsOfType<AiEditorMenuScreenViewModel>(game.RootScreen);
        menu.ShowBattleAisCommand.Execute(null);
        menu.SelectedAi = menu.Ais.Single(item => item.Id == design.Id);
        menu.EditCommand.Execute(null);

        return (game, Assert.IsOfType<AiDesignerScreenViewModel>(game.RootScreen));
    }

    private static AiDesignerPaletteItem Palette(AiDesignerScreenViewModel designer, Func<AiDesignerPaletteItem, bool> match)
    {
        return designer.Palette.SelectMany(group => group.Items).Single(match);
    }

    [Test]
    public async Task LoadsAndRoundTripsTest()
    {
        (_, AiDesignerScreenViewModel designer) = await OpenAsync(Courier);

        Assert.That(designer.IsBattle, Is.True);
        Assert.That(designer.Stages, Has.Count.EqualTo(3));
        Assert.That(designer.Stages.Select(stage => stage.ShowSlots), Is.All.False);
        Assert.That(designer.Stages[0].Groups, Has.Count.EqualTo(3));
        Assert.That(designer.Stages[1].Rules, Has.Count.EqualTo(8));
        Assert.That(designer.ToBattleDesign(), Is.EqualTo(Courier));
        Assert.That(designer.HasChanges, Is.False);
        Assert.That(designer.HasProblems, Is.False, designer.ProblemText);

        // Only battle blocks are offered; map actions are not.
        Assert.That(designer.Palette.SelectMany(group => group.Items).Select(item => item.Category).Distinct(),
            Is.EquivalentTo(new[] { AiDesignerCategory.Condition, AiDesignerCategory.Target, AiDesignerCategory.Behaviour }));
        Assert.That(designer.Palette.SelectMany(group => group.Items).Count(), Is.EqualTo(7 + 6 + 5));
    }

    [Test]
    public async Task EditSaveUndoTest()
    {
        DesignedBattleAi plain = DesignedBattleAi.FromClassic("plain", "Plain");
        (GameViewModel game, AiDesignerScreenViewModel designer) = await OpenAsync(plain);

        // A click on a tactic adds it to the selected (only) stage; a second click doesn't add it twice.
        designer.AddBlockCommand.Execute(Palette(designer, item => item.Rule == AiDesignerRule.LeaderFirst));
        designer.AddBlockCommand.Execute(Palette(designer, item => item.Rule == AiDesignerRule.LeaderFirst));
        designer.AddBlockCommand.Execute(Palette(designer, item => item.Rule == AiDesignerRule.NeverFlee));
        Assert.That(designer.ToBattleDesign().Stages.Single().Tactics, Is.EqualTo(DesignedBattleTactics.Classic with { LeaderFirst = 100, NeverFlee = true }));

        // A condition can't go on the default stage; + Stage adds one with "from round 3".
        designer.AddBlockCommand.Execute(Palette(designer, item => item.Condition == AiDesignerCondition.CargoAbove));
        Assert.That(designer.StatusText, Is.Not.Empty);
        designer.AddStageCommand.Execute(null);
        designer.AddBlockCommand.Execute(Palette(designer, item => item.Condition == AiDesignerCondition.CargoAbove));
        designer.AddBlockCommand.Execute(Palette(designer, item => item.Rule == AiDesignerRule.ProtectLeader));
        DesignedBattleStage added = designer.ToBattleDesign().Stages[0];
        Assert.That(added.When.Single(), Is.EqualTo(new DesignedBattleCondition[] { new DesignedBattleCondition.FromRound(3), new DesignedBattleCondition.CargoAbove(1000) }));
        Assert.That(added.Tactics, Is.EqualTo(DesignedBattleTactics.Classic with { ProtectLeader = true }));
        Assert.That(designer.HasChanges, Is.True);

        designer.UndoCommand.Execute(null);
        Assert.That(designer.ToBattleDesign().Stages[0].Tactics.ProtectLeader, Is.False);
        designer.RedoCommand.Execute(null);
        Assert.That(designer.ToBattleDesign().Stages[0].Tactics.ProtectLeader, Is.True);

        designer.Name = "Bold courier";
        designer.SaveCommand.Execute(null);
        Assert.That(designer.HasChanges, Is.False);
        DesignedBattleAi saved = DesignedBattleAiLibrary.Store.LoadAll().Single();
        Assert.That(saved, Is.EqualTo(designer.ToBattleDesign()));
        Assert.That(AiBrainFormatter.FormatName("custom-battle:plain"), Is.EqualTo("Bold courier"));

        // Back goes to the AI editor's battle tab with this AI selected.
        designer.BackCommand.Execute(null);
        AiEditorMenuScreenViewModel menu = Assert.IsOfType<AiEditorMenuScreenViewModel>(game.RootScreen);
        Assert.That(menu.IsBattle, Is.True);
        Assert.That(menu.SelectedAi!.Id, Is.EqualTo("custom-battle:plain"));
    }

    [Test]
    public async Task NewBattleAiOpensInTheDesignerTest()
    {
        GameViewModel game = new TestGameViewModelBuilder().Build();
        await game.ShowMainMenuCommand.ExecuteAsync(null);
        game.ShowAiEditorCommand.Execute(null);
        AiEditorMenuScreenViewModel menu = Assert.IsOfType<AiEditorMenuScreenViewModel>(game.RootScreen);
        menu.ShowBattleAisCommand.Execute(null);
        menu.Name = "Berserker";
        menu.SelectedSource = menu.Sources.Single(option => option.Value == AiPersonalities.RuthlessId);
        menu.CreateCommand.Execute(null);

        AiDesignerScreenViewModel designer = Assert.IsOfType<AiDesignerScreenViewModel>(game.RootScreen);
        Assert.That(designer.IsBattle, Is.True);
        Assert.That(designer.Stages.Single().Rules.Select(block => block.Item.Rule), Is.EquivalentTo(new AiDesignerRule?[] { AiDesignerRule.NeverFlee, AiDesignerRule.NeverDefend }));
    }

    [Test]
    public async Task TestOpensTheSandboxWithThisAiTest()
    {
        (GameViewModel game, AiDesignerScreenViewModel designer) = await OpenAsync(DesignedBattleAi.FromClassic("plain", "Plain"));

        // Unsaved changes play in the sandbox too.
        designer.AddBlockCommand.Execute(Palette(designer, item => item.Rule == AiDesignerRule.HealersFirst));
        await designer.TestInSandboxCommand.ExecuteAsync(null);

        BattleSandboxScreenViewModel screen = Assert.IsOfType<BattleSandboxScreenViewModel>(game.RootScreen);
        Assert.That(screen.Attacker.SelectedAi.Value, Is.EqualTo("custom-battle:plain"));
        Assert.That(((DesignedBattleAiBrain)AiBrainRegistry.ResolveBattle("custom-battle:plain")).Design.Stages.Single().Tactics.HealersFirst, Is.EqualTo(100));

        screen.CloseCommand.Execute(null);
        Assert.That(game.RootScreen, Is.SameAs(designer));
        Assert.That(designer.HasChanges, Is.True, "the designer keeps its unsaved changes");
    }
}

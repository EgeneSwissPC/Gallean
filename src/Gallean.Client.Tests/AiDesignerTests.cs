using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: the AI designer screen (docs/ai-designer.md, phase 3).
[NonParallelizable]
public sealed class AiDesignerTests
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

    private static DesignedWorldAi TwoStages => new("custom:two", "Two stages", [
        DesignedWorldStage.Classic with
        {
            Name = "Late",
            When = [[new DesignedStageCondition.FromTurn(15), new DesignedStageCondition.GoldAbove(300)], [new DesignedStageCondition.LostCity()]],
            Rules = new DesignedWorldRules(HurtPercent: 60, HealedPercent: 85, AttackPowerPercent: 80, WaitForFreeCapital: true)
        },
        DesignedWorldStage.Classic
    ]);

    private static async Task<(GameViewModel Game, AiDesignerScreenViewModel Designer)> OpenAsync(DesignedWorldAi design)
    {
        DesignedAiLibrary.Store.Save(design);
        GameViewModel game = new TestGameViewModelBuilder().Build();
        await game.ShowMainMenuCommand.ExecuteAsync(null);
        game.ShowAiEditorCommand.Execute(null);
        AiEditorMenuScreenViewModel menu = Assert.IsOfType<AiEditorMenuScreenViewModel>(game.RootScreen);
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
        (_, AiDesignerScreenViewModel designer) = await OpenAsync(TwoStages);

        Assert.That(designer.Stages.Select(stage => (stage.Name, stage.IsDefault)), Is.EqualTo(new[] { ("Late", false), ("Default", true) }));
        Assert.That(designer.Stages[0].Groups.Select(group => group.Blocks.Count), Is.EqualTo(new[] { 2, 1 }));
        Assert.That(designer.Stages[0].Army.Tiers.Select(tier => tier.Blocks.Count), Is.EqualTo(new[] { 1, 1, 1, 1, 5, 1 }));
        Assert.That(designer.Stages[0].Rules.Select(block => block.Item.Rule), Is.EqualTo(new AiDesignerRule?[] { AiDesignerRule.Heal, AiDesignerRule.AttackPower, AiDesignerRule.WaitForFreeCapital }));
        Assert.That(designer.ToDesign(), Is.EqualTo(TwoStages));
        Assert.That(designer.HasChanges, Is.False);
        Assert.That(designer.HasProblems, Is.False);
    }

    [Test]
    public async Task EditUndoRedoTest()
    {
        (_, AiDesignerScreenViewModel designer) = await OpenAsync(TwoStages);
        AiDesignerStageViewModel late = designer.Stages[0];

        // Remove "Move and explore" from the army rows: it is never done in this stage.
        AiDesignerBlockViewModel move = late.Army.Tiers[^1].Blocks.Single();
        designer.RemoveBlockCommand.Execute(move);

        Assert.That(designer.ToDesign().Stages[0].Army.SelectMany(tier => tier).Contains(WorldAiActionKind.Move), Is.False);
        Assert.That(late.Army.UnusedText, Does.Contain("Move and explore"));
        Assert.That(designer.HasChanges, Is.True);

        designer.UndoCommand.Execute(null);
        Assert.That(designer.ToDesign(), Is.EqualTo(TwoStages));
        Assert.That(designer.HasChanges, Is.False);

        designer.RedoCommand.Execute(null);
        Assert.That(designer.ToDesign().Stages[0].Army, Has.Length.EqualTo(5));
    }

    [Test]
    public async Task DragBetweenRowsTest()
    {
        (_, AiDesignerScreenViewModel designer) = await OpenAsync(TwoStages);
        AiDesignerSlotViewModel army = designer.Stages[0].Army;

        // Attack moves out of the shared row into a new lowest row ...
        AiDesignerBlockViewModel attack = army.Tiers[4].Blocks.First(block => block.Item.Action == WorldAiActionKind.Attack);
        designer.MoveBlock(attack, army, int.MaxValue);

        Assert.That(designer.ToDesign().Stages[0].Army[^1], Is.EqualTo(new[] { WorldAiActionKind.Attack }));
        Assert.That(designer.ToDesign().Stages[0].Army[4], Does.Not.Contain(WorldAiActionKind.Attack));

        // ... then into the top row, next to "Use items".
        designer.MoveBlock(army.Tiers[^1].Blocks.Single(), army.Tiers[0], 1);
        Assert.That(designer.ToDesign().Stages[0].Army[0], Is.EqualTo(new[] { WorldAiActionKind.UseItem, WorldAiActionKind.Attack }));
        Assert.That(army.Tiers.Select(tier => tier.Number), Is.EqualTo(Enumerable.Range(1, army.Tiers.Count)));

        // A whole row moves up.
        AiDesignerTierViewModel last = army.Tiers[^1];
        designer.Stages[0].MoveTierUpCommand.Execute(last);
        Assert.That(army.Tiers[^2], Is.SameAs(last));

        // Army blocks don't go into the economy, conditions don't go into rows.
        Assert.That(AiDesignerScreenViewModel.Accepts(designer.Stages[0].Economy, attack.Item), Is.False);
        Assert.That(AiDesignerScreenViewModel.Accepts(army.Tiers[0], Palette(designer, item => item.Condition == AiDesignerCondition.LostCity)), Is.False);
    }

    [Test]
    public async Task PaletteClicksTest()
    {
        (_, AiDesignerScreenViewModel designer) = await OpenAsync(DesignedWorldAi.FromClassic("plain", "Plain"));
        AiDesignerStageViewModel defaultStage = designer.Stages.Single();

        // Conditions can't go on the default stage.
        designer.AddBlockCommand.Execute(Palette(designer, item => item.Condition == AiDesignerCondition.FromTurn));
        Assert.That(designer.StatusText, Is.Not.Empty);
        Assert.That(designer.HasChanges, Is.False);

        // An action that is already there isn't added twice.
        designer.AddBlockCommand.Execute(Palette(designer, item => item.Action == WorldAiActionKind.CastSpell));
        Assert.That(designer.HasChanges, Is.False);

        // A rule block, then its number.
        designer.AddBlockCommand.Execute(Palette(designer, item => item.Rule == AiDesignerRule.AttackPower));
        Assert.That(designer.ToDesign().Stages[0].Rules.AttackPowerPercent, Is.EqualTo(100));
        ((MapEditorBlockNumber)defaultStage.Rules.Single().Parts[1]).Text = "75";
        Assert.That(designer.ToDesign().Stages[0].Rules.AttackPowerPercent, Is.EqualTo(75));

        // + Stage adds a stage above the default one, with a "from turn 20" condition; conditions now go there.
        designer.AddStageCommand.Execute(null);
        Assert.That(designer.Stages.Select(stage => stage.IsDefault), Is.EqualTo(new[] { false, true }));
        Assert.That(designer.SelectedStage, Is.SameAs(designer.Stages[0]));
        designer.AddBlockCommand.Execute(Palette(designer, item => item.Condition == AiDesignerCondition.LostCity));
        Assert.That(designer.ToDesign().Stages[0].When.Single(), Is.EqualTo(new DesignedStageCondition[] { new DesignedStageCondition.FromTurn(20), new DesignedStageCondition.LostCity() }));

        designer.Stages[0].AddGroupCommand.Execute(null);
        designer.InsertBlock(Palette(designer, item => item.Condition == AiDesignerCondition.GoldAbove), designer.Stages[0].Groups[1], 0);
        Assert.That(designer.ToDesign().Stages[0].When, Has.Length.EqualTo(2));
    }

    [Test]
    public async Task ProblemsTest()
    {
        (_, AiDesignerScreenViewModel designer) = await OpenAsync(TwoStages);
        AiDesignerStageViewModel late = designer.Stages[0];

        foreach (AiDesignerBlockViewModel block in late.Groups.SelectMany(group => group.Blocks).ToList())
        {
            designer.RemoveBlockCommand.Execute(block);
        }

        Assert.That(late.HasProblem, Is.True);
        Assert.That(designer.ProblemText, Does.Contain("Late: no conditions"));

        foreach (AiDesignerBlockViewModel block in designer.Stages[1].Economy.Tiers.Concat(designer.Stages[1].Army.Tiers).SelectMany(tier => tier.Blocks).ToList())
        {
            designer.RemoveBlockCommand.Execute(block);
        }

        Assert.That(designer.Stages[1].HasProblem, Is.True);
        Assert.That(designer.ProblemText, Does.Contain("Default: the economy and army rows are empty"));
    }

    [Test]
    public async Task SaveAndLeaveTest()
    {
        (GameViewModel game, AiDesignerScreenViewModel designer) = await OpenAsync(TwoStages);

        designer.Name = "Renamed";
        designer.BackCommand.Execute(null);
        ConfirmPromptViewModel prompt = Assert.IsOfType<ConfirmPromptViewModel>(game.TopScreen);
        prompt.CloseCommand.Execute(null);
        Assert.That(game.RootScreen, Is.SameAs(designer));

        designer.SaveCommand.Execute(null);
        Assert.That(designer.HasChanges, Is.False);
        Assert.That(DesignedAiLibrary.Store.LoadAll().Single().Name, Is.EqualTo("Renamed"));
        Assert.That(AiBrainFormatter.FormatName("custom:two"), Is.EqualTo("Renamed"));

        designer.BackCommand.Execute(null);
        AiEditorMenuScreenViewModel menu = Assert.IsOfType<AiEditorMenuScreenViewModel>(game.RootScreen);
        Assert.That(menu.SelectedAi!.Name, Is.EqualTo("Renamed"));
    }

    [Test]
    public async Task CreateOpensDesignerTest()
    {
        GameViewModel game = new TestGameViewModelBuilder().Build();
        await game.ShowMainMenuCommand.ExecuteAsync(null);
        game.ShowAiEditorCommand.Execute(null);
        AiEditorMenuScreenViewModel menu = Assert.IsOfType<AiEditorMenuScreenViewModel>(game.RootScreen);

        menu.Name = "Fresh";
        menu.CreateCommand.Execute(null);

        AiDesignerScreenViewModel designer = Assert.IsOfType<AiDesignerScreenViewModel>(game.RootScreen);
        Assert.That(designer.Name, Is.EqualTo("Fresh"));
        Assert.That(designer.ToDesign().Stages.Single(), Is.EqualTo(DesignedWorldStage.Classic));
    }
}

using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: the AI designer's Test panel (docs/ai-designer.md, phase 4).
[NonParallelizable]
public sealed class AiDesignerTestPanelTests
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

    [Test]
    public async Task RunsUnsavedDesignTest()
    {
        DesignedWorldAi saved = DesignedWorldAi.FromClassic("tester", "Tester");
        DesignedAiLibrary.Store.Save(saved);
        DesignedAiLibrary.Reload();
        GameViewModel game = new TestGameViewModelBuilder().Build();
        AiDesignerScreenViewModel designer = new(game, saved);

        Assert.That(designer.TestMaps, Is.Not.Empty);
        Assert.That(designer.TestOpponent?.Value, Is.EqualTo(AiBrainRegistry.ClassicId));
        Assert.That(designer.TestOpponents.Select(option => option.Value), Does.Not.Contain("custom:tester"));

        // An unsaved second stage: the test plays the AI as it is on screen.
        designer.AddStageCommand.Execute(null);
        ((MapEditorBlockNumber)designer.Stages[0].Groups[0].Blocks[0].Parts[1]).Text = "3";
        designer.Stages[0].Name = "From turn 3";

        designer.TestMap = designer.TestMaps.First();
        designer.TestGamesText = "2";
        designer.TestTurnsText = "6";
        await designer.RunTestCommand.ExecuteAsync(null);

        Assert.That(designer.IsTesting, Is.False);
        Assert.That(designer.TestProgress, Does.StartWith("2 games of up to 6 turns"), designer.TestProgress);
        Assert.That(designer.TestResults, Has.Count.EqualTo(3));
        Assert.That(designer.TestResults[0], Does.StartWith("Tester: "));
        Assert.That(designer.TestResults[1], Does.StartWith("Classic: "));
        Assert.That(designer.TestResults[2], Does.Contain("From turn 3").And.Contain("Default"));

        // The saved file and the registry are untouched: the saved AI still has one stage.
        Assert.That(DesignedAiLibrary.Store.LoadAll().Single().Stages, Has.Length.EqualTo(1));
        Assert.That(((DesignedWorldAiBrain)AiBrainRegistry.ResolveWorld("custom:tester")).Design.Stages, Has.Length.EqualTo(1));
        Assert.That(designer.HasChanges, Is.True);
    }

    [Test]
    public async Task CancelTest()
    {
        DesignedWorldAi saved = DesignedWorldAi.FromClassic("tester", "Tester");
        DesignedAiLibrary.Store.Save(saved);
        DesignedAiLibrary.Reload();
        GameViewModel game = new TestGameViewModelBuilder().Build();
        AiDesignerScreenViewModel designer = new(game, saved);
        designer.TestGamesText = "20";
        designer.TestTurnsText = "100";

        Task run = designer.RunTestCommand.ExecuteAsync(null);
        Assert.That(designer.IsTesting, Is.True);
        designer.CancelTestCommand.Execute(null);
        await run;

        Assert.That(designer.IsTesting, Is.False);
        Assert.That(designer.TestProgress, Is.EqualTo("Test cancelled."));
        Assert.That(designer.TestResults, Is.Empty);
    }
}

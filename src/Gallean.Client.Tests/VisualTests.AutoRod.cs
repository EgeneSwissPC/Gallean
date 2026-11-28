using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Gallean.Common;
using Gallean.Configuration;
using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: auto rod, phase 2 (docs/auto-rod.md).
public sealed partial class VisualTests
{
    [AvaloniaTest]
    public async Task WorldAutoRodTest()
    {
        using Sandbox sb = Sandbox.Create();

        GameModel scenario = LoadScenario(null);
        WorldState state = scenario.World.State;
        WorldStack hero = Assert.NotNull(WorldPlan.For(state).GetStackAt(new WorldPosition(10, 14)));
        (WorldUnit heroLeader, _, _) = hero.GetLeader(scenario.Definitions);
        WorldUnit rodLeader = heroLeader with { DefinitionId = new UnitDefinitionId("G000UU0022") };
        WorldStack planter = hero with { Units = hero.Units.Replace(heroLeader, rodLeader) };
        GameModel gameModel = LoadScenario(state with { Stacks = state.Stacks.SetItem(hero.Id, planter) });

        (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);

        await gameModel.World.DispatchCommandAsync(new WorldStartGameCommand());
        WorldScreenViewModel viewModel = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(viewModel);

        await WaitForWorldAsync(window);

        for (int i = 0; i < 100 && !viewModel.SelectStackItemCommand.CanExecute(planter.Id); i++)
        {
            await SkipFramesAsync(10);
        }

        viewModel.SelectStackItemCommand.Execute(planter.Id);
        await SkipFramesAsync(30);

        Assert.That(viewModel.StackItems.Single(item => item.StackId == planter.Id).IsRodPlanter, Is.True);
        TestContext.Out.WriteLine($"auto rod can execute {viewModel.ToggleAutoRodCommand.CanExecute(null)}");

        Button button = window.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "AutoRodButton");
        window.MouseMove(Assert.NotNull(button.TranslatePoint(new Point(10, 10), window)));
        await SkipFramesAsync(30);
        await CaptureAsync(window, "1-card-button");

        RodPlantPromptViewModel prompt = new("Rod Planter");
        gameViewModel.OpenScreen(prompt);
        await SkipFramesAsync(30);
        await CaptureAsync(window, "2-prompt");
        prompt.CloseCommand.Execute(null);

        SettingsScreenViewModel settings = new(gameViewModel);
        settings.Language = Language.Russian;

        RodPlantPromptViewModel russian = new("Жезлоносец");
        gameViewModel.OpenScreen(russian);
        await SkipFramesAsync(30);
        await CaptureAsync(window, "3-prompt-russian");
        russian.CloseCommand.Execute(null);

        window.MouseMove(Assert.NotNull(button.TranslatePoint(new Point(10, 10), window)));
        await SkipFramesAsync(30);
        await CaptureAsync(window, "4-card-russian");
        settings.Language = Language.English;
    }
}

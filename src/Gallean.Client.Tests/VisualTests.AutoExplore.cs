using Avalonia.Headless.NUnit;
using Gallean.Common;
using Gallean.Configuration;
using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: auto explore, phase 2 (docs/auto-explore.md).
public sealed partial class VisualTests
{
    [AvaloniaTest]
    public async Task WorldAutoExploreTest()
    {
        using Sandbox sb = Sandbox.Create();

        GameModel gameModel = LoadScenarioWithExtraArmies(2);

        (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);

        await gameModel.World.DispatchCommandAsync(new WorldStartGameCommand());
        WorldScreenViewModel viewModel = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(viewModel);

        await WaitForWorldAsync(window);

        WorldStackId hero = Assert.NotNull(WorldPlan.For(gameModel.Snapshot.WorldState).GetStackAt(new WorldPosition(10, 14))).Id;

        for (int i = 0; i < 100 && !viewModel.SelectStackItemCommand.CanExecute(hero); i++)
        {
            await SkipFramesAsync(10);
        }

        viewModel.SelectStackItemCommand.Execute(hero);
        await SkipFramesAsync(30);

        Assert.That(viewModel.ToggleAutoExploreCommand.CanExecute(null), Is.True);
        await CaptureAsync(window, "1-button");

        WorldPosition start = gameModel.Snapshot.WorldState.Stacks[hero].Position;
        Task explore = viewModel.ToggleAutoExploreCommand.ExecuteAsync(null);

        for (int i = 0; i < 200 && !explore.IsCompleted; i++)
        {
            await SkipFramesAsync(10);
        }

        Assert.That(explore.IsCompleted, Is.True);
        await SkipFramesAsync(30);

        WorldStack after = gameModel.Snapshot.WorldState.Stacks[hero];
        TestContext.Out.WriteLine($"hero {start} -> {after.Position}, {after.MovementPoints} move points left, exploring {viewModel.IsExploring(hero)}");
        Assert.That(after.Position, Is.Not.EqualTo(start));

        viewModel.SelectStackItemCommand.Execute(hero);
        await SkipFramesAsync(30);
        await CaptureAsync(window, "2-exploring-selected");

        if (viewModel.StackItems.FirstOrDefault(item => item.StackId != hero) is { } other)
        {
            viewModel.SelectStackItemCommand.Execute(other.StackId);
            await SkipFramesAsync(30);
            await CaptureAsync(window, "3-exploring-unselected");
        }

        SettingsScreenViewModel settings = new(gameViewModel);
        settings.Language = Language.Russian;
        viewModel.SelectStackItemCommand.Execute(hero);
        await SkipFramesAsync(30);
        await CaptureAsync(window, "4-russian");
        settings.Language = Language.English;
    }

    // Every tile seen: the Explore button is greyed for the hero.
    [AvaloniaTest]
    public async Task WorldAutoExploreNothingLeftTest()
    {
        using Sandbox sb = Sandbox.Create();

        GameModel started = LoadScenario(null);
        await started.World.DispatchCommandAsync(new WorldStartGameCommand());
        WorldState state = started.World.State;
        WorldStack heroStack = Assert.NotNull(WorldPlan.For(state).GetStackAt(new WorldPosition(10, 14)));
        WorldPlayer owner = state.Players[heroStack.OwnerId];
        WorldState allSeen = state with
        {
            Players = state.Players.SetItem(owner.Id, owner with { FogOfWar = ImmutableBitmap.Create(state.Map.Width, state.Map.Height, true) })
        };
        GameModel gameModel = LoadScenario(allSeen);

        (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);

        WorldScreenViewModel viewModel = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(viewModel);

        await WaitForWorldAsync(window);

        for (int i = 0; i < 100 && !viewModel.SelectStackItemCommand.CanExecute(heroStack.Id); i++)
        {
            await SkipFramesAsync(10);
        }

        viewModel.SelectStackItemCommand.Execute(heroStack.Id);
        await SkipFramesAsync(30);

        Assert.That(viewModel.ToggleAutoExploreCommand.CanExecute(null), Is.False);
        await CaptureAsync(window, "1-greyed");
    }
}

using System.Collections.Immutable;
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
    // User decision after phase 2: on End turn the explorers walk at the same time and the camera stays put.
    [AvaloniaTest]
    public async Task WorldAutoExploreTogetherTest()
    {
        using Sandbox sb = Sandbox.Create();

        // Three armies (the hero and two copies next to it) with no move points left, so Explore only sets them exploring.
        GameModel loaded = LoadScenario(null);
        await loaded.World.DispatchCommandAsync(new WorldStartGameCommand());
        WorldState state = loaded.World.State;
        WorldPlayerId me = state.TurnInfo.CurrentPlayerId;
        WorldPlan plan = WorldPlan.For(state);
        WorldStack hero = Assert.NotNull(plan.GetStackAt(new WorldPosition(10, 14)));
        ImmutableSortedDictionary<WorldStackId, WorldStack> stacks = state.Stacks.SetItem(hero.Id, hero with { MovementPoints = 0 });
        int copies = 0;

        foreach (WorldPosition position in new[] { new WorldPosition(11, 15), new WorldPosition(9, 15), new WorldPosition(11, 13), new WorldPosition(9, 13), new WorldPosition(10, 16), new WorldPosition(12, 14) })
        {
            WorldMapCell cell = state.Map.GetCellAt(position);

            if (copies < 2 && plan.GetStackAt(position) is null && plan.GetFortificationAt(position) is null && !plan.IsObstacleAt(position) &&
                cell.Terrain != WorldMapTerrain.Water && cell.Feature != WorldMapFeature.Mountain)
            {
                WorldStack copy = hero with { Id = new WorldStackId($"Copy{copies}"), Position = position, MovementPoints = 0 };
                stacks = stacks.Add(copy.Id, copy);
                copies++;
            }
        }

        Assert.That(copies, Is.EqualTo(2));
        List<WorldStackId> armies = [hero.Id, new WorldStackId("Copy0"), new WorldStackId("Copy1")];
        GameModel gameModel = LoadScenario(state with { Stacks = stacks });

        (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);

        WorldScreenViewModel viewModel = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(viewModel);

        await WaitForWorldAsync(window);

        foreach (WorldStackId id in armies)
        {
            for (int i = 0; i < 100 && !viewModel.SelectStackItemCommand.CanExecute(id); i++)
            {
                await SkipFramesAsync(10);
            }

            viewModel.SelectStackItemCommand.Execute(id);
            await SkipFramesAsync(5);
            await viewModel.ToggleAutoExploreCommand.ExecuteAsync(null);
        }

        TestContext.Out.WriteLine("exploring: " + string.Join(", ", armies.Select(id => $"{id.Value}={viewModel.IsExploring(id)}")));

        // Skip to our next turn without playing the computer players (their AI isn't part of this test).
        do
        {
            await gameModel.World.DispatchCommandAsync(new WorldEndTurnCommand());
        }
        while (gameModel.Snapshot.WorldState.TurnInfo.CurrentPlayerId != me);

        for (int i = 0; i < 300 && !viewModel.EndTurnCommand.CanExecute(null); i++)
        {
            await SkipFramesAsync(10);
        }

        foreach (WorldStackId id in armies)
        {
            ICommandResult<WorldCommandError, WorldEvent, AutoExploreResult> pick = gameModel.World.DryRunCommand(new WorldGetAutoExploreTargetCommand(id));
            TestContext.Out.WriteLine($"{id.Value} MP {gameModel.Snapshot.WorldState.Stacks[id].MovementPoints}: {(pick.Failed ? pick.Error.ToString() : pick.Data.ToString())}");
        }

        Dictionary<WorldStackId, WorldPosition> before = armies.ToDictionary(id => id, id => gameModel.Snapshot.WorldState.Stacks[id].Position);
        int cameraJumps = 0;
        viewModel.CameraFocusRequested += (_, _) =>
        {
            if (viewModel.PlayingEntry is WorldEventQueue.Entry.StackMoveGroup)
            {
                cameraJumps++;
            }
        };

        Task endTurn = viewModel.EndTurnCommand.ExecuteAsync(null);

        WorldEventQueue.Entry.StackMoveGroup? group = null;

        for (int i = 0; i < 200 && group is null; i++)
        {
            await SkipFramesAsync(2);
            group = viewModel.PlayingEntry as WorldEventQueue.Entry.StackMoveGroup;
        }

        Assert.That(group, Is.Not.Null);
        TestContext.Out.WriteLine($"group of {group!.Moves.Count}: " + string.Join(", ", group.Moves.Select(move => $"{move.StackId.Value} {move.Steps.Count} steps")));

        await SkipFramesAsync(3);
        await CaptureAsync(window, "1-walking");
        await SkipFramesAsync(6);
        await CaptureAsync(window, "2-walking");

        for (int i = 0; i < 600 && !endTurn.IsCompleted; i++)
        {
            await SkipFramesAsync(10);
        }

        foreach (WorldStackId id in armies)
        {
            TestContext.Out.WriteLine($"{id.Value}: {before[id]} -> {gameModel.Snapshot.WorldState.Stacks.GetValueOrDefault(id)?.Position}");
        }

        TestContext.Out.WriteLine($"camera jumps while the group played: {cameraJumps}");
        Assert.That(cameraJumps, Is.Zero);
        Assert.That(group.Moves.Count, Is.GreaterThanOrEqualTo(2));
    }
}

using Avalonia.Headless.NUnit;
using Gallean.Common;
using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: move orders, phase 1 (docs/move-orders.md).
public sealed partial class VisualTests
{
    [AvaloniaTest]
    public async Task WorldMoveOrdersBarTest()
    {
        using Sandbox sb = Sandbox.Create();

        // The hero starts with few move points left, so a far tile in the revealed area takes more than one turn.
        GameModel started = LoadScenarioWithExtraArmies(8);
        await started.World.DispatchCommandAsync(new WorldStartGameCommand());
        WorldState extra = started.World.State;
        WorldStack heroStack = Assert.NotNull(WorldPlan.For(extra).GetStackAt(new WorldPosition(10, 14)));
        GameModel gameModel = LoadScenario(extra with { Stacks = extra.Stacks.SetItem(heroStack.Id, heroStack with { MovementPoints = 4 }) });

        (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);

        WorldScreenViewModel viewModel = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(viewModel);

        await WaitForWorldAsync(window);

        // Two-click move of the hero to a far tile: it walks this turn's part and keeps an order.
        WorldStackId hero = Assert.NotNull(WorldPlan.For(gameModel.Snapshot.WorldState).GetStackAt(new WorldPosition(10, 14))).Id;
        WorldPosition target = FindOrderTarget(gameModel, hero);

        for (int i = 0; i < 100 && !viewModel.SelectStackItemCommand.CanExecute(hero); i++)
        {
            await SkipFramesAsync(10);
        }

        viewModel.SelectStackItemCommand.Execute(hero);
        await SkipFramesAsync(10);
        await viewModel.HandleMapClickAsync(target, null, 1);
        await viewModel.HandleMapClickAsync(target, null, 1);

        WorldPosition last = default;

        for (int i = 0; i < 40 && last != gameModel.Snapshot.WorldState.Stacks[hero].Position; i++)
        {
            last = gameModel.Snapshot.WorldState.Stacks[hero].Position;
            await SkipFramesAsync(60);
        }

        await SkipFramesAsync(30);

        TestContext.Out.WriteLine($"hero at {gameModel.Snapshot.WorldState.Stacks[hero].Position}, order={viewModel.HasMoveOrder(hero)}");
        Assert.That(viewModel.HasMoveOrder(hero), Is.True);
        Assert.That(viewModel.StackItems[^1].StackId, Is.EqualTo(hero));
        TestContext.Out.WriteLine($"turns={viewModel.StackItems[^1].OrderTurns}");
        Assert.That(viewModel.StackItems[^1].OrderTurns, Is.GreaterThan(0));

        viewModel.SelectStackItemCommand.Execute(hero);
        await SkipFramesAsync(30);

        await CaptureAsync(window, "1-selected");

        viewModel.SelectStackItemCommand.Execute(viewModel.StackItems[0].StackId);
        await SkipFramesAsync(30);

        await CaptureAsync(window, "2-unselected");
    }

    // A revealed tile the hero reaches in two or three turns without any encounter on the way.
    private static WorldPosition FindOrderTarget(GameModel gameModel, WorldStackId stackId)
    {
        WorldState state = gameModel.Snapshot.WorldState;
        WorldStack stack = state.Stacks[stackId];

        WorldPosition? best = null;
        int bestTurn = -1;

        for (int y = 0; y < state.Map.Height; y++)
        {
            for (int x = 0; x < state.Map.Width; x++)
            {
                WorldPosition position = new(x, y);

                if (position.GetDistance(stack.Position) > 40 || WorldPlan.For(state).GetStackAt(position) is not null)
                {
                    continue;
                }

                ICommandResult<WorldCommandError, WorldEvent, WorldGetPathCommand.Result> result =
                    gameModel.World.DryRunCommand(new WorldGetPathCommand(stackId, position));

                if (result.Failed || result.Data.Path.Segments.IsEmpty)
                {
                    continue;
                }

                int turn = result.Data.Path.Segments[^1].Turn;

                if (result.Data.Encounter is null && result.Data.Remainder.Segments.IsEmpty && turn is >= 1 and <= 2 && turn > bestTurn)
                {
                    best = position;
                    bestTurn = turn;
                }
            }
        }

        TestContext.Out.WriteLine($"target {best}, last turn {bestTurn}, mp {stack.MovementPoints}");

        if (best is not null)
        {
            return best.Value;
        }

        throw new InvalidOperationException("No order target found");
    }
}

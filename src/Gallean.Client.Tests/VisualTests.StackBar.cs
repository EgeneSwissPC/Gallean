using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using Gallean.Common;
using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

public sealed partial class VisualTests
{
    // Copies of the hero army on free tiles around the capital of S107, so the bar has `extra` more cards.
    private GameModel LoadScenarioWithExtraArmies(int extra)
    {
        GameModel scenario = LoadScenario(null);
        WorldState state = scenario.World.State;
        WorldPlan plan = WorldPlan.For(state);
        WorldStack hero = Assert.NotNull(plan.GetStackAt(new WorldPosition(10, 14)));
        ImmutableSortedDictionary<WorldStackId, WorldStack> stacks = state.Stacks;
        int added = 0;

        for (int y = 8; y <= 22 && added < extra; y++)
        {
            for (int x = 4; x <= 18 && added < extra; x++)
            {
                WorldPosition position = new(x, y);

                if (plan.GetStackAt(position) is not null || plan.GetFortificationAt(position) is not null)
                {
                    continue;
                }

                WorldStack copy = hero with { Id = new WorldStackId($"Copy{added:D2}"), Position = position };
                stacks = stacks.Add(copy.Id, copy);
                added++;
            }
        }

        Assert.That(added, Is.EqualTo(extra));

        return LoadScenario(state with { Stacks = stacks });
    }

    [AvaloniaTest]
    public async Task WorldStackBarCompactTest()
    {
        using Sandbox sb = Sandbox.Create();

        GameModel gameModel = LoadScenarioWithExtraArmies(15);

        (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);

        await gameModel.World.DispatchCommandAsync(new WorldStartGameCommand());
        WorldScreenViewModel viewModel = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(viewModel);

        WorldScreenView view = await WaitForWorldAsync(window);
        WorldStackBarPanel bar = view.GetVisualDescendants().OfType<WorldStackBarPanel>().Single();

        await viewModel.HandleMapClickAsync(new WorldPosition(10, 14), null, 1);

        for (int i = 0; i < 6; i++)
        {
            viewModel.SelectNextStackCommand.Execute(null);
        }

        await SkipFramesAsync(30);

        Assert.That(viewModel.StackItems, Has.Count.EqualTo(16));
        Assert.That(bar.IsScrolling, Is.False);

        await CaptureAsync(window);
    }

    [AvaloniaTest]
    public async Task WorldStackBarScrollTest()
    {
        using Sandbox sb = Sandbox.Create();

        GameModel gameModel = LoadScenarioWithExtraArmies(54);

        (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);

        await gameModel.World.DispatchCommandAsync(new WorldStartGameCommand());
        WorldScreenViewModel viewModel = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(viewModel);

        WorldScreenView view = await WaitForWorldAsync(window);
        WorldStackBarPanel bar = view.GetVisualDescendants().OfType<WorldStackBarPanel>().Single();

        await viewModel.HandleMapClickAsync(new WorldPosition(10, 14), null, 1);
        await SkipFramesAsync(30);

        Assert.That(viewModel.StackItems, Has.Count.EqualTo(55));
        Assert.That(bar.IsScrolling, Is.True);

        int heroIndex = viewModel.StackItems.ToList().FindIndex(item => item.IsSelected);
        double heroOffset = bar.ScrollOffset;

        Assert.That(bar.Children[heroIndex].Bounds.X, Is.GreaterThanOrEqualTo(0.0));
        Assert.That(bar.Children[heroIndex].Bounds.Right, Is.LessThanOrEqualTo(bar.Bounds.Width));

        await CaptureAsync(window, "1-first");

        // Tab round to the 6th card, at the other end of the bar from the hero (S107's hero sorts last).
        Assert.That(heroIndex, Is.GreaterThan(40));

        for (int i = 0; i < (5 - heroIndex + 55) % 55; i++)
        {
            viewModel.SelectNextStackCommand.Execute(null);
        }

        await SkipFramesAsync(30);

        int selectedIndex = viewModel.StackItems.ToList().FindIndex(item => item.IsSelected);
        Control selectedCard = bar.Children[selectedIndex];
        Rect selectedBounds = selectedCard.Bounds;

        Assert.That(selectedIndex, Is.EqualTo(5));
        Assert.That(bar.ScrollOffset, Is.LessThan(heroOffset));
        Assert.That(selectedBounds.X, Is.GreaterThanOrEqualTo(0.0));
        Assert.That(selectedBounds.Right, Is.LessThanOrEqualTo(bar.Bounds.Width));

        await CaptureAsync(window, "2-tabbed");

        // Wheel down over the bar scrolls it towards the last cards; the selection stays.
        Point overBar = Assert.NotNull(selectedCard.TranslatePoint(new Point(selectedBounds.Width / 2, 60), window));
        double offset = bar.ScrollOffset;
        window.MouseWheel(overBar, new Vector(0, -3), RawInputModifiers.None);
        await SkipFramesAsync(30);

        Assert.That(bar.ScrollOffset, Is.EqualTo(offset + 3 * 3 * 48.0));
        Assert.That(viewModel.StackItems[selectedIndex].IsSelected, Is.True);

        await CaptureAsync(window, "3-wheel");
    }
}

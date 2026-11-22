using Avalonia.Headless.NUnit;
using Gallean.Common;
using Gallean.Configuration;
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

    // Phase 3: the order prompt during End turn, in English and Russian.
    [AvaloniaTest]
    public async Task WorldMoveOrderPromptTest()
    {
        using Sandbox sb = Sandbox.Create();

        GameModel gameModel = LoadScenario(null);

        (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);

        await gameModel.World.DispatchCommandAsync(new WorldStartGameCommand());
        WorldScreenViewModel viewModel = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(viewModel);

        await WaitForWorldAsync(window);

        // Real targets on S107: a neutral army (its units are always visible) and, if the map has
        // one, an army of another race (not spied: only its leader and the thief note).
        WorldState state = gameModel.Snapshot.WorldState;
        WorldStack hero = Assert.NotNull(WorldPlan.For(state).GetStackAt(new WorldPosition(10, 14)));
        string heroName = UnitName(gameModel, hero);
        WorldStack neutral = state.Stacks.Values
            .Where(stack => state.Players[stack.OwnerId].Race == WorldRace.Neutral)
            .OrderBy(stack => stack.Position.GetDistance(hero.Position))
            .First();
        WorldStack? rival = state.Stacks.Values
            .Where(stack => stack.OwnerId != hero.OwnerId && state.Players[stack.OwnerId].Race != WorldRace.Neutral)
            .OrderBy(stack => stack.Position.GetDistance(hero.Position))
            .FirstOrDefault();

        TestContext.Out.WriteLine($"attacker {heroName}, neutral {UnitName(gameModel, neutral)} at {neutral.Position}, rival {(rival is null ? "none" : UnitName(gameModel, rival))}");

        WorldStackInfoViewModel known = WorldStackInfoViewModel.Create(state, hero.OwnerId, neutral, gameModel.Definitions, gameModel.Texts, gameModel.Stats);

        MoveOrderPromptViewModel prompt = new(known, heroName, UnitName(gameModel, neutral), isTargetKnown: true);
        gameViewModel.OpenScreen(prompt);
        await SkipFramesAsync(30);

        await CaptureAsync(window, "1-english");

        prompt.CloseCommand.Execute(null);

        SettingsScreenViewModel settings = new(gameViewModel);
        settings.Language = Language.Russian;

        MoveOrderPromptViewModel russian = new(known, heroName, UnitName(gameModel, neutral), isTargetKnown: true);
        gameViewModel.OpenScreen(russian);
        await SkipFramesAsync(30);

        await CaptureAsync(window, "2-russian");

        russian.CloseCommand.Execute(null);
        settings.Language = Language.English;

        if (rival is not null)
        {
            WorldStackInfoViewModel unknown = WorldStackInfoViewModel.Create(state, hero.OwnerId, rival, gameModel.Definitions, gameModel.Texts, gameModel.Stats);
            MoveOrderPromptViewModel hidden = new(unknown, heroName, UnitName(gameModel, rival), isTargetKnown: true);
            gameViewModel.OpenScreen(hidden);
            await SkipFramesAsync(30);

            await CaptureAsync(window, "3-not-spied");

            hidden.CloseCommand.Execute(null);
        }

        // A city as the target: the game's city info (garrison and visiting party).
        WorldCity town = state.Fortifications.Values.OfType<WorldCity>().Where(c => c.OwnerId != hero.OwnerId).OrderBy(c => c.Entrance.GetDistance(hero.Position)).First();
        WorldFortificationInfoViewModel city = WorldFortificationInfoViewModel.CreateCity(
            state, hero.OwnerId, town, WorldPlan.For(state).GetStackAt(town.Entrance), gameModel.Definitions, gameModel.Texts, gameModel.Stats);
        TestContext.Out.WriteLine($"city {gameModel.Texts.GetScenarioText(town.NameTextId)} owner race {state.Players[town.OwnerId].Race}");
        MoveOrderPromptViewModel cityPrompt = new(city, heroName, gameModel.Texts.GetScenarioText(town.NameTextId), isTargetKnown: true);
        gameViewModel.OpenScreen(cityPrompt);
        await SkipFramesAsync(30);

        await CaptureAsync(window, "4-city");

        cityPrompt.CloseCommand.Execute(null);
    }

    private static string UnitName(GameModel gameModel, WorldStack stack)
    {
        (WorldUnit leader, GridPosition _, UnitDefinition _) = stack.GetLeader(gameModel.Definitions);

        return UnitNameResolver.Resolve(leader, gameModel.Definitions, gameModel.Texts);
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

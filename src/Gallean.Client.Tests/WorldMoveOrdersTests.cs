using Gallean.Common;
using Gallean.Configuration;
using Gallean.Engine;
using Gallean.Engine.Tests;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: move orders, phase 1 (docs/move-orders.md).
public sealed class WorldMoveOrdersTests
{
    private static readonly UnitDefinition Leader = TestHelpers.SimpleLeaderDefinition(
        TestHelpers.SimpleUnitDefinition("Leader", maxHitpoints: 100, initiative: 50, damage: 10),
        movementPoints: 4, leadership: 6);

    private static readonly WorldStackId First = new("Stack1");
    private static readonly WorldStackId Second = new("Stack2");

    private sealed record Setup(GameModel GameModel, GameViewModel GameViewModel, WorldScreenViewModel World);

    // A 12x2 strip (wider with an enemy): Stack1 at (0,0), Stack2 at (0,1). With 4 move points an
    // army walks 2 tiles a turn. An enemy army belongs to a computer player 2; a bag lies on the map.
    private static Setup CreateWorld(
        bool showPathOnHover = true,
        bool tabSkipsOrders = true,
        int movementPoints = 4,
        WorldPosition? enemyPosition = null,
        WorldPosition[]? bagPositions = null,
        int width = 12,
        WorldPosition? sitePosition = null,
        bool spiedEnemy = false)
    {
        WorldMap map = TestHelpers.SimpleMap(width, 2);
        WorldPlayer player = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Undead, map) with { SpiedPlayers = spiedEnemy ? [new WorldPlayerId(2)] : [] };
        WorldPlayer enemyPlayer = TestHelpers.SimplePlayer(new WorldPlayerId(2), WorldRace.Heretic, map) with { IsAi = true };

        IGameDefinitionProvider stackDefinitions = new TestGameDefinitionProviderBuilder()
            .AddUnit(Leader)
            .Build();

        WorldStack MakeStack(WorldStackId id, WorldPosition position, string unitId) => TestHelpers.SimpleStack(
            id,
            player.Id,
            position,
            TestHelpers.StackUnitGrid(
                stackDefinitions,
                [(new WorldUnit(new WorldUnitId(unitId), Leader.Id, 100, 0, 1, [], null, false), GridPosition.FrontBottom)]),
            movementPoints: movementPoints);

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(Leader)
            .WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, LordCategory.Warrior))
            .WithPlayer(player)
            .WithStack(MakeStack(First, new WorldPosition(0, 0), "leader1"))
            .WithStack(MakeStack(Second, new WorldPosition(0, 1), "leader2"))
            .WithSubrace(TestHelpers.NeutralSubrace);

        if (enemyPosition is not null)
        {
            builder = builder
                .WithDefinition(TestHelpers.SimpleLord(enemyPlayer.LordId, enemyPlayer.Race, LordCategory.Warrior))
                .WithPlayer(enemyPlayer)
                .WithStack(TestHelpers.SimpleStack(
                    new WorldStackId("Enemy"),
                    enemyPlayer.Id,
                    enemyPosition.Value,
                    TestHelpers.StackUnitGrid(
                        stackDefinitions,
                        [(new WorldUnit(new WorldUnitId("enemy"), Leader.Id, 100, 0, 1, [], null, false), GridPosition.FrontBottom)]),
                    movementPoints: 0));
        }

        foreach (WorldPosition bag in bagPositions ?? [])
        {
            builder = builder.WithBag(new WorldBag(new WorldBagId($"Bag{bag.X}{bag.Y}"), bag, new WorldObjectVariant(0), WorldInventory.Empty, AiPriority: 0));
        }

        if (sitePosition is not null)
        {
            builder = builder.WithSite(new WorldTrainerSite(
                new WorldSiteId("trainer"),
                new WorldFootprint(sitePosition.Value, 1, 1),
                new WorldObjectVariant(0),
                new ScenarioTextId("Trainer"),
                new ScenarioTextId("Greeting"),
                VisitorIds: [],
                AiPriority: 0));
        }

        (GameSession session, _) = builder.BuildSession();

        GameModel gameModel = new(session, TestGameTextProvider.Instance, NullLog.Instance, SynchronousTaskScheduler.Instance);
        GameViewModel gameViewModel = new TestGameViewModelBuilder()
            .WithSettings(GameSettings.Default with { ShowPathOnHover = showPathOnHover, TabSkipsOrders = tabSkipsOrders })
            .Build();
        WorldScreenViewModel world = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(world);
        world.CompleteGameStart();

        return new Setup(gameModel, gameViewModel, world);
    }

    private static List<WorldPosition> GetPathPositions(WorldScreenViewModel world)
    {
        IAssetMetadataProvider metadata = new TestMetadataProviderBuilder().Build();
        WorldRenderState.PathOverlay? path = new WorldRenderComposer(world.GameModel.Definitions, GameScenarioProvider.Empty, metadata)
            .Compose(world.RenderRequest)
            .SelectedPath;

        return path is null ? [] : [.. path.Steps.Select(step => step.Position)];
    }

    private static WorldPosition PositionOf(Setup setup, WorldStackId id)
    {
        return setup.GameModel.Snapshot.WorldState.Stacks[id].Position;
    }

    private static WorldStackItemViewModel Item(WorldScreenViewModel world, WorldStackId id)
    {
        return world.StackItems.Single(item => item.StackId == id);
    }

    // Selects Stack1 and orders it to (6,0) with the hover path: it walks to (2,0) this turn.
    private static async Task OrderFirstAsync(Setup setup, int x = 6)
    {
        WorldScreenViewModel world = setup.World;

        world.SelectStackItemCommand.Execute(First);
        world.UpdateHover(new WorldPosition(x, 0), null);
        await world.HandleMapClickAsync(new WorldPosition(x, 0), null, 1);
        world.CompleteAnimation();
        world.CompleteAnimation();
    }

    // Ends the turn without the End turn command (no walking); with an enemy the computer player's
    // turn is skipped the same way.
    private static async Task NextTurnAsync(Setup setup)
    {
        await setup.GameModel.World.DispatchCommandAsync(new WorldEndTurnCommand());

        if (setup.GameModel.Snapshot.WorldState.TurnInfo.CurrentPlayerId != new WorldPlayerId(1))
        {
            await setup.GameModel.World.DispatchCommandAsync(new WorldEndTurnCommand());
        }

        setup.World.CompleteAnimation();
        setup.World.CompleteAnimation();
    }

    // Presses End turn and plays the walks' animations until the command is done.
    private static async Task PressEndTurnAsync(Setup setup)
    {
        Task task = setup.World.EndTurnCommand.ExecuteAsync(null);

        for (int i = 0; i < 200 && !task.IsCompleted; i++)
        {
            setup.World.CompleteAnimation();
        }

        Assert.That(task.IsCompleted, Is.True, "End turn didn't finish");
        await task;

        setup.World.CompleteAnimation();
        setup.World.CompleteAnimation();
    }

    private static int TurnOf(Setup setup)
    {
        return setup.GameModel.Snapshot.WorldState.TurnInfo.TurnNumber;
    }

    [Test]
    public async Task FarClickCreatesOrderTest()
    {
        Setup setup = CreateWorld();
        WorldScreenViewModel world = setup.World;

        Assert.That(world.HasMoveOrder(First), Is.False);

        await OrderFirstAsync(setup);

        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(2, 0)));
        Assert.That(world.HasMoveOrder(First), Is.True);
        Assert.That(world.HasMoveOrder(Second), Is.False);
        Assert.That(Item(world, First).HasMoveOrder, Is.True);
        // (3,0)-(4,0) next turn, (5,0)-(6,0) the turn after.
        Assert.That(Item(world, First).OrderTurns, Is.EqualTo(2));
        Assert.That(Item(world, Second).OrderTurns, Is.Null);
    }

    [Test]
    public async Task ShortMoveIsNoOrderTest()
    {
        Setup setup = CreateWorld();

        await OrderFirstAsync(setup, x: 2);

        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(2, 0)));
        Assert.That(setup.World.HasMoveOrder(First), Is.False);
    }

    [Test]
    public async Task OrderedCardsSortLastTest()
    {
        Setup setup = CreateWorld();
        WorldScreenViewModel world = setup.World;

        Assert.That(world.StackItems.Select(item => item.StackId), Is.EqualTo(new[] { First, Second }));

        await OrderFirstAsync(setup);

        Assert.That(world.StackItems.Select(item => item.StackId), Is.EqualTo(new[] { Second, First }));
    }

    [Test]
    public async Task OrderPathStaysUnderMouseTest()
    {
        Setup setup = CreateWorld();
        WorldScreenViewModel world = setup.World;

        await OrderFirstAsync(setup);

        world.UpdateHover(new WorldPosition(9, 0), null);

        Assert.That(GetPathPositions(world), Is.EqualTo(new[] { new WorldPosition(3, 0), new WorldPosition(4, 0), new WorldPosition(5, 0), new WorldPosition(6, 0) }));
    }

    [Test]
    public async Task FirstClickReplansTest()
    {
        Setup setup = CreateWorld();
        WorldScreenViewModel world = setup.World;

        await OrderFirstAsync(setup);
        await NextTurnAsync(setup);

        world.SelectStackItemCommand.Execute(First);
        world.UpdateHover(new WorldPosition(9, 0), null);
        await world.HandleMapClickAsync(new WorldPosition(9, 0), null, 1);

        // The first click only switches to the hover path: nothing moves, the order stays.
        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(2, 0)));
        Assert.That(world.HasMoveOrder(First), Is.True);

        world.UpdateHover(new WorldPosition(8, 0), null);

        Assert.That(GetPathPositions(world)[^1], Is.EqualTo(new WorldPosition(8, 0)));

        await world.HandleMapClickAsync(new WorldPosition(8, 0), null, 1);
        world.CompleteAnimation();
        world.CompleteAnimation();
        world.ClearHover();

        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(4, 0)));
        Assert.That(world.HasMoveOrder(First), Is.True);
        Assert.That(GetPathPositions(world)[^1], Is.EqualTo(new WorldPosition(8, 0)));
    }

    [Test]
    public async Task SelectionChangeEndsReplanTest()
    {
        Setup setup = CreateWorld();
        WorldScreenViewModel world = setup.World;

        await OrderFirstAsync(setup);
        await NextTurnAsync(setup);

        world.SelectStackItemCommand.Execute(First);
        await world.HandleMapClickAsync(new WorldPosition(9, 0), null, 1);
        world.SelectStackItemCommand.Execute(Second);
        world.SelectStackItemCommand.Execute(First);
        world.UpdateHover(new WorldPosition(9, 0), null);

        Assert.That(GetPathPositions(world)[^1], Is.EqualTo(new WorldPosition(6, 0)));
    }

    [Test]
    public async Task HoverOffKeepsTwoClicksTest()
    {
        Setup setup = CreateWorld(showPathOnHover: false);
        WorldScreenViewModel world = setup.World;

        world.SelectStackItemCommand.Execute(First);
        await world.HandleMapClickAsync(new WorldPosition(6, 0), null, 1);

        // A selected but unconfirmed path is no order yet.
        Assert.That(world.HasMoveOrder(First), Is.False);

        await world.HandleMapClickAsync(new WorldPosition(6, 0), null, 1);
        world.CompleteAnimation();
        world.CompleteAnimation();

        Assert.That(world.HasMoveOrder(First), Is.True);
    }

    [Test]
    public async Task CancelOrderTest()
    {
        Setup setup = CreateWorld();
        WorldScreenViewModel world = setup.World;

        world.SelectStackItemCommand.Execute(First);

        Assert.That(world.CancelMoveOrderCommand.CanExecute(null), Is.False);

        await OrderFirstAsync(setup);

        Assert.That(world.CancelMoveOrderCommand.CanExecute(null), Is.True);

        world.CancelMoveOrderCommand.Execute(null);
        world.ClearHover();

        Assert.That(world.HasMoveOrder(First), Is.False);
        Assert.That(Item(world, First).HasMoveOrder, Is.False);
        Assert.That(world.StackItems.Select(item => item.StackId), Is.EqualTo(new[] { First, Second }));
        Assert.That(GetPathPositions(world), Is.Empty);
        Assert.That(world.CancelMoveOrderCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task ArrivalEndsOrderTest()
    {
        Setup setup = CreateWorld();
        WorldScreenViewModel world = setup.World;

        await OrderFirstAsync(setup, x: 4);
        await NextTurnAsync(setup);

        world.SelectStackItemCommand.Execute(First);

        Assert.That(world.ContinuePathCommand.CanExecute(null), Is.True);

        world.ContinuePathCommand.Execute(null);
        world.CompleteAnimation();
        world.CompleteAnimation();

        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(4, 0)));
        Assert.That(world.HasMoveOrder(First), Is.False);
        Assert.That(Item(world, First).HasMoveOrder, Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task TabSkipsOrdersTest(bool tabSkipsOrders)
    {
        Setup setup = CreateWorld(tabSkipsOrders: tabSkipsOrders);
        WorldScreenViewModel world = setup.World;

        await OrderFirstAsync(setup);
        await NextTurnAsync(setup);

        world.SelectStackItemCommand.Execute(Second);
        world.SelectNextStackCommand.Execute(null);

        WorldStackId expected = tabSkipsOrders ? Second : First;

        Assert.That(world.StackItems.Single(item => item.IsSelected).StackId, Is.EqualTo(expected));
    }

    [Test]
    public async Task EndTurnHighlightIgnoresOrdersTest()
    {
        Setup setup = CreateWorld();
        WorldScreenViewModel world = setup.World;

        await OrderFirstAsync(setup);
        await NextTurnAsync(setup);

        Assert.That(world.IsEndTurnHighlighted, Is.False);

        world.SelectStackItemCommand.Execute(Second);
        world.FinishStackCommand.Execute(null);

        // Only the ordered army has move points left.
        Assert.That(world.IsEndTurnHighlighted, Is.True);
    }

    [Test]
    public async Task OCyclesOrderedArmiesTest()
    {
        Setup setup = CreateWorld();
        WorldScreenViewModel world = setup.World;

        world.SelectStackItemCommand.Execute(Second);

        Assert.That(world.SelectNextOrderedStackCommand.CanExecute(null), Is.False);

        await OrderFirstAsync(setup);
        world.SelectStackItemCommand.Execute(Second);

        Assert.That(world.SelectNextOrderedStackCommand.CanExecute(null), Is.True);

        world.SelectNextOrderedStackCommand.Execute(null);

        Assert.That(world.StackItems.Single(item => item.IsSelected).StackId, Is.EqualTo(First));
        Assert.That(world.SelectNextOrderedStackCommand.CanExecute(null), Is.False);
        Assert.That(world.SelectPreviousOrderedStackCommand.CanExecute(null), Is.False);
    }

    // Phase 2: orders walk on End turn.

    [Test]
    public async Task EndTurnWalksOrderTest()
    {
        Setup setup = CreateWorld();
        WorldScreenViewModel world = setup.World;

        await OrderFirstAsync(setup, x: 6);
        int turn = TurnOf(setup);

        // No move points left this turn: nothing walks, the turn ends.
        await PressEndTurnAsync(setup);

        Assert.That(TurnOf(setup), Is.EqualTo(turn + 1));
        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(2, 0)));

        await PressEndTurnAsync(setup);

        Assert.That(TurnOf(setup), Is.EqualTo(turn + 2));
        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(4, 0)));
        Assert.That(world.HasMoveOrder(First), Is.True);

        await PressEndTurnAsync(setup);

        // Arrived with no move points left: the order is done and the turn ended.
        Assert.That(TurnOf(setup), Is.EqualTo(turn + 3));
        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(6, 0)));
        Assert.That(world.HasMoveOrder(First), Is.False);
        Assert.That(world.IsAutoWalking, Is.False);
    }

    [Test]
    public async Task ArrivalWithMovesLeftKeepsTurnTest()
    {
        Setup setup = CreateWorld();
        WorldScreenViewModel world = setup.World;

        await OrderFirstAsync(setup, x: 3);
        await NextTurnAsync(setup);
        int turn = TurnOf(setup);

        await PressEndTurnAsync(setup);

        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(3, 0)));
        Assert.That(TurnOf(setup), Is.EqualTo(turn));
        Assert.That(setup.GameViewModel.Alert.Current?.Caption, Is.EqualTo("An army with an order needs you"));
        Assert.That(setup.GameViewModel.Alert.Current?.Details, Does.Contain("arrived with 2 move points left"));
        Assert.That(world.StackItems.Single(item => item.IsSelected).StackId, Is.EqualTo(First));

        // The second End turn doesn't ask again.
        await PressEndTurnAsync(setup);

        Assert.That(TurnOf(setup), Is.EqualTo(turn + 1));
    }

    [Test]
    public async Task FinishedArmyWaitsTest()
    {
        Setup setup = CreateWorld();
        WorldScreenViewModel world = setup.World;

        await OrderFirstAsync(setup, x: 6);
        await NextTurnAsync(setup);

        world.SelectStackItemCommand.Execute(First);
        world.FinishStackCommand.Execute(null);
        await PressEndTurnAsync(setup);

        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(2, 0)));
        Assert.That(world.HasMoveOrder(First), Is.True);

        await PressEndTurnAsync(setup);

        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(4, 0)));
    }

    // Phase 3: the end-of-turn questions.

    // The enemy at (5,0) intercepts at (4,x). Stack1 gets an order past it and stops at (2,0) on
    // turn 1 (out of moves); next turn the interception is in reach.
    private static async Task<Setup> OrderPastEnemyAsync(bool spiedEnemy = false)
    {
        Setup setup = CreateWorld(enemyPosition: new WorldPosition(5, 0), width: 16, spiedEnemy: spiedEnemy);

        await OrderFirstAsync(setup, x: 12);
        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(2, 0)));
        Assert.That(setup.World.HasMoveOrder(First), Is.True);

        await NextTurnAsync(setup);

        return setup;
    }

    // Presses End turn and plays animations until the order prompt opens.
    private static async Task<(Task EndTurn, MoveOrderPromptViewModel Prompt)> EndTurnUntilPromptAsync(Setup setup)
    {
        Task task = setup.World.EndTurnCommand.ExecuteAsync(null);

        for (int i = 0; i < 200 && setup.GameViewModel.TopScreen is not MoveOrderPromptViewModel && !task.IsCompleted; i++)
        {
            setup.World.CompleteAnimation();
            await Task.Yield();
        }

        MoveOrderPromptViewModel prompt = Assert.IsOfType<MoveOrderPromptViewModel>(setup.GameViewModel.TopScreen);

        return (task, prompt);
    }

    private static async Task FinishAsync(Setup setup, Task task)
    {
        for (int i = 0; i < 200 && !task.IsCompleted; i++)
        {
            setup.World.CompleteAnimation();
            await Task.Yield();
        }

        Assert.That(task.IsCompleted, Is.True, "End turn didn't finish");
        await task;
    }

    [Test]
    public async Task PlannedAttackAsksFirstTest()
    {
        Setup setup = await OrderPastEnemyAsync();
        int turn = TurnOf(setup);

        (Task task, MoveOrderPromptViewModel prompt) = await EndTurnUntilPromptAsync(setup);

        // Asked before anyone walked, about the right army and enemy.
        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(2, 0)));
        Assert.That(prompt.IsTargetKnown, Is.True);
        Assert.That(prompt.Message.Text, Is.EqualTo("name: the path leads into battle with name."));
        Assert.That(setup.World.StackItems.Single(item => item.IsSelected).StackId, Is.EqualTo(First));

        prompt.PlanAnotherPathCommand.Execute(null);
        await FinishAsync(setup, task);

        // Plan another path: nothing moved, the turn is open, the path follows the mouse at once.
        Assert.That(TurnOf(setup), Is.EqualTo(turn));
        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(2, 0)));
        Assert.That(setup.World.HasMoveOrder(First), Is.True);

        setup.World.UpdateHover(new WorldPosition(2, 1), null);

        Assert.That(GetPathPositions(setup.World), Is.EqualTo(new[] { new WorldPosition(2, 1) }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PromptShowsEnemyUnitsWhenKnownTest(bool spiedEnemy)
    {
        Setup setup = await OrderPastEnemyAsync(spiedEnemy);

        (Task task, MoveOrderPromptViewModel prompt) = await EndTurnUntilPromptAsync(setup);

        // Like the right-click info: the units of a spied race, the thief note otherwise.
        WorldStackInfoViewModel info = Assert.IsOfType<WorldStackInfoViewModel>(prompt.TargetInfo);

        if (spiedEnemy)
        {
            Assert.That(info.Note, Is.Null);
            Assert.That(info.Grid.Units, Has.Count.EqualTo(1));
        }
        else
        {
            Assert.That(info.Note, Is.EqualTo("A thief must spy on this race to gain information about it."));
            Assert.That(info.Grid.Units, Is.Empty);
        }

        prompt.PlanAnotherPathCommand.Execute(null);
        await FinishAsync(setup, task);
    }

    [Test]
    public async Task PlannedAttackCancelOrderTest()
    {
        Setup setup = await OrderPastEnemyAsync();
        int turn = TurnOf(setup);

        (Task task, MoveOrderPromptViewModel prompt) = await EndTurnUntilPromptAsync(setup);

        prompt.CancelOrderCommand.Execute(null);
        await FinishAsync(setup, task);

        // The order is gone; the army still has its move points, so the turn stays open once.
        Assert.That(setup.World.HasMoveOrder(First), Is.False);
        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(2, 0)));
        Assert.That(TurnOf(setup), Is.EqualTo(turn));
        Assert.That(setup.GameViewModel.Alert.Current?.Details, Does.Contain("stopped with 4 move points left"));
    }

    [Test]
    public async Task PlannedAttackFightsTest()
    {
        Setup setup = await OrderPastEnemyAsync();

        (Task task, MoveOrderPromptViewModel prompt) = await EndTurnUntilPromptAsync(setup);

        prompt.AttackCommand.Execute(null);

        for (int i = 0; i < 50 && setup.GameModel.Snapshot.BattleState is null; i++)
        {
            setup.World.CompleteAnimation();
            await Task.Yield();
        }

        // The accepted walk runs into the interception and the battle starts.
        Assert.That(setup.GameModel.Snapshot.BattleState, Is.Not.Null);
        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(4, 0)).Or.EqualTo(new WorldPosition(4, 1)));
        Assert.That(task.IsCompleted, Is.False);
    }

    [Test]
    public async Task EscapePlansAnotherPathTest()
    {
        Setup setup = await OrderPastEnemyAsync();
        int turn = TurnOf(setup);

        (Task task, MoveOrderPromptViewModel prompt) = await EndTurnUntilPromptAsync(setup);

        prompt.CloseCommand.Execute(null);
        await FinishAsync(setup, task);

        Assert.That(setup.GameViewModel.TopScreen, Is.SameAs(setup.World));
        Assert.That(TurnOf(setup), Is.EqualTo(turn));
        Assert.That(setup.World.HasMoveOrder(First), Is.True);
    }

    [Test]
    public async Task ReachedSiteOpensTest()
    {
        Setup setup = CreateWorld(sitePosition: new WorldPosition(5, 0));

        await OrderFirstAsync(setup, x: 5);
        await NextTurnAsync(setup);
        await PressEndTurnAsync(setup);

        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(4, 0)));

        Task task = setup.World.EndTurnCommand.ExecuteAsync(null);

        for (int i = 0; i < 100 && setup.GameViewModel.TopScreen is WorldScreenViewModel && !task.IsCompleted; i++)
        {
            setup.World.CompleteAnimation();
            await Task.Yield();
        }

        // The army walked onto the site and the site screen opened during End turn.
        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(5, 0)));
        ManagementScreenViewModel site = Assert.IsOfType<ManagementScreenViewModel>(setup.GameViewModel.TopScreen);
        Assert.That(task.IsCompleted, Is.False);

        // Closing it: the army still has 2 move points, so the turn stays open once.
        int turn = TurnOf(setup);

        site.CloseCommand.Execute(null);
        await FinishAsync(setup, task);

        Assert.That(TurnOf(setup), Is.EqualTo(turn));
        Assert.That(setup.GameViewModel.Alert.Current?.Details, Does.Contain("reached Trainer"));
    }

    [Test]
    public async Task WalkPicksUpOrderedBagTest()
    {
        // Paths never cross a bag, so a walk meets one only as its destination.
        Setup setup = CreateWorld(bagPositions: [new WorldPosition(5, 0)]);
        WorldScreenViewModel world = setup.World;

        await OrderFirstAsync(setup, x: 5);
        await NextTurnAsync(setup);
        int turn = TurnOf(setup);

        await PressEndTurnAsync(setup);

        // Next to the bag with no move points left: the turn ended.
        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(4, 0)));
        Assert.That(TurnOf(setup), Is.EqualTo(turn + 1));

        await PressEndTurnAsync(setup);

        // Picked up from the next tile (no move points spent), then the walk went on to the tile.
        Assert.That(setup.GameModel.Snapshot.WorldState.Bags, Is.Empty);
        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(5, 0)));
        Assert.That(setup.GameViewModel.Alert.Current?.Details, Does.Contain("arrived with 2 move points left"));
    }

    [Test]
    public async Task SpaceFastForwardsWalksTest()
    {
        Setup setup = CreateWorld();
        WorldScreenViewModel world = setup.World;

        await OrderFirstAsync(setup, x: 6);
        await NextTurnAsync(setup);

        Assert.That(world.FastForwardCommand.CanExecute(null), Is.False);

        Task task = world.EndTurnCommand.ExecuteAsync(null);

        Assert.That(world.IsAutoWalking, Is.True);
        Assert.That(world.FastForwardCommand.CanExecute(null), Is.True);

        world.FastForwardCommand.Execute(null);

        Assert.That(world.IsFastForward, Is.True);

        for (int i = 0; i < 200 && !task.IsCompleted; i++)
        {
            world.CompleteAnimation();
        }

        await task;

        Assert.That(world.IsAutoWalking, Is.False);
        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(4, 0)));
    }

    [Test]
    public async Task DestinationTakenByOwnArmyTest()
    {
        Setup setup = CreateWorld();
        WorldScreenViewModel world = setup.World;

        // Both armies are ordered to (4,0); Stack1 walks first (same level, lower ID) and takes it.
        await OrderFirstAsync(setup, x: 4);
        world.SelectStackItemCommand.Execute(Second);
        world.UpdateHover(new WorldPosition(4, 0), null);
        await world.HandleMapClickAsync(new WorldPosition(4, 0), null, 1);
        world.CompleteAnimation();
        world.CompleteAnimation();
        world.ClearHover();

        Assert.That(world.HasMoveOrder(First) && world.HasMoveOrder(Second), Is.True);

        await NextTurnAsync(setup);
        int turn = TurnOf(setup);
        WorldPosition secondBefore = PositionOf(setup, Second);

        await PressEndTurnAsync(setup);

        // Stack2 is still blocked after the retry pass: reported, the turn stays open.
        Assert.That(PositionOf(setup, First), Is.EqualTo(new WorldPosition(4, 0)));
        Assert.That(PositionOf(setup, Second), Is.EqualTo(secondBefore));
        Assert.That(TurnOf(setup), Is.EqualTo(turn));
        Assert.That(setup.GameViewModel.Alert.Current?.Details, Does.Contain("could not continue its path"));
    }
}

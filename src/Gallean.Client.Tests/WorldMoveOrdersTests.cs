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

    // A 12x2 strip: Stack1 at (0,0), Stack2 at (0,1). With 4 move points an army walks 2 tiles a turn.
    private static Setup CreateWorld(bool showPathOnHover = true, bool tabSkipsOrders = true, int movementPoints = 4)
    {
        WorldMap map = TestHelpers.SimpleMap(12, 2);
        WorldPlayer player = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Undead, map);

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

        (GameSession session, _) = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(Leader)
            .WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, LordCategory.Warrior))
            .WithPlayer(player)
            .WithStack(MakeStack(First, new WorldPosition(0, 0), "leader1"))
            .WithStack(MakeStack(Second, new WorldPosition(0, 1), "leader2"))
            .WithSubrace(TestHelpers.NeutralSubrace)
            .BuildSession();

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

    private static async Task NextTurnAsync(Setup setup)
    {
        await setup.GameModel.World.DispatchCommandAsync(new WorldEndTurnCommand());
        setup.World.CompleteAnimation();
        setup.World.CompleteAnimation();
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
}

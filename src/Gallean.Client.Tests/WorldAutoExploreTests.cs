using Gallean.Common;
using Gallean.Configuration;
using Gallean.Engine;
using Gallean.Engine.Tests;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: auto explore, phase 2 (docs/auto-explore.md).
public sealed class WorldAutoExploreTests
{
    private static readonly WorldStackId Explorer = new("Explorer");
    private static readonly WorldStackId Other = new("Other");

    private sealed record Setup(GameModel GameModel, GameViewModel GameViewModel, WorldScreenViewModel World);

    // A strip of plain land, width x 2. The player has seen the columns x <= seenUpTo (plus what its
    // armies see at the start, radius 1). Explorer at (0,0), a second army at (0,1).
    private static Setup CreateWorld(int width = 16, int seenUpTo = 2, int movementPoints = 4, WorldPosition? enemyPosition = null)
    {
        UnitDefinition leader = TestHelpers.SimpleLeaderDefinition(
            TestHelpers.SimpleUnitDefinition("Leader", maxHitpoints: 100, initiative: 50, damage: 10),
            movementPoints: movementPoints, leadership: 6);
        WorldMap map = TestHelpers.SimpleMap(width, 2);
        ImmutableBitmap.Builder fog = ImmutableBitmap.Create(width, 2).ToBuilder();

        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x <= Math.Min(seenUpTo, width - 1); x++)
            {
                fog.Set(x, y);
            }
        }

        WorldPlayer player = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Undead, map) with { FogOfWar = fog.ToImmutable() };
        WorldPlayer enemyPlayer = TestHelpers.SimplePlayer(new WorldPlayerId(2), WorldRace.Heretic, map) with { IsAi = true };
        IGameDefinitionProvider stackDefinitions = new TestGameDefinitionProviderBuilder().AddUnit(leader).Build();

        WorldStack MakeStack(WorldStackId id, WorldPlayerId owner, WorldPosition position, string unitId, int points) => TestHelpers.SimpleStack(
            id,
            owner,
            position,
            TestHelpers.StackUnitGrid(stackDefinitions, [(new WorldUnit(new WorldUnitId(unitId), leader.Id, 100, 0, 1, [], null, false), GridPosition.FrontBottom)]),
            movementPoints: points);

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(leader)
            .WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, LordCategory.Warrior))
            .WithPlayer(player)
            .WithStack(MakeStack(Explorer, player.Id, new WorldPosition(0, 0), "leader1", movementPoints))
            .WithStack(MakeStack(Other, player.Id, new WorldPosition(0, 1), "leader2", movementPoints))
            .WithSubrace(TestHelpers.NeutralSubrace);

        if (enemyPosition is { } enemy)
        {
            builder = builder
                .WithDefinition(TestHelpers.SimpleLord(enemyPlayer.LordId, enemyPlayer.Race, LordCategory.Warrior))
                .WithPlayer(enemyPlayer)
                .WithStack(MakeStack(new WorldStackId("Enemy"), enemyPlayer.Id, enemy, "enemy", 0));
        }

        (GameSession session, _) = builder.BuildSession();

        GameModel gameModel = new(session, TestGameTextProvider.Instance, NullLog.Instance, SynchronousTaskScheduler.Instance);
        GameViewModel gameViewModel = new TestGameViewModelBuilder().WithSettings(GameSettings.Default with { ShowPathOnHover = true }).Build();
        WorldScreenViewModel world = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(world);
        world.CompleteGameStart();

        return new Setup(gameModel, gameViewModel, world);
    }

    private static async Task RunAsync(Setup setup, Task task)
    {
        for (int i = 0; i < 400 && !task.IsCompleted; i++)
        {
            setup.World.CompleteAnimation();
        }

        Assert.That(task.IsCompleted, Is.True, "the command didn't finish");
        await task;
        setup.World.CompleteAnimation();
        setup.World.CompleteAnimation();
    }

    private static async Task ExploreAsync(Setup setup)
    {
        setup.World.SelectStackItemCommand.Execute(Explorer);
        await RunAsync(setup, setup.World.ToggleAutoExploreCommand.ExecuteAsync(null));
    }

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

    private static WorldStack StackOf(Setup setup, WorldStackId id)
    {
        return setup.GameModel.Snapshot.WorldState.Stacks[id];
    }

    private static WorldStackItemViewModel Item(Setup setup, WorldStackId id)
    {
        return setup.World.StackItems.Single(item => item.StackId == id);
    }

    private static int SeenColumns(Setup setup)
    {
        ImmutableBitmap fog = setup.GameModel.Snapshot.WorldState.Players[new WorldPlayerId(1)].FogOfWar;
        int x = 0;

        while (x < fog.Width && fog.Get(x, 0))
        {
            x++;
        }

        return x;
    }

    [Test]
    public async Task ExploreWalksRightAwayTest()
    {
        Setup setup = CreateWorld();

        Assert.That(setup.World.ToggleAutoExploreCommand.CanExecute(null), Is.False, "nothing selected");

        await ExploreAsync(setup);

        Assert.That(setup.World.IsExploring(Explorer), Is.True);
        Assert.That(StackOf(setup, Explorer).Position.X, Is.EqualTo(2), "4 move points = 2 tiles toward the fog");
        Assert.That(StackOf(setup, Explorer).MovementPoints, Is.Zero);
        Assert.That(Item(setup, Explorer).IsExploring, Is.True);
        Assert.That(Item(setup, Explorer).HasMoveOrder, Is.True, "explorers look like ordered armies");
        Assert.That(Item(setup, Explorer).OrderTurns, Is.Null, "the eye replaces the turn count");
        Assert.That(setup.World.StackItems[^1].StackId, Is.EqualTo(Explorer), "explorers sit at the end of the bar like orders");
        Assert.That(Item(setup, Other).IsExploring, Is.False);
    }

    [Test]
    public async Task ExplorerWalksOnEndTurnTest()
    {
        Setup setup = CreateWorld();

        await ExploreAsync(setup);
        await NextTurnAsync(setup);

        int before = StackOf(setup, Explorer).Position.X;
        int seenBefore = SeenColumns(setup);

        // The other army is finished so End turn isn't held for it.
        setup.World.SelectStackItemCommand.Execute(Other);
        setup.World.FinishStackCommand.Execute(null);
        await RunAsync(setup, setup.World.EndTurnCommand.ExecuteAsync(null));

        Assert.That(StackOf(setup, Explorer).Position.X, Is.GreaterThan(before));
        Assert.That(SeenColumns(setup), Is.GreaterThan(seenBefore));
        Assert.That(setup.World.IsExploring(Explorer), Is.True);
    }

    [Test]
    public async Task ExplorerKeepsGoingWithMovePointsLeftTest()
    {
        // 40 move points = 20 tiles: it reaches several fog edges in one turn instead of stopping at the first.
        Setup setup = CreateWorld(width: 30, movementPoints: 40);

        await ExploreAsync(setup);

        Assert.That(StackOf(setup, Explorer).Position.X, Is.GreaterThan(4));
        Assert.That(SeenColumns(setup), Is.GreaterThan(6));
    }

    [Test]
    public async Task EverythingSeenGreysTheButtonTest()
    {
        Setup setup = CreateWorld(width: 4, seenUpTo: 3);

        setup.World.SelectStackItemCommand.Execute(Explorer);

        Assert.That(setup.World.ToggleAutoExploreCommand.CanExecute(null), Is.False);
        Assert.That(setup.World.IsExploring(Explorer), Is.False);
    }

    [Test]
    public async Task ExplorerStopsWhenNothingIsLeftTest()
    {
        // 8 tiles wide, plenty of move points: it uncovers the rest, then gets "no more land" and becomes a normal army.
        Setup setup = CreateWorld(width: 8, movementPoints: 40);

        await ExploreAsync(setup);

        Assert.That(SeenColumns(setup), Is.EqualTo(8));
        Assert.That(setup.World.IsExploring(Explorer), Is.False);
        Assert.That(Item(setup, Explorer).IsExploring, Is.False);
        Assert.That(setup.World.ToggleAutoExploreCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task CancelEndsExploringTest()
    {
        Setup setup = CreateWorld();

        await ExploreAsync(setup);
        setup.World.SelectStackItemCommand.Execute(Explorer);

        Assert.That(setup.World.CancelMoveOrderCommand.CanExecute(null), Is.True);

        setup.World.CancelMoveOrderCommand.Execute(null);

        Assert.That(setup.World.IsExploring(Explorer), Is.False);
        Assert.That(setup.World.HasMoveOrder(Explorer), Is.False);
        Assert.That(Item(setup, Explorer).IsExploring, Is.False);
    }

    [Test]
    public async Task ExploreAgainEndsExploringTest()
    {
        Setup setup = CreateWorld();

        await ExploreAsync(setup);
        await ExploreAsync(setup);

        Assert.That(setup.World.IsExploring(Explorer), Is.False);
        Assert.That(setup.World.HasMoveOrder(Explorer), Is.False);
    }

    [Test]
    public async Task PlayerPathEndsExploringTest()
    {
        Setup setup = CreateWorld();

        await ExploreAsync(setup);
        await NextTurnAsync(setup);

        setup.World.SelectStackItemCommand.Execute(Explorer);
        // Like any ordered army: the first click switches to the hover path, the second one moves.
        setup.World.UpdateHover(new WorldPosition(1, 1), null);
        await setup.World.HandleMapClickAsync(new WorldPosition(1, 1), null, 1);
        setup.World.UpdateHover(new WorldPosition(1, 1), null);
        await setup.World.HandleMapClickAsync(new WorldPosition(1, 1), null, 1);
        setup.World.CompleteAnimation();
        setup.World.CompleteAnimation();

        Assert.That(setup.World.IsExploring(Explorer), Is.False);
        Assert.That(StackOf(setup, Explorer).Position, Is.EqualTo(new WorldPosition(1, 1)));
    }

    [Test]
    public async Task SpottedEnemyStopsTheExplorerTest()
    {
        // An enemy hidden at (12,0); with 40 move points the explorer would walk past it. It stops once the
        // enemy comes into view within 3 tiles, keeps exploring for next turn, and the turn stays open.
        Setup setup = CreateWorld(width: 30, movementPoints: 40, enemyPosition: new WorldPosition(12, 0));

        await ExploreAsync(setup);

        WorldStack explorer = StackOf(setup, Explorer);
        TestContext.Out.WriteLine($"explorer at {explorer.Position}, {explorer.MovementPoints} move points left");
        Assert.That(explorer.Position.X, Is.LessThan(12));
        Assert.That(12 - explorer.Position.X, Is.LessThanOrEqualTo(3));
        Assert.That(explorer.MovementPoints, Is.GreaterThan(0));
        Assert.That(setup.World.IsExploring(Explorer), Is.True);
    }

    [Test]
    public async Task ControlWithoutEnemyWalksFurtherTest()
    {
        Setup setup = CreateWorld(width: 30, movementPoints: 40);

        await ExploreAsync(setup);

        Assert.That(StackOf(setup, Explorer).Position.X, Is.GreaterThan(12));
    }

    [Test]
    public async Task ExplorerDoesNotHoldTheEndTurnHighlightTest()
    {
        Setup setup = CreateWorld();

        await ExploreAsync(setup);
        await NextTurnAsync(setup);

        setup.World.SelectStackItemCommand.Execute(Other);
        setup.World.FinishStackCommand.Execute(null);

        Assert.That(setup.World.IsEndTurnHighlighted, Is.True);
    }
}

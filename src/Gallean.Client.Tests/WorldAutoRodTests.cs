using Gallean.Common;
using Gallean.Configuration;
using Gallean.Engine;
using Gallean.Engine.Tests;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: auto rod, phase 2 (docs/auto-rod.md).
public sealed class WorldAutoRodTests
{
    private static readonly WorldStackId Planter = new("Planter");
    private static readonly WorldStackId Other = new("Other");
    private static readonly WorldPlayerId Me = new(1);

    private sealed record Setup(GameModel GameModel, GameViewModel GameViewModel, WorldScreenViewModel World);

    // Plain land, width x 3. The player has seen the columns x <= seenUpTo. Planter (a rod planter) at (0,1),
    // a plain army at (0,0).
    private static Setup CreateWorld(
        int width = 16,
        int seenUpTo = 9,
        int movementPoints = 20,
        int gold = 150,
        WorldPosition? node = null,
        WorldPosition? enemyRod = null)
    {
        UnitDefinition leader = TestHelpers.SimpleLeaderDefinition(
            TestHelpers.SimpleUnitDefinition("Leader", maxHitpoints: 100, initiative: 50, damage: 10),
            movementPoints: movementPoints, leadership: 6);
        UnitDefinition planter = leader with
        {
            Id = new UnitDefinitionId("Planter"),
            LeaderCategory = UnitLeaderCategory.Rod,
            LeaderAbilities = LeaderAbility.Rod
        };
        WorldMap map = TestHelpers.SimpleMap(width, 3);
        ImmutableBitmap.Builder fog = ImmutableBitmap.Create(width, 3).ToBuilder();

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x <= Math.Min(seenUpTo, width - 1); x++)
            {
                fog.Set(x, y);
            }
        }

        WorldPlayer player = TestHelpers.SimplePlayer(Me, WorldRace.Undead, map) with
        {
            FogOfWar = fog.ToImmutable(),
            Resources = new WorldResourceSet(gold, 0, 0, 0, 0, 0)
        };
        WorldPlayer enemyPlayer = TestHelpers.SimplePlayer(new WorldPlayerId(2), WorldRace.Heretic, map) with { IsAi = true };
        IGameDefinitionProvider stackDefinitions = new TestGameDefinitionProviderBuilder().AddUnit(leader).AddUnit(planter).Build();

        WorldStack MakeStack(WorldStackId id, WorldPosition position, UnitDefinition definition, string unitId) => TestHelpers.SimpleStack(
            id,
            player.Id,
            position,
            TestHelpers.StackUnitGrid(stackDefinitions, [(new WorldUnit(new WorldUnitId(unitId), definition.Id, 100, 0, 1, [], null, false), GridPosition.FrontBottom)]),
            movementPoints: movementPoints);

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(leader)
            .WithDefinition(planter)
            .WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, LordCategory.Warrior))
            .WithPlayer(player)
            .WithDefinition(TestHelpers.SimpleLord(enemyPlayer.LordId, enemyPlayer.Race, LordCategory.Warrior))
            .WithPlayer(enemyPlayer)
            .WithStack(MakeStack(Planter, new WorldPosition(0, 1), planter, "planter"))
            .WithStack(MakeStack(Other, new WorldPosition(0, 0), leader, "other"))
            .WithSubrace(TestHelpers.NeutralSubrace);

        if (node is { } nodePosition)
        {
            builder = builder.WithResourceNode(new WorldResourceNode(new WorldResourceNodeId("node"), nodePosition, WorldResourceType.Gold, AiPriority: 3));
        }

        if (enemyRod is { } rodPosition)
        {
            builder = builder.WithRod(new WorldRod(new WorldRodId("enemyRod"), enemyPlayer.Id, rodPosition));
        }

        (GameSession session, _) = builder.BuildSession();

        GameModel gameModel = new(session, TestGameTextProvider.Instance, NullLog.Instance, SynchronousTaskScheduler.Instance);
        GameViewModel gameViewModel = new TestGameViewModelBuilder().WithSettings(GameSettings.Default with { ShowPathOnHover = true }).Build();
        WorldScreenViewModel world = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(world);
        world.CompleteGameStart();

        return new Setup(gameModel, gameViewModel, world);
    }

    // Runs a command until it finishes or the plant prompt opens (then returns the prompt).
    private static async Task<RodPlantPromptViewModel?> RunAsync(Setup setup, Task task)
    {
        for (int i = 0; i < 400 && !task.IsCompleted && setup.GameViewModel.TopScreen is not RodPlantPromptViewModel; i++)
        {
            setup.World.CompleteAnimation();
            await Task.Yield();
        }

        if (setup.GameViewModel.TopScreen is RodPlantPromptViewModel prompt)
        {
            return prompt;
        }

        Assert.That(task.IsCompleted, Is.True, "the command didn't finish");
        await task;
        setup.World.CompleteAnimation();
        setup.World.CompleteAnimation();

        return null;
    }

    private static Task<RodPlantPromptViewModel?> AutoRodAsync(Setup setup, out Task task)
    {
        setup.World.SelectStackItemCommand.Execute(Planter);
        task = setup.World.ToggleAutoRodCommand.ExecuteAsync(null);

        return RunAsync(setup, task);
    }

    private static WorldState State(Setup setup)
    {
        return setup.GameModel.Snapshot.WorldState;
    }

    private static WorldStackItemViewModel Item(Setup setup, WorldStackId id)
    {
        return setup.World.StackItems.Single(item => item.StackId == id);
    }

    private static int Distance(WorldPosition a, WorldPosition b)
    {
        return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    [Test]
    public void OnlyRodPlantersGetTheButtonTest()
    {
        Setup setup = CreateWorld(node: new WorldPosition(5, 1));

        Assert.That(Item(setup, Planter).IsRodPlanter, Is.True);
        Assert.That(Item(setup, Other).IsRodPlanter, Is.False);

        setup.World.SelectStackItemCommand.Execute(Other);
        Assert.That(setup.World.ToggleAutoRodCommand.CanExecute(null), Is.False);

        setup.World.SelectStackItemCommand.Execute(Planter);
        Assert.That(setup.World.ToggleAutoRodCommand.CanExecute(null), Is.True);
    }

    [Test]
    public async Task PullsUpHostileRodWithoutAskingTest()
    {
        Setup setup = CreateWorld(enemyRod: new WorldPosition(5, 1));

        RodPlantPromptViewModel? prompt = await AutoRodAsync(setup, out _);

        Assert.That(prompt, Is.Null, "removing a rod doesn't ask (decision B3)");
        Assert.That(State(setup).Rods, Is.Empty);
        Assert.That(setup.World.IsAutoRodding(Planter), Is.True);
        Assert.That(Item(setup, Planter).IsAutoRodding, Is.True);
        Assert.That(Item(setup, Planter).IsExploring, Is.False, "the rod mark replaces the eye");
    }

    [Test]
    public async Task WalksToResourceAndAsksBeforePlantingTest()
    {
        WorldPosition node = new(6, 1);
        Setup setup = CreateWorld(node: node);

        RodPlantPromptViewModel? prompt = await AutoRodAsync(setup, out Task task);

        Assert.That(prompt, Is.Not.Null, "planting asks (decision B3)");
        Assert.That(Distance(State(setup).Stacks[Planter].Position, node), Is.LessThanOrEqualTo(2));
        Assert.That(State(setup).Rods, Is.Empty, "nothing planted before the answer");

        prompt!.PlantCommand.Execute(null);
        await RunAsync(setup, task);

        WorldRod rod = State(setup).Rods.Values.Single();
        Assert.That(Distance(rod.Position, node), Is.EqualTo(1));
        Assert.That(State(setup).Players[Me].Resources.Gold, Is.Zero);
        Assert.That(setup.World.IsAutoRodding(Planter), Is.True);
    }

    [Test]
    public async Task SkipAsksAboutAnotherSpotAndStopEndsAutoRodTest()
    {
        Setup setup = CreateWorld(node: new WorldPosition(6, 1));

        RodPlantPromptViewModel? first = await AutoRodAsync(setup, out Task task);
        Assert.That(first, Is.Not.Null);

        first!.SkipCommand.Execute(null);
        RodPlantPromptViewModel? second = await RunAsync(setup, task);

        Assert.That(second, Is.Not.Null, "the next spot around the same resource");
        Assert.That(second, Is.Not.SameAs(first));

        second!.StopCommand.Execute(null);
        await RunAsync(setup, task);

        Assert.That(State(setup).Rods, Is.Empty);
        Assert.That(setup.World.IsAutoRodding(Planter), Is.False);
        Assert.That(setup.World.IsExploring(Planter), Is.False);
    }

    [Test]
    public async Task ExploresWhenNoJobIsKnownTest()
    {
        // The resource lies in the fog: it explores toward it instead.
        Setup setup = CreateWorld(width: 30, seenUpTo: 3, node: new WorldPosition(25, 1));

        RodPlantPromptViewModel? prompt = await AutoRodAsync(setup, out _);

        Assert.That(prompt, Is.Null);
        Assert.That(State(setup).Stacks[Planter].Position.X, Is.GreaterThan(3));
        Assert.That(setup.World.IsAutoRodding(Planter), Is.True);
        Assert.That(setup.World.IsExploring(Planter), Is.True, "auto rod walks as an explorer");
    }

    [Test]
    public async Task ShortOfGoldExploresInsteadOfPlantingTest()
    {
        Setup setup = CreateWorld(width: 30, seenUpTo: 9, gold: 0, node: new WorldPosition(4, 1));

        RodPlantPromptViewModel? prompt = await AutoRodAsync(setup, out _);

        Assert.That(prompt, Is.Null, "no plant prompt without gold (decision C1)");
        Assert.That(State(setup).Stacks[Planter].Position.X, Is.GreaterThan(5), "it walked on to the fog");
        Assert.That(setup.World.IsAutoRodding(Planter), Is.True);
    }

    [Test]
    public async Task RodAgainEndsAutoRodTest()
    {
        Setup setup = CreateWorld(width: 30, seenUpTo: 3);

        await AutoRodAsync(setup, out _);
        Assert.That(setup.World.IsAutoRodding(Planter), Is.True);

        await AutoRodAsync(setup, out _);

        Assert.That(setup.World.IsAutoRodding(Planter), Is.False);
        Assert.That(setup.World.IsExploring(Planter), Is.False);
    }

    [Test]
    public async Task ExploreSwitchesAutoRodToExploringTest()
    {
        Setup setup = CreateWorld(width: 30, seenUpTo: 3, movementPoints: 4);

        await AutoRodAsync(setup, out _);
        Assert.That(setup.World.IsAutoRodding(Planter), Is.True);

        setup.World.SelectStackItemCommand.Execute(Planter);
        Assert.That(setup.World.ToggleAutoExploreCommand.CanExecute(null), Is.True);
        await RunAsync(setup, setup.World.ToggleAutoExploreCommand.ExecuteAsync(null));

        Assert.That(setup.World.IsAutoRodding(Planter), Is.False);
        Assert.That(setup.World.IsExploring(Planter), Is.True);
        Assert.That(Item(setup, Planter).IsExploring, Is.True);
    }

    private static async Task NextTurnAsync(Setup setup)
    {
        await setup.GameModel.World.DispatchCommandAsync(new WorldEndTurnCommand());

        if (setup.GameModel.Snapshot.WorldState.TurnInfo.CurrentPlayerId != Me)
        {
            await setup.GameModel.World.DispatchCommandAsync(new WorldEndTurnCommand());
        }

        setup.World.CompleteAnimation();
        setup.World.CompleteAnimation();
    }

    private static Task<RodPlantPromptViewModel?> EndTurnAsync(Setup setup, out Task task)
    {
        // The other army is finished so End turn isn't held for it.
        setup.World.SelectStackItemCommand.Execute(Other);
        setup.World.FinishStackCommand.Execute(null);
        task = setup.World.EndTurnCommand.ExecuteAsync(null);

        return RunAsync(setup, task);
    }

    [Test]
    public async Task PlanterWalksOnEndTurnAndAsksAtTheSpotTest()
    {
        // 4 move points = 2 tiles a turn: it gets next to the spot with no points left, so the question
        // comes at the start of the next End turn, before the walks.
        WorldPosition node = new(6, 1);
        Setup setup = CreateWorld(movementPoints: 4, node: node);

        Assert.That(await AutoRodAsync(setup, out _), Is.Null);
        await NextTurnAsync(setup);

        RodPlantPromptViewModel? prompt = await EndTurnAsync(setup, out Task task);

        for (int turn = 0; turn < 3 && prompt is null; turn++)
        {
            await NextTurnAsync(setup);
            prompt = await EndTurnAsync(setup, out task);
        }

        Assert.That(prompt, Is.Not.Null);
        prompt!.PlantCommand.Execute(null);
        await RunAsync(setup, task);

        Assert.That(Distance(State(setup).Rods.Values.Single().Position, node), Is.EqualTo(1));
    }
}

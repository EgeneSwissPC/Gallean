using Gallean.Common;
using Gallean.Configuration;
using Gallean.Engine;
using Gallean.Engine.Tests;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: the selected army's path follows the mouse and one click moves the army.
public sealed class WorldHoverPathTests
{
    private static readonly UnitDefinition Leader = TestHelpers.SimpleLeaderDefinition(
        TestHelpers.SimpleUnitDefinition("Leader", maxHitpoints: 100, initiative: 50, damage: 10),
        movementPoints: 24, leadership: 6);

    private sealed record Setup(GameModel GameModel, GameViewModel GameViewModel, WorldScreenViewModel World, WorldStack Stack);

    private static Setup CreateWorld(bool showPathOnHover, int movementPoints = 24, WorldPosition? otherStackPosition = null)
    {
        WorldMap map = TestHelpers.SimpleMap(5, 1);
        WorldPlayer player = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Undead, map);

        IGameDefinitionProvider stackDefinitions = new TestGameDefinitionProviderBuilder()
            .AddUnit(Leader)
            .Build();

        WorldStack stack = TestHelpers.SimpleStack(
            new WorldStackId("Stack1"),
            player.Id,
            new WorldPosition(0, 0),
            TestHelpers.StackUnitGrid(
                stackDefinitions,
                [(new WorldUnit(new WorldUnitId("leader"), Leader.Id, 100, 0, 1, [], null, false), GridPosition.FrontBottom)]),
            movementPoints: movementPoints);

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(Leader)
            .WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, LordCategory.Warrior))
            .WithPlayer(player)
            .WithStack(stack)
            .WithSubrace(TestHelpers.NeutralSubrace);

        if (otherStackPosition is not null)
        {
            builder = builder.WithStack(TestHelpers.SimpleStack(
                new WorldStackId("Stack2"),
                player.Id,
                otherStackPosition.Value,
                TestHelpers.StackUnitGrid(
                    stackDefinitions,
                    [(new WorldUnit(new WorldUnitId("leader2"), Leader.Id, 100, 0, 1, [], null, false), GridPosition.FrontBottom)]),
                movementPoints: 24));
        }

        (GameSession session, _) = builder.BuildSession();

        GameModel gameModel = new(session, TestGameTextProvider.Instance, NullLog.Instance, SynchronousTaskScheduler.Instance);
        GameViewModel gameViewModel = new TestGameViewModelBuilder()
            .WithSettings(GameSettings.Default with { ShowPathOnHover = showPathOnHover })
            .Build();
        WorldScreenViewModel world = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(world);
        world.CompleteGameStart();

        return new Setup(gameModel, gameViewModel, world, stack);
    }

    private static WorldRenderState.PathOverlay? GetSelectedPath(WorldScreenViewModel world)
    {
        IAssetMetadataProvider metadata = new TestMetadataProviderBuilder().Build();

        return new WorldRenderComposer(world.GameModel.Definitions, GameScenarioProvider.Empty, metadata)
            .Compose(world.RenderRequest)
            .SelectedPath;
    }

    private static List<WorldPosition> GetPathPositions(WorldScreenViewModel world)
    {
        WorldRenderState.PathOverlay? path = GetSelectedPath(world);

        return path is null ? [] : [.. path.Steps.Select(step => step.Position)];
    }

    [Test]
    public async Task PathFollowsHoverTest()
    {
        Setup setup = CreateWorld(showPathOnHover: true);
        WorldScreenViewModel world = setup.World;

        await world.HandleMapClickAsync(new WorldPosition(0, 0), null, 1);

        Assert.That(GetSelectedPath(world), Is.Null);

        world.UpdateHover(new WorldPosition(2, 0), null);

        Assert.That(GetPathPositions(world), Is.EqualTo(new[] { new WorldPosition(1, 0), new WorldPosition(2, 0) }));

        world.UpdateHover(new WorldPosition(3, 0), null);

        Assert.That(GetPathPositions(world), Is.EqualTo(new[] { new WorldPosition(1, 0), new WorldPosition(2, 0), new WorldPosition(3, 0) }));
        Assert.That(GetSelectedPath(world)!.Origin, Is.EqualTo(new WorldPosition(0, 0)));

        world.UpdateHover(new WorldPosition(0, 0), null);

        Assert.That(GetSelectedPath(world), Is.Null);

        world.UpdateHover(new WorldPosition(1, 0), null);
        world.ClearHover();

        Assert.That(GetSelectedPath(world), Is.Null);
        Assert.That(setup.GameModel.Snapshot.WorldState.Stacks[setup.Stack.Id].Position, Is.EqualTo(new WorldPosition(0, 0)));
    }

    [Test]
    public async Task SingleClickMovesTest()
    {
        Setup setup = CreateWorld(showPathOnHover: true);
        WorldScreenViewModel world = setup.World;

        await world.HandleMapClickAsync(new WorldPosition(0, 0), null, 1);
        world.UpdateHover(new WorldPosition(3, 0), null);
        await world.HandleMapClickAsync(new WorldPosition(3, 0), null, 1);
        world.CompleteAnimation();
        world.CompleteAnimation();
        world.CompleteAnimation();

        Assert.That(setup.GameModel.Snapshot.WorldState.Stacks[setup.Stack.Id].Position, Is.EqualTo(new WorldPosition(3, 0)));
        Assert.That(GetSelectedPath(world), Is.Null);
    }

    // Upstream 0.17 (651e4200): a tile next to the entrance counts as the site. One click on a site walks the army to
    // the nearest such tile and opens the site screen, like upstream's second click does.
    [Test]
    public async Task SingleClickEntersSiteTest()
    {
        WorldMap map = TestHelpers.SimpleMap(8, 8);
        WorldPlayer player = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Human, map);
        WorldMerchantSite site = new(
            Id: new WorldSiteId("merchant"),
            Footprint: new WorldFootprint(new WorldPosition(2, 2), 3, 3),
            Variant: new WorldObjectVariant(0),
            TitleTextId: new ScenarioTextId("Title"),
            GreetingTextId: new ScenarioTextId("Greeting"),
            Items: [],
            VisitorIds: [],
            AiPriority: 3);
        WorldStack stack = TestHelpers.SimpleStack(
            new WorldStackId("Stack1"),
            player.Id,
            new WorldPosition(7, 7),
            TestHelpers.SingleUnitGrid(new WorldUnitId("leader"), Leader),
            movementPoints: 24);

        (GameSession session, _) = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(Leader)
            .WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, LordCategory.Warrior))
            .WithPlayer(player)
            .WithSite(site)
            .WithSubrace(TestHelpers.NeutralSubrace)
            .WithStack(stack)
            .BuildSession();

        GameModel gameModel = new(session, TestGameTextProvider.Instance, NullLog.Instance, SynchronousTaskScheduler.Instance);
        GameViewModel gameViewModel = new TestGameViewModelBuilder()
            .WithSettings(GameSettings.Default with { ShowPathOnHover = true })
            .Build();
        WorldScreenViewModel world = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(world);
        world.CompleteGameStart();

        await world.HandleMapClickAsync(stack.Position, null, 1);
        world.UpdateHover(new WorldPosition(3, 3), null);
        await world.HandleMapClickAsync(new WorldPosition(3, 3), null, 1);

        for (int i = 0; i < 20 && gameViewModel.TopScreen is WorldScreenViewModel; i++)
        {
            world.CompleteAnimation();
        }

        Assert.That(gameViewModel.TopScreen, Is.InstanceOf<ManagementScreenViewModel>());
        WorldPosition position = gameModel.Snapshot.WorldState.Stacks[stack.Id].Position;
        Assert.That(position.GetDistance(site.Entrance), Is.LessThanOrEqualTo(1));
        Assert.That(position, Is.Not.EqualTo(site.Entrance), "it stops next to the entrance");
    }

    [Test]
    public async Task PartialMoveKeepsDestinationTest()
    {
        Setup setup = CreateWorld(showPathOnHover: true, movementPoints: 4);
        WorldScreenViewModel world = setup.World;

        await world.HandleMapClickAsync(new WorldPosition(0, 0), null, 1);
        world.UpdateHover(new WorldPosition(3, 0), null);
        await world.HandleMapClickAsync(new WorldPosition(3, 0), null, 1);
        world.CompleteAnimation();
        world.CompleteAnimation();
        world.ClearHover();

        Assert.That(setup.GameModel.Snapshot.WorldState.Stacks[setup.Stack.Id].Position, Is.EqualTo(new WorldPosition(2, 0)));
        Assert.That(GetSelectedPath(world)!.Origin, Is.EqualTo(new WorldPosition(2, 0)));
        Assert.That(GetPathPositions(world), Is.EqualTo(new[] { new WorldPosition(3, 0) }));
    }

    [Test]
    public async Task NoPathOverOwnStackTest()
    {
        Setup setup = CreateWorld(showPathOnHover: true, otherStackPosition: new WorldPosition(4, 0));
        WorldScreenViewModel world = setup.World;

        await world.HandleMapClickAsync(new WorldPosition(0, 0), null, 1);
        world.UpdateHover(new WorldPosition(4, 0), null);

        Assert.That(GetSelectedPath(world), Is.Null);

        await world.HandleMapClickAsync(new WorldPosition(4, 0), null, 1);

        Assert.That(setup.GameModel.Snapshot.WorldState.Stacks[setup.Stack.Id].Position, Is.EqualTo(new WorldPosition(0, 0)));
    }

    [Test]
    public async Task SettingsToggleTest()
    {
        Setup setup = CreateWorld(showPathOnHover: true);
        WorldScreenViewModel world = setup.World;
        SettingsScreenViewModel settings = new(setup.GameViewModel);

        Assert.That(settings.ShowPathOnHover, Is.True);

        settings.ShowPathOnHover = false;

        Assert.That(setup.GameViewModel.Settings.ShowPathOnHover, Is.False);

        await world.HandleMapClickAsync(new WorldPosition(0, 0), null, 1);
        world.UpdateHover(new WorldPosition(2, 0), null);

        Assert.That(GetSelectedPath(world), Is.Null);

        settings.ShowPathOnHover = true;
        world.UpdateHover(new WorldPosition(3, 0), null);

        Assert.That(GetPathPositions(world), Is.EqualTo(new[] { new WorldPosition(1, 0), new WorldPosition(2, 0), new WorldPosition(3, 0) }));

        setup.GameViewModel.IsWindowed = true;

        Assert.That(setup.GameViewModel.Settings.ShowPathOnHover, Is.True);
    }

    [Test]
    public async Task SettingOffKeepsClickToSelectTest()
    {
        Setup setup = CreateWorld(showPathOnHover: false);
        WorldScreenViewModel world = setup.World;

        await world.HandleMapClickAsync(new WorldPosition(0, 0), null, 1);
        world.UpdateHover(new WorldPosition(2, 0), null);

        Assert.That(GetSelectedPath(world), Is.Null);

        await world.HandleMapClickAsync(new WorldPosition(2, 0), null, 1);

        Assert.That(GetPathPositions(world), Is.EqualTo(new[] { new WorldPosition(1, 0), new WorldPosition(2, 0) }));
        Assert.That(setup.GameModel.Snapshot.WorldState.Stacks[setup.Stack.Id].Position, Is.EqualTo(new WorldPosition(0, 0)));
    }
}

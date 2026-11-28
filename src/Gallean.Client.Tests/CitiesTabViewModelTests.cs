using System.Collections.Immutable;
using Gallean.Common;
using Gallean.Engine;
using Gallean.Engine.Tests;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade (docs/cities-overview.md): the cities list.
public sealed class CitiesTabViewModelTests
{
    private static readonly UnitDefinition LeaderDefinition = TestHelpers.SimpleLeaderDefinition(
        TestHelpers.SimpleUnitDefinition("leader", maxHitpoints: 100, initiative: 50, damage: 10),
        movementPoints: 20,
        leadership: 3);

    private static readonly UnitDefinition SoldierDefinition = TestHelpers.SimpleUnitDefinition("soldier", maxHitpoints: 35, initiative: 50, damage: 10);

    private static readonly WorldCityDefinition Level1 = TestHelpers.SimpleCityDefinition(new WorldCityDefinitionId(WorldRace.Human, 1), new WorldResourceSet(150, 0, 0, 0, 0, 0));
    private static readonly WorldCityDefinition Level2 = TestHelpers.SimpleCityDefinition(new WorldCityDefinitionId(WorldRace.Human, 2), new WorldResourceSet(250, 0, 0, 0, 0, 0));
    private static readonly WorldCityDefinition Level3 = TestHelpers.SimpleCityDefinition(new WorldCityDefinitionId(WorldRace.Human, 3), new WorldResourceSet(500, 0, 0, 0, 0, 0));

    private static readonly WorldFortificationId CastleId = new("castle");
    private static readonly WorldFortificationId AlphaId = new("alpha");
    private static readonly WorldFortificationId ZetaId = new("zeta");

    private sealed record Setup(GameSession Session, IEngineContext Context, GameViewModel Game, WorldScreenViewModel World);

    [SetUp]
    public void ResetSortOrder()
    {
        CitiesTabViewModel.LastSortOrder = CitySortOrder.Name;
    }

    private static WorldStack Army(string id, WorldPlayerId owner, WorldPosition position, IGameDefinitionProvider definitions)
    {
        WorldUnit leader = new(new WorldUnitId(id + "-leader"), LeaderDefinition.Id, 100, 0, LeaderDefinition.Level, [], null, false);

        return TestHelpers.SimpleStack(new WorldStackId(id), owner, position, TestHelpers.StackUnitGrid(definitions, [(leader, GridPosition.FrontBottom)]), movementPoints: 20);
    }

    // Capital "castle" (2..6, 2..6), "zeta" level 2 (11..14, 3..6) with a soldier, "alpha" level 1 (3..6, 13..16) with an
    // army of the player inside, an Undead army at (20, 6): 6 tiles from Zeta, 14 from the others.
    private static Setup Build(ImmutableArray<int> townIncome = default, int alphaRiotTurns = 0, int zetaSoldierHitpoints = 35, int gold = 300)
    {
        WorldMap map = TestHelpers.SimpleMap(30, 30);
        WorldPlayer player = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Human, map) with
        {
            Resources = new WorldResourceSet(gold, 0, 0, 0, 0, 0)
        };
        WorldPlayer enemy = TestHelpers.SimplePlayer(new WorldPlayerId(2), WorldRace.Undead, map);
        IGameDefinitionProvider definitions = new TestGameDefinitionProviderBuilder().AddUnit(LeaderDefinition).AddUnit(SoldierDefinition).Build();

        WorldUnit soldier = new(new WorldUnitId("soldier"), SoldierDefinition.Id, zetaSoldierHitpoints, 0, SoldierDefinition.Level, [], null, false);

        WorldCapital castle = TestHelpers.SimpleCapital(CastleId, player.Id, new WorldPosition(6, 6), UnitGrid<WorldUnit>.Empty) with
        {
            NameTextId = new ScenarioTextId("Castle")
        };
        WorldCity zeta = TestHelpers.SimpleCity(
            ZetaId,
            Level2,
            player.Id,
            new WorldPosition(14, 6),
            new UnitGrid<WorldUnit>.Builder().Place(soldier, GridPosition.FrontCenter, false).Build()) with
        {
            NameTextId = new ScenarioTextId("Zeta")
        };
        WorldCity alpha = TestHelpers.SimpleCity(AlphaId, Level1, player.Id, new WorldPosition(6, 16), UnitGrid<WorldUnit>.Empty) with
        {
            NameTextId = new ScenarioTextId("Alpha"),
            RiotTurns = alphaRiotTurns
        };

        (GameSession session, IEngineContext context) = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithTownIncome(townIncome.IsDefault ? [] : townIncome)
            .WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, LordCategory.Warrior))
            .WithDefinition(TestHelpers.SimpleLord(enemy.LordId, enemy.Race, LordCategory.Warrior))
            .WithPlayer(player)
            .WithPlayer(enemy)
            .WithDefinition(LeaderDefinition)
            .WithDefinition(SoldierDefinition)
            .WithDefinition(Level1)
            .WithDefinition(Level2)
            .WithDefinition(Level3)
            .WithCapital(castle)
            .WithCity(zeta)
            .WithCity(alpha)
            .WithStack(Army("visitor", player.Id, alpha.Entrance, definitions))
            .WithStack(Army("enemy", enemy.Id, new WorldPosition(20, 6), definitions))
            .WithSubrace(TestHelpers.NeutralSubrace)
            .BuildSession();

        session.World.DispatchCommand(new WorldStartGameCommand());

        GameModel gameModel = new(session, TestGameTextProvider.Instance, NullLog.Instance, SynchronousTaskScheduler.Instance);
        GameViewModel gameViewModel = new TestGameViewModelBuilder().Build();
        WorldScreenViewModel world = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(world);
        world.CompleteGameStart();
        gameViewModel.DevTools.IsFogEnabled = false;

        return new Setup(session, context, gameViewModel, world);
    }

    private static (ManagementScreenViewModel Screen, CitiesTabViewModel Cities) OpenCities(Setup setup)
    {
        Assert.That(setup.World.OpenCitiesCommand.CanExecute(null), Is.True);

        setup.World.OpenCitiesCommand.Execute(null);

        ManagementScreenViewModel screen = Assert.IsOfType<ManagementScreenViewModel>(setup.Game.TopScreen);

        return (screen, Assert.IsOfType<CitiesTabViewModel>(screen.SelectedTab));
    }

    private static CityRowViewModel Row(CitiesTabViewModel cities, WorldFortificationId id)
    {
        return cities.Rows.Single(row => row.Id == id);
    }

    [Test]
    public void RowsTest()
    {
        Setup setup = Build();
        (ManagementScreenViewModel screen, CitiesTabViewModel cities) = OpenCities(setup);
        GlobalVariables globals = setup.Context.Globals;
        int cityGrowth1 = FortificationCalculator.GetTerrainGrowth(setup.Session.World.State.Fortifications[AlphaId], globals);
        int cityGrowth2 = FortificationCalculator.GetTerrainGrowth(setup.Session.World.State.Fortifications[ZetaId], globals);
        WorldUnit visitor = setup.Session.World.State.Stacks[new WorldStackId("visitor")].GetLeader(setup.Context.Definitions).Unit;

        CityRowViewModel castle = Row(cities, CastleId);
        CityRowViewModel alpha = Row(cities, AlphaId);
        CityRowViewModel zeta = Row(cities, ZetaId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(screen.Tabs.Select(tab => tab.TabHeader), Is.EqualTo(["Cities"]));
            Assert.That(cities.Rows.Select(row => row.Name), Is.EqualTo(["Castle", "Alpha", "Zeta"]));
            Assert.That(cities.Rows.Select(row => row.LevelText), Is.EqualTo(["Capital", "Level 1", "Level 2"]));
            Assert.That(cities.HasTownIncome, Is.False);
            Assert.That(cities.Rows.Select(row => row.GoldText), Is.EqualTo(new string?[] { null, null, null }));
            Assert.That(cities.Rows.Select(row => row.LandText), Is.EqualTo([$"+{globals.CapitalTerrainTilesPerTurn}", $"+{cityGrowth1}", $"+{cityGrowth2}"]));
            Assert.That(cities.CountText, Is.EqualTo("2 cities"));
            Assert.That(cities.LevelsText, Is.EqualTo("Levels: 3"));
            Assert.That(cities.LandText, Is.EqualTo($"Land: +{globals.CapitalTerrainTilesPerTurn + cityGrowth1 + cityGrowth2} per turn"));

            Assert.That(castle.ImageKey, Is.EqualTo(new AssetSpriteKey.CapitalImage(WorldRace.Human)));
            Assert.That(zeta.ImageKey, Is.EqualTo(new AssetSpriteKey.CityImage(2)));
            Assert.That(castle.UpgradeText, Is.Null);
            Assert.That(castle.Statuses, Is.EqualTo(new[] { new CityStatusViewModel("Enemy 14 tiles away", IsWarning: false), new CityStatusViewModel("No garrison", IsWarning: true) }));

            Assert.That(zeta.GarrisonText, Is.EqualTo("1 / 2"));
            Assert.That(zeta.GarrisonFaces, Is.EqualTo(new[] { new CityGarrisonFaceViewModel(new AssetSpriteKey.UnitFace(SoldierDefinition.Id), IsDead: false, IsWounded: false) }));
            Assert.That(zeta.Statuses, Is.EqualTo(new[] { new CityStatusViewModel("Enemy 6 tiles away", IsWarning: true) }));
            Assert.That(zeta.HasVisitor, Is.False);
            Assert.That(zeta.UpgradeText, Is.EqualTo("To level 3"));
            Assert.That(zeta.UpgradeCost, Is.EqualTo(new ResourceSetViewModel(new WorldResourceSet(250, 0, 0, 0, 0, 0), ResourceSetProjection.All)));
            Assert.That(zeta.CanUpgrade, Is.True);

            Assert.That(alpha.VisitorName, Is.EqualTo(UnitNameResolver.Resolve(visitor, setup.Context.Definitions, TestGameTextProvider.Instance)));
            Assert.That(alpha.VisitorFaceKey, Is.EqualTo(new AssetSpriteKey.UnitFace(LeaderDefinition.Id)));
            Assert.That(alpha.GarrisonText, Is.EqualTo("0 / 1"));
            Assert.That(alpha.Center, Is.EqualTo(new WorldPosition(5, 15)));
        }
    }

    [Test]
    public void TownIncomeTest()
    {
        Setup setup = Build(townIncome: [10, 25, 50, 75, 100]);
        (_, CitiesTabViewModel cities) = OpenCities(setup);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cities.HasTownIncome, Is.True);
            Assert.That(cities.Rows.Select(row => row.GoldText), Is.EqualTo(["+100", "+10", "+25"]));
            Assert.That(cities.GoldText, Is.EqualTo("Gold: +135 per turn"));
        }
    }

    [Test]
    public void StatusesTest()
    {
        // Riots count down at the start of the turn: 3 on the map is 2 left.
        Setup setup = Build(alphaRiotTurns: 3, zetaSoldierHitpoints: 20);
        (_, CitiesTabViewModel cities) = OpenCities(setup);

        CityRowViewModel alpha = Row(cities, AlphaId);
        CityRowViewModel zeta = Row(cities, ZetaId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(alpha.Statuses.Select(status => status.Text), Is.EqualTo(["Riot: 2 turns left", "Enemy 14 tiles away", "No garrison"]));
            Assert.That(alpha.Statuses[0].IsWarning, Is.True);
            Assert.That(alpha.CanUpgrade, Is.False);
            Assert.That(zeta.Statuses.Select(status => status.Text), Is.EqualTo(["Enemy 6 tiles away", "1 wounded unit"]));
            Assert.That(zeta.GarrisonFaces.Single().IsWounded, Is.True);
        }
    }

    [Test]
    public void SortTest()
    {
        Setup setup = Build();
        (ManagementScreenViewModel screen, CitiesTabViewModel cities) = OpenCities(setup);

        cities.SortByCommand.Execute(CitySortOrder.Level);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cities.Rows.Select(row => row.Name), Is.EqualTo(["Castle", "Zeta", "Alpha"]));
            Assert.That(cities.IsLevelOrder, Is.True);
            Assert.That(cities.IsNameOrder, Is.False);
        }

        cities.SortByCommand.Execute(CitySortOrder.Name);
        Assert.That(cities.Rows.Select(row => row.Name), Is.EqualTo(["Castle", "Alpha", "Zeta"]));

        cities.SortByCommand.Execute(CitySortOrder.Danger);
        Assert.That(cities.Rows.Select(row => row.Name), Is.EqualTo(["Castle", "Zeta", "Alpha"]));

        // The order picked last is kept for the next time the list opens.
        setup.Game.CloseScreen(screen);
        (_, CitiesTabViewModel reopened) = OpenCities(setup);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reopened.SortOrder, Is.EqualTo(CitySortOrder.Danger));
            Assert.That(reopened.Rows.Select(row => row.Name), Is.EqualTo(["Castle", "Zeta", "Alpha"]));
        }
    }

    [Test]
    public async Task UpgradeFromRowTest()
    {
        Setup setup = Build(gold: 160);
        int goldBefore = setup.Session.World.State.Players[new WorldPlayerId(1)].Resources.Gold;
        (ManagementScreenViewModel screen, CitiesTabViewModel cities) = OpenCities(setup);

        cities.UpgradeCityCommand.Execute(Row(cities, AlphaId));

        ConfirmPromptViewModel prompt = Assert.IsOfType<ConfirmPromptViewModel>(setup.Game.TopScreen);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt.Title.Text, Is.EqualTo("Upgrade Alpha to level 2"));
            Assert.That(prompt.ConfirmPrice, Is.EqualTo(new ResourceSetViewModel(new WorldResourceSet(150, 0, 0, 0, 0, 0), ResourceSetProjection.All)));
        }

        await prompt.ConfirmCommand.ExecuteAsync(null);

        WorldCity alpha = Assert.IsOfType<WorldCity>(setup.Session.World.State.Fortifications[AlphaId]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(alpha.DefinitionId, Is.EqualTo(Level2.Id));
            Assert.That(setup.Game.TopScreen, Is.SameAs(screen));
            Assert.That(Row(cities, AlphaId).LevelText, Is.EqualTo("Level 2"));
            Assert.That(Row(cities, AlphaId).UpgradeText, Is.EqualTo("Upgraded this turn"));
            Assert.That(Row(cities, AlphaId).CanUpgrade, Is.False);
            Assert.That(setup.Session.World.State.Players[new WorldPlayerId(1)].Resources.Gold, Is.EqualTo(goldBefore - 150));
            // Too little gold left for Zeta's 250: still shown, not allowed.
            Assert.That(Row(cities, ZetaId).UpgradeText, Is.EqualTo("To level 3"));
            Assert.That(Row(cities, ZetaId).CanUpgrade, Is.False);
            Assert.That(cities.LevelsText, Is.EqualTo("Levels: 4"));
        }

        // A row that can't upgrade does nothing (no prompt).
        cities.UpgradeCityCommand.Execute(Row(cities, ZetaId));
        Assert.That(setup.Game.TopScreen, Is.SameAs(screen));
    }

    [Test]
    public void EnterCityTest()
    {
        Setup setup = Build();
        (ManagementScreenViewModel screen, CitiesTabViewModel cities) = OpenCities(setup);

        cities.EnterCityCommand.Execute(Row(cities, ZetaId));

        ManagementScreenViewModel city = Assert.IsOfType<ManagementScreenViewModel>(setup.Game.TopScreen);
        CityTabViewModel cityTab = Assert.IsOfType<CityTabViewModel>(city.SelectedTab);

        Assert.That(cityTab.CityTitle, Is.EqualTo("Zeta"));

        // Closing the city goes back to the list.
        setup.Game.CloseScreen(city);
        Assert.That(setup.Game.TopScreen, Is.SameAs(screen));
    }

    [Test]
    public void ShowOnMapTest()
    {
        Setup setup = Build();
        (_, CitiesTabViewModel cities) = OpenCities(setup);

        cities.ShowOnMapCommand.Execute(Row(cities, AlphaId));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(setup.Game.TopScreen, Is.SameAs(setup.World));
            Assert.That(setup.World.CameraFocus, Is.EqualTo(new WorldPosition(5, 15)));
        }
    }

    [Test]
    public void NoCitiesTest()
    {
        WorldMap map = TestHelpers.SimpleMap(2, 1);
        WorldPlayer player = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Human, map);

        (GameSession session, _) = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, LordCategory.Warrior))
            .WithPlayer(player)
            .BuildSession();

        session.World.DispatchCommand(new WorldStartGameCommand());

        GameModel gameModel = new(session, TestGameTextProvider.Instance, NullLog.Instance, SynchronousTaskScheduler.Instance);
        GameViewModel gameViewModel = new TestGameViewModelBuilder().Build();
        WorldScreenViewModel world = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(world);
        world.CompleteGameStart();

        Assert.That(world.OpenCitiesCommand.CanExecute(null), Is.False);
    }

    // Phase 2: previous / next city on a city screen.
    private static ManagementScreenViewModel OpenCapital(Setup setup)
    {
        setup.World.OpenCapitalCommand.Execute(null);

        return Assert.IsOfType<ManagementScreenViewModel>(setup.Game.TopScreen);
    }

    private static string Title(ManagementScreenViewModel screen)
    {
        return screen.Tabs.OfType<CityTabViewModel>().Single().CityTitle;
    }

    [Test]
    public void CycleTest()
    {
        Setup setup = Build();
        ManagementScreenViewModel castle = OpenCapital(setup);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(castle.HasCityCycle, Is.True);
            Assert.That(castle.CityPositionText, Is.EqualTo("1 / 3"));
            Assert.That(castle.SelectNextCityCommand.CanExecute(null), Is.True);
        }

        castle.SelectNextCityCommand.Execute(null);
        ManagementScreenViewModel alpha = Assert.IsOfType<ManagementScreenViewModel>(setup.Game.TopScreen);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Title(alpha), Is.EqualTo("Alpha"));
            Assert.That(alpha.CityPositionText, Is.EqualTo("2 / 3"));
        }

        alpha.SelectNextCityCommand.Execute(null);
        ManagementScreenViewModel zeta = Assert.IsOfType<ManagementScreenViewModel>(setup.Game.TopScreen);
        Assert.That(Title(zeta), Is.EqualTo("Zeta"));

        // Wraps around both ways.
        zeta.SelectNextCityCommand.Execute(null);
        ManagementScreenViewModel again = Assert.IsOfType<ManagementScreenViewModel>(setup.Game.TopScreen);
        Assert.That(Title(again), Is.EqualTo("Castle"));

        again.SelectPreviousCityCommand.Execute(null);
        ManagementScreenViewModel back = Assert.IsOfType<ManagementScreenViewModel>(setup.Game.TopScreen);
        Assert.That(Title(back), Is.EqualTo("Zeta"));

        // The switched screens replace each other: closing the last one is back on the map.
        setup.Game.CloseScreen(back);
        Assert.That(setup.Game.TopScreen, Is.SameAs(setup.World));
    }

    [Test]
    public void CycleKeepsTabTest()
    {
        Setup setup = Build();
        ManagementScreenViewModel castle = OpenCapital(setup);

        // From a capital-only tab (Buildings) the next city opens on its City tab.
        castle.SelectNextTabCommand.Execute(null);
        castle.SelectNextTabCommand.Execute(null);
        Assert.That(castle.SelectedTab.TabHeader, Is.EqualTo("Buildings"));

        castle.SelectNextCityCommand.Execute(null);
        ManagementScreenViewModel alpha = Assert.IsOfType<ManagementScreenViewModel>(setup.Game.TopScreen);
        Assert.IsOfType<CityTabViewModel>(alpha.SelectedTab);

        // The Party tab stays the Party tab.
        alpha.SelectNextTabCommand.Execute(null);
        Assert.IsOfType<VisitingStackTabViewModel>(alpha.SelectedTab);

        alpha.SelectNextCityCommand.Execute(null);
        ManagementScreenViewModel zeta = Assert.IsOfType<ManagementScreenViewModel>(setup.Game.TopScreen);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Title(zeta), Is.EqualTo("Zeta"));
            Assert.That(zeta.SelectedTab, Is.InstanceOf<VisitingStackTabViewModel>());
        }
    }

    [Test]
    public void CycleFollowsListOrderTest()
    {
        Setup setup = Build();
        (ManagementScreenViewModel list, CitiesTabViewModel cities) = OpenCities(setup);

        cities.SortByCommand.Execute(CitySortOrder.Level);
        cities.EnterCityCommand.Execute(Row(cities, ZetaId));

        ManagementScreenViewModel zeta = Assert.IsOfType<ManagementScreenViewModel>(setup.Game.TopScreen);
        Assert.That(zeta.CityPositionText, Is.EqualTo("2 / 3"));

        zeta.SelectNextCityCommand.Execute(null);
        ManagementScreenViewModel alpha = Assert.IsOfType<ManagementScreenViewModel>(setup.Game.TopScreen);
        Assert.That(Title(alpha), Is.EqualTo("Alpha"));

        // Esc from a switched city still goes back to the list it was opened from.
        setup.Game.CloseScreen(alpha);
        Assert.That(setup.Game.TopScreen, Is.SameAs(list));
    }

    [Test]
    public void CycleBlockedWhileTypingTest()
    {
        Setup setup = Build();
        ManagementScreenViewModel castle = OpenCapital(setup);

        castle.SelectedTab.IsTyping = true;
        castle.SelectNextCityCommand.Execute(null);

        Assert.That(setup.Game.TopScreen, Is.SameAs(castle));
    }

    [Test]
    public void NoCycleWithOneCityTest()
    {
        WorldMap map = TestHelpers.SimpleMap(8, 8);
        WorldPlayer player = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Human, map);
        WorldCapital capital = TestHelpers.SimpleCapital(CastleId, player.Id, new WorldPosition(4, 4), UnitGrid<WorldUnit>.Empty);

        (GameSession session, _) = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, LordCategory.Warrior))
            .WithPlayer(player)
            .WithCapital(capital)
            .BuildSession();

        session.World.DispatchCommand(new WorldStartGameCommand());

        GameModel gameModel = new(session, TestGameTextProvider.Instance, NullLog.Instance, SynchronousTaskScheduler.Instance);
        GameViewModel gameViewModel = new TestGameViewModelBuilder().Build();
        ManagementScreenViewModel screen = ManagementScreenViewModel.CreateForFortification(gameModel, gameViewModel, CastleId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(screen.HasCityCycle, Is.False);
            Assert.That(screen.CityPositionText, Is.Empty);
            Assert.That(screen.SelectNextCityCommand.CanExecute(null), Is.False);
        }
    }
}


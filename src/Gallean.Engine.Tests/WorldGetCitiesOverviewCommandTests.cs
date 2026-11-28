using System.Collections.Immutable;
using Gallean.Common;

namespace Gallean.Engine.Tests;

// Gallean Upgrade (docs/cities-overview.md): the rows of the cities list.
public sealed class WorldGetCitiesOverviewCommandTests
{
    private static readonly WorldPlayerId Viewer = new(1);
    private static readonly WorldPlayerId Enemy = new(2);
    private static readonly WorldPlayerId Ally = new(3);
    private static readonly WorldPlayerId Neutrals = new(4);

    private static readonly UnitDefinition LeaderDefinition = TestHelpers.SimpleLeaderDefinition(
        TestHelpers.SimpleUnitDefinition("leader", maxHitpoints: 100, initiative: 50, damage: 10),
        movementPoints: 20,
        leadership: 3);

    private static readonly UnitDefinition SoldierDefinition = TestHelpers.SimpleUnitDefinition("soldier", maxHitpoints: 35, initiative: 50, damage: 10);

    private static readonly WorldCityDefinition Level1 = TestHelpers.SimpleCityDefinition(new WorldCityDefinitionId(WorldRace.Human, 1), new WorldResourceSet(150, 0, 0, 0, 0, 0));
    private static readonly WorldCityDefinition Level2 = TestHelpers.SimpleCityDefinition(new WorldCityDefinitionId(WorldRace.Human, 2), new WorldResourceSet(250, 0, 0, 0, 0, 0));
    private static readonly WorldCityDefinition Level3 = TestHelpers.SimpleCityDefinition(new WorldCityDefinitionId(WorldRace.Human, 3), new WorldResourceSet(500, 0, 0, 0, 0, 0));
    private static readonly WorldCityDefinition Level4 = TestHelpers.SimpleCityDefinition(new WorldCityDefinitionId(WorldRace.Human, 4), new WorldResourceSet(750, 0, 0, 0, 0, 0));
    private static readonly WorldCityDefinition Level5 = TestHelpers.SimpleCityDefinition(new WorldCityDefinitionId(WorldRace.Human, 5), WorldResourceSet.Empty);
    private static readonly WorldCityDefinition UndeadLevel1 = TestHelpers.SimpleCityDefinition(new WorldCityDefinitionId(WorldRace.Undead, 1), new WorldResourceSet(150, 0, 0, 0, 0, 0));

    private static WorldStack Army(string id, WorldPlayerId owner, WorldPosition position, IGameDefinitionProvider definitions)
    {
        WorldUnit leader = new(new WorldUnitId(id + "-leader"), LeaderDefinition.Id, 100, 0, LeaderDefinition.Level, [], null, false);

        return TestHelpers.SimpleStack(new WorldStackId(id), owner, position, TestHelpers.StackUnitGrid(definitions, [(leader, GridPosition.FrontBottom)]), movementPoints: 20);
    }

    private static (GameSession Session, IEngineContext Context) Build(ImmutableArray<int> townIncome, bool hideTile = false)
    {
        WorldMap map = TestHelpers.SimpleMap(40, 40);
        ImmutableBitmap.Builder fog = new(ImmutableBitmap.Create(map.Width, map.Height, initialValue: true));

        if (hideTile)
        {
            fog.Clear(16, 30);
        }

        WorldPlayer viewer = TestHelpers.SimplePlayer(Viewer, WorldRace.Human, map) with
        {
            Resources = new WorldResourceSet(600, 0, 0, 0, 0, 0),
            FogOfWar = fog.ToImmutable()
        };
        WorldPlayer enemy = TestHelpers.SimplePlayer(Enemy, WorldRace.Undead, map);
        WorldPlayer ally = TestHelpers.SimplePlayer(Ally, WorldRace.Elf, map);
        WorldPlayer neutrals = TestHelpers.SimplePlayer(Neutrals, WorldRace.Neutral, map);

        IGameDefinitionProvider definitions = new TestGameDefinitionProviderBuilder().AddUnit(LeaderDefinition).AddUnit(SoldierDefinition).Build();

        WorldUnit wounded = new(new WorldUnitId("wounded"), SoldierDefinition.Id, 20, 0, SoldierDefinition.Level, [], null, false);
        WorldUnit dead = new(new WorldUnitId("dead"), SoldierDefinition.Id, 0, 0, SoldierDefinition.Level, [], null, false);
        WorldUnit healthy = new(new WorldUnitId("healthy"), SoldierDefinition.Id, 35, 0, SoldierDefinition.Level, [], null, false);

        WorldCapital capital = TestHelpers.SimpleCapital(new WorldFortificationId("capital"), Viewer, new WorldPosition(6, 6), UnitGrid<WorldUnit>.Empty);

        // Footprint (11..14, 3..6), level 3, two soldiers of three hurt.
        WorldCity milonia = TestHelpers.SimpleCity(
            new WorldFortificationId("milonia"),
            Level3,
            Viewer,
            new WorldPosition(14, 6),
            new UnitGrid<WorldUnit>.Builder()
                .Place(wounded, GridPosition.FrontBottom, false)
                .Place(dead, GridPosition.FrontCenter, false)
                .Place(healthy, GridPosition.FrontTop, false)
                .Build());

        // Footprint (13..16, 17..20), rioting.
        WorldCity rioting = TestHelpers.SimpleCity(new WorldFortificationId("rioting"), Level2, Viewer, new WorldPosition(16, 20), UnitGrid<WorldUnit>.Empty) with
        {
            RiotTurns = 3
        };

        WorldCity foreign = TestHelpers.SimpleCity(new WorldFortificationId("foreign"), UndeadLevel1, Enemy, new WorldPosition(36, 36), UnitGrid<WorldUnit>.Empty);

        (GameSession session, IEngineContext context) = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithTownIncome(townIncome)
            .WithDefinition(TestHelpers.SimpleLord(viewer.LordId, viewer.Race, LordCategory.Warrior))
            .WithDefinition(TestHelpers.SimpleLord(enemy.LordId, enemy.Race, LordCategory.Warrior))
            .WithDefinition(TestHelpers.SimpleLord(ally.LordId, ally.Race, LordCategory.Warrior))
            .WithPlayer(viewer)
            .WithPlayer(enemy)
            .WithPlayer(ally)
            .WithPlayer(neutrals)
            .WithRelation(new WorldRelation(new WorldRelationId(WorldRace.Human, WorldRace.Elf), 100, 100, Alliance: true, AllianceTurn: 0, PermanentWar: false, PermanentAlliance: false))
            .WithDefinition(LeaderDefinition)
            .WithDefinition(SoldierDefinition)
            .WithDefinition(Level1)
            .WithDefinition(Level2)
            .WithDefinition(Level3)
            .WithDefinition(Level4)
            .WithDefinition(Level5)
            .WithDefinition(UndeadLevel1)
            .WithCapital(capital)
            .WithCity(milonia)
            .WithCity(rioting)
            .WithCity(foreign)
            // Counted: 5 tiles east of Milonia.
            .WithStack(Army("enemy", Enemy, new WorldPosition(19, 5), definitions))
            // Not counted: an invisible enemy, an ally, a neutral army, all right next to Milonia.
            .WithStack(Army("concealed", Enemy, new WorldPosition(15, 4), definitions) with { Concealed = true })
            .WithStack(Army("ally", Ally, new WorldPosition(15, 6), definitions))
            .WithStack(Army("neutral", Neutrals, new WorldPosition(15, 5), definitions))
            // 10 tiles south of the rioting town; on a hidden tile when hideTile is set.
            .WithStack(Army("far", Enemy, new WorldPosition(16, 30), definitions))
            .WithSubrace(TestHelpers.NeutralSubrace)
            .BuildSession();

        _ = session.World.DispatchCommand(new WorldStartGameCommand());

        return (session, context);
    }

    private static WorldGetCitiesOverviewCommand.Result Overview(GameSession session, IEngineContext context)
    {
        ICommandResult<WorldCommandError, WorldEvent, WorldGetCitiesOverviewCommand.Result> result = new WorldGetCitiesOverviewCommand().Execute(session.World.State, context);

        Assert.That(result.Failed, Is.False);

        return result.Data;
    }

    [Test]
    public void RowsTest()
    {
        (GameSession session, IEngineContext context) = Build([]);
        WorldGetCitiesOverviewCommand.Result overview = Overview(session, context);
        GlobalVariables globals = context.Globals;

        Assert.That(overview.Cities.Select(city => city.FortificationId.Value), Is.EquivalentTo(new[] { "capital", "milonia", "rioting" }));

        WorldGetCitiesOverviewCommand.City capital = overview.Cities.Single(city => city.FortificationId.Value == "capital");
        WorldGetCitiesOverviewCommand.City milonia = overview.Cities.Single(city => city.FortificationId.Value == "milonia");
        WorldGetCitiesOverviewCommand.City rioting = overview.Cities.Single(city => city.FortificationId.Value == "rioting");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(capital.TerrainGrowth, Is.EqualTo(globals.CapitalTerrainTilesPerTurn));
            Assert.That(capital.Growth, Is.Null);
            Assert.That(capital.TotalCapacity, Is.EqualTo(globals.CapitalCityLevel));
            Assert.That(capital.TownIncome, Is.Null);

            Assert.That(milonia.TerrainGrowth, Is.EqualTo(FortificationCalculator.GetTerrainGrowth(session.World.State.Fortifications[milonia.FortificationId], globals)));
            Assert.That(milonia.OccupiedCapacity, Is.EqualTo(3));
            Assert.That(milonia.TotalCapacity, Is.EqualTo(3));
            Assert.That(milonia.WoundedUnits, Is.EqualTo(1));
            Assert.That(milonia.DeadUnits, Is.EqualTo(1));
            Assert.That(milonia.Growth, Is.EqualTo(new WorldGetCityInfoCommand.Growth(new WorldResourceSet(500, 0, 0, 0, 0, 0), null)));
            Assert.That(milonia.NearestEnemyDistance, Is.EqualTo(5));
            Assert.That(milonia.TownIncome, Is.Null);

            Assert.That(rioting.Growth!.Error, Is.EqualTo(WorldCommandError.CityRioting));
            Assert.That(rioting.NearestEnemyDistance, Is.EqualTo(10));
        }
    }

    [Test]
    public void TownIncomeTest()
    {
        (GameSession session, IEngineContext context) = Build([10, 25, 50, 75, 100]);
        WorldGetCitiesOverviewCommand.Result overview = Overview(session, context);

        Assert.That(
            overview.Cities.OrderBy(city => city.FortificationId.Value).Select(city => (city.FortificationId.Value, city.TownIncome)),
            Is.EqualTo(new (string, int?)[] { ("capital", 100), ("milonia", 50), ("rioting", 0) }));
    }

    [Test]
    public void HiddenEnemyTest()
    {
        (GameSession session, IEngineContext context) = Build([], hideTile: true);

        Assert.That(session.World.State.Players[Viewer].FogOfWar.Get(16, 30), Is.False, "the test needs the tile still hidden after the start");

        WorldGetCitiesOverviewCommand.City rioting = Overview(session, context).Cities.Single(city => city.FortificationId.Value == "rioting");

        // The hidden army 10 tiles south is skipped; the nearest one left is the enemy east of Milonia.
        Assert.That(rioting.NearestEnemyDistance, Is.EqualTo(12));
    }

    [Test]
    public void UpgradedThisTurnTest()
    {
        (GameSession session, IEngineContext context) = Build([]);

        _ = session.World.DispatchCommand(new WorldUpgradeCityCommand(new WorldFortificationId("milonia")));

        WorldGetCitiesOverviewCommand.City milonia = Overview(session, context).Cities.Single(city => city.FortificationId.Value == "milonia");

        Assert.That(milonia.Growth!.Error, Is.EqualTo(WorldCommandError.CityAlreadyGrownThisTurn));
    }

    [TestCase(11, 4, 0)]
    [TestCase(9, 2, 1)]
    [TestCase(14, 5, 1)]
    [TestCase(12, 2, 1)]
    [TestCase(20, 4, 7)]
    [TestCase(11, 14, 8)]
    public void DistanceTest(int x, int y, int expected)
    {
        // Footprint (10..13, 3..6).
        WorldFootprint footprint = new(new WorldPosition(10, 3), 4, 4);

        Assert.That(WorldGetCitiesOverviewCommand.GetDistance(footprint, new WorldPosition(x, y)), Is.EqualTo(expected));
    }
}

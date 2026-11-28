using Gallean.Common;

namespace Gallean.Engine.Tests;

public sealed class AutoRodCalculatorTests
{
    private static readonly WorldPlayerId Me = new(1);
    private static readonly WorldPlayerId Enemy = new(2);
    private static readonly WorldStackId Planter = new("Planter");

    private sealed record Setup(
        Func<int, int, bool> Seen,
        WorldPosition PlanterAt,
        int Gold = 150,
        bool RodAbility = true,
        WorldPosition? Node = null,
        WorldPosition? EnemyRod = null,
        WorldPosition? OwnRod = null,
        WorldPosition? EnemyArmy = null,
        int Width = 16,
        int Height = 8);

    private static WorldEngine Build(Setup setup)
    {
        WorldMap map = TestHelpers.SimpleMap(setup.Width, setup.Height);
        ImmutableBitmap.Builder fog = ImmutableBitmap.Create(map.Width, map.Height).ToBuilder();

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (setup.Seen(x, y))
                {
                    fog.Set(x, y);
                }
            }
        }

        WorldPlayer me = TestHelpers.SimplePlayer(Me, WorldRace.Undead, map) with
        {
            FogOfWar = fog.ToImmutable(),
            Resources = new WorldResourceSet(setup.Gold, 0, 0, 0, 0, 0)
        };
        WorldPlayer other = TestHelpers.SimplePlayer(Enemy, WorldRace.Human, map);
        UnitDefinition leader = TestHelpers.SimpleLeaderDefinition(
            TestHelpers.SimpleUnitDefinition("leader", maxHitpoints: 100, initiative: 50, damage: 10),
            movementPoints: 20, leadership: 1);
        UnitDefinition planter = setup.RodAbility
            ? leader with { Id = new UnitDefinitionId("planter"), LeaderCategory = UnitLeaderCategory.Rod, LeaderAbilities = LeaderAbility.Rod }
            : leader with { Id = new UnitDefinitionId("planter") };

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(leader)
            .WithDefinition(planter)
            .WithDefinition(TestHelpers.SimpleLord(me.LordId, me.Race, LordCategory.Warrior))
            .WithDefinition(TestHelpers.SimpleLord(other.LordId, other.Race, LordCategory.Warrior))
            .WithPlayer(me)
            .WithPlayer(other)
            .WithStack(Stack("Planter", Me, setup.PlanterAt, planter));

        if (setup.Node is { } node)
        {
            builder = builder.WithResourceNode(new WorldResourceNode(new WorldResourceNodeId("node"), node, WorldResourceType.Gold, AiPriority: 3));
        }

        if (setup.EnemyRod is { } enemyRod)
        {
            builder = builder.WithRod(new WorldRod(new WorldRodId("enemyRod"), Enemy, enemyRod));
        }

        if (setup.OwnRod is { } ownRod)
        {
            builder = builder.WithRod(new WorldRod(new WorldRodId("ownRod"), Me, ownRod));
        }

        if (setup.EnemyArmy is { } army)
        {
            builder = builder.WithStack(Stack("Enemy", Enemy, army, leader));
        }

        WorldEngine engine = builder.Build();
        _ = engine.DispatchCommand(new WorldStartGameCommand());

        return engine;
    }

    private static WorldStack Stack(string id, WorldPlayerId owner, WorldPosition position, UnitDefinition leader)
    {
        return TestHelpers.SimpleStack(
            new WorldStackId(id),
            owner,
            position,
            TestHelpers.SingleUnitGrid(new WorldUnitId(id + "Leader"), leader),
            movementPoints: 20);
    }

    private static AutoRodPick Pick(WorldEngine engine, IReadOnlySet<WorldPosition>? skipped = null)
    {
        return engine.CheckCommand(new WorldGetAutoRodJobCommand(Planter, skipped), []);
    }

    private static bool LeftHalf(int x, int y)
    {
        return x <= 9;
    }

    private static int Distance(WorldPosition a, WorldPosition b)
    {
        return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    [Test]
    public void WalksToSeenResourceAndPlantsNextToItTest()
    {
        WorldPosition node = new(7, 4);
        WorldEngine engine = Build(new Setup(LeftHalf, new WorldPosition(1, 4), Node: node));

        AutoRodPick pick = Pick(engine);

        Assert.That(pick.ShortOfGold, Is.False);
        Assert.That(pick.Job, Is.TypeOf<AutoRodJob.Plant>());
        AutoRodJob.Plant plant = (AutoRodJob.Plant)pick.Job;
        Assert.That(Distance(plant.Target, node), Is.EqualTo(1));
        Assert.That(Distance(plant.StandAt, plant.Target), Is.LessThanOrEqualTo(1));
        Assert.That(plant.StandAt, Is.Not.EqualTo(plant.Target));
        Assert.That(plant.PathCost, Is.GreaterThan(0));
        Assert.That(plant.Resource, Is.EqualTo(WorldResourceType.Gold));
    }

    [Test]
    public void PlantsWithoutMovingWhenAlreadyInRangeTest()
    {
        WorldEngine engine = Build(new Setup(LeftHalf, new WorldPosition(5, 4), Node: new WorldPosition(7, 4)));

        AutoRodPick pick = Pick(engine);

        Assert.That(pick.Job, Is.TypeOf<AutoRodJob.Plant>());
        AutoRodJob.Plant plant = (AutoRodJob.Plant)pick.Job;
        Assert.That(plant.StandAt, Is.EqualTo(new WorldPosition(5, 4)));
        Assert.That(plant.PathCost, Is.EqualTo(0));
        Assert.That(Distance(plant.Target, new WorldPosition(7, 4)), Is.EqualTo(1));
    }

    [Test]
    public void PlantSpotIsAcceptedByTheRealCommandTest()
    {
        WorldEngine engine = Build(new Setup(LeftHalf, new WorldPosition(5, 4), Node: new WorldPosition(7, 4)));
        AutoRodJob.Plant plant = (AutoRodJob.Plant)Pick(engine).Job;

        _ = engine.DispatchCommand(new WorldCreateRodCommand(Planter, plant.Target));

        Assert.That(engine.State.Rods.Values.Select(rod => rod.Position), Is.EqualTo(new[] { plant.Target }));
    }

    [Test]
    public void ResourceInTheFogIsUnknownSoItExploresTest()
    {
        WorldEngine engine = Build(new Setup(LeftHalf, new WorldPosition(1, 4), Node: new WorldPosition(13, 4)));

        AutoRodPick pick = Pick(engine);

        Assert.That(pick.Job, Is.TypeOf<AutoRodJob.Explore>());
        Assert.That(((AutoRodJob.Explore)pick.Job).Target.Position.X, Is.EqualTo(9));
    }

    [Test]
    public void RemovesSeenHostileRodTest()
    {
        WorldPosition rod = new(7, 4);
        WorldEngine engine = Build(new Setup(LeftHalf, new WorldPosition(1, 4), EnemyRod: rod));

        AutoRodPick pick = Pick(engine);

        Assert.That(pick.Job, Is.TypeOf<AutoRodJob.Remove>());
        AutoRodJob.Remove remove = (AutoRodJob.Remove)pick.Job;
        Assert.That(remove.Target, Is.EqualTo(rod));
        Assert.That(Distance(remove.StandAt, rod), Is.EqualTo(1));
    }

    [Test]
    public void HostileRodInTheFogIsUnknownTest()
    {
        WorldEngine engine = Build(new Setup(LeftHalf, new WorldPosition(1, 4), EnemyRod: new WorldPosition(13, 4)));

        Assert.That(Pick(engine).Job, Is.TypeOf<AutoRodJob.Explore>());
    }

    [Test]
    public void ShortOfGoldStillRemovesRodsTest()
    {
        WorldEngine engine = Build(new Setup(LeftHalf, new WorldPosition(1, 4), Gold: 0, Node: new WorldPosition(4, 4), EnemyRod: new WorldPosition(8, 2)));

        AutoRodPick pick = Pick(engine);

        Assert.That(pick.ShortOfGold, Is.True);
        Assert.That(pick.Job, Is.TypeOf<AutoRodJob.Remove>());
    }

    [Test]
    public void ShortOfGoldSkipsPlantJobsAndExploresTest()
    {
        WorldEngine engine = Build(new Setup(LeftHalf, new WorldPosition(1, 4), Gold: 0, Node: new WorldPosition(4, 4)));

        AutoRodPick pick = Pick(engine);

        Assert.That(pick.ShortOfGold, Is.True);
        Assert.That(pick.Job, Is.TypeOf<AutoRodJob.Explore>());
    }

    [Test]
    public void SkippedSpotsAreNotPickedAgainTest()
    {
        WorldPosition node = new(7, 4);
        HashSet<WorldPosition> all = [.. ScenarioLocationCalculator.EnumerateTiles(node, 1, TestHelpers.SimpleMap(16, 8))];
        WorldEngine engine = Build(new Setup(LeftHalf, new WorldPosition(1, 4), Node: node));

        AutoRodJob.Plant first = (AutoRodJob.Plant)Pick(engine).Job;
        AutoRodJob second = Pick(engine, new HashSet<WorldPosition> { first.Target }).Job;

        Assert.That(second, Is.TypeOf<AutoRodJob.Plant>());
        Assert.That(((AutoRodJob.Plant)second).Target, Is.Not.EqualTo(first.Target));
        Assert.That(Pick(engine, all).Job, Is.TypeOf<AutoRodJob.Explore>());
    }

    [Test]
    public void ResourceWithRodNextToItIsNoJobTest()
    {
        WorldEngine engine = Build(new Setup(LeftHalf, new WorldPosition(1, 4), Node: new WorldPosition(7, 4), OwnRod: new WorldPosition(8, 4)));

        Assert.That(Pick(engine).Job, Is.TypeOf<AutoRodJob.Explore>());
    }

    [Test]
    public void OwnRodIsNotRemovedTest()
    {
        WorldEngine engine = Build(new Setup((_, _) => true, new WorldPosition(1, 4), OwnRod: new WorldPosition(7, 4)));

        Assert.That(Pick(engine).Job, Is.EqualTo(AutoRodJob.NothingLeft.Instance));
    }

    [Test]
    public void EverythingSeenAndNoJobIsNothingLeftTest()
    {
        WorldEngine engine = Build(new Setup((_, _) => true, new WorldPosition(1, 4)));

        Assert.That(Pick(engine).Job, Is.EqualTo(AutoRodJob.NothingLeft.Instance));
    }

    [Test]
    public void JobNearKnownEnemyArmyIsSkippedTest()
    {
        WorldEngine engine = Build(new Setup(LeftHalf, new WorldPosition(1, 1), Node: new WorldPosition(7, 6), EnemyArmy: new WorldPosition(7, 7)));

        Assert.That(Pick(engine).Job, Is.TypeOf<AutoRodJob.Explore>());
    }

    [Test]
    public void LeaderWithoutRodAbilityIsNotRodPlanterTest()
    {
        WorldEngine engine = Build(new Setup(LeftHalf, new WorldPosition(1, 4), RodAbility: false, Node: new WorldPosition(7, 4)));

        Assert.That(Pick(engine).Job, Is.EqualTo(AutoRodJob.NotRodPlanter.Instance));
    }

    [Test]
    public void NearerJobWinsTest()
    {
        // Rod removal (interest 105) right next to the planter beats planting (120) 3-4 turns away; like RodAction,
        // distance only scales the interest down (a plant one turn away would still win).
        WorldEngine engine = Build(new Setup((_, _) => true, new WorldPosition(1, 4), Node: new WorldPosition(38, 4), EnemyRod: new WorldPosition(3, 4), Width: 40));

        Assert.That(Pick(engine).Job, Is.TypeOf<AutoRodJob.Remove>());
    }
}

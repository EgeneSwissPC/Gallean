using Gallean.Common;

namespace Gallean.Engine.Tests;

public sealed class AutoExploreCalculatorTests
{
    private static readonly WorldPlayerId Me = new(1);
    private static readonly WorldPlayerId Enemy = new(2);

    // rows: map characters (TestHelpers.BuildMap: '.' plain, '^' mountain, '~' water, '+' forest, '=' road).
    // seen: tiles the player has seen before the game starts (vision around armies is added by the start).
    private static WorldEngine Build(
        string[] rows,
        Func<int, int, bool> seen,
        WorldPosition explorer,
        WorldPosition? enemy = null,
        WorldPosition? ownBlocker = null,
        bool ship = false)
    {
        WorldMap map = TestHelpers.BuildMap(rows);
        ImmutableBitmap.Builder fog = ImmutableBitmap.Create(map.Width, map.Height).ToBuilder();

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (seen(x, y))
                {
                    fog.Set(x, y);
                }
            }
        }

        WorldPlayer me = TestHelpers.SimplePlayer(Me, WorldRace.Undead, map) with { FogOfWar = fog.ToImmutable() };
        WorldPlayer other = TestHelpers.SimplePlayer(Enemy, WorldRace.Human, map);
        UnitDefinition leader = TestHelpers.SimpleLeaderDefinition(
            TestHelpers.SimpleUnitDefinition("leader", maxHitpoints: 100, initiative: 50, damage: 10),
            movementPoints: 20, leadership: 1);
        UnitDefinition explorerLeader = ship ? leader with { Id = new UnitDefinitionId("ship"), IsWaterOnly = true } : leader;

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(leader)
            .WithDefinition(explorerLeader)
            .WithDefinition(TestHelpers.SimpleLord(me.LordId, me.Race, LordCategory.Warrior))
            .WithDefinition(TestHelpers.SimpleLord(other.LordId, other.Race, LordCategory.Warrior))
            .WithPlayer(me)
            .WithPlayer(other)
            .WithStack(Stack("Explorer", Me, explorer, explorerLeader));

        if (enemy is { } enemyPosition)
        {
            builder = builder.WithStack(Stack("Enemy", Enemy, enemyPosition, leader));
        }

        if (ownBlocker is { } blockerPosition)
        {
            builder = builder.WithStack(Stack("Blocker", Me, blockerPosition, leader));
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

    private static AutoExploreResult Explore(WorldEngine engine, WorldPosition? previous = null, AutoExploreSettings? settings = null)
    {
        return engine.CheckCommand(new WorldGetAutoExploreTargetCommand(new WorldStackId("Explorer"), previous, settings), []);
    }

    private static string[] Plain(int width, int height, params (int X, int Y, char Ch)[] tiles)
    {
        char[][] rows = Enumerable.Range(0, height).Select(_ => new string('.', width).ToCharArray()).ToArray();

        foreach ((int x, int y, char ch) in tiles)
        {
            rows[y][x] = ch;
        }

        return rows.Select(row => new string(row)).ToArray();
    }

    // A road along y = 2 runs from x = 0 to the fog edge at x = 5; the explorer stands at (2, 6), nearer to open edges.
    private static string[] RoadMap()
    {
        return Plain(12, 12, Enumerable.Range(0, 12).Select(x => (x, 2, '=')).ToArray());
    }

    [Test]
    public void FollowsRoadIntoFogTest()
    {
        WorldEngine engine = Build(RoadMap(), (x, _) => x <= 5, new WorldPosition(2, 6));

        AutoExploreResult result = Explore(engine);

        Assert.That(result, Is.TypeOf<AutoExploreResult.Target>());
        AutoExploreResult.Target target = (AutoExploreResult.Target)result;
        Assert.That(target.Position, Is.EqualTo(new WorldPosition(5, 2)));
        Assert.That(target.IsRoadIntoFog, Is.True);
        Assert.That(target.RevealedTiles, Is.EqualTo(3));
    }

    [Test]
    public void WithoutRoadBonusTakesNearestEdgeTest()
    {
        WorldEngine engine = Build(RoadMap(), (x, _) => x <= 5, new WorldPosition(2, 6));

        AutoExploreResult result = Explore(engine, settings: AutoExploreSettings.Default with { RoadBonusTiles = 0 });

        Assert.That(result, Is.TypeOf<AutoExploreResult.Target>());
        AutoExploreResult.Target target = (AutoExploreResult.Target)result;
        Assert.That(target.IsRoadIntoFog, Is.False);
        Assert.That(target.PathCost, Is.EqualTo(6));
    }

    // A ring of mountains around (8, 8); its inside is hidden, everything else is seen.
    private static string[] RingMap(bool gap)
    {
        List<(int, int, char)> tiles = [];

        for (int y = 6; y <= 10; y++)
        {
            for (int x = 6; x <= 10; x++)
            {
                if (x == 6 || x == 10 || y == 6 || y == 10)
                {
                    tiles.Add((x, y, '^'));
                }
            }
        }

        if (gap)
        {
            tiles.Add((8, 6, '.'));
        }

        return Plain(13, 13, [.. tiles]);
    }

    private static bool OutsideRing(int x, int y)
    {
        return !(x >= 7 && x <= 9 && y >= 7 && y <= 9);
    }

    [Test]
    public void MountainRingSeenOnAllSidesIsNothingLeftTest()
    {
        WorldEngine engine = Build(RingMap(gap: false), OutsideRing, new WorldPosition(1, 1));

        Assert.That(Explore(engine), Is.EqualTo(AutoExploreResult.NothingLeft.Instance));
    }

    [Test]
    public void MountainRingWithGapLeadsThroughTheGapTest()
    {
        WorldEngine engine = Build(RingMap(gap: true), OutsideRing, new WorldPosition(1, 1));

        AutoExploreResult result = Explore(engine);

        Assert.That(result, Is.TypeOf<AutoExploreResult.Target>());
        Assert.That(((AutoExploreResult.Target)result).Position, Is.EqualTo(new WorldPosition(8, 6)));
    }

    [Test]
    public void OwnArmyInTheGapIsNoTargetNowTest()
    {
        WorldEngine engine = Build(RingMap(gap: true), OutsideRing, new WorldPosition(1, 1), ownBlocker: new WorldPosition(8, 6));

        Assert.That(Explore(engine), Is.EqualTo(AutoExploreResult.NoTargetNow.Instance));
    }

    [Test]
    public void HiddenLandAcrossWaterIsReachableForLandArmyTest()
    {
        // A water channel x = 4..5 separates the explorer from a hidden strip at x >= 8.
        string[] rows = Plain(10, 6, [.. Enumerable.Range(0, 6).SelectMany(y => new[] { (4, y, '~'), (5, y, '~') })]);
        WorldEngine engine = Build(rows, (x, _) => x <= 7, new WorldPosition(1, 2));

        AutoExploreResult result = Explore(engine);

        Assert.That(result, Is.TypeOf<AutoExploreResult.Target>());
        Assert.That(((AutoExploreResult.Target)result).Position.X, Is.EqualTo(7));
    }

    [Test]
    public void EverythingSeenIsNothingLeftTest()
    {
        WorldEngine engine = Build(Plain(6, 6), (_, _) => true, new WorldPosition(1, 1));

        Assert.That(Explore(engine), Is.EqualTo(AutoExploreResult.NothingLeft.Instance));
    }

    // Two fog edges: the road edge at (5, 2) is best; an enemy near it pushes the explorer to another edge.
    [Test]
    public void KeepsSafetyDistanceFromKnownEnemyTest()
    {
        WorldEngine engine = Build(RoadMap(), (x, _) => x <= 5, new WorldPosition(2, 8), enemy: new WorldPosition(3, 1));

        AutoExploreResult result = Explore(engine);

        Assert.That(result, Is.TypeOf<AutoExploreResult.Target>());
        WorldPosition position = ((AutoExploreResult.Target)result).Position;
        Assert.That(Math.Max(Math.Abs(position.X - 3), Math.Abs(position.Y - 1)), Is.GreaterThan(3));
    }

    [Test]
    public void EnemyFarAwayDoesNotChangeTheTargetTest()
    {
        WorldEngine engine = Build(RoadMap(), (x, _) => x <= 5, new WorldPosition(2, 8), enemy: new WorldPosition(0, 11));

        AutoExploreResult result = Explore(engine);

        Assert.That(result, Is.TypeOf<AutoExploreResult.Target>());
        Assert.That(((AutoExploreResult.Target)result).Position, Is.EqualTo(new WorldPosition(5, 2)));
    }

    [Test]
    public void EnemiesAtEveryEdgeIsNoTargetNowTest()
    {
        // Only one column of fog edge (x = 5, y = 0..5); an enemy in the middle covers all of it within 3 tiles.
        WorldEngine engine = Build(Plain(8, 6), (x, _) => x <= 5, new WorldPosition(0, 0), enemy: new WorldPosition(3, 3));

        Assert.That(Explore(engine), Is.EqualTo(AutoExploreResult.NoTargetNow.Instance));
    }

    [Test]
    public void KeepsPreviousTargetWhileItIsAnEdgeTest()
    {
        WorldEngine engine = Build(RoadMap(), (x, _) => x <= 5, new WorldPosition(2, 6));
        WorldPosition previous = new(5, 10);

        AutoExploreResult result = Explore(engine, previous);

        Assert.That(result, Is.TypeOf<AutoExploreResult.Target>());
        Assert.That(((AutoExploreResult.Target)result).Position, Is.EqualTo(previous));
    }

    [Test]
    public void DropsPreviousTargetThatIsNoLongerAnEdgeTest()
    {
        WorldEngine engine = Build(RoadMap(), (x, _) => x <= 5, new WorldPosition(2, 6));

        AutoExploreResult result = Explore(engine, new WorldPosition(2, 2));

        Assert.That(result, Is.TypeOf<AutoExploreResult.Target>());
        Assert.That(((AutoExploreResult.Target)result).Position, Is.EqualTo(new WorldPosition(5, 2)));
    }

    // A lake at x = 0..5 with land beyond; the land at x >= 8 is hidden. A ship only sails deep water (all neighbours
    // water), so it can never reach that land; a land army on the same tile can (decision H: per army).
    private static string[] LakeMap()
    {
        return Plain(10, 10, [.. Enumerable.Range(0, 10).SelectMany(y => Enumerable.Range(0, 6).Select(x => (x, y, '~')))]);
    }

    [Test]
    public void ShipOnLakeHasNothingLeftTest()
    {
        WorldEngine engine = Build(LakeMap(), (x, _) => x <= 7, new WorldPosition(2, 5), ship: true);

        Assert.That(Explore(engine), Is.EqualTo(AutoExploreResult.NothingLeft.Instance));
    }

    [Test]
    public void LandArmyOnLakeStillExploresTest()
    {
        WorldEngine engine = Build(LakeMap(), (x, _) => x <= 7, new WorldPosition(2, 5));

        Assert.That(Explore(engine), Is.TypeOf<AutoExploreResult.Target>());
    }

    [Test]
    public void CountHiddenMatchesTileByTileCountTest()
    {
        // Odd width so rows start at every bit offset of the 64-bit words; radius up to the game's maximum of 8.
        WorldMap map = TestHelpers.BuildMap(Plain(37, 23));
        Random random = new(7);
        ImmutableBitmap.Builder builder = ImmutableBitmap.Create(map.Width, map.Height).ToBuilder();

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (random.Next(3) > 0)
                {
                    builder.Set(x, y);
                }
            }
        }

        ImmutableBitmap seen = builder.ToImmutable();

        for (int radius = 1; radius <= 8; radius++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int expected = 0;

                    for (int yy = Math.Max(0, y - radius); yy <= Math.Min(map.Height - 1, y + radius); yy++)
                    {
                        for (int xx = Math.Max(0, x - radius); xx <= Math.Min(map.Width - 1, x + radius); xx++)
                        {
                            expected += seen.Get(xx, yy) ? 0 : 1;
                        }
                    }

                    Assert.That(AutoExploreCalculator.CountHidden(map, seen, new WorldPosition(x, y), radius), Is.EqualTo(expected), $"({x}, {y}) r {radius}");
                }
            }
        }
    }
}

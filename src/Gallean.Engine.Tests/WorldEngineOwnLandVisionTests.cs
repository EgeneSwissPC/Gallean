using Gallean.Common;

namespace Gallean.Engine.Tests;

public sealed class WorldEngineOwnLandVisionTests
{
    private static readonly WorldSpellDefinitionId FogSpellId = new("fog");

    private static WorldMap Map(int size, params (int X, int Y, char Terrain)[] tiles)
    {
        char[][] rows = new char[size][];

        for (int y = 0; y < size; y++)
        {
            rows[y] = new string('.', size).ToCharArray();
        }

        foreach ((int x, int y, char terrain) in tiles)
        {
            rows[y][x] = terrain;
        }

        return TestHelpers.BuildMap(rows.Select(row => new string(row)));
    }

    private static HashSet<WorldPosition> Revealed(ImmutableBitmap fog)
    {
        HashSet<WorldPosition> revealed = [];

        for (int y = 0; y < fog.Height; y++)
        {
            for (int x = 0; x < fog.Width; x++)
            {
                if (fog.Get(x, y))
                {
                    revealed.Add(new WorldPosition(x, y));
                }
            }
        }

        return revealed;
    }

    private static HashSet<WorldPosition> TilesOf(WorldMap map, WorldMapTerrain terrain)
    {
        HashSet<WorldPosition> tiles = [];

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                WorldPosition position = new(x, y);

                if (map.GetCellAt(position).Terrain == terrain)
                {
                    tiles.Add(position);
                }
            }
        }

        return tiles;
    }

    private static WorldEngine BuildSinglePlayer(WorldMap map, WorldRod? rod = null)
    {
        WorldPlayer player = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Undead, map) with
        {
            FogOfWar = ImmutableBitmap.Create(map.Width, map.Height)
        };

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithGlobals(new GlobalVariables { RodTerrainTilesPerTurn = 4 })
            .WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, LordCategory.Warrior))
            .WithPlayer(player);

        if (rod is not null)
        {
            builder = builder.WithRod(rod);
        }

        return builder.Build();
    }

    [Test]
    public void OwnLandVisibleFromStartOnLargeMapTest()
    {
        WorldMap map = Map(256, (200, 200, 'U'), (201, 200, 'U'), (50, 50, 'H'));
        WorldEngine engine = BuildSinglePlayer(map);

        _ = engine.DispatchCommand(new WorldStartGameCommand());

        Assert.That(Revealed(engine.State.Players[new WorldPlayerId(1)].FogOfWar), Is.EquivalentTo(new[]
        {
            new WorldPosition(200, 200),
            new WorldPosition(201, 200)
        }));
    }

    [Test]
    public void SmallMapKeepsFogOnOwnLandTest()
    {
        WorldMap map = Map(255, (200, 200, 'U'), (201, 200, 'U'));
        WorldEngine engine = BuildSinglePlayer(map);

        _ = engine.DispatchCommand(new WorldStartGameCommand());

        Assert.That(Revealed(engine.State.Players[new WorldPlayerId(1)].FogOfWar), Is.Empty);
    }

    [Test]
    public void ExpandedLandRevealedAtTurnStartTest()
    {
        WorldMap map = Map(256, (200, 200, 'U'));
        WorldRod rod = new(new WorldRodId("Rod1"), new WorldPlayerId(1), new WorldPosition(200, 200));
        WorldEngine engine = BuildSinglePlayer(map, rod);

        _ = engine.DispatchCommand(new WorldStartGameCommand());

        Assert.That(Revealed(engine.State.Players[rod.OwnerId].FogOfWar), Is.EquivalentTo(new[] { rod.Position }));

        _ = engine.DispatchCommand(new WorldEndTurnCommand());

        HashSet<WorldPosition> land = TilesOf(engine.State.Map, WorldMapTerrain.Undead);

        Assert.That(land, Has.Count.GreaterThan(1));
        Assert.That(Revealed(engine.State.Players[rod.OwnerId].FogOfWar), Is.EquivalentTo(land));
    }

    [Test]
    public void FogSpellCannotConcealOwnLandTest()
    {
        WorldMap map = Map(256, (10, 10, 'H'), (11, 10, 'H'), (20, 20, 'U'));

        WorldSpellDefinition spell = TestHelpers.SimpleSpellDefinition(FogSpellId, new WorldFogSpellEffect()) with
        {
            AreaRadius = null
        };

        WorldPlayer player = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Undead, map) with
        {
            Spellbook = new WorldPlayerSpellbook([], [FogSpellId])
        };

        WorldPlayer enemy = TestHelpers.SimplePlayer(new WorldPlayerId(2), WorldRace.Human, map);

        WorldEngine engine = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(spell)
            .WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, LordCategory.Warrior))
            .WithPlayer(player)
            .WithDefinition(TestHelpers.SimpleLord(enemy.LordId, enemy.Race, LordCategory.Warrior))
            .WithPlayer(enemy)
            .Build();

        _ = engine.DispatchCommand(new WorldCastSpellCommand(FogSpellId, Target: null));

        Assert.That(Revealed(engine.State.Players[enemy.Id].FogOfWar), Is.EquivalentTo(new[]
        {
            new WorldPosition(10, 10),
            new WorldPosition(11, 10)
        }));
        Assert.That(engine.State.Players[player.Id].FogOfWar, Is.EqualTo(ImmutableBitmap.Create(map.Width, map.Height, initialValue: true)));
    }
}

using Gallean.Common;

namespace Gallean.Engine.Tests;

/// <summary>Gallean Upgrade: on large maps summons and illusions stay while their owner pays their casting cost every turn.</summary>
public sealed class WorldEngineSummonUpkeepTests
{
    private static readonly WorldSpellDefinitionId SummonSpellId = new("summon");
    private static readonly WorldResourceSet Upkeep = new(0, 40, 0, 0, 0, 0);

    private static UnitDefinition SpellUnit(string id, UnitCategory category, int lifetime)
    {
        return TestHelpers.SimpleUnitDefinition(id, maxHitpoints: 100, initiative: 50, damage: 10) with
        {
            Category = category,
            Leadership = 1,
            MovementPoints = 20,
            Lifetime = lifetime
        };
    }

    private static WorldStack UpkeepStack(string id, WorldPlayerId ownerId, UnitDefinition unit, WorldPosition position)
    {
        return TestHelpers.SimpleStack(
                new WorldStackId(id),
                ownerId,
                position,
                new UnitGrid<WorldUnit>.Builder()
                    .Place(TestHelpers.SimpleUnit(new WorldUnitId("U" + id), unit), GridPosition.FrontCenter, unit.IsLarge)
                    .Build(),
                movementPoints: 20) with
            {
                SummonUpkeep = Upkeep
            };
    }

    private static (WorldEngine Engine, WorldPlayer Player1) CreateEngine(int mapSize, UnitDefinition unit, WorldResourceSet resources, params WorldStack[] stacks)
    {
        WorldMap map = TestHelpers.SimpleMap(mapSize, mapSize);

        WorldPlayer player1 = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Undead, map) with
        {
            Resources = resources,
            Spellbook = new WorldPlayerSpellbook([], [SummonSpellId])
        };

        WorldPlayer player2 = TestHelpers.SimplePlayer(new WorldPlayerId(2), WorldRace.Heretic, map);

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(unit)
            .WithDefinition(TestHelpers.SimpleSpellDefinition(SummonSpellId, new WorldSummonStackSpellEffect(unit.Id)) with { CastingCost = Upkeep })
            .WithDefinition(TestHelpers.SimpleLord(player1.LordId, player1.Race, LordCategory.Warrior))
            .WithPlayer(player1)
            .WithDefinition(TestHelpers.SimpleLord(player2.LordId, player2.Race, LordCategory.Warrior))
            .WithPlayer(player2)
            .WithSubrace(TestHelpers.NeutralSubrace);

        foreach (WorldStack stack in stacks)
        {
            builder = builder.WithStack(stack);
        }

        return (builder.Build(), player1);
    }

    // Ends player 1's turn and player 2's turn, so player 1's next turn starts (income: 100 gold, 25 death mana).
    private static List<WorldEvent> PlayRound(WorldEngine engine)
    {
        List<WorldEvent> events = [];

        for (int i = 0; i < 2; i++)
        {
            ICommandResult<WorldCommandError, WorldEvent, Empty> result = engine.DispatchCommand(new WorldEndTurnCommand(), dryRun: false);
            Assert.That(result.Error, Is.Null);
            events.AddRange(result.Events);
        }

        return events;
    }

    [TestCase(UnitCategory.Summon, 1)]
    [TestCase(UnitCategory.Illusion, 3)]
    public void CastOnLargeMapSetsUpkeepInsteadOfLifetimeTest(UnitCategory category, int lifetime)
    {
        UnitDefinition unit = SpellUnit("spellunit", category, lifetime);
        (WorldEngine engine, _) = CreateEngine(LargeMapRules.MinMapSize, unit, new WorldResourceSet(0, 100, 0, 0, 0, 0));

        engine.DispatchCommand(new WorldCastSpellCommand(SummonSpellId, new WorldSpellTarget(new WorldPosition(1, 1), Radius: 0)), dryRun: false);

        WorldStack stack = engine.State.Stacks.Values.Single();
        Assert.That(stack.ExpirationTurn, Is.Null);
        Assert.That(stack.SummonUpkeep, Is.EqualTo(Upkeep));
    }

    [Test]
    public void CastOnSmallMapKeepsLifetimeTest()
    {
        UnitDefinition unit = SpellUnit("spellunit", UnitCategory.Summon, 1);
        (WorldEngine engine, _) = CreateEngine(LargeMapRules.MinMapSize - 1, unit, new WorldResourceSet(0, 100, 0, 0, 0, 0));

        engine.DispatchCommand(new WorldCastSpellCommand(SummonSpellId, new WorldSpellTarget(new WorldPosition(1, 1), Radius: 0)), dryRun: false);

        WorldStack stack = engine.State.Stacks.Values.Single();
        Assert.That(stack.ExpirationTurn, Is.EqualTo(engine.State.TurnInfo.TurnNumber + 1));
        Assert.That(stack.SummonUpkeep, Is.Null);
    }

    [TestCase(UnitCategory.Summon, 1)]
    [TestCase(UnitCategory.Illusion, 3)]
    public void SummonStaysWhilePaidTest(UnitCategory category, int lifetime)
    {
        UnitDefinition unit = SpellUnit("spellunit", category, lifetime);
        WorldStack summon = UpkeepStack("Stack0001", new WorldPlayerId(1), unit, new WorldPosition(1, 1));
        (WorldEngine engine, WorldPlayer player1) = CreateEngine(LargeMapRules.MinMapSize, unit, new WorldResourceSet(0, 100, 0, 0, 0, 0), summon);

        for (int round = 1; round <= 3; round++)
        {
            List<WorldEvent> events = PlayRound(engine);

            Assert.That(events, Does.Contain(new WorldSummonUpkeepPaidEvent(player1.Id, Upkeep)));
            Assert.That(engine.State.Stacks.ContainsKey(summon.Id), Is.True);
            // 100 + round x (25 income - 40 upkeep)
            Assert.That(engine.State.Players[player1.Id].Resources.DeathMana, Is.EqualTo(100 - 15 * round));
        }
    }

    [Test]
    public void UnpaidSummonsAreDismissedTest()
    {
        UnitDefinition unit = SpellUnit("spellunit", UnitCategory.Summon, 1);
        WorldStack[] summons =
        [
            UpkeepStack("Stack0001", new WorldPlayerId(1), unit, new WorldPosition(1, 1)),
            UpkeepStack("Stack0002", new WorldPlayerId(1), unit, new WorldPosition(2, 2)),
            UpkeepStack("Stack0003", new WorldPlayerId(1), unit, new WorldPosition(3, 3))
        ];
        // 70 + 25 income = 95: pays two summons (80), the third one is dismissed.
        (WorldEngine engine, WorldPlayer player1) = CreateEngine(LargeMapRules.MinMapSize, unit, new WorldResourceSet(0, 70, 0, 0, 0, 0), summons);

        List<WorldEvent> events = PlayRound(engine);

        Assert.That(events, Does.Contain(new WorldSummonUpkeepPaidEvent(player1.Id, new WorldResourceSet(0, 80, 0, 0, 0, 0))));
        Assert.That(events.OfType<WorldStackDestroyedEvent>().Count(), Is.EqualTo(1));
        Assert.That(engine.State.Stacks.Count, Is.EqualTo(2));
        Assert.That(engine.State.Players[player1.Id].Resources.DeathMana, Is.EqualTo(15));
    }

    [Test]
    public void DismissedSummonIsRandomTest()
    {
        UnitDefinition unit = SpellUnit("spellunit", UnitCategory.Summon, 1);
        HashSet<WorldStackId> dismissed = [];

        for (int seed = 0; seed < 20; seed++)
        {
            WorldStack[] summons =
            [
                UpkeepStack("Stack0001", new WorldPlayerId(1), unit, new WorldPosition(1, 1)),
                UpkeepStack("Stack0002", new WorldPlayerId(1), unit, new WorldPosition(2, 2))
            ];
            (WorldEngine engine, _) = CreateEngineWithSeed(seed, unit, summons);

            dismissed.UnionWith(PlayRound(engine).OfType<WorldStackDestroyedEvent>().Select(ev => ev.StackId));
        }

        Assert.That(dismissed, Has.Count.EqualTo(2));
    }

    private static (WorldEngine Engine, WorldPlayer Player1) CreateEngineWithSeed(int seed, UnitDefinition unit, WorldStack[] stacks)
    {
        // 30 + 25 income = 55: pays exactly one of the two summons.
        WorldMap map = TestHelpers.SimpleMap(LargeMapRules.MinMapSize, LargeMapRules.MinMapSize);
        WorldPlayer player1 = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Undead, map) with { Resources = new WorldResourceSet(0, 30, 0, 0, 0, 0) };
        WorldPlayer player2 = TestHelpers.SimplePlayer(new WorldPlayerId(2), WorldRace.Heretic, map);

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithRandom(new TestSeedRandomProvider(seed))
            .WithDefinition(unit)
            .WithDefinition(TestHelpers.SimpleLord(player1.LordId, player1.Race, LordCategory.Warrior))
            .WithPlayer(player1)
            .WithDefinition(TestHelpers.SimpleLord(player2.LordId, player2.Race, LordCategory.Warrior))
            .WithPlayer(player2)
            .WithSubrace(TestHelpers.NeutralSubrace);

        foreach (WorldStack stack in stacks)
        {
            builder = builder.WithStack(stack);
        }

        return (builder.Build(), player1);
    }

    [Test]
    public void ArmiesWithoutUpkeepAreNotChargedTest()
    {
        UnitDefinition unit = SpellUnit("spellunit", UnitCategory.Summon, 1);
        WorldStack placed = UpkeepStack("Stack0001", new WorldPlayerId(1), unit, new WorldPosition(1, 1)) with { SummonUpkeep = null };
        (WorldEngine engine, WorldPlayer player1) = CreateEngine(LargeMapRules.MinMapSize, unit, new WorldResourceSet(0, 0, 0, 0, 0, 0), placed);

        List<WorldEvent> events = PlayRound(engine);

        Assert.That(events.OfType<WorldSummonUpkeepPaidEvent>(), Is.Empty);
        Assert.That(engine.State.Stacks.ContainsKey(placed.Id), Is.True);
        Assert.That(engine.State.Players[player1.Id].Resources.DeathMana, Is.EqualTo(25));
    }

    [Test]
    public async Task UpkeepSurvivesSaveTest()
    {
        UnitDefinition unit = SpellUnit("spellunit", UnitCategory.Summon, 1);
        WorldStack summon = UpkeepStack("Stack0001", new WorldPlayerId(1), unit, new WorldPosition(1, 1));
        (WorldEngine engine, _) = CreateEngine(8, unit, WorldResourceSet.Empty, summon);
        using MemoryStream stream = new();

        await WorldStateJsonSerializer.SerializeAsync(stream, engine.State);
        stream.Position = 0;
        WorldState actual = await WorldStateJsonSerializer.DeserializeAsync(stream);

        Assert.That(actual.Stacks[summon.Id].SummonUpkeep, Is.EqualTo(Upkeep));
        Assert.That(actual, Is.EqualTo(engine.State));
    }
}

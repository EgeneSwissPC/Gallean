using System.Collections.Immutable;

namespace Gallean.Engine.Tests;

// Gallean Upgrade: designed map AIs (docs/ai-designer.md, phase 1).
// Reads or changes the designed AIs in AiBrainRegistry (static): never alongside other such fixtures.
[NonParallelizable]
public sealed class DesignedWorldAiTests
{
    private static readonly WorldPlayerId AiId = new(1);
    private static readonly WorldPlayerId HumanId = new(2);

    // The world of WorldEngineAiActionTests.PerformsActionTest: the AI's first action moves an army east.
    private static TestWorldEngineBuilder Builder(int gold = 0)
    {
        WorldMap map = TestHelpers.BuildMap([
            ".^^^^",
            "....."
        ]);

        UnitDefinition mover = TestHelpers.SimpleLeaderDefinition(
            TestHelpers.SimpleUnitDefinition("mover", maxHitpoints: 10, initiative: 40, damage: 5),
            movementPoints: 4, leadership: 6);
        UnitDefinition enemy = TestHelpers.SimpleLeaderDefinition(
            TestHelpers.SimpleUnitDefinition("enemy", maxHitpoints: 5, initiative: 1, damage: 1),
            movementPoints: 6, leadership: 6);

        WorldPlayer player1 = TestHelpers.SimplePlayer(AiId, WorldRace.Undead, map) with { IsAi = true };
        player1 = player1 with { Resources = player1.Resources with { Gold = gold } };
        WorldPlayer player2 = TestHelpers.SimplePlayer(HumanId, WorldRace.Heretic, map);

        WorldStack moverStack = TestHelpers.SimpleStack(
            new WorldStackId("Mover"),
            player1.Id,
            new WorldPosition(0, 1),
            new UnitGrid<WorldUnit>.Builder().Place(TestHelpers.SimpleUnit(new WorldUnitId("mover"), mover), GridPosition.FrontBottom, false).Build(),
            movementPoints: 4);
        WorldStack enemyStack = TestHelpers.SimpleStack(
            new WorldStackId("Enemy"),
            player2.Id,
            new WorldPosition(4, 1),
            new UnitGrid<WorldUnit>.Builder().Place(TestHelpers.SimpleUnit(new WorldUnitId("enemy"), enemy), GridPosition.FrontBottom, false).Build(),
            movementPoints: 0);

        return new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(mover)
            .WithDefinition(enemy)
            .WithDefinition(TestHelpers.SimpleLord(player1.LordId, player1.Race, LordCategory.Warrior))
            .WithPlayer(player1)
            .WithDefinition(TestHelpers.SimpleLord(player2.LordId, player2.Race, LordCategory.Warrior))
            .WithPlayer(player2)
            .WithStack(moverStack)
            .WithStack(enemyStack);
    }

    private static DesignedWorldStage Stage(string name, ImmutableArray<ImmutableArray<DesignedStageCondition>> when)
    {
        return DesignedWorldStage.Classic with { Name = name, When = when };
    }

    private static DesignedWorldAi FullDesign()
    {
        return new DesignedWorldAi("custom:full", "Full test", [
            DesignedWorldStage.Classic with
            {
                Name = "Defend",
                When = [[new DesignedStageCondition.EnemyNearCapital(5)], [new DesignedStageCondition.LostCity(), new DesignedStageCondition.CitiesBelow(3)]],
                Army = [[WorldAiActionKind.Attack], [WorldAiActionKind.Heal, WorldAiActionKind.AutoFit]],
                Rules = new DesignedWorldRules(HurtPercent: 50, HealedPercent: 70, ReinforcePercent: 40, AttackPowerPercent: 90, LeaderSavingsGold: 0, WaitForFreeCapital: true,
                    KeepArmiesHome: 2, GatherArmies: 3, RushEnemyCapital: true, GoldReserve: 250, SpendAbovePercent: 85)
            },
            Stage("Rich", [[new DesignedStageCondition.FromTurn(10), new DesignedStageCondition.GoldAbove(1000)]]),
            DesignedWorldStage.Classic
        ]);
    }

    [Test]
    public void FileRoundTripTest()
    {
        DesignedWorldAi design = FullDesign() with { Id = "custom:full" };

        DesignedWorldAi read = DesignedWorldAiStore.Read("full", DesignedWorldAiStore.Write(design));

        Assert.That(read, Is.EqualTo(design));
    }

    [Test]
    public void UnknownKindsAreSkippedTest()
    {
        byte[] json = System.Text.Encoding.UTF8.GetBytes("""
            {"formatVersion": 9, "name": "Future", "stages": [{"name": "A",
              "when": [[{"kind": "moonIsFull"}, {"kind": "fromTurn", "turn": 3}], [{"kind": "moonIsFull"}]],
              "economy": [["CastSpell", "Teleport"], ["Teleport"]], "army": [["Attack"]], "rules": {"hurtPercent": 60, "future": 1}}]}
            """);

        DesignedWorldAi read = DesignedWorldAiStore.Read("future", json);

        Assert.That(read.Id, Is.EqualTo("custom:future"));
        DesignedWorldStage stage = read.Stages.Single();
        Assert.That(stage.When.Single(), Is.EqualTo(new DesignedStageCondition[] { new DesignedStageCondition.FromTurn(3) }));
        Assert.That(stage.Economy.Single(), Is.EqualTo(new[] { WorldAiActionKind.CastSpell }));
        Assert.That(stage.Rules, Is.EqualTo(new DesignedWorldRules(HurtPercent: 60)));
    }

    [Test]
    public void StoreSaveListDeleteTest()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gallean-ais-" + Guid.NewGuid().ToString("N"));

        try
        {
            DesignedWorldAiStore store = new(directory);
            store.Save(DesignedWorldAi.FromClassic("b-ai", "Second"));
            store.Save(DesignedWorldAi.FromClassic("a-ai", "First"));
            File.WriteAllText(Path.Combine(directory, "broken.json"), "{ not json");

            Assert.That(store.LoadAll().Select(design => design.Id), Is.EqualTo(new[] { "custom:a-ai", "custom:b-ai" }));

            store.Delete("custom:a-ai");
            Assert.That(store.LoadAll().Select(design => design.Name), Is.EqualTo(new[] { "Second" }));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void ClassicStageGivesClassicPrioritiesTest()
    {
        WorldAiProfile profile = DesignedStageEvaluator.ToProfile(DesignedWorldStage.Classic);

        Assert.That(profile.Priorities, Is.EquivalentTo(new Dictionary<WorldAiActionKind, int>
        {
            [WorldAiActionKind.CastSpell] = 4,
            [WorldAiActionKind.ResearchSpell] = 3,
            [WorldAiActionKind.HireLeader] = 2,
            [WorldAiActionKind.ConstructBuilding] = 1,
            [WorldAiActionKind.PurchaseUnit] = 0,
            [WorldAiActionKind.UseItem] = 6,
            [WorldAiActionKind.Resurrect] = 5,
            [WorldAiActionKind.Heal] = 4,
            [WorldAiActionKind.AutoFit] = 3,
            [WorldAiActionKind.Attack] = 2,
            [WorldAiActionKind.Thief] = 2,
            [WorldAiActionKind.Rod] = 2,
            [WorldAiActionKind.EnterCity] = 2,
            [WorldAiActionKind.TakeBag] = 2,
            [WorldAiActionKind.Move] = 1
        }));
        // Plus overflow spending, which every designed AI has (docs/ai-overflow-spending.md).
        Assert.That(profile with { Priorities = null }, Is.EqualTo(WorldAiProfile.Classic with { SpendAbovePercent = WorldAiSpending.DefaultSpendAbovePercent, SummonUpkeepPercent = WorldAiSpending.DefaultSummonUpkeepPercent, UseAllSpells = true }));
    }

    [Test]
    public void RulesBecomeProfileTest()
    {
        WorldAiProfile profile = DesignedStageEvaluator.ToProfile(FullDesign().Stages[0]);

        Assert.That((profile.HurtThreshold, profile.HealedThreshold, profile.ReinforceThreshold), Is.EqualTo((500, 700, 400)));
        Assert.That((profile.AttackPowerPercent, profile.LeaderOvercommitGold, profile.WaitForFreeCapital), Is.EqualTo((90, 0, true)));
        Assert.That((profile.KeepArmiesHome, profile.GatherArmies, profile.RushEnemyCapital, profile.GoldReserve), Is.EqualTo((2, 3, true, 250)));
        Assert.That(profile.SpendAbovePercent, Is.EqualTo(85), "the Spend when rich block");
        Assert.That(DesignedStageEvaluator.ToProfile(FullDesign().Stages[1]).SpendAbovePercent, Is.EqualTo(WorldAiSpending.DefaultSpendAbovePercent), "no block: 70 %");
        Assert.That(profile.Priorities!.Keys, Is.EquivalentTo(new[]
        {
            WorldAiActionKind.CastSpell, WorldAiActionKind.ResearchSpell, WorldAiActionKind.HireLeader, WorldAiActionKind.ConstructBuilding,
            WorldAiActionKind.PurchaseUnit, WorldAiActionKind.Attack, WorldAiActionKind.Heal, WorldAiActionKind.AutoFit
        }));
        Assert.That((profile.Priorities[WorldAiActionKind.Attack], profile.Priorities[WorldAiActionKind.Heal]), Is.EqualTo((2, 1)));
    }

    [Test]
    public void ClassicCopyPlaysLikeClassicTest()
    {
        DesignedWorldAiBrain designed = new(DesignedWorldAi.FromClassic("copy", "Copy"));
        GameSession classic = Builder().BuildSession().Session;
        GameSession copy = Builder().BuildSession().Session;
        IEngineContext context = copy.EngineContext;

        for (int i = 0; i < 5 && classic.World.State.TurnInfo.CurrentPlayerId == AiId; i++)
        {
            WorldAiMemory classicMemory = classic.World.State.AiMemories.GetValueOrDefault(AiId, WorldAiMemory.Empty);
            WorldAiMemory copyMemory = copy.World.State.AiMemories.GetValueOrDefault(AiId, WorldAiMemory.Empty);

            WorldAiActionPlan classicPlan = ClassicWorldAiBrain.Instance.DecideAction(classic.EngineContext, classic.World.State, classicMemory);
            WorldAiActionPlan copyPlan = designed.DecideAction(context, copy.World.State, copyMemory);

            Assert.That(copyPlan.Command, Is.EqualTo(classicPlan.Command), $"action {i}");
            Assert.That(copyPlan.Score, Is.EqualTo(classicPlan.Score), $"action {i}");

            classic.World.DispatchCommand(classicPlan.Command);
            copy.World.DispatchCommand(copyPlan.Command);
        }
    }

    [Test]
    public void ActionLeftOutIsNeverDoneTest()
    {
        DesignedWorldAi noMove = new("custom:no-move", "No move", [DesignedWorldStage.Classic with
        {
            Army = [[WorldAiActionKind.UseItem], [WorldAiActionKind.Resurrect], [WorldAiActionKind.Heal], [WorldAiActionKind.AutoFit], [WorldAiActionKind.Attack]]
        }]);
        GameSession session = Builder().BuildSession().Session;

        WorldAiActionPlan classicPlan = ClassicWorldAiBrain.Instance.DecideAction(session.EngineContext, session.World.State, WorldAiMemory.Empty);
        WorldAiActionPlan plan = new DesignedWorldAiBrain(noMove).DecideAction(session.EngineContext, session.World.State, WorldAiMemory.Empty);

        Assert.That(classicPlan.Command, Is.InstanceOf<WorldAiMoveStackCommand>());
        Assert.That(plan.Command, Is.Not.InstanceOf<WorldAiMoveStackCommand>());
    }

    [Test]
    public void ConditionsTest()
    {
        WorldState state = Builder(gold: 600).Build().State;
        WorldAiMemory memory = WorldAiMemory.Empty;
        int turn = state.TurnInfo.TurnNumber;

        bool IsTrue(DesignedStageCondition condition, WorldAiMemory? with = null) => DesignedStageEvaluator.IsTrue(condition, state, AiId, with ?? memory);

        Assert.That(IsTrue(new DesignedStageCondition.FromTurn(turn)), Is.True);
        Assert.That(IsTrue(new DesignedStageCondition.FromTurn(turn + 1)), Is.False);
        Assert.That(IsTrue(new DesignedStageCondition.GoldAbove(599)), Is.True);
        Assert.That(IsTrue(new DesignedStageCondition.GoldAbove(600)), Is.False);
        // The test player owns no city.
        Assert.That(IsTrue(new DesignedStageCondition.CitiesBelow(1)), Is.True);
        Assert.That(IsTrue(new DesignedStageCondition.CitiesBelow(0)), Is.False);
        // No capital: nothing can be near it.
        Assert.That(IsTrue(new DesignedStageCondition.EnemyNearCapital(99)), Is.False);
        Assert.That(IsTrue(new DesignedStageCondition.LostCity()), Is.False);
        Assert.That(IsTrue(new DesignedStageCondition.LostCity(), memory with { LostCityTurn = turn }), Is.True);
    }

    [Test]
    public void FirstTrueStageWinsTest()
    {
        WorldState state = Builder(gold: 600).Build().State;
        int turn = state.TurnInfo.TurnNumber;
        DesignedWorldAi design = new("custom:stages", "Stages", [
            Stage("Late", [[new DesignedStageCondition.FromTurn(turn + 5)]]),
            Stage("Rich or late", [[new DesignedStageCondition.FromTurn(turn + 5)], [new DesignedStageCondition.GoldAbove(500), new DesignedStageCondition.CitiesBelow(1)]]),
            Stage("Rich", [[new DesignedStageCondition.GoldAbove(100)]]),
            DesignedWorldStage.Classic
        ]);

        Assert.That(DesignedStageEvaluator.FindActiveStage(design, state, AiId, WorldAiMemory.Empty)?.Name, Is.EqualTo("Rich or late"));

        DesignedWorldAi noDefault = design with { Stages = [design.Stages[0]] };
        Assert.That(DesignedStageEvaluator.FindActiveStage(noDefault, state, AiId, WorldAiMemory.Empty), Is.Null);
    }

    [Test]
    public void LostCityIsNoticedAtTurnStartTest()
    {
        GameSession session = Builder().BuildSession().Session;
        WorldState state = session.World.State;
        DesignedWorldAiBrain brain = new(DesignedWorldAi.FromClassic("copy", "Copy"));

        // The memory remembers a city from the previous turn that the player doesn't own any more.
        WorldAiMemory previousTurn = WorldAiMemory.Empty with { OwnedCities = [new WorldFortificationId("FT0001")] };
        WorldAiActionPlan plan = brain.DecideAction(session.EngineContext, state, previousTurn);

        Assert.That(plan.Memory.LostCityTurn, Is.EqualTo(state.TurnInfo.TurnNumber));
        Assert.That(plan.Memory.OwnedCities, Is.Empty);

        // Later actions in the same turn keep the mark; nothing new is lost.
        WorldAiActionPlan next = brain.DecideAction(session.EngineContext, state, plan.Memory);
        Assert.That(next.Memory.LostCityTurn, Is.EqualTo(state.TurnInfo.TurnNumber));
    }

    [Test]
    public async Task MemorySurvivesSaveTest()
    {
        WorldEngine engine = Builder().Build();
        WorldAiMemory memory = new WorldAiMemory(new WorldAiTurnMemory(3, AiId)) with { OwnedCities = [new WorldFortificationId("FT0002")], LostCityTurn = 3 };
        WorldState state = engine.State with { AiMemories = engine.State.AiMemories.SetItem(AiId, memory) };
        using MemoryStream stream = new();

        await WorldStateJsonSerializer.SerializeAsync(stream, state);
        stream.Position = 0;
        WorldState actual = await WorldStateJsonSerializer.DeserializeAsync(stream);

        Assert.That(actual.AiMemories[AiId], Is.EqualTo(memory));
    }

    [Test]
    public void RegistryTest()
    {
        DesignedWorldAi design = DesignedWorldAi.FromClassic("registry-test", "Registry test");

        try
        {
            Assert.That(AiBrainRegistry.ResolveWorld("custom:registry-test").Info.Id, Is.EqualTo(AiPersonalities.ClassicPlusId));

            AiBrainRegistry.SetDesignedWorldAis([design]);

            Assert.That(AiBrainRegistry.WorldBrains.Last(), Is.EqualTo(new AiBrainInfo("custom:registry-test", "Registry test", "Designed in the AI editor.")));
            Assert.That(AiBrainRegistry.ResolveWorld("custom:registry-test"), Is.InstanceOf<DesignedWorldAiBrain>());
        }
        finally
        {
            AiBrainRegistry.SetDesignedWorldAis([]);
        }
    }
}

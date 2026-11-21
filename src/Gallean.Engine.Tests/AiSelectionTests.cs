namespace Gallean.Engine.Tests;

// Gallean Upgrade: the AI chosen in the settings reaches the world and battle AI (docs/ai-switching.md).
public sealed class AiSelectionTests
{
    private sealed class CountingWorldBrain(string id) : IWorldAiBrain
    {
        public int Calls { get; private set; }

        public AiBrainInfo Info { get; } = new(id, id, "test");

        public WorldAiActionPlan DecideAction(IEngineContext context, WorldState state, WorldAiMemory memory, IWorldAiTrace? trace = null)
        {
            Calls++;
            return ClassicWorldAiBrain.Instance.DecideAction(context, state, memory, trace);
        }
    }

    private sealed class RecordingBattleBrain(string id) : IBattleAiBrain
    {
        public List<BattleSide> Sides { get; } = [];

        public AiBrainInfo Info { get; } = new(id, id, "test");

        public BattleAiActionPlan DecideAction(IEngineContext context, BattleState state, bool canUseItems, bool canRetreat)
        {
            Sides.Add(state.CurrentUnit.Side);
            return ClassicBattleAiBrain.Instance.DecideAction(context, state, canUseItems, canRetreat);
        }
    }

    private static string NewId() => "test-" + Guid.NewGuid().ToString("N");

    // Same world as WorldEngineAiActionTests.PerformsActionTest: the first AI action moves an army,
    // so it is still the AI's turn for a second action.
    private static GameSession BuildSession()
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

        WorldPlayer player1 = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Undead, map) with { IsAi = true };
        WorldPlayer player2 = TestHelpers.SimplePlayer(new WorldPlayerId(2), WorldRace.Heretic, map);

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
            .WithStack(enemyStack)
            .BuildSession().Session;
    }

    private static BattleEngine BuildBattle()
    {
        UnitDefinition attacker = TestHelpers.SimpleUnitDefinition("attacker", maxHitpoints: 60, initiative: 50, damage: 10);
        UnitDefinition defender = TestHelpers.SimpleUnitDefinition("defender", maxHitpoints: 60, initiative: 40, damage: 10);

        return new TestBattleEngineBuilder()
            .WithDefinition(attacker)
            .WithDefinition(defender)
            .WithAttacker(TestHelpers.SimpleBattleUnit(new BattleUnitId(0), null, attacker.Id, BattleSide.Attacker, hitpoints: 60), new GridPosition(0, 0))
            .WithDefender(TestHelpers.SimpleBattleUnit(new BattleUnitId(1), null, defender.Id, BattleSide.Defender, hitpoints: 60), new GridPosition(0, 0))
            .Build();
    }

    [Test]
    public void SessionUsesSelectedWorldAiAndSwitchesMidGameTest()
    {
        CountingWorldBrain brain = new(NewId());
        AiBrainRegistry.RegisterForTests(brain);
        GameSession session = BuildSession();

        session.Ai = new AiSelection(brain.Info.Id, null);
        session.ExecuteAiAction();
        Assert.That(brain.Calls, Is.EqualTo(1));

        // Switching back to Classic in the middle of the turn applies to the next action.
        session.Ai = AiSelection.Classic;
        session.ExecuteAiAction();
        Assert.That(brain.Calls, Is.EqualTo(1));
    }

    [Test]
    public void AiOnlyGameNeedsOptInTest()
    {
        GameSession session = BuildSession();
        WorldState state = session.World.State;
        GameSession aiOnly = new(session.EngineContext, state with
        {
            Players = state.Players.SetItems(state.Players.Select(pair => KeyValuePair.Create(pair.Key, pair.Value with { IsAi = true })))
        });

        Assert.That(() => aiOnly.ExecuteAiAction(), Throws.InstanceOf<WorldCommandException>());

        aiOnly.AllowAiOnly = true;
        aiOnly.ExecuteAiAction();

        Assert.That(aiOnly.World.State.Stacks, Is.Not.EqualTo(state.Stacks));
    }

    [Test]
    public void UnknownWorldAiPlaysLikeClassicTest()
    {
        GameSession classic = BuildSession();
        classic.ExecuteAiAction();

        GameSession unknown = BuildSession();
        unknown.Ai = new AiSelection("removed-ai", "removed-ai");
        unknown.ExecuteAiAction();

        Assert.That(unknown.World.State.Stacks, Is.EqualTo(classic.World.State.Stacks));
    }

    [Test]
    public void BattleAiActionUsesSelectedBrainTest()
    {
        RecordingBattleBrain brain = new(NewId());
        AiBrainRegistry.RegisterForTests(brain);
        BattleEngine engine = BuildBattle();

        Assert.That(engine.DispatchCommand(new BattleAiActionCommand(CanUseItems: false) { BrainId = brain.Info.Id }).Error, Is.Null);

        Assert.That(brain.Sides, Is.EqualTo(new[] { BattleSide.Attacker }));
    }

    [Test]
    public void AutoResolveUsesEachSideBrainTest()
    {
        RecordingBattleBrain attackerBrain = new(NewId());
        RecordingBattleBrain defenderBrain = new(NewId());
        AiBrainRegistry.RegisterForTests(attackerBrain);
        AiBrainRegistry.RegisterForTests(defenderBrain);
        BattleEngine engine = BuildBattle();

        Assert.That(engine.DispatchCommand(new BattleAutoResolveCommand { AttackerAiId = attackerBrain.Info.Id, DefenderAiId = defenderBrain.Info.Id }).Error, Is.Null);

        Assert.That(engine.State.Outcome, Is.Not.Null);
        Assert.That(attackerBrain.Sides, Is.Not.Empty.And.All.EqualTo(BattleSide.Attacker));
        Assert.That(defenderBrain.Sides, Is.Not.Empty.And.All.EqualTo(BattleSide.Defender));
    }

    [Test]
    public void AutoResolveWithoutSelectionUsesClassicTest()
    {
        RecordingBattleBrain brain = new(NewId());
        AiBrainRegistry.RegisterForTests(brain);
        BattleEngine engine = BuildBattle();

        Assert.That(engine.DispatchCommand(new BattleAutoResolveCommand { AttackerAiId = brain.Info.Id }).Error, Is.Null);

        // Only the attacker has a selected AI; the defender keeps Classic.
        Assert.That(brain.Sides, Is.Not.Empty.And.All.EqualTo(BattleSide.Attacker));
    }
}

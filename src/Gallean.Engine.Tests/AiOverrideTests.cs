namespace Gallean.Engine.Tests;

// Gallean Upgrade: computer players with their own AI (docs/ai-switching.md).
public sealed class AiOverrideTests
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

    private static readonly WorldPlayerId AiId = new(1);
    private static readonly WorldPlayerId HumanId = new(2);

    private static string NewId() => "test-" + Guid.NewGuid().ToString("N");

    private static TestWorldEngineBuilder Builder()
    {
        WorldMap map = TestHelpers.BuildMap([
            ".^^^^",
            "....."
        ]);

        UnitDefinition mover = TestHelpers.SimpleLeaderDefinition(
            TestHelpers.SimpleUnitDefinition("mover", maxHitpoints: 10, initiative: 40, damage: 5),
            movementPoints: 4, leadership: 6);

        WorldPlayer player1 = TestHelpers.SimplePlayer(AiId, WorldRace.Undead, map) with { IsAi = true };
        WorldPlayer player2 = TestHelpers.SimplePlayer(HumanId, WorldRace.Heretic, map);

        WorldStack moverStack = TestHelpers.SimpleStack(
            new WorldStackId("Mover"),
            player1.Id,
            new WorldPosition(0, 1),
            new UnitGrid<WorldUnit>.Builder().Place(TestHelpers.SimpleUnit(new WorldUnitId("mover"), mover), GridPosition.FrontBottom, false).Build(),
            movementPoints: 4);

        return new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(mover)
            .WithDefinition(TestHelpers.SimpleLord(player1.LordId, player1.Race, LordCategory.Warrior))
            .WithPlayer(player1)
            .WithDefinition(TestHelpers.SimpleLord(player2.LordId, player2.Race, LordCategory.Warrior))
            .WithPlayer(player2)
            .WithStack(moverStack);
    }

    [Test]
    public void SetAndClearTest()
    {
        WorldEngine engine = Builder().Build();
        WorldAiOverride own = new("builder", null);

        engine.CheckCommand(new WorldSetAiOverrideCommand(AiId, own), [new WorldAiOverrideChangedEvent(AiId, own)]);
        Assert.That(engine.State.Players[AiId].AiOverride, Is.EqualTo(own));

        // Setting the same value again changes nothing.
        engine.CheckCommand(new WorldSetAiOverrideCommand(AiId, own), []);

        // An override with both parts empty is the same as none.
        engine.CheckCommand(new WorldSetAiOverrideCommand(AiId, new WorldAiOverride(null, null)), [new WorldAiOverrideChangedEvent(AiId, null)]);
        Assert.That(engine.State.Players[AiId].AiOverride, Is.Null);
    }

    [Test]
    public void HumanPlayerCannotHaveOwnAiTest()
    {
        WorldEngine engine = Builder().Build();

        engine.CheckCommandError(new WorldSetAiOverrideCommand(HumanId, new WorldAiOverride("builder", null)), WorldCommandError.AiPlayerRequired);
        engine.CheckCommandError(new WorldSetAiOverrideCommand(new WorldPlayerId(99), new WorldAiOverride("builder", null)), WorldCommandError.AiPlayerRequired);
    }

    [Test]
    public void OwnWorldAiWinsOverSettingsTest()
    {
        CountingWorldBrain own = new(NewId());
        CountingWorldBrain global = new(NewId());
        AiBrainRegistry.RegisterForTests(own);
        AiBrainRegistry.RegisterForTests(global);

        GameSession session = Builder().BuildSession().Session;
        session.Ai = new AiSelection(global.Info.Id, null);
        Assert.That(session.World.DispatchCommand(new WorldSetAiOverrideCommand(AiId, new WorldAiOverride(own.Info.Id, null))).Failed, Is.False);

        session.ExecuteAiAction();

        Assert.That(own.Calls, Is.EqualTo(1));
        Assert.That(global.Calls, Is.Zero);
        Assert.That(session.GetWorldAiId(AiId), Is.EqualTo(own.Info.Id));
        Assert.That(session.GetWorldAiId(HumanId), Is.EqualTo(global.Info.Id));
    }

    [Test]
    public void OwnBattleAiFollowsBattleSideTest()
    {
        UnitDefinition enemy = TestHelpers.SimpleLeaderDefinition(
            TestHelpers.SimpleUnitDefinition("enemy", maxHitpoints: 5, initiative: 1, damage: 1),
            movementPoints: 6, leadership: 6);
        WorldStack enemyStack = TestHelpers.SimpleStack(
            new WorldStackId("Enemy"),
            HumanId,
            new WorldPosition(1, 1),
            new UnitGrid<WorldUnit>.Builder().Place(TestHelpers.SimpleUnit(new WorldUnitId("enemy"), enemy), GridPosition.FrontBottom, false).Build(),
            movementPoints: 0);

        GameSession session = Builder().WithDefinition(enemy).WithStack(enemyStack).BuildSession().Session;
        session.Ai = new AiSelection(null, "global-battle-ai");
        Assert.That(session.World.DispatchCommand(new WorldSetAiOverrideCommand(AiId, new WorldAiOverride(null, "own-battle-ai"))).Failed, Is.False);

        // The AI attacks the human army next to it; the battle waits for the human side.
        session.ExecuteAiAction();

        Assert.That(session.Battle, Is.Not.Null);
        BattleSession battle = session.Battle!;
        Assert.That(battle.AttackerOwnerId, Is.EqualTo(AiId));
        Assert.That(battle.DefenderOwnerId, Is.EqualTo(HumanId));
        Assert.That(session.GetBattleAiId(BattleSide.Attacker), Is.EqualTo("own-battle-ai"));
        Assert.That(session.GetBattleAiOverride(BattleSide.Defender), Is.Null);
        Assert.That(session.GetBattleAiId(BattleSide.Defender), Is.EqualTo("global-battle-ai"));
    }

    [Test]
    public void BattleOverrideWithoutBattleIsNullTest()
    {
        GameSession session = Builder().BuildSession().Session;
        session.Ai = new AiSelection(null, "ruthless");

        Assert.That(session.GetBattleAiOverride(BattleSide.Attacker), Is.Null);
        Assert.That(session.GetBattleAiId(BattleSide.Defender), Is.EqualTo("ruthless"));
    }

    [Test]
    public async Task OwnAiSurvivesSaveTest()
    {
        WorldEngine engine = Builder().Build();
        engine.CheckCommand(new WorldSetAiOverrideCommand(AiId, new WorldAiOverride("builder", "ruthless")), [new WorldAiOverrideChangedEvent(AiId, new WorldAiOverride("builder", "ruthless"))]);
        using MemoryStream stream = new();

        await WorldStateJsonSerializer.SerializeAsync(stream, engine.State);
        string json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        Assert.That(json, Does.Contain("\"worldAi\":\"builder\"").And.Not.Contain("isEmpty"));
        stream.Position = 0;
        WorldState actual = await WorldStateJsonSerializer.DeserializeAsync(stream);

        Assert.That(actual.Players[AiId].AiOverride, Is.EqualTo(new WorldAiOverride("builder", "ruthless")));
        Assert.That(actual.Players[HumanId].AiOverride, Is.Null);
        Assert.That(actual, Is.EqualTo(engine.State));
    }
}

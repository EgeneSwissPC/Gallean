using Gallean.Common;

namespace Gallean.Engine.Tests;

// A scenario can contain several neutral players. Each is a separate side: hostile to the others, taking its own
// turn and planning with its own AI memory.
public sealed class WorldEngineMultipleNeutralPlayersTests
{
    private static readonly WorldPlayerId NeutralAId = new(1);
    private static readonly WorldPlayerId HumanId = new(2);
    private static readonly WorldPlayerId NeutralBId = new(3);

    private static readonly WorldSubrace SubraceA = TestHelpers.NeutralSubrace;
    private static readonly WorldSubrace SubraceB = new(new WorldSubraceId("subraceB"), NeutralBId, WorldSubraceType.Neutral, BannerVariant: new WorldObjectVariant(5));

    private static readonly UnitDefinition Strong = TestHelpers.SimpleLeaderDefinition(
        TestHelpers.SimpleUnitDefinition("strong", maxHitpoints: 100, initiative: 50, damage: 50),
        movementPoints: 6, leadership: 6);
    private static readonly UnitDefinition Weak = TestHelpers.SimpleLeaderDefinition(
        TestHelpers.SimpleUnitDefinition("weak", maxHitpoints: 10, initiative: 40, damage: 5),
        movementPoints: 6, leadership: 6);

    private static WorldStack Stack(string id, WorldPlayerId ownerId, WorldSubrace subrace, int x, UnitDefinition definition, WorldCanonicalStackOrder order, int movementPoints)
    {
        return TestHelpers.SimpleStack(
                new WorldStackId(id),
                ownerId,
                new WorldPosition(x, 0),
                new UnitGrid<WorldUnit>.Builder()
                    .Place(TestHelpers.SimpleUnit(new WorldUnitId(id), definition), GridPosition.FrontBottom, false)
                    .Build(),
                movementPoints) with
            {
                SubraceId = subrace.Id,
                Order = order
            };
    }

    private static (GameSession Session, IEngineContext Context) Build(WorldMap map, WorldPlayerId currentPlayerId, params WorldStack[] stacks)
    {
        WorldPlayer neutralA = TestHelpers.SimplePlayer(NeutralAId, WorldRace.Neutral, map) with { IsAi = true };
        WorldPlayer human = TestHelpers.SimplePlayer(HumanId, WorldRace.Heretic, map);
        WorldPlayer neutralB = TestHelpers.SimplePlayer(NeutralBId, WorldRace.Neutral, map) with { IsAi = true };

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithTurnInfo(new WorldTurnInfo(1, currentPlayerId))
            .WithDefinition(Strong)
            .WithDefinition(Weak)
            .WithSubrace(SubraceA)
            .WithSubrace(SubraceB);

        foreach (WorldPlayer player in new[] { neutralA, human, neutralB })
        {
            builder.WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, LordCategory.Warrior)).WithPlayer(player);
        }

        foreach (WorldStack stack in stacks)
        {
            builder.WithStack(stack);
        }

        return builder.BuildSession();
    }

    private static void PlayAiTurn(GameSession session)
    {
        WorldPlayerId playerId = session.World.State.TurnInfo.CurrentPlayerId;

        for (int i = 0; i < 200 && session.World.State.TurnInfo.CurrentPlayerId == playerId; i++)
        {
            Assert.That(session.World.DispatchCommand(new WorldAiActionCommand()).Error, Is.Null);
        }

        Assert.That(session.World.State.TurnInfo.CurrentPlayerId, Is.Not.EqualTo(playerId), "AI did not end its turn.");
    }

    [Test]
    public void HostilityTest()
    {
        WorldMap map = TestHelpers.SimpleMap(4, 1);
        (GameSession session, _) = Build(map, HumanId, Stack("Human", HumanId, SubraceA, 0, Weak, WorldStackOrder.Normal.Instance, 0));
        WorldState state = session.World.State;

        Assert.That(DiplomacyCalculator.IsHostile(state, NeutralAId, NeutralBId), Is.True);
        Assert.That(DiplomacyCalculator.IsHostile(state, NeutralBId, NeutralAId), Is.True);
        Assert.That(DiplomacyCalculator.IsHostile(state, NeutralAId, NeutralAId), Is.False);
        Assert.That(DiplomacyCalculator.IsHostile(state, NeutralAId, HumanId), Is.True);
    }

    [Test]
    public void SeparateTurnsAndMemoryTest()
    {
        WorldMap map = TestHelpers.SimpleMap(12, 1);
        (GameSession session, _) = Build(
            map,
            HumanId,
            Stack("Human", HumanId, SubraceA, 11, Weak, WorldStackOrder.Normal.Instance, 0),
            Stack("StandA", NeutralAId, SubraceA, 0, Weak, WorldStackOrder.Stand.Instance, 6),
            Stack("StandB", NeutralBId, SubraceB, 5, Weak, WorldStackOrder.Stand.Instance, 6));

        Assert.That(session.World.DispatchCommand(new WorldEndTurnCommand()).Error, Is.Null);
        Assert.That(session.World.State.TurnInfo, Is.EqualTo(new WorldTurnInfo(1, NeutralAId)));

        PlayAiTurn(session);
        Assert.That(session.World.State.TurnInfo, Is.EqualTo(new WorldTurnInfo(1, NeutralBId)));

        PlayAiTurn(session);
        Assert.That(session.World.State.TurnInfo, Is.EqualTo(new WorldTurnInfo(2, HumanId)));

        Assert.That(session.World.State.AiMemories.Keys, Is.EquivalentTo(new[] { NeutralAId, NeutralBId }));
    }

    [Test]
    public void NeutralPlayersAttackEachOtherTest()
    {
        WorldMap map = TestHelpers.SimpleMap(12, 1);
        WorldStack ownStack = Stack("OwnA", NeutralAId, SubraceA, 0, Weak, WorldStackOrder.Stand.Instance, 0);
        WorldStack roamer = Stack("RoamerA", NeutralAId, SubraceA, 1, Strong, WorldStackOrder.Roam.Instance, 6);
        WorldStack target = Stack("WeakB", NeutralBId, SubraceB, 3, Weak, WorldStackOrder.Stand.Instance, 0);
        (GameSession session, IEngineContext engine) = Build(
            map,
            NeutralAId,
            Stack("Human", HumanId, SubraceA, 11, Weak, WorldStackOrder.Normal.Instance, 0),
            ownStack,
            roamer,
            target);
        TestWorldAiRunner runner = new(session, engine);

        Assert.That(TestWorldAiTraceFormatter.Format(runner.PlayTurn()), Has.Some.StartsWith("[P2").And.Contains("Move RoamerA -> (3, 0) (Attacker wins)"));

        Assert.That(session.World.State.Stacks.ContainsKey(target.Id), Is.False, "Neutral player A did not attack neutral player B.");
        Assert.That(session.World.State.Stacks[roamer.Id].OwnerId, Is.EqualTo(NeutralAId));
        Assert.That(session.World.State.Stacks[ownStack.Id], Is.EqualTo(ownStack));
    }

    [Test]
    public async Task SaveRoundTripTest()
    {
        WorldMap map = TestHelpers.SimpleMap(12, 1);
        (GameSession session, _) = Build(
            map,
            HumanId,
            Stack("Human", HumanId, SubraceA, 11, Weak, WorldStackOrder.Normal.Instance, 0),
            Stack("StandA", NeutralAId, SubraceA, 0, Weak, WorldStackOrder.Stand.Instance, 6),
            Stack("StandB", NeutralBId, SubraceB, 5, Weak, WorldStackOrder.Stand.Instance, 6));

        Assert.That(session.World.DispatchCommand(new WorldEndTurnCommand()).Error, Is.Null);
        PlayAiTurn(session);
        PlayAiTurn(session);

        using MemoryStream stream = new();
        await WorldStateJsonSerializer.SerializeAsync(stream, session.World.State);
        stream.Position = 0;
        WorldState actual = await WorldStateJsonSerializer.DeserializeAsync(stream);

        Assert.That(actual, Is.EqualTo(session.World.State));
        Assert.That(actual.Players.Values.Count(player => player.Race == WorldRace.Neutral), Is.EqualTo(2));
    }
}

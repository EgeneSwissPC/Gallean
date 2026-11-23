namespace Gallean.Engine.Tests;

// Gallean Upgrade: the "Refuse to flee" battle mode in the engine (AI action and auto-resolve).
public sealed class BattleEngineNoRetreatTests
{
    private static readonly BattleUnitId LeaderId = new(0);
    private static readonly BattleUnitId AllyId = new(1);
    private static readonly BattleUnitId EnemyId = new(2);

    // A leader and an ally against an enemy far too strong for them: the AI flees with the leader. The enemy hits
    // for less than the leader's hitpoints, so the retreat is safe (upstream 0.17: no retreat that ends in death).
    private static BattleEngine StrongEnemyBattle(int leaderDamage = 10, int enemyDamage = 90, int enemyHitpoints = 600)
    {
        UnitDefinition leaderDefinition = TestHelpers.SimpleUnitDefinition("leader", maxHitpoints: 100, initiative: 50, damage: leaderDamage) with
        {
            Category = UnitCategory.Leader,
            LeaderCategory = UnitLeaderCategory.Fighter,
            Leadership = 1
        };
        UnitDefinition allyDefinition = TestHelpers.SimpleUnitDefinition("ally", maxHitpoints: 50, initiative: 40, damage: leaderDamage);
        UnitDefinition enemyDefinition = TestHelpers.SimpleUnitDefinition("enemy", maxHitpoints: enemyHitpoints, initiative: 30, damage: enemyDamage);

        return new TestBattleEngineBuilder()
            .WithDefinition(leaderDefinition)
            .WithDefinition(allyDefinition)
            .WithDefinition(enemyDefinition)
            .WithAttacker(TestHelpers.SimpleBattleUnit(LeaderId, null, leaderDefinition.Id, BattleSide.Attacker, hitpoints: 100), new GridPosition(0, 0))
            .WithAttacker(TestHelpers.SimpleBattleUnit(AllyId, null, allyDefinition.Id, BattleSide.Attacker, hitpoints: 50), new GridPosition(0, 1))
            .WithDefender(TestHelpers.SimpleBattleUnit(EnemyId, null, enemyDefinition.Id, BattleSide.Defender, hitpoints: enemyHitpoints), new GridPosition(0, 0))
            .Build();
    }

    private static IReadOnlyList<BattleEvent> Dispatch(BattleEngine engine, IBattleCommand<Gallean.Common.Empty> command)
    {
        ICommandResult<BattleCommandError, BattleEvent, Gallean.Common.Empty> result = engine.DispatchCommand(command);
        Assert.That(result.Error, Is.Null);

        return result.Events;
    }

    [Test]
    public void AiActionRetreatsByDefaultTest()
    {
        IReadOnlyList<BattleEvent> events = Dispatch(StrongEnemyBattle(), new BattleAiActionCommand(CanUseItems: false));

        Assert.That(events.OfType<BattleUnitRetreatingEvent>().Select(ev => ev.UnitId), Does.Contain(LeaderId));
    }

    [Test]
    public void AiActionWithoutRetreatAttacksTest()
    {
        IReadOnlyList<BattleEvent> events = Dispatch(StrongEnemyBattle(), new BattleAiActionCommand(CanUseItems: false, CanRetreat: false));

        Assert.That(events.OfType<BattleUnitRetreatingEvent>(), Is.Empty);
        Assert.That(events.OfType<BattleUnitAttackEvent>().Select(ev => ev.AttackerId), Is.EqualTo(new[] { LeaderId }));
    }

    [Test]
    public void AutoResolveRetreatsByDefaultTest()
    {
        BattleEngine engine = StrongEnemyBattle();
        IReadOnlyList<BattleEvent> events = Dispatch(engine, new BattleAutoResolveCommand());

        Assert.That(events.OfType<BattleUnitRetreatingEvent>(), Is.Not.Empty);
        Assert.That(engine.State.Outcome, Is.Not.Null);
    }

    [Test]
    public void AutoResolveWithoutRetreatFightsToTheEndTest()
    {
        BattleEngine engine = StrongEnemyBattle();
        IReadOnlyList<BattleEvent> events = Dispatch(engine, new BattleAutoResolveCommand { NoRetreatSide = BattleSide.Attacker });

        Assert.That(events.OfType<BattleUnitRetreatingEvent>(), Is.Empty);
        Assert.That(events, Does.Contain(new BattleUnitDiedEvent(LeaderId)));
        Assert.That(events, Does.Contain(new BattleUnitDiedEvent(AllyId)));
        Assert.That(engine.State.Outcome, Is.EqualTo(new BattleOutcome(BattleSide.Defender)));
    }

    [Test]
    public void AutoResolveWithoutRetreatWinsLongBattleTest()
    {
        // The plain auto-resolve makes the attacker retreat after 15 rounds; refusing to flee
        // keeps attacking and wins (the enemy hits for 1, the army for 10 against 300 hit points).
        BattleEngine plain = StrongEnemyBattle(leaderDamage: 5, enemyDamage: 1, enemyHitpoints: 300);
        Dispatch(plain, new BattleAutoResolveCommand());

        BattleEngine engine = StrongEnemyBattle(leaderDamage: 5, enemyDamage: 1, enemyHitpoints: 300);
        IReadOnlyList<BattleEvent> events = Dispatch(engine, new BattleAutoResolveCommand { NoRetreatSide = BattleSide.Attacker });

        Assert.That(plain.State.Outcome, Is.Not.EqualTo(new BattleOutcome(BattleSide.Attacker)));
        Assert.That(events.OfType<BattleUnitRetreatingEvent>(), Is.Empty);
        Assert.That(engine.State.Outcome, Is.EqualTo(new BattleOutcome(BattleSide.Attacker)));
        Assert.That(engine.State.RoundNumber, Is.GreaterThan(16));
    }

    [Test]
    public void AutoResolveWithoutRetreatStalemateEndsTest()
    {
        // Nobody can hurt anybody: the side that refuses to flee retreats after round 45
        // instead of the auto-resolve failing.
        BattleEngine engine = StrongEnemyBattle(leaderDamage: 0, enemyDamage: 0, enemyHitpoints: 300);
        IReadOnlyList<BattleEvent> events = Dispatch(engine, new BattleAutoResolveCommand { NoRetreatSide = BattleSide.Attacker });

        Assert.That(events.OfType<BattleUnitRetreatingEvent>(), Is.Not.Empty);
        Assert.That(engine.State.Outcome, Is.Not.Null);
        Assert.That(engine.State.RoundNumber, Is.GreaterThan(45));
    }

    // Upstream 0.17 (609292b6): a unit that waited and then retreats needs one more turn to escape, so Wait then
    // Retreat is its own path. The leader waits; when its turn comes back, Refuse to flee must still never retreat it.
    [TestCase(true)]
    [TestCase(false)]
    public void WaitThenRetreatTest(bool canRetreat)
    {
        // The enemy hits for 40, so a leader hit while waiting can still retreat safely.
        BattleEngine engine = StrongEnemyBattle(enemyDamage: 40);
        Assert.That(engine.State.CurrentUnitId, Is.EqualTo(LeaderId));
        Dispatch(engine, new BattleWaitCommand(LeaderId));

        List<BattleEvent> leaderEvents = [];

        for (int i = 0; i < 10 && engine.State.Outcome is null && leaderEvents.Count == 0; i++)
        {
            bool isLeader = engine.State.CurrentUnitId == LeaderId;
            bool isAttacker = engine.State.CurrentUnit.Side == BattleSide.Attacker;
            IReadOnlyList<BattleEvent> events = Dispatch(engine, new BattleAiActionCommand(CanUseItems: false, CanRetreat: !isAttacker || canRetreat));

            if (isLeader)
            {
                leaderEvents.AddRange(events);
            }
        }

        Assert.That(leaderEvents, Is.Not.Empty, "the leader's turn came back after the wait");

        if (canRetreat)
        {
            // Control: Classic retreats here, and the wait costs the extra turn.
            Assert.That(leaderEvents.OfType<BattleUnitRetreatingEvent>().Select(ev => (ev.UnitId, ev.RemainingTurns)), Does.Contain((LeaderId, 2)));
        }
        else
        {
            Assert.That(leaderEvents.OfType<BattleUnitRetreatingEvent>(), Is.Empty);
            Assert.That(engine.State.GetUnit(LeaderId).StatusEffects.OfType<BattleRetreatStatusEffect>(), Is.Empty);
        }
    }
}

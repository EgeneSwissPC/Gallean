namespace Gallean.Engine.Tests;

// Gallean Upgrade: the "heal and defend" battle mode.
public sealed class BattleEngineHealModeTests
{
    private static UnitDefinition Cleric(UnitAttackReach reach = UnitAttackReach.Any)
    {
        return TestHelpers.SimpleUnitDefinition("cleric", maxHitpoints: 100, initiative: 60, damage: 0) with
        {
            Attack = TestHelpers.SimpleAttack(new UnitAttackDefinitionId("cleric"), initiative: 60, damage: 0, accuracy: 100, reach: reach) with
            {
                Type = UnitAttackType.Heal,
                Heal = 30
            }
        };
    }

    private static readonly UnitDefinition Fighter = TestHelpers.SimpleUnitDefinition("fighter", maxHitpoints: 100, initiative: 50, damage: 10);
    private static readonly UnitDefinition Archer = TestHelpers.SimpleUnitDefinition("archer", maxHitpoints: 100, initiative: 45, damage: 10);
    private static readonly UnitDefinition Enemy = TestHelpers.SimpleUnitDefinition("enemy", maxHitpoints: 100, initiative: 10, damage: 5);

    private static BattleEngine Build(UnitDefinition cleric, int clericHp, int fighterHp, int archerHp)
    {
        return new TestBattleEngineBuilder()
            .WithDefinition(cleric)
            .WithDefinition(Fighter)
            .WithDefinition(Archer)
            .WithDefinition(Enemy)
            .WithAttacker(TestHelpers.SimpleBattleUnit(new BattleUnitId(0), null, cleric.Id, BattleSide.Attacker, hitpoints: clericHp), GridPosition.BackCenter)
            .WithAttacker(TestHelpers.SimpleBattleUnit(new BattleUnitId(1), null, Fighter.Id, BattleSide.Attacker, hitpoints: fighterHp), GridPosition.FrontBottom)
            .WithAttacker(TestHelpers.SimpleBattleUnit(new BattleUnitId(2), null, Archer.Id, BattleSide.Attacker, hitpoints: archerHp), GridPosition.FrontTop)
            .WithDefender(TestHelpers.SimpleBattleUnit(new BattleUnitId(3), null, Enemy.Id, BattleSide.Defender, hitpoints: 100), GridPosition.FrontBottom)
            .Build();
    }

    [Test]
    public void HealerHealsMostWoundedAllyTest()
    {
        BattleEngine engine = Build(Cleric(), clericHp: 90, fighterHp: 70, archerHp: 40);

        ICommandResult<BattleCommandError, BattleEvent, Gallean.Common.Empty> result =
            engine.DispatchCommand(new BattleHealOrDefendCommand(new BattleUnitId(0)));

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Events[0], Is.EqualTo(new BattleUnitAttackEvent(
            new BattleUnitId(0),
            new BattleAttackUnitTarget(new BattleUnitId(2)),
            [new UnitAttackDefinitionId("cleric")])));
        Assert.That(engine.State.GetUnit(new BattleUnitId(2)).Hitpoints, Is.EqualTo(70));
        Assert.That(engine.State.GetUnit(new BattleUnitId(1)).Hitpoints, Is.EqualTo(70));
        Assert.That(engine.State.CurrentUnitId, Is.EqualTo(new BattleUnitId(1)));
    }

    [Test]
    public void OtherUnitsDefendTest()
    {
        BattleEngine engine = Build(Cleric(), clericHp: 100, fighterHp: 70, archerHp: 40);

        engine.DispatchCommand(new BattleHealOrDefendCommand(new BattleUnitId(0)));

        ICommandResult<BattleCommandError, BattleEvent, Gallean.Common.Empty> result =
            engine.DispatchCommand(new BattleHealOrDefendCommand(new BattleUnitId(1)));

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Events[0], Is.EqualTo(new BattleUnitDefendedEvent(new BattleUnitId(1))));
        Assert.That(engine.State.GetUnit(new BattleUnitId(3)).Hitpoints, Is.EqualTo(100));
    }

    [Test]
    public void HealerDefendsWhenNobodyIsWoundedTest()
    {
        BattleEngine engine = Build(Cleric(), clericHp: 100, fighterHp: 100, archerHp: 100);

        ICommandResult<BattleCommandError, BattleEvent, Gallean.Common.Empty> result =
            engine.DispatchCommand(new BattleHealOrDefendCommand(new BattleUnitId(0)));

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Events[0], Is.EqualTo(new BattleUnitDefendedEvent(new BattleUnitId(0))));
    }

    [Test]
    public void AllReachHealerHealsTest()
    {
        BattleEngine engine = Build(Cleric(UnitAttackReach.All), clericHp: 100, fighterHp: 100, archerHp: 40);

        engine.DispatchCommand(new BattleHealOrDefendCommand(new BattleUnitId(0)));

        Assert.That(engine.State.GetUnit(new BattleUnitId(2)).Hitpoints, Is.EqualTo(70));
    }

    [Test]
    public void NotUnitTurnTest()
    {
        BattleEngine engine = Build(Cleric(), clericHp: 100, fighterHp: 70, archerHp: 40);

        engine.CheckCommandError(new BattleHealOrDefendCommand(new BattleUnitId(1)), BattleCommandError.NotUnitTurn);
    }

    [Test]
    public void StatusTest()
    {
        BattleEngine wounded = Build(Cleric(), clericHp: 100, fighterHp: 70, archerHp: 100);

        Assert.That(
            wounded.DispatchCommand(new BattleGetHealModeStatusCommand(BattleSide.Attacker), dryRun: true).Data,
            Is.EqualTo(new BattleGetHealModeStatusCommand.Result(IsWounded: true, HasHealer: true)));
        Assert.That(
            wounded.DispatchCommand(new BattleGetHealModeStatusCommand(BattleSide.Defender), dryRun: true).Data,
            Is.EqualTo(new BattleGetHealModeStatusCommand.Result(IsWounded: false, HasHealer: false)));

        BattleEngine healthy = Build(Cleric(), clericHp: 100, fighterHp: 100, archerHp: 100);

        Assert.That(
            healthy.DispatchCommand(new BattleGetHealModeStatusCommand(BattleSide.Attacker), dryRun: true).Data!.CanKeepHealing,
            Is.False);

        BattleEngine deadHealer = Build(Cleric(), clericHp: 0, fighterHp: 70, archerHp: 100);

        Assert.That(
            deadHealer.DispatchCommand(new BattleGetHealModeStatusCommand(BattleSide.Attacker), dryRun: true).Data,
            Is.EqualTo(new BattleGetHealModeStatusCommand.Result(IsWounded: true, HasHealer: false)));
    }
}

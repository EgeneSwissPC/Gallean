namespace Gallean.Engine.Tests;

// Gallean Upgrade: the AI personalities (docs/ai-switching.md, phase 5).
public sealed class AiPersonalitiesTests
{
    [Test]
    public void RegisteredTest()
    {
        Assert.That(AiBrainRegistry.WorldBrains.Select(brain => brain.Id), Is.EqualTo(new[]
        {
            AiBrainRegistry.ClassicId, AiPersonalities.ClassicPlusId, AiPersonalities.AggressiveId, AiPersonalities.BuilderId, AiPersonalities.ExpansionistId
        }));
        Assert.That(AiBrainRegistry.BattleBrains.Select(brain => brain.Id), Is.EqualTo(new[]
        {
            AiBrainRegistry.ClassicId, AiPersonalities.CautiousId, AiPersonalities.RuthlessId
        }));
    }

    [Test]
    public void ClassicProfileKeepsOriginalValuesTest()
    {
        // The Classic brain passes no profile: these defaults must stay the original constants.
        Assert.That(WorldAiProfile.Classic, Is.EqualTo(new WorldAiProfile
        {
            AttackPowerPercent = 100,
            HurtThreshold = 800,
            HealedThreshold = 900,
            ReinforceThreshold = 800,
            LeaderOvercommitGold = 400,
            WaitForFreeCapital = false,
            BuildingPriority = 1,
            UnitPriority = 0,
            RodPriority = 2
        }));
        Assert.That(BattleAiProfile.Classic, Is.EqualTo(new BattleAiProfile { RetreatPowerShare = 250, CanRetreat = true, CanDefend = true }));
    }

    [Test]
    public void EveryWorldPersonalityHasTheIdleFixTest()
    {
        foreach (AiBrainInfo info in AiBrainRegistry.WorldBrains.Where(brain => brain.Id != AiBrainRegistry.ClassicId))
        {
            ProfileWorldAiBrain brain = (ProfileWorldAiBrain)AiBrainRegistry.ResolveWorld(info.Id);
            Assert.That(brain.Profile.WaitForFreeCapital, Is.True, info.Id);
        }
    }

    // A leader and an ally against a far stronger enemy (as in BattleEngineNoRetreatTests).
    private static BattleEngine StrongEnemyBattle(int enemyDamage)
    {
        UnitDefinition leaderDefinition = TestHelpers.SimpleUnitDefinition("leader", maxHitpoints: 100, initiative: 50, damage: 10) with
        {
            Category = UnitCategory.Leader,
            LeaderCategory = UnitLeaderCategory.Fighter,
            Leadership = 1
        };
        UnitDefinition allyDefinition = TestHelpers.SimpleUnitDefinition("ally", maxHitpoints: 50, initiative: 40, damage: 10);
        UnitDefinition enemyDefinition = TestHelpers.SimpleUnitDefinition("enemy", maxHitpoints: 600, initiative: 30, damage: enemyDamage);

        return new TestBattleEngineBuilder()
            .WithDefinition(leaderDefinition)
            .WithDefinition(allyDefinition)
            .WithDefinition(enemyDefinition)
            .WithAttacker(TestHelpers.SimpleBattleUnit(new BattleUnitId(0), null, leaderDefinition.Id, BattleSide.Attacker, hitpoints: 100), new GridPosition(0, 0))
            .WithAttacker(TestHelpers.SimpleBattleUnit(new BattleUnitId(1), null, allyDefinition.Id, BattleSide.Attacker, hitpoints: 50), new GridPosition(0, 1))
            .WithDefender(TestHelpers.SimpleBattleUnit(new BattleUnitId(2), null, enemyDefinition.Id, BattleSide.Defender, hitpoints: 600), new GridPosition(0, 0))
            .Build();
    }

    private static IReadOnlyList<BattleEvent> Act(string brainId, int enemyDamage)
    {
        BattleEngine engine = StrongEnemyBattle(enemyDamage);
        ICommandResult<BattleCommandError, BattleEvent, Gallean.Common.Empty> result = engine.DispatchCommand(new BattleAiActionCommand(CanUseItems: false) { BrainId = brainId });
        Assert.That(result.Error, Is.Null);

        return result.Events;
    }

    [Test]
    public void CautiousFleesWhereClassicFightsTest()
    {
        // Between the two retreat shares (25 % Classic, 40 % Cautious) Classic keeps fighting.
        int damage = Enumerable.Range(5, 60).First(d =>
            !Act(AiBrainRegistry.ClassicId, d).OfType<BattleUnitRetreatingEvent>().Any() &&
            Act(AiPersonalities.CautiousId, d).OfType<BattleUnitRetreatingEvent>().Any());

        Assert.That(damage, Is.GreaterThan(0));
    }

    [Test]
    public void RuthlessAttacksWhereClassicFleesTest()
    {
        Assert.That(Act(AiBrainRegistry.ClassicId, 100).OfType<BattleUnitRetreatingEvent>(), Is.Not.Empty);

        IReadOnlyList<BattleEvent> ruthless = Act(AiPersonalities.RuthlessId, 100);
        Assert.That(ruthless.OfType<BattleUnitRetreatingEvent>(), Is.Empty);
        Assert.That(ruthless.OfType<BattleUnitAttackEvent>(), Is.Not.Empty);
    }
}

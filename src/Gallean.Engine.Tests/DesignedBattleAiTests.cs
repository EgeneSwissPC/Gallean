using System.Collections.Immutable;
using Gallean.Common;
using Gallean.Entities.GameData;

namespace Gallean.Engine.Tests;

// Gallean Upgrade (battle AI designer, docs/battle-ai-designer.md phase 7): designed battle AIs in the engine.
// Every target preference and behaviour block is shown changing the decision against a Classic control.
[NonParallelizable]
public sealed class DesignedBattleAiTests
{
    private static readonly BattleUnitId Attacker = new(0);
    private static readonly BattleUnitId First = new(1);
    private static readonly BattleUnitId Second = new(2);

    [TearDown]
    public void TearDown()
    {
        AiBrainRegistry.SetDesignedBattleAis([]);
    }

    private sealed record Defender(UnitDefinition Definition, int Hitpoints, GridPosition Position, bool Defending = false);

    private static UnitDefinition Unit(string id, int hitpoints, int damage = 10, int initiative = 30)
    {
        return TestHelpers.SimpleUnitDefinition(id, maxHitpoints: hitpoints, initiative: initiative, damage: damage);
    }

    private static BattleEngine Battle(UnitDefinition attacker, int attackerHitpoints, params Defender[] defenders)
    {
        TestBattleEngineBuilder builder = new TestBattleEngineBuilder()
            .WithDefinition(attacker)
            .WithAttacker(TestHelpers.SimpleBattleUnit(Attacker, null, attacker.Id, BattleSide.Attacker, hitpoints: attackerHitpoints), GridPosition.FrontCenter);

        for (int index = 0; index < defenders.Length; index++)
        {
            Defender defender = defenders[index];
            BattleUnit unit = TestHelpers.SimpleBattleUnit(new BattleUnitId(index + 1), null, defender.Definition.Id, BattleSide.Defender, hitpoints: defender.Hitpoints);

            if (defender.Defending)
            {
                unit = unit with { StatusEffects = [new BattleDefendStatusEffect()] };
            }

            builder = builder.WithDefinition(defender.Definition).WithDefender(unit, defender.Position);
        }

        return builder.Build();
    }

    // The attacker's chosen target with a designed AI of one stage with these tactics.
    private static BattleUnitId? Choose(BattleEngine engine, DesignedBattleTactics tactics)
    {
        string id = "custom-battle:test-" + Guid.NewGuid().ToString("N");
        AiBrainRegistry.SetDesignedBattleAis([new DesignedBattleAi(id, "Test", [DesignedBattleStage.Classic with { Tactics = tactics }])]);

        ICommandResult<BattleCommandError, BattleEvent, Empty> result = engine.DispatchCommand(new BattleAiActionCommand(CanUseItems: false) { BrainId = id }, dryRun: true);
        Assert.That(result.Error, Is.Null);

        return result.Events.OfType<BattleUnitAttackEvent>().Select(ev => ev.Target).OfType<BattleAttackUnitTarget>().Select(target => (BattleUnitId?)target.UnitId).FirstOrDefault();
    }

    private static UnitDefinition Strong(string id) => Unit(id, 200, damage: 60, initiative: 90);

    // The weak plain unit is Classic's pick; the preference must move the attack to the special one.
    private static void AssertPreference(DesignedBattleTactics tactics, BattleEngine engine, BattleUnitId preferred)
    {
        Assert.That(Choose(engine, DesignedBattleTactics.Classic), Is.Not.EqualTo(preferred), "control: Classic picks another target");
        Assert.That(Choose(engine, tactics), Is.EqualTo(preferred));
    }

    [Test]
    public void HealersFirstTest()
    {
        UnitDefinition healer = Unit("healer", 150) with
        {
            Attack = TestHelpers.SimpleAttack(new UnitAttackDefinitionId("heal"), 30, 0, 100, UnitAttackReach.Any) with { Type = UnitAttackType.Heal, Heal = 30 }
        };
        BattleEngine engine = Battle(Strong("a"), 200, new Defender(Unit("weak", 70), 70, GridPosition.FrontBottom), new Defender(healer, 150, GridPosition.BackCenter));

        AssertPreference(DesignedBattleTactics.Classic with { HealersFirst = 1000 }, engine, Second);
    }

    [Test]
    public void CastersFirstTest()
    {
        UnitDefinition mage = Unit("mage", 150) with
        {
            Attack = TestHelpers.SimpleAttack(new UnitAttackDefinitionId("fire"), 30, 10, 100, UnitAttackReach.All)
        };
        BattleEngine engine = Battle(Strong("a"), 200, new Defender(Unit("weak", 70), 70, GridPosition.FrontBottom), new Defender(mage, 150, GridPosition.BackCenter));

        AssertPreference(DesignedBattleTactics.Classic with { CastersFirst = 1000 }, engine, Second);
    }

    [Test]
    public void LeaderFirstTest()
    {
        UnitDefinition leader = Unit("leader", 150) with { Category = UnitCategory.Leader, LeaderCategory = UnitLeaderCategory.Fighter, Leadership = 2 };
        BattleEngine engine = Battle(Strong("a"), 200, new Defender(Unit("weak", 70), 70, GridPosition.FrontBottom), new Defender(leader, 150, GridPosition.BackCenter));

        AssertPreference(DesignedBattleTactics.Classic with { LeaderFirst = 1000 }, engine, Second);
    }

    [Test]
    public void HardestHitterFirstTest()
    {
        BattleEngine engine = Battle(Strong("a"), 200, new Defender(Unit("weak", 70, damage: 5), 70, GridPosition.FrontBottom), new Defender(Unit("brute", 150, damage: 80), 150, GridPosition.FrontTop));

        AssertPreference(DesignedBattleTactics.Classic with { HardestHitterFirst = 1000 }, engine, Second);
    }

    [Test]
    public void WoundedFirstTest()
    {
        // The defending unit has the fewest hit points; Classic avoids units that defend.
        BattleEngine engine = Battle(Strong("a"), 200, new Defender(Unit("plain", 200), 90, GridPosition.FrontBottom), new Defender(Unit("guard", 200), 80, GridPosition.FrontTop, Defending: true));

        AssertPreference(DesignedBattleTactics.Classic with { WoundedFirst = 1000 }, engine, Second);
    }

    [Test]
    public void FocusFireTest()
    {
        // The big unit has lost two thirds of its hit points but still has more than the small one.
        BattleEngine engine = Battle(Strong("a"), 200, new Defender(Unit("small", 70), 70, GridPosition.FrontBottom), new Defender(Unit("big", 300), 100, GridPosition.FrontTop));

        AssertPreference(DesignedBattleTactics.Classic with { FocusFire = 1000 }, engine, Second);
    }

    [Test]
    public void HurtLeaderDefendsTest()
    {
        UnitDefinition leader = Strong("lead") with { Category = UnitCategory.Leader, LeaderCategory = UnitLeaderCategory.Fighter, Leadership = 2 };
        BattleEngine engine = Battle(leader, 60, new Defender(Unit("foe", 100), 100, GridPosition.FrontBottom));

        string id = "custom-battle:protect";
        AiBrainRegistry.SetDesignedBattleAis([
            new DesignedBattleAi(id, "Defends", [DesignedBattleStage.Classic with { Tactics = DesignedBattleTactics.Classic with { LeaderDefendsBelowPercent = 50, NeverFlee = true } }]),
            new DesignedBattleAi("custom-battle:plain", "Plain", [DesignedBattleStage.Classic with { Tactics = DesignedBattleTactics.Classic with { ProtectLeader = true, LeaderDefendsBelowPercent = 25, NeverFlee = true } }])
        ]);

        IReadOnlyList<BattleEvent> plain = engine.DispatchCommand(new BattleAiActionCommand(false) { BrainId = "custom-battle:plain" }, dryRun: true).Events;
        IReadOnlyList<BattleEvent> protect = engine.DispatchCommand(new BattleAiActionCommand(false) { BrainId = id }, dryRun: true).Events;

        // The leader is at 60 of 200 = 30 %: defends below 50 %, attacks with 25 % (or with "protect" alone).
        Assert.That(plain.OfType<BattleUnitAttackEvent>(), Is.Not.Empty, "control: a leader above the threshold attacks");
        Assert.That(protect, Does.Contain(new BattleUnitDefendedEvent(Attacker)));
    }

    [Test]
    public void ProtectLeaderHealsTheLeaderFirstTest()
    {
        // An attacker-side healer with a hurt soldier (more missing hit points) and a hurt leader.
        UnitDefinition healer = Unit("cleric", 100, initiative: 95) with
        {
            Attack = TestHelpers.SimpleAttack(new UnitAttackDefinitionId("cleric-heal"), 95, 0, 100, UnitAttackReach.Any) with { Type = UnitAttackType.Heal, Heal = 40 }
        };
        UnitDefinition leader = Unit("lord", 100) with { Category = UnitCategory.Leader, LeaderCategory = UnitLeaderCategory.Fighter, Leadership = 3 };
        UnitDefinition soldier = Unit("soldier", 200);
        BattleUnitId leaderId = new(5);
        BattleUnitId soldierId = new(6);
        BattleEngine engine = new TestBattleEngineBuilder()
            .WithDefinition(healer).WithDefinition(leader).WithDefinition(soldier).WithDefinition(Unit("foe", 100))
            .WithAttacker(TestHelpers.SimpleBattleUnit(Attacker, null, healer.Id, BattleSide.Attacker, 100), GridPosition.BackCenter)
            .WithAttacker(TestHelpers.SimpleBattleUnit(leaderId, null, leader.Id, BattleSide.Attacker, 60), GridPosition.FrontCenter)
            .WithAttacker(TestHelpers.SimpleBattleUnit(soldierId, null, soldier.Id, BattleSide.Attacker, 100), GridPosition.FrontBottom)
            .WithDefender(TestHelpers.SimpleBattleUnit(First, null, new UnitDefinitionId("foe"), BattleSide.Defender, 100), GridPosition.FrontCenter)
            .Build();

        BattleUnitId? classic = Choose(engine, DesignedBattleTactics.Classic);
        BattleUnitId? protect = Choose(engine, DesignedBattleTactics.Classic with { ProtectLeader = true });

        Assert.That(classic, Is.EqualTo(soldierId), "control: Classic heals the unit missing the most hit points");
        Assert.That(protect, Is.EqualTo(leaderId));
    }

    [Test]
    public void FleeBlocksTest()
    {
        // A leader against a far stronger enemy: Classic flees, "never flee" fights, "flee below 1 %" fights too.
        UnitDefinition leader = Unit("lead", 100, initiative: 90) with { Category = UnitCategory.Leader, LeaderCategory = UnitLeaderCategory.Fighter, Leadership = 2 };
        UnitDefinition ally = Unit("ally", 50, initiative: 20);
        BattleEngine engine = new TestBattleEngineBuilder()
            .WithDefinition(leader).WithDefinition(ally).WithDefinition(Unit("giant", 600, damage: 90))
            .WithAttacker(TestHelpers.SimpleBattleUnit(Attacker, null, leader.Id, BattleSide.Attacker, 100), GridPosition.FrontCenter)
            .WithAttacker(TestHelpers.SimpleBattleUnit(new BattleUnitId(7), null, ally.Id, BattleSide.Attacker, 50), GridPosition.FrontBottom)
            .WithDefender(TestHelpers.SimpleBattleUnit(First, null, new UnitDefinitionId("giant"), BattleSide.Defender, 600), GridPosition.FrontCenter)
            .Build();

        bool Flees(DesignedBattleTactics tactics)
        {
            string id = "custom-battle:flee-" + Guid.NewGuid().ToString("N");
            AiBrainRegistry.SetDesignedBattleAis([new DesignedBattleAi(id, "Flee", [DesignedBattleStage.Classic with { Tactics = tactics }])]);

            return engine.DispatchCommand(new BattleAiActionCommand(false) { BrainId = id }, dryRun: true).Events.OfType<BattleUnitRetreatingEvent>().Any();
        }

        Assert.That(Flees(DesignedBattleTactics.Classic), Is.True, "control");
        Assert.That(Flees(DesignedBattleTactics.Classic with { NeverFlee = true }), Is.False);
        Assert.That(Flees(DesignedBattleTactics.Classic with { FleeBelowPercent = 1 }), Is.False);
    }

    [Test]
    public void StagesFollowTheirConditionsTest()
    {
        // Two stages: from round 3 the attack goes to the healer; before that Classic's choice.
        UnitDefinition healer = Unit("healer", 150) with
        {
            Attack = TestHelpers.SimpleAttack(new UnitAttackDefinitionId("heal"), 30, 0, 100, UnitAttackReach.Any) with { Type = UnitAttackType.Heal, Heal = 30 }
        };
        Defender[] defenders = [new Defender(Unit("weak", 70), 70, GridPosition.FrontBottom), new Defender(healer, 150, GridPosition.BackCenter)];
        DesignedBattleAi design = new("custom-battle:stages", "Stages",
        [
            new DesignedBattleStage("Late", [[new DesignedBattleCondition.FromRound(3)]], DesignedBattleTactics.Classic with { HealersFirst = 1000 }),
            DesignedBattleStage.Classic
        ]);
        AiBrainRegistry.SetDesignedBattleAis([design]);

        BattleUnitId? Target(int round)
        {
            BattleEngine engine = new TestBattleEngineBuilder()
                .WithRoundNumber(round)
                .WithDefinition(Strong("a")).WithDefinition(defenders[0].Definition).WithDefinition(healer)
                .WithAttacker(TestHelpers.SimpleBattleUnit(Attacker, null, new UnitDefinitionId("a"), BattleSide.Attacker, 200), GridPosition.FrontCenter)
                .WithDefender(TestHelpers.SimpleBattleUnit(First, null, new UnitDefinitionId("weak"), BattleSide.Defender, 70), GridPosition.FrontBottom)
                .WithDefender(TestHelpers.SimpleBattleUnit(Second, null, healer.Id, BattleSide.Defender, 150), GridPosition.BackCenter)
                .Build();

            return engine.DispatchCommand(new BattleAiActionCommand(false) { BrainId = design.Id }, dryRun: true).Events
                .OfType<BattleUnitAttackEvent>().Select(ev => ev.Target).OfType<BattleAttackUnitTarget>().Select(target => (BattleUnitId?)target.UnitId).FirstOrDefault();
        }

        Assert.That(Target(1), Is.EqualTo(First));
        Assert.That(Target(3), Is.EqualTo(Second));
    }

    [Test]
    public void ConditionsTest()
    {
        UnitDefinition leader = Strong("lead") with { Category = UnitCategory.Leader, LeaderCategory = UnitLeaderCategory.Fighter, Leadership = 2 };
        TestBattleEngineBuilder builder = new TestBattleEngineBuilder()
            .WithRoundNumber(4)
            .WithInfo(new BattleInfo(new BattleSideInfo(false) { CargoValue = 900, EquippedValue = 500, CarriesQuestItem = true }, new BattleSideInfo(false)))
            .WithDefinition(leader).WithDefinition(Unit("foe", 100, damage: 20))
            .WithAttacker(TestHelpers.SimpleBattleUnit(Attacker, null, leader.Id, BattleSide.Attacker, 50), GridPosition.FrontCenter)
            .WithDefender(TestHelpers.SimpleBattleUnit(First, null, new UnitDefinitionId("foe"), BattleSide.Defender, 100), GridPosition.FrontCenter);
        BattleEngine engine = builder.Build();
        IEngineContext context = builder.Context!;
        BattleState state = engine.State;

        bool True(BattleSide side, DesignedBattleCondition condition) => DesignedBattleAiBrain.IsTrue(context, state, side, condition);

        Assert.That(True(BattleSide.Attacker, new DesignedBattleCondition.FromRound(4)), Is.True);
        Assert.That(True(BattleSide.Attacker, new DesignedBattleCondition.FromRound(5)), Is.False);

        // The stronger attacker holds more than half the power; the two sides add up to 100 %.
        double share = DesignedBattleAiBrain.GetPowerSharePercent(context, state, BattleSide.Attacker);
        Assert.That(share, Is.GreaterThan(50).And.LessThan(100));
        Assert.That(DesignedBattleAiBrain.GetPowerSharePercent(context, state, BattleSide.Defender), Is.EqualTo(100 - share).Within(0.01));
        Assert.That(True(BattleSide.Attacker, new DesignedBattleCondition.PowerShareAbove((int)share - 1)), Is.True);
        Assert.That(True(BattleSide.Attacker, new DesignedBattleCondition.PowerShareAbove((int)share + 1)), Is.False);
        Assert.That(True(BattleSide.Attacker, new DesignedBattleCondition.PowerShareBelow((int)share + 1)), Is.True);
        Assert.That(True(BattleSide.Defender, new DesignedBattleCondition.PowerShareBelow(50)), Is.True);

        // Leader at 50 of 200 hit points = 25 %; the defender has no leader.
        Assert.That(True(BattleSide.Attacker, new DesignedBattleCondition.LeaderHitpointsBelow(30)), Is.True);
        Assert.That(True(BattleSide.Attacker, new DesignedBattleCondition.LeaderHitpointsBelow(20)), Is.False);
        Assert.That(True(BattleSide.Defender, new DesignedBattleCondition.LeaderHitpointsBelow(100)), Is.False);

        // Cargo 900 in the bag, 500 equipped.
        Assert.That(True(BattleSide.Attacker, new DesignedBattleCondition.CargoAbove(1000)), Is.False);
        Assert.That(True(BattleSide.Attacker, new DesignedBattleCondition.CargoAbove(1000, IncludeEquipped: true)), Is.True);
        Assert.That(True(BattleSide.Attacker, new DesignedBattleCondition.CarriesQuestItem()), Is.True);
        Assert.That(True(BattleSide.Defender, new DesignedBattleCondition.CarriesQuestItem()), Is.False);
    }

    [Test]
    public void BattleKnowsTheCargoTest()
    {
        GlobalDatabase globalDb = GlobalDatabaseLoader.Load(RiseOfTheElves.GlobalDatabasePath);
        CampaignDatabase campaignDb = CampaignDatabaseLoader.Load(RiseOfTheElves.CampaignDatabasePath);
        AiBattleBench bench = AiBattleBench.CreateWithGeneratedHost(globalDb, campaignDb);
        BattleArmy army = new("Rich", [new BattleArmyUnit(new UnitDefinitionId("g000uu0019"), 1, GridPosition.FrontCenter)])
        {
            Cargo = [new ItemDefinitionId("G000IG7001"), new ItemDefinitionId("G000IG7003")]
        };
        BattleArmy poor = army with { Cargo = [] };

        BattleState rich = bench.CreateBattle(army, poor);
        int expected = new[] { "G000IG7001", "G000IG7003" }.Sum(id => bench.Definitions.GetItem(new ItemDefinitionId(id)).Value.Gold);

        Assert.That(expected, Is.GreaterThan(0));
        Assert.That(rich.Info.Attacker.CargoValue, Is.EqualTo(expected));
        Assert.That(rich.Info.Defender.CargoValue, Is.Zero);
        Assert.That(rich.Info.Attacker.CarriesQuestItem, Is.False);
    }

    [Test]
    public void ClassicCopyPlaysLikeClassicTest()
    {
        GlobalDatabase globalDb = GlobalDatabaseLoader.Load(RiseOfTheElves.GlobalDatabasePath);
        CampaignDatabase campaignDb = CampaignDatabaseLoader.Load(RiseOfTheElves.CampaignDatabasePath);
        AiBattleBench bench = AiBattleBench.CreateWithGeneratedHost(globalDb, campaignDb);
        AiBrainRegistry.SetDesignedBattleAis([DesignedBattleAi.FromClassic("copy", "Copy")]);
        BattleArmy empire = new("Empire", [
            new BattleArmyUnit(new UnitDefinitionId("g000uu0019"), 3, GridPosition.BackCenter),
            new BattleArmyUnit(new UnitDefinitionId("g000uu0001"), 1, GridPosition.FrontBottom),
            new BattleArmyUnit(new UnitDefinitionId("g000uu0011"), 1, GridPosition.BackTop)]);
        BattleArmy undead = new("Undead", [
            new BattleArmyUnit(new UnitDefinitionId("g000uu0096"), 2, GridPosition.BackCenter),
            new BattleArmyUnit(new UnitDefinitionId("g000uu0086"), 1, GridPosition.FrontBottom),
            new BattleArmyUnit(new UnitDefinitionId("g000uu0080"), 1, GridPosition.BackTop)]);

        string Run(string ai) => string.Join(";", bench.Run(new BattleBenchSide(empire, ai), new BattleBenchSide(undead, ai), 60, 1, swapSides: true)
            .Runs.Select(run => $"{run.Winner}/{run.Rounds}/{run.A.Hitpoints}/{run.B.Hitpoints}"));

        Assert.That(Run("custom-battle:copy"), Is.EqualTo(Run(AiBrainRegistry.ClassicId)));
    }

    [Test]
    public void PersonalityCopiesKeepTheirKnobsTest()
    {
        DesignedBattleAi ruthless = DesignedBattleAi.FromBuiltIn("ruthless", "r", "R");
        DesignedBattleAi cautious = DesignedBattleAi.FromBuiltIn("cautious", "c", "C");

        Assert.That(ruthless.Stages.Single().Tactics, Is.EqualTo(DesignedBattleTactics.Classic with { NeverFlee = true, CanDefend = false }));
        Assert.That(cautious.Stages.Single().Tactics.FleeBelowPercent, Is.EqualTo(40));
        Assert.That(DesignedBattleTactics.Classic.ToProfile(), Is.EqualTo(BattleAiProfile.Classic));
    }

    [Test]
    public void MissingDesignPlaysClassicTest()
    {
        AiBrainRegistry.SetDesignedBattleAis([]);

        Assert.That(AiBrainRegistry.ResolveBattle("custom-battle:gone"), Is.SameAs(ClassicBattleAiBrain.Instance));
        AiBrainRegistry.SetDesignedBattleAis([DesignedBattleAi.FromClassic("here", "Here")]);
        Assert.That(AiBrainRegistry.BattleBrains.Select(info => info.Id), Does.Contain("custom-battle:here"));
        Assert.That(AiBrainRegistry.ResolveBattle("custom-battle:here"), Is.InstanceOf<DesignedBattleAiBrain>());
    }

    [Test]
    public void StoreRoundTripTest()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gallean-battle-ais-" + Guid.NewGuid().ToString("N"));

        try
        {
            DesignedBattleAi design = new("custom-battle:bold", "Bold",
            [
                new DesignedBattleStage("Rich and losing",
                    [[new DesignedBattleCondition.CargoAbove(1000, true), new DesignedBattleCondition.PowerShareBelow(50)], [new DesignedBattleCondition.CarriesQuestItem()]],
                    DesignedBattleTactics.Classic with { ProtectLeader = true, FleeBelowPercent = 60 }),
                new DesignedBattleStage("Late", [[new DesignedBattleCondition.FromRound(3), new DesignedBattleCondition.LeaderHitpointsBelow(40), new DesignedBattleCondition.PowerShareAbove(65)]],
                    DesignedBattleTactics.Classic with { WoundedFirst = 100, FocusFire = 50, NeverFlee = true, CanDefend = false, LeaderDefendsBelowPercent = 35 }),
                DesignedBattleStage.Classic with { Tactics = DesignedBattleTactics.Classic with { HealersFirst = 100, CastersFirst = 100, LeaderFirst = 100, HardestHitterFirst = 100 } }
            ]);
            DesignedBattleAiStore store = new(directory);

            store.Save(design);
            File.WriteAllText(Path.Combine(directory, "broken.json"), "{ nope");

            DesignedBattleAi back = store.LoadAll().Single();
            Assert.That(back, Is.EqualTo(design));

            store.Delete(design.Id);
            Assert.That(store.LoadAll(), Is.Empty);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public void StoreSkipsUnknownConditionsTest()
    {
        byte[] json = System.Text.Encoding.UTF8.GetBytes("""{ "name": "N", "stages": [ { "name": "S", "when": [ [ { "kind": "moonIsFull" } ], [ { "kind": "fromRound", "round": 2 } ] ], "tactics": { "leaderFirst": 100 } } ] }""");

        DesignedBattleAi design = DesignedBattleAiStore.Read("n", json);

        Assert.That(design.Stages.Single().When.Single().Single(), Is.EqualTo(new DesignedBattleCondition.FromRound(2)));
        Assert.That(design.Stages.Single().Tactics, Is.EqualTo(DesignedBattleTactics.Classic with { LeaderFirst = 100 }));
    }
}

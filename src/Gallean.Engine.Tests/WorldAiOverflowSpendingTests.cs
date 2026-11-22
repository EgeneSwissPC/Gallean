namespace Gallean.Engine.Tests;

// Gallean Upgrade: overflow spending (docs/ai-overflow-spending.md, phase 2). Every test runs Classic as the
// control (it does not spend here) and Classic+ (it does).
public sealed class WorldAiOverflowSpendingTests
{
    private static readonly WorldSpellDefinitionId AttackSpellId = new("attack");
    private static readonly WorldSpellDefinitionId HealSpellId = new("heal");
    private static readonly WorldPlayerBuildingDefinitionId MageGuildId = new("MageGuild");

    private static WorldResourceSet Death(int amount) => new(Gold: 0, DeathMana: amount, RuneMana: 0, LifeMana: 0, InfernalMana: 0, GroveMana: 0);

    private static UnitDefinition Leader()
    {
        return TestHelpers.SimpleLeaderDefinition(
            TestHelpers.SimpleUnitDefinition("leader", maxHitpoints: 100, initiative: 50, damage: 10),
            movementPoints: 6, leadership: 6);
    }

    private static WorldStack Stack(string id, WorldPlayerId ownerId, UnitDefinition leader, WorldPosition position, int hitpoints = 100)
    {
        return TestHelpers.SimpleStack(
            new WorldStackId(id),
            ownerId,
            position,
            new UnitGrid<WorldUnit>.Builder()
                .Place(TestHelpers.SimpleUnit(new WorldUnitId(id + "Leader"), leader) with { Hitpoints = hitpoints }, GridPosition.BackBottom, false)
                .Build(),
            movementPoints: 6);
    }

    // An Undead AI with an own army at (0, 0), a human player without armies and a neutral army far away at (10, 10).
    private static WorldEngine Build(WorldPlayer ai, int ownHitpoints = 100, params WorldSpellDefinition[] spells)
    {
        WorldMap map = TestHelpers.SimpleMap(12, 12);
        UnitDefinition leader = Leader();
        WorldPlayer human = TestHelpers.SimplePlayer(new WorldPlayerId(2), WorldRace.Heretic, map);
        WorldPlayer neutral = TestHelpers.SimplePlayer(new WorldPlayerId(3), WorldRace.Neutral, map) with { IsAi = true };

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(leader)
            .WithDefinition(TestHelpers.SimpleLord(ai.LordId, ai.Race, LordCategory.Warrior))
            .WithPlayer(ai)
            .WithDefinition(TestHelpers.SimpleLord(human.LordId, human.Race, LordCategory.Warrior))
            .WithPlayer(human)
            .WithDefinition(TestHelpers.SimpleLord(neutral.LordId, neutral.Race, LordCategory.Warrior))
            .WithPlayer(neutral)
            .WithDefinition(TestHelpers.SimpleBuildingDefinition(MageGuildId, WorldPlayerBuildingCategory.Spell))
            .WithResearchableSpell(new WorldSpellResearchCost(WorldRace.Undead, AttackSpellId, Death(200)))
            .WithStack(Stack("Own", ai.Id, leader, new WorldPosition(0, 0), ownHitpoints))
            .WithStack(Stack("Neutral", neutral.Id, leader, new WorldPosition(10, 10)));

        foreach (WorldSpellDefinition spell in spells)
        {
            builder = builder.WithDefinition(spell);
        }

        return builder.Build();
    }

    private static WorldSpellDefinition AttackSpell() =>
        TestHelpers.SimpleSpellDefinition(AttackSpellId, new WorldAttackSpellEffect(UnitAttackSource.Fire, 20)) with { CastingCost = Death(100) };

    private static WorldSpellDefinition HealSpell() =>
        TestHelpers.SimpleSpellDefinition(HealSpellId, new WorldHealSpellEffect(50)) with { CastingCost = Death(100) };

    private static WorldPlayer Ai(int deathMana, WorldPlayerSpellbook spellbook)
    {
        WorldMap map = TestHelpers.SimpleMap(12, 12);

        return TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Undead, map) with
        {
            IsAi = true,
            Resources = Death(deathMana),
            Buildings = new WorldPlayerBuildings([MageGuildId], [MageGuildId]),
            Spellbook = spellbook
        };
    }

    private static IReadOnlyList<WorldEvent> FirstAction(WorldEngine engine, string brainId)
    {
        WorldCommandResult result = engine.DispatchCommand(new WorldAiActionCommand { BrainId = brainId });
        Assert.That(result.Error, Is.Null);

        return result.Events;
    }

    [Test]
    public void AttackSpellOnFarArmyTest()
    {
        WorldPlayer ai = Ai(9000, new WorldPlayerSpellbook([], [AttackSpellId]));

        Assert.That(FirstAction(Build(ai, spells: AttackSpell()), AiBrainRegistry.ClassicId).OfType<WorldSpellCastEvent>(), Is.Empty,
            "Classic only attacks with spells near its cities (neutrals never by chance)");

        WorldSpellCastEvent[] casts = [.. FirstAction(Build(ai, spells: AttackSpell()), AiPersonalities.ClassicPlusId).OfType<WorldSpellCastEvent>()];
        Assert.That(casts.Select(cast => (cast.SpellId, cast.Target?.Center)), Is.EqualTo(new[] { (AttackSpellId, (WorldPosition?)new WorldPosition(10, 10)) }));
    }

    [Test]
    public void BelowThresholdNoSpendingTest()
    {
        // Below 70 % of 9999 (6999): Classic+ plays like Classic.
        WorldPlayer ai = Ai(6998, new WorldPlayerSpellbook([], [AttackSpellId]));

        Assert.That(FirstAction(Build(ai, spells: AttackSpell()), AiPersonalities.ClassicPlusId).OfType<WorldSpellCastEvent>(), Is.Empty);
    }

    [Test]
    public void HealLightlyWoundedArmyTest()
    {
        // 80 HP of 100: Classic heals only armies at or below half health.
        WorldPlayer ai = Ai(9000, new WorldPlayerSpellbook([], [HealSpellId]));

        Assert.That(FirstAction(Build(ai, 80, HealSpell()), AiBrainRegistry.ClassicId).OfType<WorldSpellCastEvent>(), Is.Empty);
        Assert.That(FirstAction(Build(ai, 80, HealSpell()), AiPersonalities.ClassicPlusId).OfType<WorldSpellCastEvent>().Select(cast => cast.SpellId),
            Is.EqualTo(new[] { HealSpellId }));
    }

    [Test]
    public void ResearchOffTurnTest()
    {
        // Turn 1 is not a research turn for Classic (every 5th turn only).
        WorldPlayer ai = Ai(9000, new WorldPlayerSpellbook([AttackSpellId], []));

        Assert.That(FirstAction(Build(ai, spells: AttackSpell()), AiBrainRegistry.ClassicId).OfType<WorldSpellResearchedEvent>(), Is.Empty);
        Assert.That(FirstAction(Build(ai, spells: AttackSpell()), AiPersonalities.ClassicPlusId).OfType<WorldSpellResearchedEvent>().Select(ev => ev.SpellId),
            Is.EqualTo(new[] { AttackSpellId }));
    }

    [Test]
    public void ProfilesTest()
    {
        Assert.That(WorldAiProfile.Classic.SpendAbovePercent, Is.Zero);

        foreach (string id in new[] { AiPersonalities.ClassicPlusId, AiPersonalities.AggressiveId, AiPersonalities.BuilderId, AiPersonalities.ExpansionistId })
        {
            Assert.That(((ProfileWorldAiBrain)AiBrainRegistry.ResolveWorld(id)).Profile.SpendAbovePercent, Is.EqualTo(WorldAiSpending.DefaultSpendAbovePercent), id);
        }

        DesignedWorldStage stage = DesignedWorldAi.FromClassic("custom:test", "Test").Stages[0];
        Assert.That(DesignedStageEvaluator.ToProfile(stage).SpendAbovePercent, Is.EqualTo(WorldAiSpending.DefaultSpendAbovePercent));
    }

    // Phase 2b: a rod planter between a near gold mine and a farther rune source. The race's cheapest level 3
    // spell needs rune mana it lacks: Classic+ goes for the rune source (also when not rich), Classic for the mine.
    private static (GameSession Session, IEngineContext Context, WorldPlayer Player) RodGame(int runeX = 9, bool withMine = true)
    {
        WorldMap map = TestHelpers.SimpleMap(Math.Max(12, runeX + 3), 3);
        WorldSpellDefinitionId levelThreeId = new("levelThree");
        WorldSpellDefinition levelThree = TestHelpers.SimpleSpellDefinition(levelThreeId, new WorldAttackSpellEffect(UnitAttackSource.Fire, 20)) with { Level = 3 };

        UnitDefinition planterDefinition = TestHelpers.SimpleLeaderDefinition(
                TestHelpers.SimpleUnitDefinition("planter", maxHitpoints: 100, initiative: 50, damage: 10),
                movementPoints: 6, leadership: 2) with
            {
                LeaderCategory = UnitLeaderCategory.Rod,
                LeaderAbilities = LeaderAbility.Rod
            };

        WorldPlayer player = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Undead, map) with
        {
            IsAi = true,
            Resources = new WorldResourceSet(Gold: 150, DeathMana: 2000, RuneMana: 0, LifeMana: 0, InfernalMana: 0, GroveMana: 0),
            Spellbook = new WorldPlayerSpellbook([levelThreeId], [])
        };

        WorldStack planter = TestHelpers.SimpleStack(
            new WorldStackId("Planter"),
            player.Id,
            new WorldPosition(5, 1),
            new UnitGrid<WorldUnit>.Builder().Place(TestHelpers.SimpleUnit(new WorldUnitId("planter"), planterDefinition), GridPosition.BackBottom, false).Build(),
            movementPoints: 6);

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(planterDefinition)
            .WithDefinition(levelThree)
            .WithResearchableSpell(new WorldSpellResearchCost(WorldRace.Undead, levelThreeId,
                new WorldResourceSet(Gold: 0, DeathMana: 450, RuneMana: 450, LifeMana: 0, InfernalMana: 0, GroveMana: 0)))
            .WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, LordCategory.Warrior))
            .WithPlayer(player)
            .WithStack(planter)
            .WithResourceNode(new WorldResourceNode(new WorldResourceNodeId("rune"), new WorldPosition(runeX, 1), WorldResourceType.RuneMana, AiPriority: 3));

        if (withMine)
        {
            builder = builder.WithResourceNode(new WorldResourceNode(new WorldResourceNodeId("mine"), new WorldPosition(2, 1), WorldResourceType.Gold, AiPriority: 3));
        }

        (GameSession session, IEngineContext context) = builder.BuildSession();

        return (session, context, player);
    }

    private static int? FirstTargetX(IWorldAiBrain brain, int runeX = 9, bool withMine = true)
    {
        (GameSession session, IEngineContext context, _) = RodGame(runeX, withMine);
        WorldAiActionPlan plan = brain.DecideAction(context, session.World.State, WorldAiMemory.Empty);

        return plan.Command switch
        {
            WorldAiMoveStackCommand move => move.TargetPosition.X,
            WorldCreateRodCommand create => create.TargetPosition.X,
            _ => null
        };
    }

    // A needed rune source 70 tiles away (MP 6, so beyond Classic's 10 turns of 6 MP): only Classic+ walks there.
    [Test]
    public void RodPlanterWalksFarForNeededManaTest()
    {
        Assert.That(FirstTargetX(ClassicWorldAiBrain.Instance, runeX: 75, withMine: false), Is.Null.Or.LessThanOrEqualTo(5), "Classic: too far");
        Assert.That(FirstTargetX(AiBrainRegistry.ResolveWorld(AiPersonalities.ClassicPlusId), runeX: 75, withMine: false), Is.GreaterThan(5), "Classic+: needed, within 30 turns");
    }

    [Test]
    public void RodPlanterGoesForNeededManaTest()
    {
        Assert.That(FirstTargetX(ClassicWorldAiBrain.Instance), Is.LessThan(5), "Classic: the nearer gold mine");
        Assert.That(FirstTargetX(AiBrainRegistry.ResolveWorld(AiPersonalities.ClassicPlusId)), Is.GreaterThan(5), "Classic+: the rune source it needs");
    }

    [Test]
    public void NeededManaTest()
    {
        (GameSession session, IEngineContext context, WorldPlayer player) = RodGame();
        WorldPlayer current = session.World.State.Players[player.Id];
        WorldAiProfile ours = WorldAiProfile.Classic with { SpendAbovePercent = WorldAiSpending.DefaultSpendAbovePercent };

        Assert.That(WorldAiSpending.GetNeededMana(WorldAiProfile.Classic, current, context), Is.Empty);
        Assert.That(WorldAiSpending.GetNeededMana(ours, current, context), Is.EquivalentTo(new[] { WorldResourceType.RuneMana }));
        Assert.That(WorldAiSpending.GetNeededMana(ours, current with { Resources = current.Resources with { RuneMana = 450 } }, context), Is.Empty);
    }
}

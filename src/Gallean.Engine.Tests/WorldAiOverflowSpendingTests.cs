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

    private static WorldEngine BuildOnTurn(WorldPlayer ai, int turn, params WorldSpellDefinition[] spells)
    {
        WorldMap map = TestHelpers.SimpleMap(12, 12);
        UnitDefinition leader = Leader();
        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithTurnInfo(new WorldTurnInfo(turn, ai.Id))
            .WithDefinition(leader)
            .WithDefinition(TestHelpers.SimpleLord(ai.LordId, ai.Race, LordCategory.Warrior))
            .WithPlayer(ai)
            .WithDefinition(TestHelpers.SimpleLord(new LordDefinitionId("Lord2"), WorldRace.Heretic, LordCategory.Warrior))
            .WithPlayer(TestHelpers.SimplePlayer(new WorldPlayerId(2), WorldRace.Heretic, map))
            .WithDefinition(TestHelpers.SimpleBuildingDefinition(MageGuildId, WorldPlayerBuildingCategory.Spell))
            .WithResearchableSpell(new WorldSpellResearchCost(WorldRace.Undead, AttackSpellId, Death(200)))
            .WithStack(Stack("Own", ai.Id, leader, new WorldPosition(0, 0)));

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

    // docs/ai-unused-features.md phase 1: summon upkeep awareness on large maps. Income 300 death mana, limit 50 %.
    private static (GameSession Session, IEngineContext Context, WorldPlayer Player) SummonGame(int runningUpkeep, int deathMana = 9000)
    {
        WorldMap map = TestHelpers.SimpleMap(LargeMapRules.MinMapSize, LargeMapRules.MinMapSize);
        WorldSpellDefinitionId summonId = new("summon");
        UnitDefinition leader = Leader();
        UnitDefinition summoned = TestHelpers.SimpleUnitDefinition("summoned", maxHitpoints: 50, initiative: 40, damage: 10) with
        {
            Category = UnitCategory.Summon,
            Leadership = 1,
            MovementPoints = 20,
            Lifetime = 3
        };
        WorldPlayer ai = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Undead, map) with
        {
            IsAi = true,
            Resources = Death(deathMana),
            Spellbook = new WorldPlayerSpellbook([], [summonId])
        };
        WorldPlayer neutral = TestHelpers.SimplePlayer(new WorldPlayerId(3), WorldRace.Neutral, map) with { IsAi = true };

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(leader)
            .WithDefinition(summoned)
            .WithDefinition(TestHelpers.SimpleSpellDefinition(summonId, new WorldSummonStackSpellEffect(summoned.Id)) with { CastingCost = Death(100) })
            .WithRace(TestHelpers.SimpleRace(WorldRace.Undead, new GlobalTextId("undead"), WorldMapTerrain.Neutral) with { Income = Death(300) })
            .WithDefinition(TestHelpers.SimpleLord(ai.LordId, ai.Race, LordCategory.Warrior))
            .WithPlayer(ai)
            .WithDefinition(TestHelpers.SimpleLord(neutral.LordId, neutral.Race, LordCategory.Warrior))
            .WithPlayer(neutral)
            .WithStack(Stack("Own", ai.Id, leader, new WorldPosition(0, 0)))
            .WithStack(Stack("Neutral", neutral.Id, leader, new WorldPosition(10, 10)))
            .WithSubrace(TestHelpers.NeutralSubrace);

        if (runningUpkeep > 0)
        {
            builder = builder.WithStack(Stack("Summoned", ai.Id, leader, new WorldPosition(30, 30)) with { SummonUpkeep = Death(runningUpkeep) });
        }

        (GameSession session, IEngineContext context) = builder.BuildSession();

        return (session, context, ai);
    }

    // Income 300 death mana. Below the spending line the limit is 50 % (150), at or above it 100 % (300).
    [TestCase(0, 2000, true)]
    [TestCase(100, 2000, false)]
    [TestCase(100, 9000, true)]
    [TestCase(250, 9000, false)]
    public void SummonUpkeepLimitTest(int runningUpkeep, int deathMana, bool summons)
    {
        (GameSession session, IEngineContext context, WorldPlayer ai) = SummonGame(runningUpkeep, deathMana);
        WorldState state = session.World.State;
        WorldPlayer player = state.Players[ai.Id];
        WorldAiProfile ours = AiBrainRegistry.ResolveWorld(AiPersonalities.ClassicPlusId) is ProfileWorldAiBrain brain ? brain.Profile : throw new AssertionException("Classic+");

        Assert.That(WorldAiSpending.CanAffordSummonUpkeep(ours, state, player, Death(100), context), Is.EqualTo(summons));
        Assert.That(WorldAiSpending.CanAffordSummonUpkeep(WorldAiProfile.Classic, state, player, Death(100), context), Is.True, "Classic: no check");

        // Only a rich AI casts here (spending mode reaches the far neutral army), so check the AI's choice at 9000.
        if (deathMana >= 7000)
        {
            WorldAiActionPlan plan = AiBrainRegistry.ResolveWorld(AiPersonalities.ClassicPlusId).DecideAction(context, state, WorldAiMemory.Empty);
            Assert.That(plan.Command is WorldCastSpellCommand, Is.EqualTo(summons), plan.Command.ToString());
        }
    }

    // docs/ai-unused-features.md phase 2: an attack spell marked "not for the AI" (AI_CAT None) on a research turn
    // (turn 5) without spending mode: Classic skips it, our AIs research it.
    [Test]
    public void ResearchNotForAiSpellTest()
    {
        WorldPlayer ai = Ai(2000, new WorldPlayerSpellbook([AttackSpellId], []));
        WorldSpellDefinition notForAi = AttackSpell() with { AiType = WorldAiSpellType.None };

        IReadOnlyList<WorldEvent> Research(string brainId)
        {
            WorldEngine engine = BuildOnTurn(ai, 5, notForAi);
            return FirstAction(engine, brainId);
        }

        Assert.That(Research(AiBrainRegistry.ClassicId).OfType<WorldSpellResearchedEvent>(), Is.Empty, "Classic skips AI_CAT None");
        Assert.That(Research(AiPersonalities.ClassicPlusId).OfType<WorldSpellResearchedEvent>().Select(ev => ev.SpellId), Is.EqualTo(new[] { AttackSpellId }));
    }

    // docs/ai-unused-features.md phase 3: area spells (radius 1), 2000 mana so only the two-army rule applies.
    private static IReadOnlyList<WorldEvent> AreaFirstAction(string brainId, WorldSpellDefinition spell, params (string Id, int Owner, WorldPosition Position, int Hitpoints)[] stacks)
    {
        WorldMap map = TestHelpers.SimpleMap(12, 12);
        UnitDefinition leader = Leader();
        WorldPlayer ai = Ai(2000, new WorldPlayerSpellbook([], [spell.Id]));
        WorldPlayer human = TestHelpers.SimplePlayer(new WorldPlayerId(2), WorldRace.Heretic, map);
        WorldPlayer neutral = TestHelpers.SimplePlayer(new WorldPlayerId(3), WorldRace.Neutral, map) with { IsAi = true };

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(leader)
            .WithDefinition(spell)
            .WithDefinition(TestHelpers.SimpleBuildingDefinition(MageGuildId, WorldPlayerBuildingCategory.Spell))
            .WithDefinition(TestHelpers.SimpleLord(ai.LordId, ai.Race, LordCategory.Warrior))
            .WithPlayer(ai)
            .WithDefinition(TestHelpers.SimpleLord(human.LordId, human.Race, LordCategory.Warrior))
            .WithPlayer(human)
            .WithDefinition(TestHelpers.SimpleLord(neutral.LordId, neutral.Race, LordCategory.Warrior))
            .WithPlayer(neutral);

        foreach ((string id, int owner, WorldPosition position, int hitpoints) in stacks)
        {
            builder = builder.WithStack(Stack(id, new WorldPlayerId(owner), leader, position, hitpoints));
        }

        return FirstAction(builder.Build(), brainId);
    }

    private static WorldSpellDefinition AreaAttack() => AttackSpell() with { AreaRadius = 1 };

    [Test]
    public void AreaAttackOnTwoArmiesTest()
    {
        (string, int, WorldPosition, int)[] stacks = [("Own", 1, new WorldPosition(0, 0), 100), ("N1", 3, new WorldPosition(10, 10), 100), ("N2", 3, new WorldPosition(11, 10), 100)];

        Assert.That(AreaFirstAction(AiBrainRegistry.ClassicId, AreaAttack(), stacks).OfType<WorldSpellCastEvent>(), Is.Empty, "Classic never casts area spells");
        WorldSpellCastEvent[] casts = [.. AreaFirstAction(AiPersonalities.ClassicPlusId, AreaAttack(), stacks).OfType<WorldSpellCastEvent>()];
        Assert.That(casts.Select(cast => cast.Target!.Center), Is.EqualTo(new[] { new WorldPosition(10, 10) }).Or.EqualTo(new[] { new WorldPosition(11, 10) }));
    }

    [Test]
    public void AreaAttackSparesOwnArmiesTest()
    {
        // The own army next to both neutrals would be hit: no cast. One neutral alone is below the two-army rule.
        Assert.That(AreaFirstAction(AiPersonalities.ClassicPlusId, AreaAttack(),
            ("Own", 1, new WorldPosition(10, 11), 100), ("N1", 3, new WorldPosition(10, 10), 100), ("N2", 3, new WorldPosition(11, 10), 100)).OfType<WorldSpellCastEvent>(), Is.Empty);
        Assert.That(AreaFirstAction(AiPersonalities.ClassicPlusId, AreaAttack(),
            ("Own", 1, new WorldPosition(0, 0), 100), ("N1", 3, new WorldPosition(10, 10), 100)).OfType<WorldSpellCastEvent>(), Is.Empty);
    }

    [Test]
    public void AreaHealOnTwoWoundedArmiesTest()
    {
        WorldSpellDefinition areaHeal = HealSpell() with { AreaRadius = 1 };
        (string, int, WorldPosition, int)[] stacks = [("A", 1, new WorldPosition(5, 5), 80), ("B", 1, new WorldPosition(6, 5), 80), ("N1", 3, new WorldPosition(11, 11), 100)];

        Assert.That(AreaFirstAction(AiBrainRegistry.ClassicId, areaHeal, stacks).OfType<WorldSpellCastEvent>(), Is.Empty);
        Assert.That(AreaFirstAction(AiPersonalities.ClassicPlusId, areaHeal, stacks).OfType<WorldSpellCastEvent>().Select(cast => cast.SpellId), Is.EqualTo(new[] { HealSpellId }));
    }
}

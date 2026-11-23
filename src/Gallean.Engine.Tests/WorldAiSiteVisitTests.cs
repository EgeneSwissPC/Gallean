namespace Gallean.Engine.Tests;

// Gallean Upgrade: docs/ai-unused-features.md phase 4. Fighting armies of our AIs visit sites and buy what they can
// use; Classic is the control in every test (it never does).
public sealed class WorldAiSiteVisitTests
{
    private static readonly WorldFootprint SiteFootprint = new(new WorldPosition(2, 2), 3, 3);
    private static readonly WorldPosition Entrance = new(4, 4);

    private static readonly UnitDefinition LeaderDefinition = TestHelpers.SimpleLeaderDefinition(
            TestHelpers.SimpleUnitDefinition("leader", maxHitpoints: 100, initiative: 50, damage: 10),
            movementPoints: 6, leadership: 4) with
        {
            TrainingCost = new WorldResourceSet(5, 0, 0, 0, 0, 0)
        };

    private static readonly UnitDefinition MercenaryDefinition = TestHelpers.SimpleUnitDefinition("merc", maxHitpoints: 80, initiative: 40, damage: 15) with
    {
        PurchaseCost = new WorldResourceSet(200, 0, 0, 0, 0, 0)
    };

    private static readonly ItemDefinition Potion = TestHelpers.SimpleItemDefinition(new ItemDefinitionId("potion"), ItemCategory.PotionHeal) with
    {
        Value = new WorldResourceSet(50, 0, 0, 0, 0, 0),
        Effect = new HealItemEffect(50)
    };

    private static readonly WorldSpellDefinition SiteSpell = TestHelpers.SimpleSpellDefinition(new WorldSpellDefinitionId("siteSpell"), new WorldAttackSpellEffect(UnitAttackSource.Fire, 20)) with
    {
        PurchaseCost = new WorldResourceSet(300, 0, 0, 0, 0, 0)
    };

    private static WorldSite Mercenaries() => new WorldMercenarySite(new WorldSiteId("site"), SiteFootprint, new WorldObjectVariant(0),
        new ScenarioTextId("Title"), new ScenarioTextId("Greeting"), [new WorldMercenaryUnit(MercenaryDefinition.Id, 1, false)], [], 3);

    private static WorldSite Merchant() => new WorldMerchantSite(new WorldSiteId("site"), SiteFootprint, new WorldObjectVariant(0),
        new ScenarioTextId("Title"), new ScenarioTextId("Greeting"), [new WorldMerchantItem(Potion.Id, 5)], [], 3);

    private static WorldSite Mage() => new WorldMageSite(new WorldSiteId("site"), SiteFootprint, new WorldObjectVariant(0),
        new ScenarioTextId("Title"), new ScenarioTextId("Greeting"), [SiteSpell.Id], [], 3);

    private static WorldSite Trainer() => new WorldTrainerSite(new WorldSiteId("site"), SiteFootprint, new WorldObjectVariant(0),
        new ScenarioTextId("Title"), new ScenarioTextId("Greeting"), [], 3);

    private static WorldAiActionPlan FirstPlan(string brainId, WorldSite site, WorldPosition position, int gold, int mapSize = 14, WorldRace race = WorldRace.Undead)
    {
        WorldMap map = TestHelpers.SimpleMap(mapSize, mapSize);
        WorldPlayer ai = TestHelpers.SimplePlayer(new WorldPlayerId(1), race, map) with
        {
            IsAi = true,
            Resources = new WorldResourceSet(gold, 0, 0, 0, 0, 0)
        };
        WorldPlayer human = TestHelpers.SimplePlayer(new WorldPlayerId(2), WorldRace.Heretic, map);
        WorldStack stack = TestHelpers.SimpleStack(
            new WorldStackId("Army"),
            ai.Id,
            position,
            new UnitGrid<WorldUnit>.Builder().Place(TestHelpers.SimpleUnit(new WorldUnitId("leaderUnit"), LeaderDefinition), GridPosition.BackBottom, false).Build(),
            movementPoints: 6);

        (GameSession session, IEngineContext context) = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(LeaderDefinition)
            .WithDefinition(MercenaryDefinition)
            .WithDefinition(Potion)
            .WithDefinition(SiteSpell)
            .WithRace(TestHelpers.SimpleRace(WorldRace.Undead, new GlobalTextId("Undead Hordes"), WorldMapTerrain.Undead))
            .WithDefinition(TestHelpers.SimpleLord(ai.LordId, ai.Race, LordCategory.Warrior))
            .WithPlayer(ai)
            .WithDefinition(TestHelpers.SimpleLord(human.LordId, human.Race, LordCategory.Warrior))
            .WithPlayer(human)
            .WithSubrace(TestHelpers.NeutralSubrace)
            .WithStack(stack)
            .WithSite(site)
            .BuildSession();

        return AiBrainRegistry.ResolveWorld(brainId).DecideAction(context, session.World.State, WorldAiMemory.Empty);
    }

    [Test]
    public void MercenaryAtSiteTest()
    {
        Assert.That(FirstPlan(AiBrainRegistry.ClassicId, Mercenaries(), Entrance, 2000).Command, Is.Not.InstanceOf<WorldPurchaseMercenaryCommand>());
        Assert.That(FirstPlan(AiPersonalities.ClassicPlusId, Mercenaries(), Entrance, 2000).Command, Is.InstanceOf<WorldPurchaseMercenaryCommand>());
    }

    // Upstream 0.17 (651e4200): a tile next to the entrance counts as the site, so our AIs shop from there.
    [Test]
    public void MercenaryNextToEntranceTest()
    {
        WorldPosition next = new(Entrance.X + 1, Entrance.Y + 1);

        Assert.That(FirstPlan(AiBrainRegistry.ClassicId, Mercenaries(), next, 2000).Command, Is.Not.InstanceOf<WorldPurchaseMercenaryCommand>());
        Assert.That(FirstPlan(AiPersonalities.ClassicPlusId, Mercenaries(), next, 2000).Command, Is.InstanceOf<WorldPurchaseMercenaryCommand>());
    }

    [Test]
    public void TrainerAtSiteTest()
    {
        // Training the leader to the next level costs 999 x 5 = 4995 gold: with 6000 it keeps 500, with 2000 it can't.
        Assert.That(FirstPlan(AiBrainRegistry.ClassicId, Trainer(), Entrance, 6000).Command, Is.Not.InstanceOf<WorldTrainUnitCommand>());
        Assert.That(FirstPlan(AiPersonalities.ClassicPlusId, Trainer(), Entrance, 6000).Command, Is.InstanceOf<WorldTrainUnitCommand>());
        Assert.That(FirstPlan(AiPersonalities.ClassicPlusId, Trainer(), Entrance, 2000).Command, Is.Not.InstanceOf<WorldTrainUnitCommand>());
    }

    [Test]
    public void MageTowerAtSiteTest()
    {
        Assert.That(FirstPlan(AiBrainRegistry.ClassicId, Mage(), Entrance, 2000).Command, Is.Not.InstanceOf<WorldPurchaseSpellCommand>());
        Assert.That(FirstPlan(AiPersonalities.ClassicPlusId, Mage(), Entrance, 2000).Command, Is.InstanceOf<WorldPurchaseSpellCommand>());
    }

    [Test]
    public void WalksToMerchantTest()
    {
        // 6 tiles away: Classic+ heads for the entrance, Classic doesn't.
        WorldPosition start = new(10, 10);

        static int Distance(WorldAiActionPlan plan) => plan.Command is WorldAiMoveStackCommand move ? move.TargetPosition.GetDistance(Entrance) : int.MaxValue;

        Assert.That(Distance(FirstPlan(AiBrainRegistry.ClassicId, Merchant(), start, 2000)), Is.GreaterThanOrEqualTo(start.GetDistance(Entrance)));
        Assert.That(Distance(FirstPlan(AiPersonalities.ClassicPlusId, Merchant(), start, 2000)), Is.LessThan(start.GetDistance(Entrance)));
    }

    // Mercenary camps: 4 turns of walking when poor, growing to 20 as gold nears the cap. 40 tiles at 6 MP a turn
    // is beyond 4 turns (2000 gold) and beyond the 6 turns of other sites, but within 16 turns (9000 gold).
    [TestCase(2000, false)]
    [TestCase(9000, true)]
    public void MercenaryWalkGrowsWithGoldTest(int gold, bool walks)
    {
        WorldPosition start = new(44, 44);
        WorldAiActionPlan plan = FirstPlan(AiPersonalities.ClassicPlusId, Mercenaries(), start, gold, mapSize: 48);
        bool towards = plan.Command is WorldAiMoveStackCommand move && move.TargetPosition.GetDistance(Entrance) < start.GetDistance(Entrance);

        Assert.That(towards, Is.EqualTo(walks), plan.Command.ToString());
    }

    // Neutral sides spend gold only through the "neutrals buy units" map rule (docs/town-income.md phase 4).
    [Test]
    public void NeutralArmiesDontShopTest()
    {
        Assert.That(FirstPlan(AiPersonalities.ClassicPlusId, Mercenaries(), Entrance, 2000, race: WorldRace.Neutral).Command, Is.Not.InstanceOf<WorldPurchaseMercenaryCommand>());
    }

    [Test]
    public void KeepsSomeGoldTest()
    {
        // A potion for 50 would leave 470 < 500: no purchase; with 600 it buys.
        Assert.That(FirstPlan(AiPersonalities.ClassicPlusId, Merchant(), Entrance, 520).Command, Is.Not.InstanceOf<WorldPurchaseItemCommand>());
        Assert.That(FirstPlan(AiPersonalities.ClassicPlusId, Merchant(), Entrance, 600).Command, Is.InstanceOf<WorldPurchaseItemCommand>());
    }
}

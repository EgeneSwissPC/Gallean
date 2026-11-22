namespace Gallean.Engine.Tests;

// Gallean Upgrade: docs/ai-unused-features.md phase 5. Leaders of our AIs equip banners, tomes, artifacts and boots
// and buy equipment at merchants; Classic is the control in every test (it never does).
public sealed class WorldAiEquipmentTests
{
    private static readonly UnitDefinition Leader = TestHelpers.SimpleLeaderDefinition(
            TestHelpers.SimpleUnitDefinition("leader", maxHitpoints: 100, initiative: 50, damage: 10),
            movementPoints: 6, leadership: 4) with
        {
            LeaderAbilities = LeaderAbility.BannerUse | LeaderAbility.TravelItemUse
        };

    private static ItemDefinition Item(string id, ItemCategory category, int gold) =>
        TestHelpers.SimpleItemDefinition(new ItemDefinitionId(id), category) with { Value = new WorldResourceSet(gold, 0, 0, 0, 0, 0) };

    private static readonly ItemDefinition Banner = Item("banner", ItemCategory.Banner, 300);
    private static readonly ItemDefinition CheapBoots = Item("cheapBoots", ItemCategory.TravelItem, 50);
    private static readonly ItemDefinition GoodBoots = Item("goodBoots", ItemCategory.TravelItem, 200);
    private static readonly ItemDefinition Sword = Item("sword", ItemCategory.Weapon, 400);

    private static WorldItem Owned(ItemDefinition definition, string id) => new(new WorldItemId(id), definition.Id, Charges: null);

    private static WorldAiActionPlan FirstPlan(string brainId, IReadOnlyList<WorldItem> bag, LeaderEquipment equipment, WorldSite? site = null, int gold = 2000)
    {
        WorldMap map = TestHelpers.SimpleMap(10, 10);
        WorldPlayer ai = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Undead, map) with
        {
            IsAi = true,
            Resources = new WorldResourceSet(gold, 0, 0, 0, 0, 0)
        };
        WorldPlayer human = TestHelpers.SimplePlayer(new WorldPlayerId(2), WorldRace.Heretic, map);
        WorldStack stack = TestHelpers.SimpleStack(
            new WorldStackId("Army"),
            ai.Id,
            new WorldPosition(4, 4),
            new UnitGrid<WorldUnit>.Builder().Place(TestHelpers.SimpleUnit(new WorldUnitId("leaderUnit"), Leader), GridPosition.BackBottom, false).Build(),
            movementPoints: 6) with
        {
            Inventory = new WorldInventory([.. bag]),
            Equipment = equipment
        };

        TestWorldEngineBuilder builder = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(Leader)
            .WithDefinition(Banner)
            .WithDefinition(CheapBoots)
            .WithDefinition(GoodBoots)
            .WithDefinition(Sword)
            .WithRace(TestHelpers.SimpleRace(WorldRace.Undead, new GlobalTextId("Undead Hordes"), WorldMapTerrain.Undead))
            .WithDefinition(TestHelpers.SimpleLord(ai.LordId, ai.Race, LordCategory.Warrior))
            .WithPlayer(ai)
            .WithDefinition(TestHelpers.SimpleLord(human.LordId, human.Race, LordCategory.Warrior))
            .WithPlayer(human)
            .WithSubrace(TestHelpers.NeutralSubrace)
            .WithStack(stack);

        if (site is not null)
        {
            builder = builder.WithSite(site);
        }

        (GameSession session, IEngineContext context) = builder.BuildSession();

        return AiBrainRegistry.ResolveWorld(brainId).DecideAction(context, session.World.State, WorldAiMemory.Empty);
    }

    private static LeaderEquipment Wearing(LeaderEquipmentSlot slot, WorldItem item) =>
        new(LeaderEquipment.Empty.Items.Add(slot, item));

    [Test]
    public void EquipsBannerFromBagTest()
    {
        WorldItem[] bag = [Owned(Banner, "b1")];

        Assert.That(FirstPlan(AiBrainRegistry.ClassicId, bag, LeaderEquipment.Empty).Command, Is.Not.InstanceOf<WorldEquipItemCommand>());
        Assert.That(FirstPlan(AiPersonalities.ClassicPlusId, bag, LeaderEquipment.Empty).Command,
            Is.EqualTo(new WorldEquipItemCommand(new WorldStackId("Army"), new WorldItemId("b1"), LeaderEquipmentSlot.Banner)));
    }

    [Test]
    public void SwapsForBetterBootsTest()
    {
        // Cheap boots worn, better boots in the bag: take the cheap ones off first (equipping fills empty slots only).
        WorldItem[] bag = [Owned(GoodBoots, "good")];
        LeaderEquipment wearing = Wearing(LeaderEquipmentSlot.Boots, Owned(CheapBoots, "cheap"));

        Assert.That(FirstPlan(AiPersonalities.ClassicPlusId, bag, wearing).Command,
            Is.EqualTo(new WorldUnequipItemCommand(new WorldStackId("Army"), LeaderEquipmentSlot.Boots)));

        // The other way round nothing changes.
        Assert.That(FirstPlan(AiPersonalities.ClassicPlusId, [Owned(CheapBoots, "cheap")], Wearing(LeaderEquipmentSlot.Boots, Owned(GoodBoots, "good"))).Command,
            Is.Not.InstanceOf<WorldUnequipItemCommand>().And.Not.InstanceOf<WorldEquipItemCommand>());
    }

    [Test]
    public void NeedsTheAbilityTest()
    {
        // The leader can't use weapons: the sword stays in the bag.
        Assert.That(FirstPlan(AiPersonalities.ClassicPlusId, [Owned(Sword, "s1")], LeaderEquipment.Empty).Command, Is.Not.InstanceOf<WorldEquipItemCommand>());
    }

    [Test]
    public void BuysBootsAtMerchantTest()
    {
        WorldSite merchant = new WorldMerchantSite(new WorldSiteId("site"), new WorldFootprint(new WorldPosition(2, 2), 3, 3), new WorldObjectVariant(0),
            new ScenarioTextId("Title"), new ScenarioTextId("Greeting"), [new WorldMerchantItem(GoodBoots.Id, 1), new WorldMerchantItem(Sword.Id, 1)], [], 3);

        Assert.That(FirstPlan(AiBrainRegistry.ClassicId, [], LeaderEquipment.Empty, merchant).Command, Is.Not.InstanceOf<WorldPurchaseItemCommand>());
        Assert.That(FirstPlan(AiPersonalities.ClassicPlusId, [], LeaderEquipment.Empty, merchant).Command,
            Is.EqualTo(new WorldPurchaseItemCommand(new WorldStackId("Army"), new WorldSiteId("site"), GoodBoots.Id)), "the boots, not the sword it can't use");

        // Already wearing as good boots: nothing to buy.
        Assert.That(FirstPlan(AiPersonalities.ClassicPlusId, [], Wearing(LeaderEquipmentSlot.Boots, Owned(GoodBoots, "worn")), merchant).Command,
            Is.Not.InstanceOf<WorldPurchaseItemCommand>());
    }
}

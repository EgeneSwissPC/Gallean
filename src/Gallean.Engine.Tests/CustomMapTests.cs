using Gallean.Entities.GameData;

namespace Gallean.Engine.Tests;

public sealed class CustomMapTests
{
    private GlobalDatabase globalDb = null!;
    private CampaignDatabase campaignDb = null!;
    private IGameDefinitionProvider definitions = null!;
    private string mapsDirectory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        globalDb = GlobalDatabaseLoader.Load(RiseOfTheElves.GlobalDatabasePath);
        campaignDb = CampaignDatabaseLoader.Load(RiseOfTheElves.CampaignDatabasePath);
        definitions = new GameDefinitionProvider(globalDb);
    }

    [SetUp]
    public void SetUp()
    {
        mapsDirectory = Path.Combine(Path.GetTempPath(), "gallean-custom-maps-" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(mapsDirectory))
        {
            Directory.Delete(mapsDirectory, recursive: true);
        }
    }

    private static CustomMap CreateMap(int size = 48)
    {
        CustomMap map = CustomMap.Create("S600SC0000", "Test map", size, WorldMapTerrain.Neutral, [WorldRace.Human, WorldRace.Elf]);
        Random random = new(1);

        map.Paint(new WorldPosition(24, 24), 4, CustomMapBrush.Water, WorldMapTerrain.Neutral, random);
        map.Paint(new WorldPosition(10, 30), 2, CustomMapBrush.Forest, WorldMapTerrain.Neutral, random);
        map.Paint(new WorldPosition(36, 10), 1, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random);
        map.Paint(new WorldPosition(40, 40), 0, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random);
        map.Paint(new WorldPosition(30, 36), 2, CustomMapBrush.Land, WorldMapTerrain.Undead, random);
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(4, 4)), Is.True);
        Assert.That(map.PlaceCapital(WorldRace.Elf, new WorldPosition(38, 4)), Is.True);

        return map;
    }

    [Test]
    public void PaintTest()
    {
        CustomMap map = CustomMap.Create("S600SC0000", "Paint", 32, WorldMapTerrain.Neutral, [WorldRace.Dwarf]);
        Random random = new(1);

        Assert.That(map.Paint(new WorldPosition(10, 10), 1, CustomMapBrush.Water, WorldMapTerrain.Neutral, random), Is.True);
        Assert.That(map.GetCell(new WorldPosition(10, 10)).Terrain, Is.EqualTo(WorldMapTerrain.Water));
        Assert.That(map.GetCell(new WorldPosition(11, 10)).Terrain, Is.EqualTo(WorldMapTerrain.Water));
        Assert.That(map.GetCell(new WorldPosition(12, 12)).Terrain, Is.EqualTo(WorldMapTerrain.Neutral));

        // Forests and mountains are not placed on water.
        Assert.That(map.Paint(new WorldPosition(10, 10), 0, CustomMapBrush.Forest, WorldMapTerrain.Neutral, random), Is.False);
        Assert.That(map.Paint(new WorldPosition(10, 10), 0, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random), Is.False);

        Assert.That(map.Paint(new WorldPosition(20, 20), 1, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random), Is.True);
        Assert.That(map.Mountains, Has.Count.EqualTo(1));
        Assert.That(map.Mountains[0].Footprint, Is.EqualTo(new WorldFootprint(new WorldPosition(19, 19), 3, 3)));
        Assert.That(map.GetCell(new WorldPosition(21, 21)).Feature, Is.EqualTo(WorldMapFeature.Mountain));

        // Clearing any tile of a mountain removes the whole mountain.
        Assert.That(map.Paint(new WorldPosition(19, 19), 0, CustomMapBrush.Clear, WorldMapTerrain.Neutral, random), Is.True);
        Assert.That(map.Mountains, Is.Empty);
        Assert.That(map.GetCell(new WorldPosition(21, 21)).Feature, Is.EqualTo(WorldMapFeature.None));

        // Land turns water back into (race) land.
        Assert.That(map.Paint(new WorldPosition(10, 10), 0, CustomMapBrush.Land, WorldMapTerrain.Dwarf, random), Is.True);
        Assert.That(map.GetCell(new WorldPosition(10, 10)).Terrain, Is.EqualTo(WorldMapTerrain.Dwarf));
    }

    [Test]
    public void CapitalTest()
    {
        CustomMap map = CustomMap.Create("S600SC0000", "Capital", 32, WorldMapTerrain.Neutral, [WorldRace.Human, WorldRace.Undead]);
        Random random = new(1);

        Assert.That(map.Validate(), Has.Count.EqualTo(2));
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(30, 30)), Is.False, "Capital outside the map.");
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(2, 2)), Is.True);
        Assert.That(map.PlaceCapital(WorldRace.Undead, new WorldPosition(4, 4)), Is.False, "Overlaps the human capital.");
        Assert.That(map.PlaceCapital(WorldRace.Elf, new WorldPosition(20, 20)), Is.False, "Elves are not on this map.");
        Assert.That(map.PlaceCapital(WorldRace.Undead, new WorldPosition(20, 20)), Is.True);
        Assert.That(map.Validate(), Is.Empty);
        Assert.That(map.GetCell(new WorldPosition(4, 4)).Terrain, Is.EqualTo(WorldMapTerrain.Human));

        // Capitals are protected from painting.
        Assert.That(map.Paint(new WorldPosition(4, 4), 0, CustomMapBrush.Water, WorldMapTerrain.Neutral, random), Is.False);
        Assert.That(map.GetCell(new WorldPosition(4, 4)).Terrain, Is.EqualTo(WorldMapTerrain.Human));

        // Moving a capital keeps one capital per player.
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(10, 2)), Is.True);
        Assert.That(map.Players.Single(player => player.Race == WorldRace.Human).Capital, Is.EqualTo(new WorldPosition(10, 2)));
    }

    [Test]
    public void SaveAndLoadTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);

        store.Save(map);

        Assert.That(store.List().Select(info => (info.ScenarioId.Value, info.Name, info.MapSize)), Is.EqualTo(new[] { ("S600SC0000", "Test map", 48) }));

        Scenario scenario = ScenarioLoader.Load(globalDb, store.LoadDatabase(map.Id), campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables());
        WorldState state = scenario.State;

        Assert.That(state.Map.Width, Is.EqualTo(48));
        Assert.That(state.Map.Cells, Is.EqualTo(map.Cells));
        Assert.That(state.Mountains.Values.Select(mountain => (mountain.Id.Value, mountain.Footprint)), Is.EquivalentTo(map.Mountains.Select(mountain => (mountain.Id, mountain.Footprint))));
        Assert.That(state.Players.Values.Select(player => player.Race), Is.EqualTo(new[] { WorldRace.Neutral, WorldRace.Human, WorldRace.Elf }));

        WorldCapital[] capitals = [.. state.Fortifications.Values.OfType<WorldCapital>()];
        Assert.That(capitals.Select(capital => capital.Footprint.TopLeft), Is.EquivalentTo(new[] { new WorldPosition(4, 4), new WorldPosition(38, 4) }));
        Assert.That(capitals.All(capital => !capital.Garrison.IsEmpty), Is.True, "Capitals have their guardian.");
        Assert.That(state.Stacks.Values.Count(stack => capitals.Any(capital => capital.Footprint.Contains(stack.Position))), Is.EqualTo(2), "Each capital has a leader stack.");

        CustomMap reopened = store.Open(map.Id, definitions);
        Assert.That(reopened.Name, Is.EqualTo(map.Name));
        Assert.That(reopened.Cells, Is.EqualTo(map.Cells));
        Assert.That(reopened.Mountains, Is.EquivalentTo(map.Mountains));
        Assert.That(reopened.Players, Is.EqualTo(map.Players));
    }

    [Test]
    public void PlayTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        store.Save(map);

        // The quest list starts a new game like this: the chosen race becomes the human player.
        ScenarioConfiguration configuration = new([new ScenarioPlayerConfiguration(WorldRace.Human, LordCategory.Warrior, new WorldObjectVariant(0), null, LeaderName: null)], WorldDifficulty.Average, Leader: null);
        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Create(globalDb, store.LoadDatabase(map.Id), campaignDb, new ScenarioId(map.Id), definitions, globals, configuration);
        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        GameSession session = new(context, scenario.State);

        Assert.That(session.World.DispatchCommand(new WorldStartGameCommand()).Error, Is.Null);
        Assert.That(session.World.State.Players.Values.Single(player => player.Race == WorldRace.Human).IsAi, Is.False);

        // Let the AI play both sides for a few turns.
        WorldState aiState = session.World.State with
        {
            Players = session.World.State.Players.SetItems(session.World.State.Players.Select(pair => KeyValuePair.Create(pair.Key, pair.Value with { IsAi = true })))
        };
        GameSession aiSession = new(context, aiState);
        TestWorldAiRunner runner = new(aiSession, context);

        for (int i = 0; i < 9; i++)
        {
            runner.PlayTurn();
        }

        Assert.That(aiSession.World.State.TurnInfo.TurnNumber, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void LargeMapTest()
    {
        CustomMap map = CustomMap.Create("S601SC0000", "Large", 300, WorldMapTerrain.Water, [WorldRace.Dwarf]);
        map.Paint(new WorldPosition(290, 290), 5, CustomMapBrush.Land, WorldMapTerrain.Neutral, new Random(1));
        map.PlaceCapital(WorldRace.Dwarf, new WorldPosition(286, 286));
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        store.Save(map);

        WorldState state = store.BuildPreview(map, definitions).State;

        Assert.That(state.Map.Width, Is.EqualTo(300));
        Assert.That(state.Map.Cells, Is.EqualTo(map.Cells));
        Assert.That(Directory.EnumerateDirectories(mapsDirectory).Select(Path.GetFileName), Is.EquivalentTo(new[] { "S601SC0000", ".preview" }));
        Assert.That(Directory.EnumerateFileSystemEntries(Path.Combine(mapsDirectory, ".preview")), Is.Empty);
    }

    [Test]
    public void IdsTest()
    {
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);

        Assert.That(store.NextId(), Is.EqualTo("S600SC0000"));
        store.Save(CreateMap());
        Assert.That(store.NextId(), Is.EqualTo("S601SC0000"));
        Assert.That(CustomMapStore.IsCustomMapId("S600SC0000"), Is.True);
        Assert.That(CustomMapStore.IsCustomMapId("S216SC0000"), Is.False);

        store.Delete("S600SC0000");
        Assert.That(store.List(), Is.Empty);
        Assert.That(store.NextId(), Is.EqualTo("S600SC0000"));
    }

    [Test]
    public void ApplyTerrainTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        WorldState before = store.BuildPreview(map, definitions).State;
        Random random = new(2);

        map.Paint(new WorldPosition(20, 40), 3, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random);
        map.Paint(new WorldPosition(24, 24), 1, CustomMapBrush.Land, WorldMapTerrain.Heretic, random);
        map.Paint(new WorldPosition(36, 10), 0, CustomMapBrush.Clear, WorldMapTerrain.Neutral, random);
        map.PlaceResource(new WorldPosition(14, 20), WorldResourceType.Gold);
        map.PlaceResource(new WorldPosition(16, 20), WorldResourceType.RuneMana);

        WorldState applied = CustomMapStore.ApplyTerrain(before, map);
        WorldState rebuilt = store.BuildPreview(map, definitions).State;

        Assert.That(applied.Map, Is.EqualTo(rebuilt.Map));
        Assert.That(applied.Mountains, Is.EqualTo(rebuilt.Mountains));
        Assert.That(applied.Fortifications, Is.EqualTo(rebuilt.Fortifications));
        Assert.That(applied.ResourceNodes, Is.EqualTo(rebuilt.ResourceNodes));
        Assert.That(applied.ResourceNodes, Has.Count.EqualTo(2));
    }

    [Test]
    public void QuestListTest()
    {
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        ScenarioDatabaseIndex bundled = ScenarioDatabaseLoader.LoadIndex(RiseOfTheElves.ScenarioDatabasePath);

        Assert.That(store.MergeInto(bundled), Is.SameAs(bundled));

        store.Save(CreateMap());
        ScenarioDatabaseIndex merged = store.MergeInto(bundled);
        IReadOnlyList<ScenarioCampaign> campaigns = ScenarioLoader.EnumerateCampaigns(GlobalDatabaseLoader.LoadIndex(RiseOfTheElves.GlobalDatabasePath), merged, campaignDb);
        ScenarioCampaign skirmish = campaigns.Single(campaign => campaign.IsSkirmish);
        ScenarioMetadata custom = skirmish.Scenarios.Last();

        Assert.That(custom.Id, Is.EqualTo(new ScenarioId("S600SC0000")));
        Assert.That(custom.MapSize, Is.EqualTo(48));
        Assert.That(custom.PlayableRaces.Select(race => race.Race), Is.EquivalentTo(new[] { WorldRace.Human, WorldRace.Elf }));
        Assert.That(skirmish.Scenarios.Count, Is.EqualTo(ScenarioLoader.EnumerateCampaigns(GlobalDatabaseLoader.LoadIndex(RiseOfTheElves.GlobalDatabasePath), bundled, campaignDb).Single(campaign => campaign.IsSkirmish).Scenarios.Count + 1));
    }

    private IReadOnlyList<UnitDefinition> Units => new CustomMapStore(mapsDirectory, globalDb, campaignDb).ListUnits(definitions);

    private CustomMapUnit FindUnit(Func<UnitDefinition, bool> predicate)
    {
        return CustomMapUnit.From(Units.Where(predicate).OrderBy(unit => unit.Id.Value, StringComparer.Ordinal).First());
    }

    private CustomMapUnit NeutralLeader => FindUnit(unit => unit is { Category: UnitCategory.Leader, Race: WorldRace.Neutral, IsLarge: false, Leadership: >= 4 });
    private CustomMapUnit NeutralSoldier => FindUnit(unit => unit is { Category: UnitCategory.Soldier, Race: WorldRace.Neutral, IsLarge: false });
    private CustomMapUnit LargeSoldier => FindUnit(unit => unit is { Category: UnitCategory.Soldier, Race: WorldRace.Neutral, IsLarge: true });
    private CustomMapUnit HumanLeader => FindUnit(unit => unit is { Category: UnitCategory.Leader, Race: WorldRace.Human, IsLarge: false });

    private static CustomMapUnit?[] Slots(params (GridPosition Position, CustomMapUnit Unit)[] units)
    {
        CustomMapUnit?[] slots = new CustomMapUnit?[GridPosition.Count];

        foreach ((GridPosition position, CustomMapUnit unit) in units)
        {
            slots[position.ToIndex()] = unit;
        }

        return slots;
    }

    [Test]
    public void StackRulesTest()
    {
        CustomMap map = CreateMap();
        CustomMapUnit?[] guard = Slots((GridPosition.FrontCenter, NeutralLeader), (GridPosition.BackCenter, NeutralSoldier));

        Assert.That(CustomMapStack.ValidateSlots(guard), Is.Null);
        Assert.That(CustomMapStack.ValidateSlots(Slots((GridPosition.FrontCenter, NeutralSoldier))), Is.EqualTo(CustomMapStackError.NoLeader));
        Assert.That(CustomMapStack.ValidateSlots(Slots((GridPosition.FrontCenter, NeutralLeader), (GridPosition.FrontTop, HumanLeader))), Is.EqualTo(CustomMapStackError.MultipleLeaders));
        Assert.That(CustomMapStack.ValidateSlots(Slots((GridPosition.FrontCenter, NeutralLeader), (GridPosition.BackTop, LargeSoldier))), Is.EqualTo(CustomMapStackError.InvalidSlot), "Large units stand in the front slot.");
        Assert.That(CustomMapStack.ValidateSlots(Slots((GridPosition.FrontCenter, NeutralLeader), (GridPosition.FrontTop, LargeSoldier), (GridPosition.BackTop, NeutralSoldier))), Is.EqualTo(CustomMapStackError.InvalidSlot), "A large unit fills both lines.");

        CustomMapUnit?[] full = [.. Enumerable.Repeat<CustomMapUnit?>(NeutralSoldier, GridPosition.Count)];
        full[GridPosition.FrontCenter.ToIndex()] = HumanLeader with { Leadership = 3 };
        Assert.That(CustomMapStack.ValidateSlots(full), Is.EqualTo(CustomMapStackError.LeadershipExceeded));

        Assert.That(map.PlaceStack(new WorldPosition(20, 40), WorldRace.Neutral, guard).Error, Is.Null);
        Assert.That(map.PlaceStack(new WorldPosition(20, 40), WorldRace.Neutral, guard).Error, Is.EqualTo(CustomMapStackError.Occupied));
        Assert.That(map.PlaceStack(new WorldPosition(24, 24), WorldRace.Neutral, guard).Error, Is.EqualTo(CustomMapStackError.Blocked), "Water.");
        Assert.That(map.PlaceStack(new WorldPosition(5, 5), WorldRace.Neutral, guard).Error, Is.EqualTo(CustomMapStackError.Blocked), "Capital.");
        Assert.That(map.PlaceStack(new WorldPosition(21, 40), WorldRace.Dwarf, guard).Error, Is.EqualTo(CustomMapStackError.UnknownOwner));
        Assert.That(map.PlaceStack(new WorldPosition(60, 40), WorldRace.Neutral, guard).Error, Is.EqualTo(CustomMapStackError.OutsideMap));

        // Armies block water and mountains under them, and capitals cannot be moved onto them.
        Random random = new(1);
        map.Paint(new WorldPosition(20, 40), 0, CustomMapBrush.Water, WorldMapTerrain.Neutral, random);
        map.Paint(new WorldPosition(20, 40), 0, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random);
        Assert.That(map.GetCell(new WorldPosition(20, 40)), Is.EqualTo(new WorldMapCell(WorldMapTerrain.Neutral, WorldMapFeature.None, new WorldObjectVariant(0))));
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(18, 38)), Is.False);

        int id = map.Stacks.Single().Id;
        Assert.That(map.UpdateStack(id, WorldRace.Human, Slots((GridPosition.FrontCenter, HumanLeader))), Is.Null);
        Assert.That(map.Stacks.Single().Owner, Is.EqualTo(WorldRace.Human));
        Assert.That(map.RemoveStack(id), Is.True);
        Assert.That(map.Stacks, Is.Empty);
    }

    [Test]
    public void StackSaveAndLoadTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);

        Assert.That(map.PlaceStack(new WorldPosition(20, 40), WorldRace.Neutral, Slots((GridPosition.BackCenter, NeutralLeader), (GridPosition.FrontTop, LargeSoldier), (GridPosition.FrontBottom, NeutralSoldier))).Error, Is.Null);
        Assert.That(map.PlaceStack(new WorldPosition(12, 6), WorldRace.Human, Slots((GridPosition.FrontCenter, HumanLeader))).Error, Is.Null);
        store.Save(map);

        WorldState state = ScenarioLoader.Load(globalDb, store.LoadDatabase(map.Id), campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables()).State;
        WorldStack guard = state.Stacks.Values.Single(stack => stack.Position == new WorldPosition(20, 40));
        WorldStack human = state.Stacks.Values.Single(stack => stack.Position == new WorldPosition(12, 6));

        Assert.That(state.Players[guard.OwnerId].Race, Is.EqualTo(WorldRace.Neutral));
        Assert.That(state.Players[human.OwnerId].Race, Is.EqualTo(WorldRace.Human));
        Assert.That(guard.Units[GridPosition.BackCenter]!.DefinitionId, Is.EqualTo(NeutralLeader.TypeId));
        Assert.That(guard.Units[GridPosition.FrontTop]!.DefinitionId, Is.EqualTo(LargeSoldier.TypeId));
        Assert.That(guard.Units[GridPosition.BackTop], Is.SameAs(guard.Units[GridPosition.FrontTop]), "The large unit fills both lines.");
        Assert.That(guard.Units[GridPosition.FrontBottom]!.DefinitionId, Is.EqualTo(NeutralSoldier.TypeId));
        Assert.That(guard.GetLeader(definitions).Unit.DefinitionId, Is.EqualTo(NeutralLeader.TypeId));

        CustomMap reopened = store.Open(map.Id, definitions);
        Assert.That(reopened.Stacks, Is.EquivalentTo(map.Stacks));
        Assert.That(reopened.Players, Is.EqualTo(map.Players), "Capital garrisons are not read back as armies.");
    }

    [Test]
    public void StackPlayTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);

        // A neutral guard next to each capital and a human raiding party in the middle.
        map.PlaceStack(new WorldPosition(10, 6), WorldRace.Neutral, Slots((GridPosition.FrontCenter, NeutralLeader), (GridPosition.FrontTop, NeutralSoldier)));
        map.PlaceStack(new WorldPosition(36, 8), WorldRace.Neutral, Slots((GridPosition.FrontCenter, NeutralLeader), (GridPosition.FrontBottom, LargeSoldier)));
        map.PlaceStack(new WorldPosition(14, 18), WorldRace.Human, Slots((GridPosition.FrontCenter, HumanLeader)));
        store.Save(map);

        ScenarioConfiguration configuration = new([new ScenarioPlayerConfiguration(WorldRace.Human, LordCategory.Warrior, new WorldObjectVariant(0), null, LeaderName: null)], WorldDifficulty.Average, Leader: null);
        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Create(globalDb, store.LoadDatabase(map.Id), campaignDb, new ScenarioId(map.Id), definitions, globals, configuration);
        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        WorldState aiState = scenario.State with
        {
            Players = scenario.State.Players.SetItems(scenario.State.Players.Select(pair => KeyValuePair.Create(pair.Key, pair.Value with { IsAi = true })))
        };
        GameSession session = new(context, aiState);

        Assert.That(session.World.DispatchCommand(new WorldStartGameCommand()).Error, Is.Null);

        TestWorldAiRunner runner = new(session, context);

        for (int i = 0; i < 12; i++)
        {
            runner.PlayTurn();
        }

        Assert.That(session.World.State.TurnInfo.TurnNumber, Is.GreaterThanOrEqualTo(4));
    }

    [Test]
    public void MoveUnitTest()
    {
        CustomMapUnit leader = NeutralLeader;
        CustomMapUnit soldier = NeutralSoldier;
        CustomMapUnit large = LargeSoldier;
        CustomMapUnit?[] slots = Slots((GridPosition.FrontCenter, leader), (GridPosition.BackCenter, soldier), (GridPosition.FrontTop, large));

        // Small unit into a free slot, then swapping two small units.
        CustomMapUnit?[] moved = CustomMapStack.MoveUnit(slots, GridPosition.BackCenter, GridPosition.BackBottom);
        Assert.That(moved, Is.EqualTo(Slots((GridPosition.FrontCenter, leader), (GridPosition.BackBottom, soldier), (GridPosition.FrontTop, large))));
        moved = CustomMapStack.MoveUnit(slots, GridPosition.FrontCenter, GridPosition.BackCenter);
        Assert.That(moved, Is.EqualTo(Slots((GridPosition.BackCenter, leader), (GridPosition.FrontCenter, soldier), (GridPosition.FrontTop, large))));

        // A large unit swaps rows, also when dropped on its back slot or when a small unit is dropped onto it.
        moved = CustomMapStack.MoveUnit(slots, GridPosition.BackTop, GridPosition.FrontBottom);
        Assert.That(moved, Is.EqualTo(Slots((GridPosition.FrontCenter, leader), (GridPosition.BackCenter, soldier), (GridPosition.FrontBottom, large))));
        moved = CustomMapStack.MoveUnit(slots, GridPosition.BackCenter, GridPosition.BackTop);
        Assert.That(moved, Is.EqualTo(Slots((GridPosition.FrontTop, leader), (GridPosition.BackTop, soldier), (GridPosition.FrontCenter, large))));
        Assert.That(CustomMapStack.ValidateSlots(moved), Is.Null);

        // Placing: a large unit needs its whole row.
        Assert.That(CustomMapStack.TryPlaceUnit(slots, GridPosition.BackBottom, large), Is.EqualTo(Slots((GridPosition.FrontCenter, leader), (GridPosition.BackCenter, soldier), (GridPosition.FrontTop, large), (GridPosition.FrontBottom, large))));
        Assert.That(CustomMapStack.TryPlaceUnit(slots, GridPosition.FrontCenter, large), Is.Null);
        Assert.That(CustomMapStack.TryPlaceUnit(slots, GridPosition.BackTop, soldier), Is.Null, "The large unit's back slot is taken.");
        Assert.That(CustomMapStack.GetOccupiedLeadership(slots), Is.EqualTo(4));
        Assert.That(CustomMapStack.GetLeadership(slots), Is.EqualTo(leader.Leadership));
    }

    [Test]
    public void ResourceRulesTest()
    {
        CustomMap map = CreateMap();
        Random random = new(1);
        WorldPosition gold = new(20, 20);

        Assert.That(map.PlaceResource(gold, WorldResourceType.Gold), Is.Null);
        Assert.That(map.GetResourceAt(gold)!.Type, Is.EqualTo(WorldResourceType.Gold));
        Assert.That(map.PlaceResource(new WorldPosition(24, 24), WorldResourceType.Gold), Is.EqualTo(CustomMapPlacementError.Blocked), "Water.");
        Assert.That(map.PlaceResource(new WorldPosition(36, 10), WorldResourceType.Gold), Is.EqualTo(CustomMapPlacementError.Blocked), "Mountain.");
        Assert.That(map.PlaceResource(new WorldPosition(5, 5), WorldResourceType.Gold), Is.EqualTo(CustomMapPlacementError.Blocked), "Capital.");
        Assert.That(map.PlaceResource(new WorldPosition(48, 5), WorldResourceType.Gold), Is.EqualTo(CustomMapPlacementError.OutsideMap));

        // A forest under a new resource is cleared.
        Assert.That(map.GetCell(new WorldPosition(10, 30)).Feature, Is.EqualTo(WorldMapFeature.Forest));
        Assert.That(map.PlaceResource(new WorldPosition(10, 30), WorldResourceType.GroveMana), Is.Null);
        Assert.That(map.GetCell(new WorldPosition(10, 30)).Feature, Is.EqualTo(WorldMapFeature.None));

        // Placing on a resource changes its type.
        Assert.That(map.PlaceResource(gold, WorldResourceType.LifeMana), Is.Null);
        Assert.That(map.Resources, Has.Count.EqualTo(2));
        Assert.That(map.GetResourceAt(gold)!.Type, Is.EqualTo(WorldResourceType.LifeMana));

        // Armies cannot stand on resources and resources cannot go under armies.
        CustomMapUnit?[] guard = Slots((GridPosition.FrontCenter, NeutralLeader));
        Assert.That(map.PlaceStack(gold, WorldRace.Neutral, guard).Error, Is.EqualTo(CustomMapStackError.Blocked));
        Assert.That(map.PlaceStack(new WorldPosition(30, 30), WorldRace.Neutral, guard).Error, Is.Null);
        Assert.That(map.PlaceResource(new WorldPosition(30, 30), WorldResourceType.Gold), Is.EqualTo(CustomMapPlacementError.Occupied));

        // Water, forests and mountains leave the resource tile alone; repainting the land under it is allowed.
        map.Paint(gold, 1, CustomMapBrush.Water, WorldMapTerrain.Neutral, random);
        Assert.That(map.GetCell(gold).Terrain, Is.EqualTo(WorldMapTerrain.Neutral));
        Assert.That(map.GetCell(new WorldPosition(21, 20)).Terrain, Is.EqualTo(WorldMapTerrain.Water));
        map.Paint(new WorldPosition(20, 18), 2, CustomMapBrush.Land, WorldMapTerrain.Human, random);
        map.Paint(gold, 0, CustomMapBrush.Forest, WorldMapTerrain.Neutral, random);
        Assert.That(map.Paint(gold, 0, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random), Is.False);
        Assert.That(map.GetCell(gold), Is.EqualTo(new WorldMapCell(WorldMapTerrain.Human, WorldMapFeature.None, new WorldObjectVariant(0))));
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(18, 18)), Is.False, "A capital cannot cover a resource.");

        Assert.That(map.RemoveResource(gold), Is.True);
        Assert.That(map.RemoveResource(gold), Is.False);
        Assert.That(map.Resources.Select(resource => resource.Type), Is.EqualTo(new[] { WorldResourceType.GroveMana }));
    }

    [Test]
    public void VillageRulesTest()
    {
        CustomMap map = CreateMap();
        Random random = new(1);

        (CustomMapVillage? village, CustomMapPlacementError? error) = map.PlaceVillage(new WorldPosition(14, 14), 2, WorldRace.Neutral, "Errgok");
        Assert.That(error, Is.Null);
        Assert.That(village!.Footprint, Is.EqualTo(new WorldFootprint(new WorldPosition(14, 14), 4, 4)));
        Assert.That(map.GetVillageAt(new WorldPosition(17, 17)), Is.EqualTo(village));
        Assert.That(map.GetVillageAt(new WorldPosition(18, 17)), Is.Null);

        Assert.That(map.PlaceVillage(new WorldPosition(16, 16), 1, WorldRace.Neutral, "").Error, Is.EqualTo(CustomMapPlacementError.Blocked), "Overlaps a village.");
        Assert.That(map.PlaceVillage(new WorldPosition(6, 6), 1, WorldRace.Neutral, "").Error, Is.EqualTo(CustomMapPlacementError.Blocked), "Overlaps a capital.");
        Assert.That(map.PlaceVillage(new WorldPosition(45, 45), 1, WorldRace.Neutral, "").Error, Is.EqualTo(CustomMapPlacementError.OutsideMap));
        Assert.That(map.PlaceVillage(new WorldPosition(20, 2), 1, WorldRace.Dwarf, "").Error, Is.EqualTo(CustomMapPlacementError.UnknownOwner));
        Assert.That(map.PlaceVillage(new WorldPosition(20, 2), 6, WorldRace.Neutral, "").Error, Is.EqualTo(CustomMapPlacementError.InvalidLevel));

        // The land under a village becomes its owner's and loses forests and mountains.
        map.Paint(new WorldPosition(30, 18), 1, CustomMapBrush.Forest, WorldMapTerrain.Neutral, random);
        Assert.That(map.PlaceVillage(new WorldPosition(29, 17), 1, WorldRace.Human, "Ford").Error, Is.Null);
        Assert.That(map.GetCell(new WorldPosition(30, 18)), Is.EqualTo(new WorldMapCell(WorldMapTerrain.Human, WorldMapFeature.None, new WorldObjectVariant(0))));

        // Villages are protected like capitals and block armies, resources and capitals.
        Assert.That(map.Paint(new WorldPosition(15, 15), 0, CustomMapBrush.Water, WorldMapTerrain.Neutral, random), Is.False);
        Assert.That(map.Paint(new WorldPosition(15, 15), 0, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random), Is.False);
        Assert.That(map.PlaceStack(new WorldPosition(15, 15), WorldRace.Neutral, Slots((GridPosition.FrontCenter, NeutralLeader))).Error, Is.EqualTo(CustomMapStackError.Blocked));
        Assert.That(map.PlaceResource(new WorldPosition(15, 15), WorldResourceType.Gold), Is.EqualTo(CustomMapPlacementError.Blocked));
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(12, 12)), Is.False);

        // Nothing can be built over an army or a resource.
        map.PlaceResource(new WorldPosition(22, 32), WorldResourceType.Gold);
        Assert.That(map.PlaceVillage(new WorldPosition(20, 30), 1, WorldRace.Neutral, "").Error, Is.EqualTo(CustomMapPlacementError.Occupied));

        // Garrisons: no leaders, at most Level units, a large unit counts twice.
        CustomMapUnit?[] two = Slots((GridPosition.FrontCenter, NeutralSoldier), (GridPosition.BackCenter, NeutralSoldier));
        Assert.That(CustomMapVillage.ValidateGarrison(two, 2), Is.Null);
        Assert.That(CustomMapVillage.ValidateGarrison(two, 1), Is.EqualTo(CustomMapPlacementError.GarrisonFull));
        Assert.That(CustomMapVillage.ValidateGarrison(Slots((GridPosition.FrontTop, LargeSoldier), (GridPosition.FrontCenter, NeutralSoldier)), 2), Is.EqualTo(CustomMapPlacementError.GarrisonFull));
        Assert.That(CustomMapVillage.ValidateGarrison(Slots((GridPosition.FrontTop, LargeSoldier), (GridPosition.FrontCenter, NeutralSoldier)), 3), Is.Null);
        Assert.That(CustomMapVillage.ValidateGarrison(Slots((GridPosition.FrontCenter, NeutralLeader)), 5), Is.EqualTo(CustomMapPlacementError.LeaderInGarrison));
        Assert.That(CustomMapVillage.ValidateGarrison(Slots((GridPosition.BackTop, LargeSoldier)), 5), Is.EqualTo(CustomMapPlacementError.InvalidSlot));
        Assert.That(CustomMapVillage.ValidateGarrison(two, 0), Is.EqualTo(CustomMapPlacementError.InvalidLevel));

        Assert.That(map.UpdateVillage(village.Id, 1, WorldRace.Neutral, "Errgok", two), Is.EqualTo(CustomMapPlacementError.GarrisonFull));
        Assert.That(map.UpdateVillage(village.Id, 3, WorldRace.Elf, "Errgok", two), Is.Null);
        CustomMapVillage updated = map.Villages.Single(candidate => candidate.Id == village.Id);
        Assert.That((updated.Level, updated.Owner, updated.Garrison.Count(unit => unit is not null)), Is.EqualTo((3, WorldRace.Elf, 2)));
        Assert.That(map.GetCell(new WorldPosition(15, 15)).Terrain, Is.EqualTo(WorldMapTerrain.Elf), "A new owner claims the land under the village.");

        // Undo snapshots are independent copies.
        CustomMap copy = map.Clone();
        Assert.That(map.RemoveVillage(village.Id), Is.True);
        Assert.That(map.Villages, Has.Count.EqualTo(1));
        Assert.That(copy.Villages, Has.Count.EqualTo(2));
    }

    [Test]
    public void VillageAndResourceSaveAndLoadTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        Random random = new(1);

        map.Paint(new WorldPosition(14, 20), 2, CustomMapBrush.Land, WorldMapTerrain.Human, random);
        Assert.That(map.PlaceResource(new WorldPosition(14, 20), WorldResourceType.Gold), Is.Null);
        Assert.That(map.PlaceResource(new WorldPosition(15, 21), WorldResourceType.LifeMana), Is.Null);
        Assert.That(map.PlaceResource(new WorldPosition(30, 36), WorldResourceType.DeathMana), Is.Null);
        Assert.That(map.PlaceResource(new WorldPosition(44, 20), WorldResourceType.InfernalMana), Is.Null);
        int guarded = map.PlaceVillage(new WorldPosition(20, 30), 3, WorldRace.Neutral, "Errgok").Village!.Id;
        Assert.That(map.UpdateVillage(guarded, 3, WorldRace.Neutral, "Errgok", Slots((GridPosition.FrontTop, LargeSoldier), (GridPosition.BackCenter, NeutralSoldier))), Is.Null);
        int human = map.PlaceVillage(new WorldPosition(12, 12), 1, WorldRace.Human, "Ford").Village!.Id;
        int unnamed = map.PlaceVillage(new WorldPosition(30, 14), 5, WorldRace.Elf, "  ").Village!.Id;
        store.Save(map);

        ScenarioDatabase database = store.LoadDatabase(map.Id);
        WorldState state = ScenarioLoader.Load(globalDb, database, campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables()).State;

        Assert.That(state.ResourceNodes.Values.Select(node => (node.Position, node.ResourceType)), Is.EquivalentTo(map.Resources.Select(resource => (resource.Position, resource.Type))));

        WorldCity[] cities = [.. state.Fortifications.Values.OfType<WorldCity>()];
        Assert.That(cities.Select(city => (city.Footprint.TopLeft, city.DefinitionId.Level, state.Players[city.OwnerId].Race)), Is.EquivalentTo(new[]
        {
            (new WorldPosition(20, 30), 3, WorldRace.Neutral),
            (new WorldPosition(12, 12), 1, WorldRace.Human),
            (new WorldPosition(30, 14), 5, WorldRace.Elf)
        }));
        WorldCity errgok = cities.Single(city => city.Footprint.TopLeft == new WorldPosition(20, 30));
        Assert.That(errgok.Garrison[GridPosition.FrontTop]!.DefinitionId, Is.EqualTo(LargeSoldier.TypeId));
        Assert.That(errgok.Garrison[GridPosition.BackTop], Is.SameAs(errgok.Garrison[GridPosition.FrontTop]));
        Assert.That(errgok.Garrison[GridPosition.BackCenter]!.DefinitionId, Is.EqualTo(NeutralSoldier.TypeId));
        Assert.That(cities.Single(city => city.Footprint.TopLeft == new WorldPosition(12, 12)).Garrison.IsEmpty, Is.True);
        Assert.That(errgok.SubraceId, Is.EqualTo(state.Subraces.Values.Single(subrace => subrace.PlayerId == errgok.OwnerId).Id));

        string? Name(int id) => database.TextScenariosByScenario[new Db.ScenarioId(map.Id)]
            .Single(text => text.IdObject.Value == CustomMapWriter.GetVillageId(map.Id, id) && text.IdString == Db.TextScenario.NameTextId).Text;
        Assert.That((Name(guarded), Name(human), Name(unnamed)), Is.EqualTo(("Errgok", "Ford", $"Village {unnamed}")));

        // Gold on Empire land pays the Empire; the neutral land mana source pays nobody.
        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Load(globalDb, database, campaignDb, new ScenarioId(map.Id), definitions, globals);
        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        WorldPlayer empire = state.Players.Values.Single(player => player.Race == WorldRace.Human);
        WorldPlayer elves = state.Players.Values.Single(player => player.Race == WorldRace.Elf);
        WorldResourceSet empireNodes = PlayerIncomeCalculator.GetIncomeInfo(empire, state, context).ResourceNodes;
        Assert.That(empireNodes.Gold, Is.GreaterThan(0));
        Assert.That(empireNodes.LifeMana, Is.GreaterThan(0));
        Assert.That(empireNodes.InfernalMana, Is.Zero);
        Assert.That(PlayerIncomeCalculator.GetIncomeInfo(elves, state, context).ResourceNodes, Is.EqualTo(WorldResourceSet.Empty));

        CustomMap reopened = store.Open(map.Id, definitions);
        Assert.That(reopened.Resources, Is.EquivalentTo(map.Resources));
        Assert.That(reopened.Villages.Where(village => village.Id != unnamed), Is.EquivalentTo(map.Villages.Where(village => village.Id != unnamed)));
        Assert.That(reopened.Villages.Single(village => village.Id == unnamed).Name, Is.EqualTo($"Village {unnamed}"));
        Assert.That(reopened.Cells, Is.EqualTo(map.Cells));
        Assert.That(reopened.Players, Is.EqualTo(map.Players));
        Assert.That(reopened.Stacks, Is.Empty, "Village garrisons are not read back as armies.");
    }

    [Test]
    public void StationedArmyTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        CustomMapUnit?[] guard = Slots((GridPosition.FrontCenter, NeutralLeader), (GridPosition.BackCenter, NeutralSoldier));
        CustomMapVillage village = map.PlaceVillage(new WorldPosition(14, 14), 2, WorldRace.Neutral, "Errgok").Village!;

        // Only the entrance takes an army, and only one of the village's owner.
        Assert.That(village.Entrance, Is.EqualTo(new WorldPosition(17, 17)));
        Assert.That(map.PlaceStack(new WorldPosition(15, 15), WorldRace.Neutral, guard).Error, Is.EqualTo(CustomMapStackError.Blocked));
        Assert.That(map.PlaceStack(village.Entrance, WorldRace.Human, Slots((GridPosition.FrontCenter, HumanLeader))).Error, Is.EqualTo(CustomMapStackError.VillageOwner));
        Assert.That(map.PlaceStack(village.Entrance, WorldRace.Neutral, guard).Error, Is.Null);
        Assert.That(map.GetStationedStack(village)!.Position, Is.EqualTo(village.Entrance));
        store.Save(map);

        WorldState state = ScenarioLoader.Load(globalDb, store.LoadDatabase(map.Id), campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables()).State;
        WorldCity city = state.Fortifications.Values.OfType<WorldCity>().Single();
        WorldStack stationed = state.Stacks.Values.Single(stack => stack.Position == city.Entrance);
        Assert.That(stationed.OwnerId, Is.EqualTo(city.OwnerId));
        Assert.That(store.LoadDatabase(map.Id).ScenarioStacksByScenario[new Db.ScenarioId(map.Id)].Single(stack => stack.StackId.Value == CustomMapWriter.GetArmyStackId(map.Id, map.Stacks.Single().Id)).InsideId?.Value,
            Is.EqualTo(CustomMapWriter.GetVillageId(map.Id, village.Id)));
        Assert.That(store.Open(map.Id, definitions).Stacks, Is.EquivalentTo(map.Stacks), "The stationed army is read back.");

        // A new owner takes the army along; removing the village removes its army.
        Assert.That(map.UpdateVillage(village.Id, 2, WorldRace.Human, "Errgok", CustomMapVillage.EmptyGarrison), Is.Null);
        Assert.That(map.Stacks.Single().Owner, Is.EqualTo(WorldRace.Human));
        Assert.That(map.RemoveVillage(village.Id), Is.True);
        Assert.That(map.Stacks, Is.Empty);
    }

    [Test]
    public void UnitLevelTest()
    {
        UnitDefinition soldier = definitions.GetUnit(NeutralSoldier.TypeId);
        UnitDefinition leader = definitions.GetUnit(NeutralLeader.TypeId);

        // A levelled unit has the full hit points of that level; levels stay between its own level and 99.
        CustomMapUnit veteran = CustomMapUnit.From(soldier, soldier.Level + 4, definitions);
        Assert.That(veteran.Level, Is.EqualTo(soldier.Level + 4));
        Assert.That(veteran.Hitpoints, Is.EqualTo(UnitStatsCalculator.GetStats(soldier, veteran.Level, [], definitions, new GlobalVariables()).MaxHitpoints));
        Assert.That(veteran.Hitpoints, Is.GreaterThan(soldier.MaxHitpoints));
        Assert.That(CustomMapUnit.From(soldier, 0, definitions), Is.EqualTo(CustomMapUnit.From(soldier)));
        Assert.That(CustomMapUnit.From(soldier, 500, definitions).Level, Is.EqualTo(CustomMapUnit.MaxLevel));

        CustomMapUnit hero = CustomMapUnit.From(leader, leader.Level + 6, definitions);
        Assert.That(CustomMapStack.ValidateSlots(Slots((GridPosition.FrontCenter, hero with { Level = 100 }))), Is.EqualTo(CustomMapStackError.InvalidLevel));
        Assert.That(CustomMapVillage.ValidateUnits(Slots((GridPosition.FrontCenter, veteran with { Level = 0 })), 2), Is.EqualTo(CustomMapPlacementError.InvalidUnitLevel));

        // Levels are saved for armies, garrisons and ruin guards and the game loads them at full health.
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        Assert.That(map.PlaceStack(new WorldPosition(20, 40), WorldRace.Neutral, Slots((GridPosition.FrontCenter, hero), (GridPosition.BackCenter, veteran))).Error, Is.Null);
        CustomMapVillage village = map.PlaceVillage(new WorldPosition(14, 14), 2, WorldRace.Neutral, "Errgok").Village!;
        Assert.That(map.UpdateVillage(village.Id, 2, WorldRace.Neutral, "Errgok", Slots((GridPosition.FrontTop, CustomMapUnit.From(soldier, 9, definitions)))), Is.Null);
        CustomMapRuin ruin = map.PlaceRuin(new WorldPosition(30, 14), 0, "Rigel").Ruin!;
        Assert.That(map.UpdateRuin(ruin.Id, 0, "Rigel", 0, null, Slots((GridPosition.FrontCenter, CustomMapUnit.From(soldier, 99, definitions)))), Is.Null);
        store.Save(map);

        WorldState state = ScenarioLoader.Load(globalDb, store.LoadDatabase(map.Id), campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables()).State;
        WorldUnit[] stackUnits = [.. state.Stacks.Values.Single(stack => stack.Position == new WorldPosition(20, 40)).Units.GetUnits().Select(entry => entry.Unit)];
        Assert.That(stackUnits.Select(unit => unit.Level), Is.EquivalentTo(new[] { hero.Level, veteran.Level }));
        WorldUnit cityUnit = state.Fortifications.Values.OfType<WorldCity>().Single().Garrison.GetUnits().Single().Unit;
        WorldUnit ruinUnit = state.Fortifications.Values.OfType<WorldRuin>().Single().Garrison.GetUnits().Single().Unit;
        Assert.That((cityUnit.Level, ruinUnit.Level), Is.EqualTo((9, 99)));

        foreach (WorldUnit unit in stackUnits.Append(cityUnit).Append(ruinUnit))
        {
            Assert.That(unit.Hitpoints, Is.EqualTo(UnitStatsCalculator.GetStats(definitions.GetUnit(unit.DefinitionId), unit.Level, [], definitions, new GlobalVariables()).MaxHitpoints));
        }

        CustomMap reopened = store.Open(map.Id, definitions);
        Assert.That(reopened.Stacks, Is.EquivalentTo(map.Stacks));
        Assert.That(reopened.Villages, Is.EqualTo(map.Villages));
        Assert.That(reopened.Ruins, Is.EqualTo(map.Ruins));
    }

    [Test]
    public void CapitalGarrisonAndArmyTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        CustomMapPlayer human = map.Players.Single(player => player.Race == WorldRace.Human);
        (CustomMapUnit?[] garrison, CustomMapUnit?[] army) = store.GetCapitalDefaults(WorldRace.Human, definitions);
        CustomMapUnit guardian = garrison.Single(unit => unit is not null)!;

        // The garrison keeps the race's guardian and holds 6 places in all.
        Assert.That(guardian.IsGuardian, Is.True);
        Assert.That(army.Single(unit => unit is not null)!.IsLeader, Is.True);
        Assert.That(human.Garrison, Is.Empty, "Unset until changed in the editor.");
        Assert.That(map.GetCapitalAt(new WorldPosition(6, 6)), Is.EqualTo(human));
        Assert.That(human.Entrance, Is.EqualTo(new WorldPosition(8, 8)));
        Assert.That(map.UpdateCapital(WorldRace.Human, Slots((GridPosition.FrontTop, NeutralSoldier))), Is.EqualTo(CustomMapPlacementError.Guardian));
        Assert.That(map.UpdateCapital(WorldRace.Human, Slots((GridPosition.FrontTop, guardian), (GridPosition.FrontCenter, guardian))), Is.EqualTo(CustomMapPlacementError.Guardian));
        Assert.That(map.UpdateCapital(WorldRace.Human, Slots((GridPosition.FrontCenter, guardian), (GridPosition.FrontTop, HumanLeader))), Is.EqualTo(CustomMapPlacementError.LeaderInGarrison));
        Assert.That(map.UpdateCapital(WorldRace.Human, Slots((GridPosition.FrontCenter, guardian), (GridPosition.FrontTop, LargeSoldier), (GridPosition.FrontBottom, LargeSoldier), (GridPosition.BackCenter, NeutralSoldier))), Is.Null, "A full grid fits: 6 places.");
        Assert.That(CustomMapVillage.ValidateUnits(Slots((GridPosition.FrontCenter, guardian)), 5), Is.EqualTo(CustomMapPlacementError.Guardian), "Guardians stay in capitals.");
        Assert.That(CustomMapStack.ValidateSlots(Slots((GridPosition.FrontCenter, HumanLeader), (GridPosition.BackCenter, guardian))), Is.EqualTo(CustomMapStackError.Guardian));

        CustomMapUnit veteran = CustomMapUnit.From(definitions.GetUnit(NeutralSoldier.TypeId), 4, definitions);
        CustomMapUnit?[] defenders = Slots((GridPosition.FrontCenter, guardian), (GridPosition.FrontTop, veteran), (GridPosition.FrontBottom, LargeSoldier), (GridPosition.BackCenter, NeutralSoldier));
        Assert.That(map.UpdateCapital(WorldRace.Human, defenders), Is.Null);

        // An army of the capital's owner can be stationed on its entrance.
        Assert.That(map.PlaceStack(new WorldPosition(6, 6), WorldRace.Human, Slots((GridPosition.FrontCenter, HumanLeader))).Error, Is.EqualTo(CustomMapStackError.Blocked));
        Assert.That(map.PlaceStack(new WorldPosition(8, 8), WorldRace.Neutral, Slots((GridPosition.FrontCenter, NeutralLeader))).Error, Is.EqualTo(CustomMapStackError.VillageOwner));
        Assert.That(map.PlaceStack(new WorldPosition(8, 8), WorldRace.Human, Slots((GridPosition.FrontCenter, HumanLeader), (GridPosition.BackCenter, NeutralSoldier))).Error, Is.Null);
        human = map.Players.Single(player => player.Race == WorldRace.Human);
        Assert.That(map.GetStationedStack(human)!.Position, Is.EqualTo(new WorldPosition(8, 8)));
        store.Save(map);

        WorldState state = ScenarioLoader.Load(globalDb, store.LoadDatabase(map.Id), campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables()).State;
        WorldPlayerId humanId = state.Players.Values.Single(player => player.Race == WorldRace.Human).Id;
        WorldCapital capital = state.Fortifications.Values.OfType<WorldCapital>().Single(fortification => fortification.OwnerId == humanId);
        Assert.That(capital.Garrison.GetUnits().Select(entry => entry.Unit.DefinitionId), Is.EquivalentTo(new[] { guardian.TypeId, veteran.TypeId, LargeSoldier.TypeId, NeutralSoldier.TypeId }));
        Assert.That(capital.Garrison.GetUnits().Count(entry => entry.Unit.DefinitionId == veteran.TypeId && entry.Unit.Level == 4), Is.EqualTo(1));
        WorldStack stationed = state.Stacks.Values.Single(stack => stack.Position == capital.Entrance);
        Assert.That((stationed.OwnerId, stationed.Units.GetUnits().Count()), Is.EqualTo((humanId, 2)));
        Assert.That(state.Stacks.Values.Count(stack => stack.OwnerId == humanId), Is.EqualTo(1), "No extra starting leader.");
        WorldPlayerId elfId = state.Players.Values.Single(player => player.Race == WorldRace.Elf).Id;
        Assert.That(state.Stacks.Values.Count(stack => stack.OwnerId == elfId), Is.EqualTo(1), "A capital without an army still gets its starting leader.");

        CustomMap reopened = store.Open(map.Id, definitions);
        Assert.That(reopened.Players, Is.EqualTo(map.Players));
        Assert.That(reopened.Stacks, Is.EquivalentTo(map.Stacks), "The Elf starting leader is not turned into an editor army.");

        // Moving the capital takes the garrison and the army along.
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(6, 12)), Is.True);
        human = map.Players.Single(player => player.Race == WorldRace.Human);
        Assert.That(human.Garrison, Is.EqualTo(defenders));
        Assert.That(map.Stacks.Single().Position, Is.EqualTo(new WorldPosition(10, 16)));
    }

    private ItemDefinitionId[] SomeItems(int count) => [.. globalDb.Items.Keys.Select(id => new ItemDefinitionId(id.Value)).OrderBy(id => id.Value, StringComparer.Ordinal).Take(count)];

    private WorldSpellDefinitionId[] SomeSpells(int count) => [.. globalDb.Spells.Keys.Select(id => new WorldSpellDefinitionId(id.Value)).OrderBy(id => id.Value, StringComparer.Ordinal).Take(count)];

    [Test]
    public void RuinBagSiteRulesTest()
    {
        CustomMap map = CreateMap();
        Random random = new(1);

        // Structures never overlap each other.
        CustomMapRuin ruin = map.PlaceRuin(new WorldPosition(14, 14), 3, "Rigel Ruin").Ruin!;
        Assert.That(ruin.Footprint, Is.EqualTo(new WorldFootprint(new WorldPosition(14, 14), 3, 3)));
        Assert.That(map.PlaceRuin(new WorldPosition(16, 16), 0, "").Error, Is.EqualTo(CustomMapPlacementError.Blocked));
        Assert.That(map.PlaceSite(new WorldPosition(12, 12), CustomMapSiteType.Merchant, 0, "", "").Error, Is.EqualTo(CustomMapPlacementError.Blocked));
        Assert.That(map.PlaceVillage(new WorldPosition(11, 11), 1, WorldRace.Neutral, "").Error, Is.EqualTo(CustomMapPlacementError.Blocked));
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(12, 12)), Is.False);
        Assert.That(map.PlaceSite(new WorldPosition(46, 46), CustomMapSiteType.Trainer, 0, "", "").Error, Is.EqualTo(CustomMapPlacementError.OutsideMap));

        // A site on the lake shore turns its water into land; variants wrap around.
        CustomMapSite site = map.PlaceSite(new WorldPosition(18, 23), CustomMapSiteType.Mercenaries, 7, "Camp", "Hi").Site!;
        Assert.That(site.Variant, Is.EqualTo(2), "Mercenary camps have five looks.");
        Assert.That(site.Footprint.EnumerateTiles().All(tile => map.GetCell(tile).Terrain != WorldMapTerrain.Water), Is.True);

        // Bags lie on land or water, but not on mountains, structures, armies or resources.
        Assert.That(map.PlaceBag(new WorldPosition(25, 25), 9).Bag!.Variant, Is.EqualTo(1));
        Assert.That(map.PlaceBag(new WorldPosition(36, 10), 0).Error, Is.EqualTo(CustomMapPlacementError.Blocked), "Mountain.");
        Assert.That(map.PlaceBag(new WorldPosition(15, 15), 0).Error, Is.EqualTo(CustomMapPlacementError.Blocked), "Ruin.");
        Assert.That(map.PlaceBag(new WorldPosition(10, 30), 0).Error, Is.Null);
        Assert.That(map.GetCell(new WorldPosition(10, 30)).Feature, Is.EqualTo(WorldMapFeature.None), "The forest under a bag is cleared.");
        Assert.That(map.PlaceBag(new WorldPosition(10, 30), 0).Error, Is.EqualTo(CustomMapPlacementError.Occupied));
        Assert.That(map.PlaceResource(new WorldPosition(10, 30), WorldResourceType.Gold), Is.EqualTo(CustomMapPlacementError.Occupied));
        Assert.That(map.PlaceStack(new WorldPosition(10, 30), WorldRace.Neutral, Slots((GridPosition.FrontCenter, NeutralLeader))).Error, Is.EqualTo(CustomMapStackError.Blocked));
        Assert.That(map.Paint(new WorldPosition(10, 30), 0, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random), Is.False);
        Assert.That(map.PlaceRuin(new WorldPosition(9, 29), 0, "").Error, Is.EqualTo(CustomMapPlacementError.Occupied));

        // Structures are protected from brushes and block armies.
        Assert.That(map.Paint(new WorldPosition(15, 15), 0, CustomMapBrush.Water, WorldMapTerrain.Neutral, random), Is.False);
        Assert.That(map.PlaceStack(new WorldPosition(16, 16), WorldRace.Neutral, Slots((GridPosition.FrontCenter, NeutralLeader))).Error, Is.EqualTo(CustomMapStackError.Blocked));

        // Ruin guards: soldiers only, room for six.
        CustomMapUnit?[] guards = Slots((GridPosition.FrontTop, LargeSoldier), (GridPosition.FrontCenter, LargeSoldier), (GridPosition.FrontBottom, LargeSoldier));
        Assert.That(map.UpdateRuin(ruin.Id, 4, "Rigel Ruin", 12000, SomeItems(1)[0], guards), Is.Null);
        CustomMapRuin updated = map.Ruins.Single();
        Assert.That((updated.Variant, updated.Gold, updated.Item), Is.EqualTo((4, CustomMapRuin.MaxGold, SomeItems(1)[0])));
        Assert.That(map.UpdateRuin(ruin.Id, 4, "", 0, null, Slots((GridPosition.FrontCenter, NeutralLeader))), Is.EqualTo(CustomMapPlacementError.LeaderInGarrison));

        // Sites keep only the goods of their type.
        Assert.That(map.UpdateSite(site.Id, 1, "Camp", "Hi", [new CustomMapMerchantItem(SomeItems(1)[0], 2)], SomeSpells(2), [new CustomMapMercenary(NeutralSoldier.TypeId, 3)]), Is.Null);
        CustomMapSite camp = map.Sites.Single();
        Assert.That((camp.Items.Length, camp.Spells.Length, camp.Units.Single()), Is.EqualTo((0, 0, new CustomMapMercenary(NeutralSoldier.TypeId, 3))));
        Assert.That(map.UpdateSite(site.Id, 1, "", "", [], [], [.. Enumerable.Repeat(new CustomMapMercenary(NeutralSoldier.TypeId, 1), CustomMapSite.MaxGoods + 1)]), Is.EqualTo(CustomMapPlacementError.TooManyGoods));
        Assert.That(map.UpdateBag(map.Bags[0].Id, 3, SomeItems(CustomMapBag.MaxItems + 1)), Is.EqualTo(CustomMapPlacementError.TooManyGoods));

        CustomMap copy = map.Clone();
        Assert.That(map.RemoveRuin(ruin.Id) && map.RemoveSite(site.Id) && map.RemoveBag(map.Bags[0].Id), Is.True);
        Assert.That((copy.Ruins.Count, copy.Sites.Count, copy.Bags.Count), Is.EqualTo((1, 1, 2)));
    }

    [Test]
    public void RuinBagSiteSaveAndLoadTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        ItemDefinitionId[] items = SomeItems(4);
        WorldSpellDefinitionId[] spells = SomeSpells(3);

        int ruin = map.PlaceRuin(new WorldPosition(14, 14), 5, "Rigel Ruin").Ruin!.Id;
        map.UpdateRuin(ruin, 5, "Rigel Ruin", 750, items[0], Slots((GridPosition.FrontTop, LargeSoldier), (GridPosition.BackCenter, NeutralSoldier)));
        int unguarded = map.PlaceRuin(new WorldPosition(20, 40), 0, "").Ruin!.Id;
        int bag = map.PlaceBag(new WorldPosition(30, 30), 2).Bag!.Id;
        map.UpdateBag(bag, 2, [items[1], items[2], items[1]]);
        map.PlaceBag(new WorldPosition(24, 24), 1);
        int merchant = map.PlaceSite(new WorldPosition(18, 6), CustomMapSiteType.Merchant, 3, "Ebhon's Shop", "My wares can help you.").Site!.Id;
        map.UpdateSite(merchant, 3, "Ebhon's Shop", "My wares can help you.", [new CustomMapMerchantItem(items[3], 2), new CustomMapMerchantItem(items[0], 1)], [], []);
        int mage = map.PlaceSite(new WorldPosition(26, 6), CustomMapSiteType.MageTower, 1, "Malavien's Tower", "").Site!.Id;
        map.UpdateSite(mage, 1, "Malavien's Tower", "", [], spells, []);
        int camp = map.PlaceSite(new WorldPosition(10, 20), CustomMapSiteType.Mercenaries, 4, "Guther's Camp", "Finest soldiers.").Site!.Id;
        map.UpdateSite(camp, 4, "Guther's Camp", "Finest soldiers.", [], [], [new CustomMapMercenary(NeutralSoldier.TypeId, 2), new CustomMapMercenary(LargeSoldier.TypeId, 1)]);
        map.PlaceSite(new WorldPosition(30, 18), CustomMapSiteType.Trainer, 2, "Metaca's Camp", "");
        store.Save(map);

        ScenarioDatabase database = store.LoadDatabase(map.Id);
        WorldState state = ScenarioLoader.Load(globalDb, database, campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables()).State;

        WorldRuin rigel = state.Fortifications.Values.OfType<WorldRuin>().Single(candidate => candidate.Footprint.TopLeft == new WorldPosition(14, 14));
        Assert.That((rigel.Variant.Value, rigel.Resources.Gold, rigel.Item, rigel.LooterId), Is.EqualTo((5, 750, (ItemDefinitionId?)items[0], (WorldPlayerId?)null)));
        Assert.That(rigel.Garrison[GridPosition.FrontTop]!.DefinitionId, Is.EqualTo(LargeSoldier.TypeId));
        Assert.That(rigel.Garrison[GridPosition.BackCenter]!.DefinitionId, Is.EqualTo(NeutralSoldier.TypeId));
        WorldRuin empty = state.Fortifications.Values.OfType<WorldRuin>().Single(candidate => candidate.Footprint.TopLeft == new WorldPosition(20, 40));
        Assert.That((empty.Garrison.IsEmpty, empty.Resources, empty.Item), Is.EqualTo((true, WorldResourceSet.Empty, (ItemDefinitionId?)null)));

        WorldBag loot = state.Bags.Values.Single(candidate => candidate.Position == new WorldPosition(30, 30));
        Assert.That(loot.Inventory.Items.Select(item => item.DefinitionId), Is.EqualTo(new[] { items[1], items[2], items[1] }));
        Assert.That(loot.Variant.Value, Is.EqualTo(2));
        Assert.That(state.Bags.Values.Single(candidate => candidate.Position == new WorldPosition(24, 24)).Inventory.Items, Is.Empty);

        Assert.That(state.Sites.Values.OfType<WorldMerchantSite>().Single().Items, Is.EqualTo(new[] { new WorldMerchantItem(items[3], 2), new WorldMerchantItem(items[0], 1) }));
        Assert.That(state.Sites.Values.OfType<WorldMageSite>().Single().Spells, Is.EqualTo(spells));
        Assert.That(state.Sites.Values.OfType<WorldMercenarySite>().Single().Units.Select(unit => (unit.DefinitionId, unit.Level)), Is.EqualTo(new[] { (NeutralSoldier.TypeId, 2), (LargeSoldier.TypeId, 1) }));
        Assert.That(state.Sites.Values.OfType<WorldTrainerSite>().Single().Variant.Value, Is.EqualTo(2));

        string? Text(string objectId, string textId) => database.TextScenariosByScenario[new Db.ScenarioId(map.Id)]
            .Single(text => text.IdObject.Value == objectId && text.IdString == textId).Text;
        Assert.That(Text(CustomMapWriter.GetRuinId(map.Id, ruin), Db.TextScenario.RuinTitleTextId), Is.EqualTo("Rigel Ruin"));
        Assert.That(Text(CustomMapWriter.GetRuinId(map.Id, unguarded), Db.TextScenario.RuinTitleTextId), Is.EqualTo($"Ruin {unguarded}"));
        Assert.That(Text(CustomMapWriter.GetSiteId(map.Id, merchant), Db.TextScenario.SiteDescriptionTextId), Is.EqualTo("My wares can help you."));

        CustomMap reopened = store.Open(map.Id, definitions);
        Assert.That(reopened.Ruins.Where(candidate => candidate.Id != unguarded), Is.EquivalentTo(map.Ruins.Where(candidate => candidate.Id != unguarded)));
        Assert.That(reopened.Bags, Is.EquivalentTo(map.Bags));
        Assert.That(reopened.Sites, Is.EquivalentTo(map.Sites));
        Assert.That(reopened.Cells, Is.EqualTo(map.Cells));
        Assert.That(reopened.Stacks, Is.Empty, "Ruin guards are not read back as armies.");
    }

    [Test]
    public void VillagePlayTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        Random random = new(1);

        // Neutral villages between the capitals, a guarded one and a free one, and mines on both sides.
        int guarded = map.PlaceVillage(new WorldPosition(14, 4), 2, WorldRace.Neutral, "Errgok").Village!.Id;
        map.UpdateVillage(guarded, 2, WorldRace.Neutral, "Errgok", Slots((GridPosition.FrontCenter, NeutralSoldier)));
        map.PlaceVillage(new WorldPosition(28, 4), 1, WorldRace.Neutral, "Cusnelle");
        map.PlaceVillage(new WorldPosition(4, 16), 2, WorldRace.Human, "Ford");
        map.Paint(new WorldPosition(12, 10), 1, CustomMapBrush.Land, WorldMapTerrain.Human, random);
        map.PlaceResource(new WorldPosition(12, 10), WorldResourceType.Gold);
        map.PlaceResource(new WorldPosition(34, 12), WorldResourceType.GroveMana);

        // A stationed army, a guarded ruin, treasure and one site of each type.
        map.PlaceStack(new WorldPosition(7, 19), WorldRace.Human, Slots((GridPosition.FrontCenter, HumanLeader)));
        int ruin = map.PlaceRuin(new WorldPosition(20, 12), 2, "Rigel Ruin").Ruin!.Id;
        map.UpdateRuin(ruin, 2, "Rigel Ruin", 500, SomeItems(1)[0], Slots((GridPosition.FrontCenter, NeutralSoldier)));
        int bag = map.PlaceBag(new WorldPosition(12, 14), 0).Bag!.Id;
        map.UpdateBag(bag, 0, SomeItems(2));
        int merchant = map.PlaceSite(new WorldPosition(10, 20), CustomMapSiteType.Merchant, 0, "Shop", "").Site!.Id;
        map.UpdateSite(merchant, 0, "Shop", "", [new CustomMapMerchantItem(SomeItems(3)[2], 3)], [], []);
        int mage = map.PlaceSite(new WorldPosition(30, 14), CustomMapSiteType.MageTower, 0, "Tower", "").Site!.Id;
        map.UpdateSite(mage, 0, "Tower", "", [], SomeSpells(2), []);
        int camp = map.PlaceSite(new WorldPosition(20, 20), CustomMapSiteType.Mercenaries, 0, "Camp", "").Site!.Id;
        map.UpdateSite(camp, 0, "Camp", "", [], [], [new CustomMapMercenary(NeutralSoldier.TypeId, 1)]);
        map.PlaceSite(new WorldPosition(30, 20), CustomMapSiteType.Trainer, 0, "Trainer", "");
        Assert.That((map.Stacks.Count, map.Ruins.Count, map.Bags.Count, map.Sites.Count), Is.EqualTo((1, 1, 1, 4)));
        store.Save(map);

        ScenarioConfiguration configuration = new([new ScenarioPlayerConfiguration(WorldRace.Human, LordCategory.Warrior, new WorldObjectVariant(0), null, LeaderName: null)], WorldDifficulty.Average, Leader: null);
        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Create(globalDb, store.LoadDatabase(map.Id), campaignDb, new ScenarioId(map.Id), definitions, globals, configuration);
        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        WorldState aiState = scenario.State with
        {
            Players = scenario.State.Players.SetItems(scenario.State.Players.Select(pair => KeyValuePair.Create(pair.Key, pair.Value with { IsAi = true })))
        };
        GameSession session = new(context, aiState);
        Assert.That(session.World.DispatchCommand(new WorldStartGameCommand()).Error, Is.Null);

        WorldPlayer empire = session.World.State.Players.Values.Single(player => player.Race == WorldRace.Human);
        int goldBefore = empire.Resources.Gold;
        TestWorldAiRunner runner = new(session, context);

        for (int i = 0; i < 12; i++)
        {
            runner.PlayTurn();
        }

        Assert.That(session.World.State.TurnInfo.TurnNumber, Is.GreaterThanOrEqualTo(4));
        Assert.That(session.World.State.Fortifications.Values.OfType<WorldCity>().Count(), Is.EqualTo(3));
        TestContext.Out.WriteLine(string.Join(", ", session.World.State.Fortifications.Values.OfType<WorldCity>().Select(city => $"{city.Id}: {session.World.State.Players[city.OwnerId].Race}")));
        TestContext.Out.WriteLine($"Turn {session.World.State.TurnInfo.TurnNumber}, Empire gold {goldBefore} -> {session.World.State.Players[empire.Id].Resources.Gold}");
    }
}

using System.Collections.Immutable;
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
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(4, 4)), Is.Null);
        Assert.That(map.PlaceCapital(WorldRace.Elf, new WorldPosition(38, 4)), Is.Null);

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
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(30, 30)), Is.EqualTo(CustomMapPlacementError.OutsideMap), "Capital outside the map.");
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(2, 2)), Is.Null);
        Assert.That(map.PlaceCapital(WorldRace.Undead, new WorldPosition(4, 4)), Is.EqualTo(CustomMapPlacementError.Blocked), "Overlaps the human capital.");
        Assert.That(map.PlaceCapital(WorldRace.Elf, new WorldPosition(20, 20)), Is.EqualTo(CustomMapPlacementError.UnknownOwner), "Elves are not on this map.");
        Assert.That(map.PlaceCapital(WorldRace.Undead, new WorldPosition(20, 20)), Is.Null);
        Assert.That(map.Validate(), Is.Empty);
        Assert.That(map.GetCell(new WorldPosition(4, 4)).Terrain, Is.EqualTo(WorldMapTerrain.Human));

        // Capitals are protected from painting.
        Assert.That(map.Paint(new WorldPosition(4, 4), 0, CustomMapBrush.Water, WorldMapTerrain.Neutral, random), Is.False);
        Assert.That(map.GetCell(new WorldPosition(4, 4)).Terrain, Is.EqualTo(WorldMapTerrain.Human));

        // Moving a capital keeps one capital per player.
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(10, 2)), Is.Null);
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
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(18, 38)), Is.EqualTo(CustomMapPlacementError.Occupied));

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
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(18, 18)), Is.EqualTo(CustomMapPlacementError.Occupied), "A capital cannot cover a resource.");

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
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(12, 12)), Is.EqualTo(CustomMapPlacementError.Blocked));

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
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(6, 12)), Is.Null);
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
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(12, 12)), Is.EqualTo(CustomMapPlacementError.Blocked));
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

    // A landmark of the given size; mountain picks a landmark of the mountain type.
    private WorldLandmarkDefinition Landmark(int size, bool mountain = false)
    {
        return globalDb.Landmarks.Keys
            .Select(id => definitions.GetLandmark(new WorldLandmarkDefinitionId(id.Value)))
            .OrderBy(definition => definition.Id.Value, StringComparer.OrdinalIgnoreCase)
            .First(definition => definition.Width == size && definition.Height == size && definition.Mountain == mountain);
    }

    [Test]
    public void LandmarkRulesTest()
    {
        CustomMap map = CreateMap();
        Random random = new(1);
        WorldLandmarkDefinition big = Landmark(3);
        WorldLandmarkDefinition small = Landmark(1);

        // On land, on the lake and half in it; the forest under one is cleared.
        CustomMapLandmark landmark = map.PlaceLandmark(new WorldPosition(14, 14), big).Landmark!;
        Assert.That((landmark.Id, landmark.Type, landmark.Footprint), Is.EqualTo((1, big.Id, new WorldFootprint(new WorldPosition(14, 14), 3, 3))));
        Assert.That(map.PlaceLandmark(new WorldPosition(23, 23), small).Error, Is.Null, "Water.");
        Assert.That(map.GetCell(new WorldPosition(23, 23)).Terrain, Is.EqualTo(WorldMapTerrain.Water), "The water stays.");
        Assert.That(map.PlaceLandmark(new WorldPosition(9, 29), big).Error, Is.Null);
        Assert.That(new WorldFootprint(new WorldPosition(9, 29), 3, 3).EnumerateTiles().All(tile => map.GetCell(tile).Feature == WorldMapFeature.None), Is.True);

        // Nothing overlaps a landmark, and it overlaps nothing.
        Assert.That(map.PlaceLandmark(new WorldPosition(16, 16), small).Error, Is.EqualTo(CustomMapPlacementError.Blocked), "Landmark.");
        Assert.That(map.PlaceLandmark(new WorldPosition(5, 5), small).Error, Is.EqualTo(CustomMapPlacementError.Blocked), "Capital.");
        Assert.That(map.PlaceLandmark(new WorldPosition(36, 10), small).Error, Is.EqualTo(CustomMapPlacementError.Blocked), "Mountain.");
        Assert.That(map.PlaceLandmark(new WorldPosition(46, 46), big).Error, Is.EqualTo(CustomMapPlacementError.OutsideMap));
        Assert.That(map.PlaceRuin(new WorldPosition(12, 12), 0, "").Error, Is.EqualTo(CustomMapPlacementError.Blocked));
        Assert.That(map.PlaceVillage(new WorldPosition(15, 16), 1, WorldRace.Neutral, "").Error, Is.EqualTo(CustomMapPlacementError.Blocked));
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(12, 12)), Is.EqualTo(CustomMapPlacementError.Blocked));
        Assert.That(map.PlaceStack(new WorldPosition(15, 15), WorldRace.Neutral, Slots((GridPosition.FrontCenter, NeutralLeader))).Error, Is.EqualTo(CustomMapStackError.Blocked));
        Assert.That(map.PlaceResource(new WorldPosition(15, 15), WorldResourceType.Gold), Is.EqualTo(CustomMapPlacementError.Blocked));
        Assert.That(map.PlaceBag(new WorldPosition(15, 15), 0).Error, Is.EqualTo(CustomMapPlacementError.Blocked));
        map.PlaceBag(new WorldPosition(30, 14), 0);
        Assert.That(map.PlaceLandmark(new WorldPosition(29, 13), big).Error, Is.EqualTo(CustomMapPlacementError.Occupied), "Bag.");

        // Land and water can be painted under it; forests and mountains can't.
        Assert.That(map.Paint(new WorldPosition(15, 15), 0, CustomMapBrush.Land, WorldMapTerrain.Undead, random), Is.True);
        Assert.That(map.GetCell(new WorldPosition(15, 15)).Terrain, Is.EqualTo(WorldMapTerrain.Undead));
        Assert.That(map.Paint(new WorldPosition(15, 15), 0, CustomMapBrush.Forest, WorldMapTerrain.Neutral, random), Is.False);
        Assert.That(map.Paint(new WorldPosition(15, 15), 0, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random), Is.False);
        Assert.That(map.GetLandmarkAt(new WorldPosition(16, 16)), Is.EqualTo(landmark));

        CustomMap copy = map.Clone();
        Assert.That(map.RemoveLandmark(landmark.Id), Is.True);
        Assert.That(map.GetLandmarkAt(new WorldPosition(16, 16)), Is.Null);
        Assert.That((map.Landmarks.Count, copy.Landmarks.Count), Is.EqualTo((2, 3)));
        Assert.That(map.PlaceLandmark(new WorldPosition(14, 14), small).Landmark!.Id, Is.EqualTo(4), "IDs are not reused.");
    }

    [Test]
    public void LandmarkSaveAndLoadTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        WorldLandmarkDefinition big = Landmark(3);
        WorldLandmarkDefinition small = Landmark(1);
        WorldLandmarkDefinition mountain = Landmark(3, mountain: true);

        map.PlaceLandmark(new WorldPosition(14, 14), big);
        map.PlaceLandmark(new WorldPosition(23, 23), small);
        int rocky = map.PlaceLandmark(new WorldPosition(20, 40), mountain).Landmark!.Id;
        store.Save(map);

        ScenarioDatabase database = store.LoadDatabase(map.Id);
        WorldState state = ScenarioLoader.Load(globalDb, database, campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables()).State;

        Assert.That(state.Landmarks.Values.Select(landmark => (landmark.Id.Value, landmark.DefinitionId, landmark.Footprint)), Is.EquivalentTo(new[]
        {
            ("S600MM0001", big.Id, new WorldFootprint(new WorldPosition(14, 14), 3, 3)),
            ("S600MM0002", small.Id, new WorldFootprint(new WorldPosition(23, 23), 1, 1)),
            (CustomMapWriter.GetLandmarkId(map.Id, rocky), mountain.Id, new WorldFootprint(new WorldPosition(20, 40), 3, 3))
        }));

        // In the game landmarks block armies; the mountain type also turns its tiles into mountains.
        WorldPlan plan = WorldPlan.For(state);
        Assert.That(plan.IsObstacleAt(new WorldPosition(15, 15)), Is.True);
        Assert.That(plan.GetLandmarkAt(new WorldPosition(23, 23))!.DefinitionId, Is.EqualTo(small.Id));
        Assert.That(state.Map.GetCellAt(new WorldPosition(21, 41)).Feature, Is.EqualTo(WorldMapFeature.Mountain));
        Assert.That(state.Map.GetCellAt(new WorldPosition(15, 15)).Feature, Is.EqualTo(WorldMapFeature.None));

        // Reopened, the map is the same, with the mountain-type landmark's tiles free again.
        CustomMap reopened = store.Open(map.Id, definitions);
        Assert.That(reopened.Landmarks, Is.EquivalentTo(map.Landmarks));
        Assert.That(reopened.Cells, Is.EqualTo(map.Cells));
        Assert.That(reopened.RemoveLandmark(rocky), Is.True);
        Assert.That(reopened.PlaceStack(new WorldPosition(21, 41), WorldRace.Neutral, Slots((GridPosition.FrontCenter, NeutralLeader))).Error, Is.Null);

        // The editor's quick preview update shows landmarks too.
        WorldState preview = CustomMapStore.ApplyTerrain(state with { Landmarks = System.Collections.Immutable.ImmutableSortedDictionary<WorldLandmarkId, WorldLandmark>.Empty }, map);
        Assert.That(preview.Landmarks.Values.Select(landmark => landmark.Footprint), Is.EquivalentTo(state.Landmarks.Values.Select(landmark => landmark.Footprint)));
    }

    [Test]
    public void RoadTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        Random random = new(1);

        // A road over open land and through the forest (which it clears); never on water, mountains, capitals or landmarks.
        for (int x = 6; x <= 14; x++)
        {
            Assert.That(map.SetRoad(new WorldPosition(x, 30), true), Is.True, $"Road at {x}.");
        }

        Assert.That(map.SetRoad(new WorldPosition(14, 31), true), Is.True);
        Assert.That(map.GetCell(new WorldPosition(10, 30)).Feature, Is.EqualTo(WorldMapFeature.Road), "Forest cleared.");
        Assert.That(map.SetRoad(new WorldPosition(10, 30), true), Is.False, "Already a road.");
        Assert.That(map.SetRoad(new WorldPosition(24, 24), true), Is.False, "Water.");
        Assert.That(map.SetRoad(new WorldPosition(36, 10), true), Is.False, "Mountain.");
        Assert.That(map.SetRoad(new WorldPosition(5, 5), true), Is.False, "Capital.");
        map.PlaceLandmark(new WorldPosition(20, 4), Landmark(1));
        Assert.That(map.SetRoad(new WorldPosition(20, 4), true), Is.False, "Landmark.");
        Assert.That(map.SetRoad(new WorldPosition(60, 4), true), Is.False, "Outside the map.");

        // Land brushes keep it, water replaces it; lifting a road leaves open land.
        map.Paint(new WorldPosition(7, 30), 0, CustomMapBrush.Land, WorldMapTerrain.Undead, random);
        Assert.That(map.GetCell(new WorldPosition(7, 30)), Is.EqualTo(new WorldMapCell(WorldMapTerrain.Undead, WorldMapFeature.Road, new WorldObjectVariant(0))));
        map.Paint(new WorldPosition(6, 30), 0, CustomMapBrush.Water, WorldMapTerrain.Neutral, random);
        Assert.That(map.GetCell(new WorldPosition(6, 30)).Terrain, Is.EqualTo(WorldMapTerrain.Water));
        Assert.That(map.SetRoad(new WorldPosition(8, 30), false), Is.True);
        Assert.That(map.SetRoad(new WorldPosition(8, 30), false), Is.False);
        Assert.That(map.GetCell(new WorldPosition(8, 30)).Feature, Is.EqualTo(WorldMapFeature.None));

        // Saved as SRoad rows: the game loads the roads (cheaper to walk) and the editor reads them back.
        store.Save(map);
        ScenarioDatabase database = store.LoadDatabase(map.Id);
        Assert.That(database.ScenarioRoadsByScenario[new Db.ScenarioId(map.Id)].Single(road => road.PositionX == 14 && road.PositionY == 30).Index, Is.EqualTo(10), "South + west, numbered like the original maps.");
        WorldState state = ScenarioLoader.Load(globalDb, database, campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables()).State;
        WorldPosition[] roads = [.. Enumerable.Range(0, map.Size * map.Size).Select(i => new WorldPosition(i % map.Size, i / map.Size)).Where(p => state.Map.GetCellAt(p).Feature == WorldMapFeature.Road)];
        Assert.That(roads, Has.Length.EqualTo(8));
        Assert.That(store.Open(map.Id, definitions).Cells, Is.EqualTo(map.Cells));
    }

    [Test]
    public void RoadPathTest()
    {
        CustomMap map = CreateMap();
        Random random = new(1);

        static void AssertJoined(IReadOnlyList<WorldPosition> path)
        {
            for (int i = 1; i < path.Count; i++)
            {
                Assert.That(Math.Abs(path[i].X - path[i - 1].X) + Math.Abs(path[i].Y - path[i - 1].Y), Is.EqualTo(1), $"Step {i} shares an edge.");
            }
        }

        // Open land: the shortest road over tile edges, as an even staircase along the straight line.
        ImmutableArray<WorldPosition> open = map.FindRoadPath(new WorldPosition(10, 12), new WorldPosition(16, 18))!.Value;
        AssertJoined(open);
        Assert.That((open[0], open[^1], open.Length), Is.EqualTo((new WorldPosition(10, 12), new WorldPosition(16, 18), 13)));
        Assert.That(open.All(tile => Math.Abs((tile.X - 10) - (tile.Y - 12)) <= 1), Is.True, "Close to the line.");
        Assert.That(map.FindRoadPath(new WorldPosition(10, 12), new WorldPosition(10, 12)), Is.EqualTo(ImmutableArray.Create(new WorldPosition(10, 12))));

        // Around the lake, never on water; nothing starts or ends on water, a capital or outside the map.
        ImmutableArray<WorldPosition> around = map.FindRoadPath(new WorldPosition(16, 24), new WorldPosition(32, 24))!.Value;
        AssertJoined(around);
        Assert.That(around.Any(tile => map.GetRoadBlock(tile) != CustomMapTileReason.None), Is.False);
        Assert.That(around.Length, Is.GreaterThan(17));
        Assert.That(map.FindRoadPath(new WorldPosition(16, 24), new WorldPosition(24, 24)), Is.Null);
        Assert.That(map.FindRoadPath(new WorldPosition(5, 5), new WorldPosition(16, 24)), Is.Null);
        Assert.That(map.FindRoadPath(new WorldPosition(16, 24), new WorldPosition(60, 24)), Is.Null);
        Assert.That(map.GetRoadBlock(new WorldPosition(36, 10)), Is.EqualTo(CustomMapTileReason.Mountain));

        // An island can't be reached.
        map.Paint(new WorldPosition(10, 40), 2, CustomMapBrush.Water, WorldMapTerrain.Neutral, random);
        map.Paint(new WorldPosition(10, 40), 0, CustomMapBrush.Land, WorldMapTerrain.Neutral, random);
        Assert.That(map.FindRoadPath(new WorldPosition(4, 46), new WorldPosition(10, 40)), Is.Null);

        // A new road joins and follows an existing one instead of running beside it.
        for (int x = 16; x <= 36; x++)
        {
            map.SetRoad(new WorldPosition(x, 44), true);
        }

        ImmutableArray<WorldPosition> joined = map.FindRoadPath(new WorldPosition(16, 43), new WorldPosition(36, 43))!.Value;
        AssertJoined(joined);
        Assert.That(joined.Count(tile => tile.Y == 44), Is.GreaterThanOrEqualTo(19));

        // Preview and laying: new tiles green, forest tiles amber (cleared), existing road left out; the shown
        // map has the roads before anything is laid.
        ImmutableArray<WorldPosition> forest = map.FindRoadPath(new WorldPosition(6, 30), new WorldPosition(14, 30))!.Value;
        CustomMapPlacementPreview preview = map.PreviewRoad(forest);
        Assert.That(preview.Fits, Is.True);
        Assert.That(preview.Count(CustomMapTileReason.ClearsForest), Is.GreaterThan(0));
        WorldMap shown = map.ToWorldMap(forest);
        Assert.That(forest.All(tile => shown.GetCellAt(tile).Feature == WorldMapFeature.Road), Is.True);
        Assert.That(map.GetCell(forest[0]).Feature, Is.Not.EqualTo(WorldMapFeature.Road));
        Assert.That(map.LayRoad(forest), Is.EqualTo((preview.Tiles.Length, (CustomMapPlacementError?)null)));
        Assert.That(forest.All(tile => map.GetCell(tile).Feature == WorldMapFeature.Road), Is.True);
        Assert.That(map.PreviewRoad(forest).Tiles, Is.Empty);
        Assert.That(map.LayRoad(forest).Laid, Is.Zero);
        Assert.That(map.LayRoad([new WorldPosition(20, 30), new WorldPosition(24, 24)]), Is.EqualTo((0, (CustomMapPlacementError?)CustomMapPlacementError.Blocked)));
        Assert.That(map.GetCell(new WorldPosition(20, 30)).Feature, Is.Not.EqualTo(WorldMapFeature.Road), "Nothing laid when blocked.");
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

        // Landmarks in the way, one of the mountain type, and one in the lake.
        map.PlaceLandmark(new WorldPosition(20, 4), Landmark(3));
        map.PlaceLandmark(new WorldPosition(16, 16), Landmark(1));
        map.PlaceLandmark(new WorldPosition(26, 28), Landmark(3, mountain: true));
        map.PlaceLandmark(new WorldPosition(24, 24), Landmark(1));
        Assert.That((map.Stacks.Count, map.Ruins.Count, map.Bags.Count, map.Sites.Count, map.Landmarks.Count), Is.EqualTo((1, 1, 1, 4, 4)));
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

    [Test]
    public void RodRulesTest()
    {
        CustomMap map = CreateMap();
        Random random = new(1);

        // On open land: the rod turns 2 tiles into its race's land, in the game's order (south, then north first).
        CustomMapRod rod = map.PlaceRod(new WorldPosition(14, 14), WorldRace.Human).Rod!;
        Assert.That((rod.Id, rod.Position, rod.Owner), Is.EqualTo((1, new WorldPosition(14, 14), WorldRace.Human)));
        Assert.That(map.GetRodAt(new WorldPosition(14, 14)), Is.EqualTo(rod));
        Assert.That(TerrainTiles(map, WorldMapTerrain.Human).Where(tile => tile.GetDistance(new WorldPosition(14, 14)) <= 3),
            Is.EquivalentTo(new[] { new WorldPosition(14, 15), new WorldPosition(14, 13) }));
        Assert.That(map.GetCell(new WorldPosition(14, 14)).Terrain, Is.EqualTo(WorldMapTerrain.Neutral), "The rod's own tile isn't claimed.");

        // No other rod within 2 tiles; the preview points at the rod that is too close.
        CustomMapPlacementPreview tooClose = map.PreviewRod(new WorldPosition(16, 16), WorldRace.Elf);
        Assert.That(tooClose.Error, Is.EqualTo(CustomMapPlacementError.Blocked));
        Assert.That(tooClose.FirstBlocked!.Value.Position, Is.EqualTo(new WorldPosition(14, 14)));
        Assert.That(map.PlaceRod(new WorldPosition(16, 14), WorldRace.Human).Error, Is.EqualTo(CustomMapPlacementError.Blocked));
        Assert.That(map.PlaceRod(new WorldPosition(14, 14), WorldRace.Human).Error, Is.EqualTo(CustomMapPlacementError.Blocked), "A rod is already there.");

        // Water, mountains, capitals and landmarks block it; an army, resource or bag is in the way.
        map.PlaceLandmark(new WorldPosition(20, 4), Landmark(1));
        map.PlaceStack(new WorldPosition(30, 14), WorldRace.Neutral, Slots((GridPosition.FrontCenter, NeutralLeader)));
        map.PlaceResource(new WorldPosition(12, 20), WorldResourceType.Gold);
        map.PlaceBag(new WorldPosition(16, 22), 0);
        Assert.That(map.PlaceRod(new WorldPosition(24, 24), WorldRace.Human).Error, Is.EqualTo(CustomMapPlacementError.Blocked), "Water.");
        Assert.That(map.PlaceRod(new WorldPosition(36, 10), WorldRace.Human).Error, Is.EqualTo(CustomMapPlacementError.Blocked), "Mountain.");
        Assert.That(map.PlaceRod(new WorldPosition(6, 6), WorldRace.Human).Error, Is.EqualTo(CustomMapPlacementError.Blocked), "Capital.");
        Assert.That(map.PlaceRod(new WorldPosition(20, 4), WorldRace.Human).Error, Is.EqualTo(CustomMapPlacementError.Blocked), "Landmark.");
        Assert.That(map.PlaceRod(new WorldPosition(30, 14), WorldRace.Human).Error, Is.EqualTo(CustomMapPlacementError.Occupied), "Army.");
        Assert.That(map.PlaceRod(new WorldPosition(12, 20), WorldRace.Human).Error, Is.EqualTo(CustomMapPlacementError.Occupied), "Resource.");
        Assert.That(map.PlaceRod(new WorldPosition(16, 22), WorldRace.Human).Error, Is.EqualTo(CustomMapPlacementError.Occupied), "Bag.");
        Assert.That(map.PlaceRod(new WorldPosition(60, 4), WorldRace.Human).Error, Is.EqualTo(CustomMapPlacementError.OutsideMap));

        // Only races on the map: no neutral rods.
        Assert.That(map.PlaceRod(new WorldPosition(30, 20), WorldRace.Neutral).Error, Is.EqualTo(CustomMapPlacementError.UnknownOwner));
        Assert.That(map.PlaceRod(new WorldPosition(30, 20), WorldRace.Dwarf).Error, Is.EqualTo(CustomMapPlacementError.UnknownOwner));

        // Forests and roads are fine and stay.
        Assert.That(map.PlaceRod(new WorldPosition(10, 30), WorldRace.Elf).Error, Is.Null);
        Assert.That(map.GetCell(new WorldPosition(10, 30)).Feature, Is.EqualTo(WorldMapFeature.Forest));
        Assert.That(map.GetCell(new WorldPosition(10, 31)), Is.EqualTo(map.GetCell(new WorldPosition(10, 31)) with { Terrain = WorldMapTerrain.Elf, Feature = WorldMapFeature.Forest }));
        Assert.That(map.SetRoad(new WorldPosition(20, 10), true), Is.True);
        Assert.That(map.PlaceRod(new WorldPosition(20, 10), WorldRace.Human).Error, Is.Null);
        Assert.That(map.GetCell(new WorldPosition(20, 10)).Feature, Is.EqualTo(WorldMapFeature.Road));

        // Nothing else goes on a rod; water and mountain brushes leave it alone, land can be painted under it.
        WorldPosition at = new(14, 14);
        Assert.That(map.PlaceStack(at, WorldRace.Human, Slots((GridPosition.FrontCenter, HumanLeader))).Error, Is.EqualTo(CustomMapStackError.Blocked));
        Assert.That(map.PlaceResource(at, WorldResourceType.Gold), Is.EqualTo(CustomMapPlacementError.Occupied));
        Assert.That(map.PlaceBag(at, 0).Error, Is.EqualTo(CustomMapPlacementError.Occupied));
        Assert.That(map.PlaceVillage(new WorldPosition(12, 12), 1, WorldRace.Neutral, "").Error, Is.EqualTo(CustomMapPlacementError.Occupied));
        Assert.That(map.PlaceLandmark(new WorldPosition(13, 13), Landmark(3)).Error, Is.EqualTo(CustomMapPlacementError.Occupied));
        Assert.That(map.SetRoad(at, true), Is.False);
        Assert.That(map.FindRoadPath(new WorldPosition(14, 17), new WorldPosition(14, 11))!.Value, Does.Not.Contain(at), "Roads go around rods.");
        Assert.That(map.Paint(at, 0, CustomMapBrush.Water, WorldMapTerrain.Neutral, random), Is.False);
        Assert.That(map.Paint(at, 0, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random), Is.False);
        Assert.That(map.Paint(at, 0, CustomMapBrush.Land, WorldMapTerrain.Undead, random), Is.True);
        Assert.That(map.GetCell(at).Terrain, Is.EqualTo(WorldMapTerrain.Undead));

        // Removing a rod leaves its land, as in the game; IDs are not reused and copies keep their rods.
        CustomMap copy = map.Clone();
        Assert.That(map.RemoveRod(at), Is.True);
        Assert.That(map.RemoveRod(at), Is.False);
        Assert.That(map.GetCell(new WorldPosition(14, 15)).Terrain, Is.EqualTo(WorldMapTerrain.Human));
        Assert.That((map.Rods.Count, copy.Rods.Count), Is.EqualTo((2, 3)));
        Assert.That(map.PlaceRod(new WorldPosition(14, 14), WorldRace.Human).Rod!.Id, Is.EqualTo(4));
    }

    [Test]
    public void RodResourceTest()
    {
        CustomMap map = CreateMap();

        // Land that reaches a resource takes the whole 3x3 around it along: the mine changes owner.
        map.PlaceResource(new WorldPosition(34, 18), WorldResourceType.Gold);
        CustomMapPlacementPreview preview = map.PreviewRod(new WorldPosition(34, 20), WorldRace.Elf);
        Assert.That(preview.Error, Is.Null);
        Assert.That(preview.Count(CustomMapTileReason.ClaimsLand), Is.EqualTo(10), "2 tiles plus the mine's 3x3.");
        Assert.That(TilesWith(preview, CustomMapTileStatus.Ok), Is.EquivalentTo(new[] { new WorldPosition(34, 20) }));
        Assert.That(map.GetLandOwner(new WorldPosition(34, 18)), Is.Null);
        map.PlaceRod(new WorldPosition(34, 20), WorldRace.Elf);
        Assert.That(map.GetLandOwner(new WorldPosition(34, 18)), Is.EqualTo(WorldRace.Elf));
        Assert.That(new WorldFootprint(new WorldPosition(33, 17), 3, 3).EnumerateTiles().All(tile => map.GetLandOwner(tile) == WorldRace.Elf), Is.True);
        Assert.That(TerrainTiles(map, WorldMapTerrain.Elf).Count(tile => tile.GetDistance(new WorldPosition(34, 19)) <= 3), Is.EqualTo(10));

        // A rod next to a mine protects it: another race's land doesn't spread into the 3x3 around it.
        map.PlaceResource(new WorldPosition(40, 30), WorldResourceType.LifeMana);
        map.PlaceRod(new WorldPosition(41, 31), WorldRace.Human);
        Assert.That(map.GetLandOwner(new WorldPosition(40, 30)), Is.EqualTo(WorldRace.Human));
        CustomMapPlacementPreview blocked = map.PreviewRod(new WorldPosition(40, 28), WorldRace.Elf);
        Assert.That(blocked.Tiles.Where(tile => tile.Reason == CustomMapTileReason.ClaimsLand).Select(tile => tile.Position),
            Is.EquivalentTo(new[] { new WorldPosition(40, 27), new WorldPosition(41, 28) }), "(40, 29) is next to the protected mine, so it is skipped.");
        map.PlaceRod(new WorldPosition(40, 28), WorldRace.Elf);
        Assert.That(map.GetLandOwner(new WorldPosition(40, 30)), Is.EqualTo(WorldRace.Human));

        // Capitals and villages of a race stop the spread; tiles that are already the race's land are walked over.
        CustomMap open = CustomMap.Create("S600SC0000", "Open", 32, WorldMapTerrain.Neutral, [WorldRace.Human, WorldRace.Elf]);
        open.PlaceCapital(WorldRace.Elf, new WorldPosition(10, 11));
        open.PlaceRod(new WorldPosition(10, 10), WorldRace.Human);
        Assert.That(TerrainTiles(open, WorldMapTerrain.Human), Is.EquivalentTo(new[] { new WorldPosition(10, 9), new WorldPosition(11, 10) }), "South is the Elf capital.");
        open.Paint(new WorldPosition(20, 20), 1, CustomMapBrush.Land, WorldMapTerrain.Human, new Random(1));
        open.PlaceRod(new WorldPosition(20, 20), WorldRace.Human);
        Assert.That(TerrainTiles(open, WorldMapTerrain.Human).Where(tile => tile.GetDistance(new WorldPosition(20, 20)) == 2),
            Is.EquivalentTo(new[] { new WorldPosition(20, 22), new WorldPosition(20, 18) }), "Inside its own land the rod claims the nearest tiles beyond it.");
    }

    private static IEnumerable<WorldPosition> TerrainTiles(CustomMap map, WorldMapTerrain terrain)
    {
        for (int y = 0; y < map.Size; y++)
        {
            for (int x = 0; x < map.Size; x++)
            {
                if (map.GetCell(new WorldPosition(x, y)).Terrain == terrain)
                {
                    yield return new WorldPosition(x, y);
                }
            }
        }
    }

    // A map for the rod tests: one of everything, race land patches, villages of both kinds, a mountain-type
    // landmark, a cluster of resources and rods of both races, one of them guarding a mine.
    private CustomMap CreateRodMap()
    {
        CustomMap map = CreateBusyMap();
        Random random = new(3);

        map.PlaceVillage(new WorldPosition(26, 10), 1, WorldRace.Neutral, "Free");
        map.PlaceVillage(new WorldPosition(34, 30), 2, WorldRace.Elf, "Grove");
        map.PlaceLandmark(new WorldPosition(10, 36), Landmark(3, mountain: true));
        map.Paint(new WorldPosition(20, 36), 2, CustomMapBrush.Land, WorldMapTerrain.Human, random);
        map.Paint(new WorldPosition(40, 20), 2, CustomMapBrush.Land, WorldMapTerrain.Elf, random);
        map.Paint(new WorldPosition(14, 40), 1, CustomMapBrush.Forest, WorldMapTerrain.Neutral, random);

        foreach (WorldPosition position in new WorldPosition[] { new(24, 34), new(25, 36), new(27, 35), new(36, 22), new(8, 40), new(30, 42), new(44, 12) })
        {
            Assert.That(map.PlaceResource(position, WorldResourceType.Gold), Is.Null, $"Resource at {position}.");
        }

        Assert.That(map.PlaceRod(new WorldPosition(31, 43), WorldRace.Elf).Error, Is.Null, "Guards the mine at 30, 42.");
        Assert.That(map.PlaceRod(new WorldPosition(22, 32), WorldRace.Human).Error, Is.Null);
        Assert.That(map.PlaceRod(new WorldPosition(42, 14), WorldRace.Elf).Error, Is.Null);

        return map;
    }

    // The editor's rod spread is a port of the game's: on every tile where a rod fits, for both races, placing it in
    // the editor gives the same land as the game's ExpansionCalculator does for a rod planted on the loaded map.
    [Test]
    public void RodPaintAgreesWithGameTest()
    {
        CustomMap map = CreateRodMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        store.Save(map);

        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Load(globalDb, store.LoadDatabase(map.Id), campaignDb, new ScenarioId(map.Id), definitions, globals);
        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        WorldState state = scenario.State;
        Assert.That(state.Rods.Count, Is.EqualTo(3));
        Assert.That(state.Map.Cells.Select(cell => cell.Terrain), Is.EqualTo(map.Cells.Select(cell => cell.Terrain)), "The game loads the editor's land.");

        int placed = 0;
        int withResources = 0;

        foreach (WorldRace race in new[] { WorldRace.Human, WorldRace.Elf })
        {
            WorldPlayerId playerId = state.Players.Values.Single(player => player.Race == race).Id;
            WorldMapTerrain terrain = WorldHelpers.GetNativeTerrain(race);

            for (int y = 0; y < map.Size; y++)
            {
                for (int x = 0; x < map.Size; x++)
                {
                    WorldPosition tile = new(x, y);

                    if (map.PreviewRod(tile, race).Error is not null)
                    {
                        continue;
                    }

                    CustomMap copy = map.Clone();
                    Assert.That(copy.PlaceRod(tile, race).Error, Is.Null);

                    WorldRod rod = new(new WorldRodId("Rod0001"), playerId, tile);
                    WorldState planted = state with { Rods = state.Rods.Add(rod.Id, rod) };
                    WorldMap game = ExpansionCalculator.ExpandRodTerrain(planted, context, rod, terrain);

                    Assert.That(copy.Cells.Select(cell => cell.Terrain), Is.EqualTo(game.Cells.Select(cell => cell.Terrain)), $"{race} rod at {tile}");

                    placed++;

                    if (copy.Cells.Select(cell => cell.Terrain).Zip(map.Cells.Select(cell => cell.Terrain)).Count(pair => pair.First != pair.Second) > CustomMap.RodTilesPerTurn)
                    {
                        withResources++;
                    }
                }
            }
        }

        TestContext.Out.WriteLine($"{placed} rods compared, {withResources} took a resource's 3x3 along");
        Assert.That(placed, Is.GreaterThan(1000));
        Assert.That(withResources, Is.GreaterThan(20));
    }

    [Test]
    public void RodSaveAndLoadTest()
    {
        CustomMap map = CreateRodMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        store.Save(map);

        // Saved as SRod rows owned by the race's player, the first rods ever read from a scenario file.
        ScenarioDatabase database = store.LoadDatabase(map.Id);
        Assert.That(database.ScenarioRodsByScenario[new Db.ScenarioId(map.Id)].Select(row => (row.RodId.Value, row.OwnerId.Value, row.PositionX, row.PositionY)), Is.EquivalentTo(new[]
        {
            ("S600RO0001", "S600PL0002", 31, 43),
            ("S600RO0002", "S600PL0001", 22, 32),
            ("S600RO0003", "S600PL0002", 42, 14)
        }));

        WorldState state = ScenarioLoader.Load(globalDb, database, campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables()).State;
        Assert.That(state.Rods.Values.Select(rod => (rod.Id.Value, state.Players[rod.OwnerId].Race, rod.Position)), Is.EquivalentTo(new[]
        {
            (CustomMapWriter.GetRodId(map.Id, 1), WorldRace.Elf, new WorldPosition(31, 43)),
            (CustomMapWriter.GetRodId(map.Id, 2), WorldRace.Human, new WorldPosition(22, 32)),
            (CustomMapWriter.GetRodId(map.Id, 3), WorldRace.Elf, new WorldPosition(42, 14))
        }));
        Assert.That(WorldPlan.For(state).IsObstacleAt(new WorldPosition(22, 32)), Is.True, "Rods block armies.");

        // Reopened, the map is the same; the editor's quick preview update shows rods too.
        CustomMap reopened = store.Open(map.Id, definitions);
        Assert.That(reopened.Rods, Is.EquivalentTo(map.Rods));
        Assert.That(reopened.Cells, Is.EqualTo(map.Cells));
        Assert.That(reopened.Landmarks.Single(landmark => landmark.Mountain).TopLeft, Is.EqualTo(new WorldPosition(10, 36)), "Mountain-type flag read back.");
        Assert.That(reopened.Landmarks, Is.EquivalentTo(map.Landmarks));

        WorldState preview = CustomMapStore.ApplyTerrain(state with { Rods = ImmutableSortedDictionary<WorldRodId, WorldRod>.Empty }, map);
        Assert.That(preview.Rods, Is.EqualTo(state.Rods));
    }

    // Locations: default names, snapping onto the map, picking among overlaps, move/resize/rename/remove.
    [Test]
    public void LocationRulesTest()
    {
        CustomMap map = CreateMap();

        CustomMapLocation first = map.PlaceLocation(new WorldPosition(10, 10), 2);
        CustomMapLocation second = map.PlaceLocation(new WorldPosition(11, 11), 0, "  Trap  ");
        CustomMapLocation third = map.PlaceLocation(new WorldPosition(0, 47), 3);
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new CustomMapLocation(1, "Location 1", new WorldPosition(10, 10), 2)));
            Assert.That(second.Name, Is.EqualTo("Trap"), "Names are trimmed.");
            Assert.That(third.Name, Is.EqualTo("Location 2"), "Default names count only default names.");
            Assert.That(third.Center, Is.EqualTo(new WorldPosition(3, 44)), "A square leaving the map snaps to the nearest fitting centre.");
            Assert.That(first.Footprint, Is.EqualTo(new WorldFootprint(new WorldPosition(8, 8), 5, 5)));
        });

        // Overlaps: the selected location wins, otherwise the smallest; both are listed under the cursor.
        Assert.Multiple(() =>
        {
            Assert.That(map.PickLocation(new WorldPosition(11, 11))!.Id, Is.EqualTo(second.Id));
            Assert.That(map.PickLocation(new WorldPosition(11, 11), first.Id)!.Id, Is.EqualTo(first.Id));
            Assert.That(map.PickLocation(new WorldPosition(12, 12), second.Id)!.Id, Is.EqualTo(first.Id), "The selected one only wins where it covers the tile.");
            Assert.That(map.PickLocation(new WorldPosition(20, 20)), Is.Null);
            Assert.That(map.GetLocationsAt(new WorldPosition(11, 11)).Select(location => location.Id), Is.EqualTo(new[] { first.Id, second.Id }));
        });

        // Move and resize keep ID and name and snap; a blank rename is ignored.
        Assert.That(map.MoveLocation(first.Id, new WorldPosition(47, 0)), Is.EqualTo(first with { Center = new WorldPosition(45, 2) }));
        Assert.That(map.ResizeLocation(first.Id, 3)!.Center, Is.EqualTo(new WorldPosition(44, 3)));
        Assert.That(map.RenameLocation(first.Id, "Northern Pass")!.Name, Is.EqualTo("Northern Pass"));
        Assert.That(map.RenameLocation(first.Id, "   ")!.Name, Is.EqualTo("Northern Pass"));
        Assert.That(() => map.ResizeLocation(first.Id, 4), Throws.TypeOf<ArgumentOutOfRangeException>(), "Radius stays 0-3 like the engine.");
        Assert.That(map.MoveLocation(99, new WorldPosition(5, 5)), Is.Null);

        // Removing frees the default name and the clone is independent.
        CustomMap clone = map.Clone();
        Assert.That(map.RemoveLocation(third.Id), Is.True);
        Assert.That(map.RemoveLocation(third.Id), Is.False);
        Assert.That(clone.Locations, Has.Count.EqualTo(3));
        Assert.That(map.PlaceLocation(new WorldPosition(30, 30), 1).Name, Is.EqualTo("Location 1"));
        Assert.That(map.Locations.Max(location => location.Id), Is.EqualTo(3), "New IDs are the highest + 1, like the other objects.");
    }

    // Locations save as SLoc rows with Tscen names, load into the game's scenario provider with the same tiles, and
    // read back into the editor unchanged. On their own they change nothing in the game.
    [Test]
    public void LocationSaveAndLoadTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        WorldState before = ScenarioLoader.Load(globalDb, SaveAndLoadDatabase(store, map), campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables()).State;

        map.PlaceLocation(new WorldPosition(20, 20), 0, "Северный перевал");
        map.PlaceLocation(new WorldPosition(21, 21), 1);
        map.PlaceLocation(new WorldPosition(30, 12), 2, "Silk Wood Warning");
        map.PlaceLocation(new WorldPosition(47, 47), 3);
        ScenarioDatabase database = SaveAndLoadDatabase(store, map);

        Assert.That(database.ScenarioLocationsByScenario[new Db.ScenarioId(map.Id)].Select(row => (row.LocationId.Value, row.PositionX, row.PositionY, row.Radius)), Is.EqualTo(new[]
        {
            ("S600LO0001", 20, 20, 0),
            ("S600LO0002", 21, 21, 1),
            ("S600LO0003", 30, 12, 2),
            ("S600LO0004", 44, 44, 3)
        }));
        Assert.That(database.TextScenariosByScenario[new Db.ScenarioId(map.Id)].Single(text => text.IdObject.Value == "S600LO0001").Text, Is.EqualTo("Северный перевал"));

        Scenario scenario = ScenarioLoader.Load(globalDb, database, campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables());
        Assert.That(scenario.Provider.Locations.Select(location => location.Id.Value), Is.EqualTo(map.Locations.Select(location => CustomMapWriter.GetLocationId(map.Id, location.Id))));

        // Agreement: the editor's square is exactly the game's zone, tile for tile.
        foreach (CustomMapLocation location in map.Locations)
        {
            ScenarioLocationDefinition loaded = scenario.Provider.GetLocation(new ScenarioLocationId(CustomMapWriter.GetLocationId(map.Id, location.Id)));
            Assert.That(ScenarioLocationCalculator.EnumerateTiles(loaded, scenario.State.Map), Is.EquivalentTo(location.Footprint.EnumerateTiles()), location.Name);

            for (int y = 0; y < map.Size; y++)
            {
                for (int x = 0; x < map.Size; x++)
                {
                    WorldPosition position = new(x, y);

                    if (ScenarioLocationCalculator.Contains(loaded, position) != location.Contains(position))
                    {
                        Assert.Fail($"{location.Name}: editor and game disagree at {position}.");
                    }
                }
            }
        }

        // Nothing in the world changes: locations aren't world objects.
        Assert.That(scenario.State.Stacks.Keys, Is.EqualTo(before.Stacks.Keys));
        Assert.That(scenario.State.Fortifications.Keys, Is.EqualTo(before.Fortifications.Keys));

        CustomMap reopened = store.Open(map.Id, definitions);
        Assert.That(reopened.Locations, Is.EqualTo(map.Locations));

        // Saving the reopened map keeps the IDs, so later events and orders still point at the same zones.
        reopened.RemoveLocation(2);
        reopened.PlaceLocation(new WorldPosition(5, 5), 0);
        store.Save(reopened);
        Assert.That(store.Open(map.Id, definitions).Locations.Select(location => (location.Id, location.Name)), Is.EqualTo(new[]
        {
            (1, "Северный перевал"),
            (3, "Silk Wood Warning"),
            (4, "Location 2"),
            (5, "Location 1")
        }));
    }

    private static ScenarioDatabase SaveAndLoadDatabase(CustomMapStore store, CustomMap map)
    {
        store.Save(map);

        return store.LoadDatabase(map.Id);
    }

    // The AI plays a map with rods: the game spreads their land every turn.
    [Test]
    public void RodPlayTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        WorldPosition humanRod = new(20, 40);

        map.PlaceRod(humanRod, WorldRace.Human);
        map.PlaceRod(new WorldPosition(40, 20), WorldRace.Elf);
        map.PlaceResource(new WorldPosition(20, 43), WorldResourceType.Gold);
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

        int HumanLandNearRod()
        {
            WorldMap world = session.World.State.Map;

            return Enumerable.Range(0, world.Width * world.Height)
                .Select(index => new WorldPosition(index % world.Width, index / world.Width))
                .Count(position => position.GetDistance(humanRod) <= 4 && world.GetCellAt(position).Terrain == WorldMapTerrain.Human);
        }

        int before = HumanLandNearRod();
        TestWorldAiRunner runner = new(session, context);

        for (int i = 0; i < 9; i++)
        {
            runner.PlayTurn();
        }

        int after = HumanLandNearRod();
        TestContext.Out.WriteLine($"Turn {session.World.State.TurnInfo.TurnNumber}: Empire land near its rod {before} -> {after}, rods {session.World.State.Rods.Count}");
        Assert.That(session.World.State.Rods.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(after, Is.GreaterThanOrEqualTo(before + (2 * CustomMap.RodTilesPerTurn)));
    }

    // The economy overview counts resources by the land under them and takes income from the game's own code, at
    // Average difficulty whatever the map's difficulty.
    [Test]
    public void EconomyTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        Random random = new(1);

        map.Paint(new WorldPosition(14, 12), 1, CustomMapBrush.Land, WorldMapTerrain.Human, random);
        map.Paint(new WorldPosition(34, 16), 1, CustomMapBrush.Land, WorldMapTerrain.Elf, random);
        Assert.That(map.PlaceResource(new WorldPosition(14, 12), WorldResourceType.Gold), Is.Null);
        Assert.That(map.PlaceResource(new WorldPosition(14, 13), WorldResourceType.LifeMana), Is.Null);
        Assert.That(map.PlaceResource(new WorldPosition(34, 16), WorldResourceType.GroveMana), Is.Null);
        Assert.That(map.PlaceResource(new WorldPosition(20, 40), WorldResourceType.Gold), Is.Null, "Neutral land.");
        Assert.That(map.PlaceResource(new WorldPosition(30, 36), WorldResourceType.Gold), Is.Null, "Undead land: no Undead player, so nobody's.");
        Assert.That(map.PlaceResource(new WorldPosition(30, 37), WorldResourceType.DeathMana), Is.Null);
        Assert.That(map.PlaceRod(new WorldPosition(16, 20), WorldRace.Human).Error, Is.Null);
        store.Save(map);

        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Load(globalDb, store.LoadDatabase(map.Id), campaignDb, new ScenarioId(map.Id), definitions, globals);
        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        CustomMapEconomy economy = CustomMapEconomy.Calculate(scenario.State, context);

        // Base income: 100 gold and 25 of the race's own mana; 50 per resource on its land.
        CustomMapEconomyRace empire = economy.GetRace(WorldRace.Human)!;
        Assert.That(empire.Rows, Is.EqualTo(new[]
        {
            new CustomMapEconomyRow(WorldResourceType.Gold, 1, 100, 50),
            new CustomMapEconomyRow(WorldResourceType.LifeMana, 1, 25, 50)
        }));
        Assert.That(empire.GetRow(WorldResourceType.Gold)!.Total, Is.EqualTo(150));
        Assert.That(empire.Rods, Is.EqualTo(1));
        Assert.That(empire.LandTiles, Is.EqualTo(map.Cells.Count(cell => cell.Terrain == WorldMapTerrain.Human)));
        Assert.That(empire.LandPercent, Is.EqualTo(TerrainTransformCalculator.GetTransformedPercentage(scenario.State.Map, WorldMapTerrain.Human)));
        Assert.That(empire.LandPercent, Is.GreaterThan(0));

        CustomMapEconomyRace elves = economy.GetRace(WorldRace.Elf)!;
        Assert.That(elves.Rows, Is.EqualTo(new[]
        {
            new CustomMapEconomyRow(WorldResourceType.Gold, 0, 100, 0),
            new CustomMapEconomyRow(WorldResourceType.GroveMana, 1, 25, 50)
        }));
        Assert.That(elves.Rods, Is.Zero);
        Assert.That(economy.Races.Select(race => race.Race), Is.EquivalentTo(new[] { WorldRace.Human, WorldRace.Elf }));
        Assert.That(economy.Unclaimed, Is.EqualTo(new[] { (WorldResourceType.Gold, 2), (WorldResourceType.DeathMana, 1) }));

        // The same numbers as the game's own income query for a human player at Average.
        WorldPlayer player = scenario.State.Players.Values.Single(candidate => candidate.Race == WorldRace.Human);
        WorldState humanAverage = scenario.State with
        {
            Difficulty = WorldDifficulty.Average,
            Players = scenario.State.Players.SetItem(player.Id, player with { IsAi = false })
        };
        WorldGetPlayerInfoCommand.Result info = new WorldGetPlayerInfoCommand(player.Id).Execute(humanAverage, context).Data!;
        Assert.That((info.TotalIncome.Gold, info.TotalIncome.LifeMana), Is.EqualTo((150, 75)));

        // Always Average: an Easy map (+30 % income) shows the same numbers.
        CustomMapEconomy easy = CustomMapEconomy.Calculate(scenario.State with { Difficulty = WorldDifficulty.Easy }, context);
        Assert.That(easy.GetRace(WorldRace.Human)!.Rows, Is.EqualTo(empire.Rows));
    }

    [Test]
    public void StartingBankTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);

        // By default 500 gold and 1500 of the race's own mana, the others 0.
        Assert.That(map.Players[0].StartingBank, Is.EqualTo(new WorldResourceSet(500, 0, 0, 1500, 0, 0)), "Empire: life mana.");
        Assert.That(map.Players[1].StartingBank, Is.EqualTo(new WorldResourceSet(500, 0, 0, 0, 0, 1500)), "Elves: grove mana.");

        // Every amount can be set; each stays within 0 and 9999.
        Assert.That(map.SetStartingBank(WorldRace.Human, new WorldResourceSet(800, 50, 0, 2000, 12000, -5)), Is.True);
        WorldResourceSet empire = new(800, 50, 0, 2000, 9999, 0);
        Assert.That(map.Players[0].StartingBank, Is.EqualTo(empire));
        Assert.That(map.SetStartingBank(WorldRace.Human, empire), Is.False, "Nothing changed.");
        Assert.That(map.SetStartingBank(WorldRace.Dwarf, empire), Is.False, "Not on the map.");
        CustomMap copy = map.Clone();
        Assert.That(copy.Players, Is.EqualTo(map.Players));
        Assert.That(map.SetStartingBank(WorldRace.Human, empire with { Gold = 1 }), Is.True);
        Assert.That(copy.Players, Is.Not.EqualTo(map.Players), "Undo copies keep their banks.");
        map.SetStartingBank(WorldRace.Human, empire);

        // Saved as the player's bank, loaded as the player's resources, read back by the editor.
        store.Save(map);
        ScenarioDatabase database = store.LoadDatabase(map.Id);
        Assert.That(database.ScenarioPlayersByScenario[new Db.ScenarioId(map.Id)].Single(row => row.PlayerId.Value == "S600PL0001").Bank,
            Is.EqualTo(new Db.ResourceSet(800, 50, 0, 2000, 9999, 0)));
        WorldState state = ScenarioLoader.Load(globalDb, database, campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables()).State;
        Assert.That(state.Players.Values.Single(player => player.Race == WorldRace.Human).Resources, Is.EqualTo(empire));
        Assert.That(state.Players.Values.Single(player => player.Race == WorldRace.Elf).Resources, Is.EqualTo(new WorldResourceSet(500, 0, 0, 0, 0, 1500)));
        Assert.That(store.Open(map.Id, definitions).Players, Is.EqualTo(map.Players));

        // In the game a human player gets it scaled by difficulty (Hard: 50 %), a computer player as it is.
        ScenarioConfiguration configuration = new([new ScenarioPlayerConfiguration(WorldRace.Human, LordCategory.Warrior, new WorldObjectVariant(0), null, LeaderName: null)], WorldDifficulty.Hard, Leader: null);
        WorldState hard = ScenarioLoader.Create(globalDb, database, campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables(), configuration).State;
        Assert.That(hard.Players.Values.Single(player => player.Race == WorldRace.Human).Resources, Is.EqualTo(new WorldResourceSet(400, 25, 0, 1000, 4999, 0)));
        Assert.That(hard.Players.Values.Single(player => player.Race == WorldRace.Elf).Resources.GroveMana, Is.EqualTo(1500));

        // Same for all: gold and the other manas as they are, the race's own mana gets the source's own mana (the two swap).
        Assert.That(map.CopyStartingBankToAll(WorldRace.Human), Is.True);
        Assert.That(map.Players[1].StartingBank, Is.EqualTo(new WorldResourceSet(800, 50, 0, 0, 9999, 2000)), "Elves: 2000 grove, the Empire's 0 grove as life.");
        Assert.That(map.Players[0].StartingBank, Is.EqualTo(empire));
        Assert.That(map.CopyStartingBankToAll(WorldRace.Human), Is.False);
    }

    // The spread preview gives the same land as the game's own turns when nobody does anything: every player human
    // and only ending turns, so each turn start spreads land and nothing else changes it.
    [Test]
    public void SpreadPreviewAgreesWithGameTest()
    {
        CustomMap map = CreateRodMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        map.PlaceVillage(new WorldPosition(14, 36), 5, WorldRace.Human, "Big");
        store.Save(map);

        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Load(globalDb, store.LoadDatabase(map.Id), campaignDb, new ScenarioId(map.Id), definitions, globals);
        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        CustomMapSpreadPreview preview = new(scenario.State, context);
        Assert.That(preview.TurnOrder, Is.EqualTo(new[] { WorldRace.Human, WorldRace.Elf }));
        Assert.That(preview.GetState(0), Is.SameAs(scenario.State));

        WorldState humans = scenario.State with
        {
            Players = scenario.State.Players.SetItems(scenario.State.Players.Select(pair => KeyValuePair.Create(pair.Key, pair.Value with { IsAi = false })))
        };
        GameSession session = new(context, humans);
        Assert.That(session.World.DispatchCommand(new WorldStartGameCommand()).Error, Is.Null);
        int players = humans.Players.Count;
        int changed = 0;

        void EndRound()
        {
            for (int i = 0; i < players; i++)
            {
                Assert.That(session.World.DispatchCommand(new WorldEndTurnCommand()).Error, Is.Null);
            }
        }

        // All human: the neutral player (first by ID) starts each round, so a round's spread is complete when the
        // next round begins. Turn 1 has no spread.
        EndRound();

        for (int turn = 1; turn <= 8; turn++)
        {
            EndRound();
            Assert.That(session.World.State.TurnInfo.TurnNumber, Is.EqualTo(turn + 2));
            Assert.That(preview.GetState(turn).Map.Cells.Select(cell => cell.Terrain), Is.EqualTo(session.World.State.Map.Cells.Select(cell => cell.Terrain)), $"After {turn} turns.");
            changed += preview.GetState(turn).Map.Cells.Zip(preview.GetState(turn - 1).Map.Cells).Count(pair => pair.First.Terrain != pair.Second.Terrain);
        }

        TestContext.Out.WriteLine($"{changed} tiles changed owner in 8 turns");
        Assert.That(changed, Is.GreaterThan(200));

        Assert.That((preview.ComputedTurns, preview.AverageTurnTime > TimeSpan.Zero), Is.EqualTo((8, true)));

        // Going back and forth reuses the turns already computed; more than MaxTurns is MaxTurns.
        Assert.That(preview.GetState(3).Map, Is.SameAs(preview.GetState(3).Map));
        Assert.That(preview.GetState(CustomMapSpreadPreview.MaxTurns + 50).Map, Is.SameAs(preview.GetState(CustomMapSpreadPreview.MaxTurns).Map));
    }

    // A map with one of everything close together, for the preview tests.
    private CustomMap CreateBusyMap()
    {
        CustomMap map = CreateMap();
        Random random = new(2);

        map.PlaceVillage(new WorldPosition(4, 16), 2, WorldRace.Human, "Ford");
        map.PlaceStack(new WorldPosition(7, 19), WorldRace.Human, Slots((GridPosition.FrontCenter, HumanLeader)));
        map.PlaceStack(new WorldPosition(14, 12), WorldRace.Neutral, Slots((GridPosition.FrontCenter, NeutralLeader)));
        map.PlaceStack(new WorldPosition(8, 8), WorldRace.Human, Slots((GridPosition.FrontCenter, HumanLeader)));
        map.PlaceRuin(new WorldPosition(20, 12), 0, "Rigel Ruin");
        map.PlaceBag(new WorldPosition(12, 14), 0);
        map.PlaceBag(new WorldPosition(22, 22), 0);
        map.PlaceResource(new WorldPosition(16, 8), WorldResourceType.Gold);
        map.PlaceSite(new WorldPosition(30, 20), CustomMapSiteType.Merchant, 0, "Shop", "");
        map.PlaceLandmark(new WorldPosition(20, 4), Landmark(3));
        map.PlaceLandmark(new WorldPosition(14, 30), Landmark(1));
        map.Paint(new WorldPosition(30, 30), 0, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random);
        map.Paint(new WorldPosition(18, 18), 1, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random);
        map.Paint(new WorldPosition(34, 28), 1, CustomMapBrush.Forest, WorldMapTerrain.Neutral, random);
        Assert.That((map.Villages.Count, map.Stacks.Count, map.Ruins.Count, map.Bags.Count, map.Resources.Count, map.Sites.Count, map.Landmarks.Count, map.Mountains.Count),
            Is.EqualTo((1, 3, 1, 2, 1, 1, 2, 4)));

        return map;
    }

    // Tiles whose change a player sees on the map: forests and mountains gone, water turned into land.
    private static HashSet<WorldPosition> VisibleChanges(CustomMap before, CustomMap after)
    {
        HashSet<WorldPosition> changed = [];

        for (int y = 0; y < before.Size; y++)
        {
            for (int x = 0; x < before.Size; x++)
            {
                WorldPosition position = new(x, y);
                WorldMapCell old = before.GetCell(position);
                WorldMapCell now = after.GetCell(position);

                if ((old.Feature != WorldMapFeature.None && now.Feature == WorldMapFeature.None) ||
                    (old.Terrain == WorldMapTerrain.Water && now.Terrain != WorldMapTerrain.Water))
                {
                    changed.Add(position);
                }
            }
        }

        return changed;
    }

    private static HashSet<WorldPosition> TilesWith(CustomMapPlacementPreview preview, CustomMapTileStatus status)
    {
        return [.. preview.Tiles.Where(tile => tile.Status == status).Select(tile => tile.Position)];
    }

    // Every tool's preview, on every tile of a busy map, matches what the click does on a copy of the map: the
    // same error, the same footprint, and the amber tiles are exactly the tiles that visibly change.
    [Test]
    public void PreviewAgreesWithPlaceTest()
    {
        CustomMap map = CreateBusyMap();
        WorldLandmarkDefinition landmark = Landmark(3);
        WorldLandmarkDefinition small = Landmark(2);
        Dictionary<string, (int Fits, int Blocked)> counts = [];

        void Check(string tool, CustomMapPlacementPreview preview, Func<CustomMap, (CustomMapPlacementError? Error, WorldFootprint? Footprint)> place)
        {
            CustomMap copy = map.Clone();
            (CustomMapPlacementError? error, WorldFootprint? footprint) = place(copy);

            Assert.That(preview.Error, Is.EqualTo(error), $"{tool} at {preview.Footprint}");

            if (error is null)
            {
                if (footprint is not null)
                {
                    Assert.That(preview.Footprint, Is.EqualTo(footprint), tool);
                }

                Assert.That(TilesWith(preview, CustomMapTileStatus.Changes), Is.EquivalentTo(VisibleChanges(map, copy)), $"{tool} at {preview.Footprint}");
                Assert.That(TilesWith(preview, CustomMapTileStatus.Blocked), Is.Empty, tool);
            }
            else
            {
                Assert.That(copy.Cells, Is.EqualTo(map.Cells), tool);
                Assert.That(TilesWith(preview, CustomMapTileStatus.Blocked), Is.Not.Empty, $"{tool}: a blocked placement shows what blocks it.");
            }

            (int fits, int blocked) = counts.GetValueOrDefault(tool);
            counts[tool] = error is null ? (fits + 1, blocked) : (fits, blocked + 1);
        }

        for (int y = 0; y < map.Size; y++)
        {
            for (int x = 0; x < map.Size; x++)
            {
                WorldPosition tile = new(x, y);
                WorldPosition capital = map.CenterFootprint(tile, CustomMap.CapitalSize, CustomMap.CapitalSize);
                WorldPosition village = map.CenterFootprint(tile, CustomMapVillage.Size, CustomMapVillage.Size);
                WorldPosition ruin = map.CenterFootprint(tile, CustomMapRuin.Size, CustomMapRuin.Size);
                WorldPosition big = map.CenterFootprint(tile, landmark.Width, landmark.Height);
                WorldPosition little = map.CenterFootprint(tile, small.Width, small.Height);

                Check("capital", map.PreviewCapital(WorldRace.Elf, capital), copy => (copy.PlaceCapital(WorldRace.Elf, capital), copy.Players[1].Footprint));
                Check("village", map.PreviewVillage(village, 1, WorldRace.Neutral), copy => Result(copy.PlaceVillage(village, 1, WorldRace.Neutral, "")));
                Check("ruin", map.PreviewRuin(ruin), copy => Result(copy.PlaceRuin(ruin, 0, "")));
                Check("site", map.PreviewSite(ruin), copy => Result(copy.PlaceSite(ruin, CustomMapSiteType.Trainer, 0, "", "")));
                Check("landmark", map.PreviewLandmark(big, landmark.Width, landmark.Height), copy => Result(copy.PlaceLandmark(big, landmark)));
                Check("landmark 2x2", map.PreviewLandmark(little, small.Width, small.Height), copy => Result(copy.PlaceLandmark(little, small)));
                Check("bag", map.PreviewBag(tile), copy => (copy.PlaceBag(tile, 0).Error, new WorldFootprint(tile, 1, 1)));

                if (map.GetResourceAt(tile) is null)
                {
                    Check("resource", map.PreviewResource(tile), copy => (copy.PlaceResource(tile, WorldResourceType.LifeMana), new WorldFootprint(tile, 1, 1)));
                }

                // Rods: the amber tiles are exactly the tiles whose land changes owner.
                CustomMapPlacementPreview rodPreview = map.PreviewRod(tile, WorldRace.Human);
                CustomMap rodCopy = map.Clone();
                CustomMapPlacementError? rodError = rodCopy.PlaceRod(tile, WorldRace.Human).Error;
                Assert.That(rodPreview.Error, Is.EqualTo(rodError), $"rod at {tile}");

                if (rodError is null)
                {
                    HashSet<WorldPosition> claimed = [.. Enumerable.Range(0, map.Size * map.Size)
                        .Where(index => map.Cells[index].Terrain != rodCopy.Cells[index].Terrain)
                        .Select(index => new WorldPosition(index % map.Size, index / map.Size))];
                    Assert.That(TilesWith(rodPreview, CustomMapTileStatus.Changes), Is.EquivalentTo(claimed), $"rod at {tile}");
                    Assert.That(TilesWith(rodPreview, CustomMapTileStatus.Blocked), Is.Empty, $"rod at {tile}");
                }
                else
                {
                    Assert.That(rodCopy.Cells, Is.EqualTo(map.Cells), $"rod at {tile}");
                    Assert.That(rodCopy.Rods, Is.Empty, $"rod at {tile}");
                    Assert.That(TilesWith(rodPreview, CustomMapTileStatus.Blocked), Is.Not.Empty, $"rod at {tile}: a blocked placement shows what blocks it.");
                }

                (int rodFits, int rodBlocked) = counts.GetValueOrDefault("rod");
                counts["rod"] = rodError is null ? (rodFits + 1, rodBlocked) : (rodFits, rodBlocked + 1);

                // Armies: the editor probes the tile with an empty army, which fails with NoLeader when the tile is fine.
                foreach (WorldRace owner in new[] { WorldRace.Neutral, WorldRace.Human })
                {
                    CustomMapPlacementPreview army = map.PreviewStack(tile, owner);
                    CustomMapStackError? placed = map.Clone().PlaceStack(tile, owner, new CustomMapUnit?[GridPosition.Count]).Error;
                    Assert.That(army.StackError ?? CustomMapStackError.NoLeader, Is.EqualTo(placed), $"army of {owner} at {tile}");
                    Assert.That(army.Tiles.Single().Status == CustomMapTileStatus.Blocked, Is.EqualTo(army.StackError is not null), $"army at {tile}");
                }
            }
        }

        foreach ((string tool, (int fits, int blocked)) in counts)
        {
            TestContext.Out.WriteLine($"{tool}: {fits} fit, {blocked} blocked");
            Assert.That((fits > 0, blocked > 0), Is.EqualTo((true, true)), $"{tool} is tested both ways.");
        }
    }

    private static (CustomMapPlacementError? Error, WorldFootprint? Footprint) Result<T>((T? Placed, CustomMapPlacementError? Error) result)
        where T : class
    {
        WorldFootprint? footprint = result.Placed switch
        {
            CustomMapVillage village => village.Footprint,
            CustomMapRuin ruin => ruin.Footprint,
            CustomMapSite site => site.Footprint,
            CustomMapLandmark landmark => landmark.Footprint,
            _ => null
        };

        return (result.Error, footprint);
    }

    // Brush previews: the tiles a stroke paints, the ones it leaves alone and the mountains it removes or creates,
    // checked against Paint on a copy.
    [Test]
    public void PreviewPaintAgreesWithPaintTest()
    {
        CustomMap map = CreateBusyMap();
        int checkedStrokes = 0;

        foreach (CustomMapBrush brush in Enum.GetValues<CustomMapBrush>())
        {
            foreach (int radius in new[] { 0, 1, 2, 3, 8 })
            {
                for (int y = 0; y < map.Size; y += 3)
                {
                    for (int x = 0; x < map.Size; x += 3)
                    {
                        WorldPosition center = new(x, y);
                        CustomMapPlacementPreview preview = map.PreviewPaint(center, radius, brush);
                        CustomMap copy = map.Clone();
                        copy.Paint(center, radius, brush, WorldMapTerrain.Dwarf, new Random(3));
                        string where = $"{brush} r{radius} at {center}";

                        HashSet<WorldPosition> changed = [.. Enumerable.Range(0, map.Size * map.Size)
                            .Select(index => new WorldPosition(index % map.Size, index / map.Size))
                            .Where(position => map.GetCell(position) != copy.GetCell(position))];
                        HashSet<WorldPosition> covered = [.. preview.Tiles.Where(tile => tile.Status != CustomMapTileStatus.Blocked).Select(tile => tile.Position)];

                        Assert.That(changed.IsSubsetOf(covered), Is.True, $"{where}: every painted tile is in the preview.");
                        Assert.That(TilesWith(preview, CustomMapTileStatus.Blocked).Overlaps(changed), Is.False, $"{where}: red tiles stay as they are.");
                        Assert.That(preview.RemovedMountains, Is.EquivalentTo(map.Mountains.Except(copy.Mountains).Select(mountain => mountain.Footprint)), where);
                        Assert.That(preview.NewMountains, Is.EquivalentTo(copy.Mountains.Except(map.Mountains).Select(mountain => mountain.Footprint)), where);

                        if (brush != CustomMapBrush.Mountain)
                        {
                            HashSet<WorldPosition> disk = [.. CustomMap.GetBrushArea(center, radius).Where(map.IsValidPosition)];
                            Assert.That(preview.Tiles.Select(tile => tile.Position).Where(disk.Contains).Count(), Is.EqualTo(disk.Count), $"{where}: the whole disk is shown.");
                        }

                        checkedStrokes++;
                    }
                }
            }
        }

        Assert.That(checkedStrokes, Is.EqualTo(5 * 5 * 16 * 16));
    }

    [Test]
    public void PreviewTileReasonsTest()
    {
        CustomMap map = CreateBusyMap();

        CustomMapTileReason ReasonAt(CustomMapPlacementPreview preview, int x, int y)
        {
            return preview.Tiles.Single(tile => tile.Position == new WorldPosition(x, y)).Reason;
        }

        // A village whose corner touches the ruin and the 3x3 mountain: just those tiles are red, the rest green.
        CustomMapPlacementPreview village = map.PreviewVillage(new WorldPosition(17, 9), 1, WorldRace.Neutral);
        Assert.That(village.Error, Is.EqualTo(CustomMapPlacementError.Blocked));
        Assert.That(ReasonAt(village, 20, 12), Is.EqualTo(CustomMapTileReason.Structure));
        Assert.That(ReasonAt(village, 17, 9), Is.EqualTo(CustomMapTileReason.None));
        Assert.That(village.FirstBlocked!.Value.Position, Is.EqualTo(new WorldPosition(20, 12)));

        // A bag inside the footprint: Occupied, with the bag tile named.
        CustomMapPlacementPreview ruin = map.PreviewRuin(new WorldPosition(11, 13));
        Assert.That(ruin.Error, Is.EqualTo(CustomMapPlacementError.Occupied));
        Assert.That(ReasonAt(ruin, 12, 14), Is.EqualTo(CustomMapTileReason.Bag));

        // A ruin on the lake shore turns water into land; on the 3x3 mountain it removes all of it, also outside.
        CustomMapPlacementPreview shore = map.PreviewRuin(new WorldPosition(19, 23));
        Assert.That(shore.Error, Is.Null);
        Assert.That(ReasonAt(shore, 21, 24), Is.EqualTo(CustomMapTileReason.WaterToLand));
        CustomMapPlacementPreview mountain = map.PreviewSite(new WorldPosition(15, 15));
        Assert.That(mountain.Error, Is.Null);
        Assert.That(mountain.RemovedMountains, Is.EqualTo(new[] { new WorldFootprint(new WorldPosition(17, 17), 3, 3) }));
        Assert.That(ReasonAt(mountain, 19, 19), Is.EqualTo(CustomMapTileReason.RemovesMountain), "Outside the footprint.");
        Assert.That(mountain.Tiles, Has.Length.EqualTo(9 + 8));

        // A landmark can't stand on that mountain; a village can be pushed back inside at the map edge.
        CustomMapPlacementPreview rocky = map.PreviewLandmark(new WorldPosition(16, 16), 2, 2);
        Assert.That((rocky.Error, ReasonAt(rocky, 17, 17), ReasonAt(rocky, 16, 16)), Is.EqualTo((CustomMapPlacementError.Blocked, CustomMapTileReason.Mountain, CustomMapTileReason.None)));
        Assert.That(map.CenterFootprint(new WorldPosition(47, 0), CustomMapVillage.Size, CustomMapVillage.Size), Is.EqualTo(new WorldPosition(44, 0)));

        // A forest under a resource or bag is cleared.
        Assert.That(ReasonAt(map.PreviewBag(new WorldPosition(10, 30)), 10, 30), Is.EqualTo(CustomMapTileReason.ClearsForest));
        Assert.That(map.PreviewResource(new WorldPosition(23, 23)).Tiles.Single().Reason, Is.EqualTo(CustomMapTileReason.Water));

        // The village entrance takes an army of its owner only.
        Assert.That(map.PreviewStack(new WorldPosition(7, 19), WorldRace.Human).StackError, Is.EqualTo(CustomMapStackError.Occupied), "Its stationed army stands there.");
        map.RemoveStack(map.GetStackAt(new WorldPosition(7, 19))!.Id);
        Assert.That(map.PreviewStack(new WorldPosition(7, 19), WorldRace.Human).Fits, Is.True);
        CustomMapPlacementPreview stranger = map.PreviewStack(new WorldPosition(7, 19), WorldRace.Neutral);
        Assert.That((stranger.StackError, stranger.Tiles.Single().Reason), Is.EqualTo((CustomMapStackError.VillageOwner, CustomMapTileReason.WrongOwner)));
        Assert.That(map.PreviewStack(new WorldPosition(6, 18), WorldRace.Human).Tiles.Single().Reason, Is.EqualTo(CustomMapTileReason.Structure));

        // Moving a capital onto its own old place is fine and takes its stationed army along; the old place is shown.
        CustomMapPlacementPreview moved = map.PreviewCapital(WorldRace.Human, new WorldPosition(6, 5));
        Assert.That(moved.Error, Is.Null, "Overlaps its own old place and its own army at (8, 8).");
        Assert.That(moved.MovesFrom, Is.EqualTo(new WorldFootprint(new WorldPosition(4, 4), 5, 5)));
        Assert.That(map.PreviewCapital(WorldRace.Human, new WorldPosition(4, 4)).MovesFrom, Is.Null);
        CustomMapPlacementPreview onElves = map.PreviewCapital(WorldRace.Human, new WorldPosition(36, 4));
        Assert.That((onElves.Error, ReasonAt(onElves, 38, 4)), Is.EqualTo((CustomMapPlacementError.Blocked, CustomMapTileReason.Structure)));

        // Brushes: Water leaves armies alone and removes a touched mountain whole; Mountains shows its blocks.
        CustomMapPlacementPreview water = map.PreviewPaint(new WorldPosition(17, 17), 0, CustomMapBrush.Water);
        Assert.That(water.RemovedMountains, Has.Length.EqualTo(1));
        Assert.That(TilesWith(water, CustomMapTileStatus.Changes), Has.Count.EqualTo(9));
        Assert.That(ReasonAt(map.PreviewPaint(new WorldPosition(14, 12), 1, CustomMapBrush.Water), 14, 12), Is.EqualTo(CustomMapTileReason.Army));
        CustomMapPlacementPreview peaks = map.PreviewPaint(new WorldPosition(40, 26), 4, CustomMapBrush.Mountain);
        Assert.That(peaks.NewMountains, Is.EqualTo(new[] { new WorldFootprint(new WorldPosition(36, 22), 5, 5) }));
        Assert.That(TilesWith(peaks, CustomMapTileStatus.Ok), Has.Count.EqualTo(25));
    }

    // Map rule: mage towers charge the markup for another race's spells only; diplomacy keeps the list price, and the
    // rule survives save and reopen (SRules.json). Original scenarios (no SRules.json) keep normal prices.
    [Test]
    public void ForeignSpellMarkupTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);
        WorldSpellDefinition[] spells = [.. store.ListSpells(definitions).Where(spell => spell.PurchaseCost.Gold > 0).OrderBy(spell => spell.Id.Value, StringComparer.Ordinal)];
        WorldSpellDefinition own = spells.First(spell => spell.Race == WorldRace.Human);
        WorldSpellDefinition foreign = spells.First(spell => spell.Race == WorldRace.Elf);
        CustomMapSite tower = map.PlaceSite(new WorldPosition(16, 16), CustomMapSiteType.MageTower, 0, "Tower", "").Site!;
        Assert.That(map.UpdateSite(tower.Id, 0, "Tower", "", [], [own.Id, foreign.Id], []), Is.Null);
        Assert.That(map.PlaceStack(new WorldPosition(20, 12), WorldRace.Human, Slots((GridPosition.FrontCenter, HumanLeader))).Error, Is.Null);
        Assert.That((map.SetForeignSpellMarkup(50), map.SetForeignSpellMarkup(50), map.SetForeignSpellMarkup(900), map.ForeignSpellMarkup), Is.EqualTo((true, false, true, CustomMap.MaxSpellMarkup)));
        Assert.That(map.SetForeignSpellMarkup(50), Is.True);
        Assert.That(map.Clone().ForeignSpellMarkup, Is.EqualTo(50));
        store.Save(map);
        Assert.That(store.Open(map.Id, definitions).ForeignSpellMarkup, Is.EqualTo(50));

        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Load(globalDb, store.LoadDatabase(map.Id), campaignDb, new ScenarioId(map.Id), definitions, globals);
        Assert.That(scenario.Provider.Definition.ForeignSpellMarkup, Is.EqualTo(50));
        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        WorldState state = scenario.State;
        WorldPlayer human = state.Players.Values.Single(player => player.Race == WorldRace.Human);
        WorldMageSite site = state.Sites.Values.OfType<WorldMageSite>().Single();
        WorldStack army = state.Stacks.Values.Single(stack => stack.OwnerId == human.Id && stack.Position == new WorldPosition(20, 12));
        human = human with { Resources = new WorldResourceSet(99999, 99999, 99999, 99999, 99999, 99999) };

        WorldResourceSet Price(WorldSpellDefinition spell) => MageSiteCalculator.GetPurchaseInfo(site, army, human, spell.Id, context).Cost;
        WorldResourceSet Marked(WorldResourceSet price) => new(
            price.Gold + (int)Math.Round(price.Gold / 2.0, MidpointRounding.AwayFromZero),
            price.DeathMana + (int)Math.Round(price.DeathMana / 2.0, MidpointRounding.AwayFromZero),
            price.RuneMana + (int)Math.Round(price.RuneMana / 2.0, MidpointRounding.AwayFromZero),
            price.LifeMana + (int)Math.Round(price.LifeMana / 2.0, MidpointRounding.AwayFromZero),
            price.InfernalMana + (int)Math.Round(price.InfernalMana / 2.0, MidpointRounding.AwayFromZero),
            price.GroveMana + (int)Math.Round(price.GroveMana / 2.0, MidpointRounding.AwayFromZero));

        Assert.That(Price(own), Is.EqualTo(own.PurchaseCost), "Own race: list price.");
        Assert.That(Price(foreign), Is.EqualTo(Marked(foreign.PurchaseCost)), "Another race: +50 %.");
        Assert.That(Price(foreign).Gold, Is.GreaterThan(foreign.PurchaseCost.Gold));
        Assert.That(DiplomacyProposalCalculator.GetTradeValue(foreign.Id, context), Is.EqualTo(foreign.PurchaseCost.Gold), "Diplomacy keeps the list price.");

        // Without the rule (0, and original scenarios with no SRules.json) prices stay as they were.
        map.SetForeignSpellMarkup(0);
        store.Save(map);
        Scenario plain = ScenarioLoader.Load(globalDb, store.LoadDatabase(map.Id), campaignDb, new ScenarioId(map.Id), definitions, globals);
        EngineContext plainContext = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, plain.Provider.Definition), plain.Provider, globals);
        Assert.That(MageSiteCalculator.GetPurchaseInfo(site, army, human, foreign.Id, plainContext).Cost, Is.EqualTo(foreign.PurchaseCost));
        Assert.That(ScenarioLoader.Load(globalDb, ScenarioDatabaseLoader.Load(RiseOfTheElves.ScenarioDatabasePath, new Db.ScenarioId("S135SC0000")), campaignDb, new ScenarioId("S135SC0000"), definitions, globals).Provider.Definition.ForeignSpellMarkup, Is.Zero);
    }

    // Map rule "neutral kinds": each neutral subrace type gets its own neutral player (only one kind per side, kinds
    // hostile to each other); plain neutrals stay with player 0; off = one neutral player. Survives save and reopen.
    [Test]
    public void NeutralKindsTest()
    {
        CustomMapUnit Named(string name, bool leader) => CustomMapUnit.From(Units
            .Where(unit => unit.Race == WorldRace.Neutral && unit.IsLarge == false && (leader ? unit.Category == UnitCategory.Leader : unit.Category != UnitCategory.Leader))
            .Where(unit => globalDb.Units[new Db.Id(unit.Id.Value)].SubraceType == (name switch { "greenskin" => Db.SubraceType.NeutralGreenSkin, "wolf" => Db.SubraceType.NeutralWolf, _ => Db.SubraceType.Neutral }))
            .OrderBy(unit => unit.Id.Value, StringComparer.Ordinal)
            .First());

        CustomMap map = CreateMap();
        CustomMapStack goblins = map.PlaceStack(new WorldPosition(14, 14), WorldRace.Neutral, Slots((GridPosition.FrontCenter, Named("greenskin", true)))).Stack!;
        CustomMapStack wolves = map.PlaceStack(new WorldPosition(14, 18), WorldRace.Neutral, Slots((GridPosition.FrontCenter, Named("wolf", true)))).Stack!;
        CustomMapStack plain = map.PlaceStack(new WorldPosition(18, 14), WorldRace.Neutral, Slots((GridPosition.FrontCenter, Named("plain", true)))).Stack!;
        CustomMapVillage village = map.PlaceVillage(new WorldPosition(4, 16), 2, WorldRace.Neutral, "Orc town").Village!;
        Assert.That(map.UpdateVillage(village.Id, 2, WorldRace.Neutral, "Orc town", Slots((GridPosition.FrontCenter, Named("greenskin", false)))), Is.Null);
        CustomMapArmyTemplate raiders = map.AddTemplate(new CustomMapArmyTemplate(0, "Raiders", WorldRace.Neutral, [.. Slots((GridPosition.FrontCenter, Named("wolf", true)))], CustomMapArmyOrder.Normal));
        CustomMapStore store = new(mapsDirectory, globalDb, campaignDb);

        WorldPlayerId OwnerOfStack(WorldState state, int id) => state.Stacks[new WorldStackId(CustomMapWriter.GetArmyStackId(map.Id, id))].OwnerId;
        Scenario Load() => ScenarioLoader.Load(globalDb, store.LoadDatabase(map.Id), campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables());

        // Off: one neutral player owns everything neutral.
        store.Save(map);
        Assert.That(Load().State.Players.Values.Count(player => player.Race == WorldRace.Neutral), Is.EqualTo(1));

        map.NeutralKinds = true;
        store.Save(map);
        Scenario scenario = Load();
        WorldState state = scenario.State;
        Assert.That(state.Players.Values.Count(player => player.Race == WorldRace.Neutral), Is.EqualTo(3), "Player 0, greenskins, wolves.");
        WorldPlayerId greenskins = OwnerOfStack(state, goblins.Id);
        WorldPlayerId wolfSide = OwnerOfStack(state, wolves.Id);
        WorldPlayerId primary = OwnerOfStack(state, plain.Id);
        Assert.That(new[] { greenskins, wolfSide, primary }.Distinct().Count(), Is.EqualTo(3));
        Assert.That(primary, Is.EqualTo(WorldCommandHelpers.GetNeutralPlayerId(state)), "Plain neutrals stay with the primary neutral player.");
        Assert.That(((WorldCity)state.Fortifications[new WorldFortificationId(CustomMapWriter.GetVillageId(map.Id, village.Id))]).OwnerId, Is.EqualTo(greenskins), "An orc garrison makes it a greenskin village.");
        Assert.That(state.Players[scenario.Provider.StackTemplates.Single().OwnerId].Id, Is.EqualTo(wolfSide), "A wolf-led template joins the wolves.");
        Assert.That((DiplomacyCalculator.IsHostile(state, greenskins, wolfSide), DiplomacyCalculator.IsHostile(state, greenskins, primary)), Is.EqualTo((true, true)));
        Assert.That(state.Players.Values.Where(player => player.Race != WorldRace.Neutral).Select(player => player.Race), Is.EquivalentTo(new[] { WorldRace.Human, WorldRace.Elf }));

        CustomMap reopened = store.Open(map.Id, definitions);
        Assert.That((reopened.NeutralKinds, reopened.Stacks.Count(stack => stack.Owner == WorldRace.Neutral), reopened.Villages.Single().Owner), Is.EqualTo((true, 3, WorldRace.Neutral)));
        Assert.That(raiders.Id, Is.EqualTo(1));
    }
}

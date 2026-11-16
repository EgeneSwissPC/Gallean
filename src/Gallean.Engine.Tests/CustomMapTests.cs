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

        WorldState applied = CustomMapStore.ApplyTerrain(before, map);
        WorldState rebuilt = store.BuildPreview(map, definitions).State;

        Assert.That(applied.Map, Is.EqualTo(rebuilt.Map));
        Assert.That(applied.Mountains, Is.EqualTo(rebuilt.Mountains));
        Assert.That(applied.Fortifications, Is.EqualTo(rebuilt.Fortifications));
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
}

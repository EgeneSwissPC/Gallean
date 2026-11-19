using System.Collections.Immutable;
using Gallean.Entities.GameData;

namespace Gallean.Engine.Tests;

// Map editor events, phase 1 of docs/map-editor-events.md: the editor model, the scenario rows the writer builds from
// it (checked through the game's own loader and engine) and the editor's own copy that reopens events unchanged.
public sealed class CustomMapEventTests
{
    private const string MapId = "S600SC0000";

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
        mapsDirectory = Path.Combine(Path.GetTempPath(), "gallean-custom-map-events-" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(mapsDirectory))
        {
            Directory.Delete(mapsDirectory, recursive: true);
        }
    }

    private static string Id(string kind, int number)
    {
        return $"S600{kind}{number:x4}";
    }

    private CustomMapStore Store => new(mapsDirectory, globalDb, campaignDb);

    private CustomMapUnit HumanLeader => CustomMapUnit.From(Store.ListUnits(definitions)
        .Where(unit => unit is { Category: UnitCategory.Leader, Race: WorldRace.Human, IsLarge: false })
        .OrderBy(unit => unit.Id.Value, StringComparer.Ordinal)
        .First());

    private CustomMapUnit NeutralSoldier => CustomMapUnit.From(Store.ListUnits(definitions)
        .Where(unit => unit is { Category: UnitCategory.Leader, Race: WorldRace.Neutral, IsLarge: false })
        .OrderBy(unit => unit.Id.Value, StringComparer.Ordinal)
        .First());

    private ItemDefinitionId FirstItem => Store.ListItems(definitions).OrderBy(item => item.Id.Value, StringComparer.Ordinal).First().Id;

    private static CustomMapUnit?[] Slots(CustomMapUnit unit)
    {
        CustomMapUnit?[] slots = new CustomMapUnit?[GridPosition.Count];
        slots[GridPosition.FrontCenter.ToIndex()] = unit;

        return slots;
    }

    // Empire capital 4,4, elven capital 38,4, a village (1) at 20,12, a ruin (1) at 12,20, an Empire army (1) at
    // 12,10, a neutral army (2) at 30,16, "North Gate" (1) 3x3 around 14,10 and "South Gate" (2) 3x3 around 13,14.
    private CustomMap CreateMap()
    {
        CustomMap map = CustomMap.Create(MapId, "Events", 48, WorldMapTerrain.Neutral, [WorldRace.Human, WorldRace.Elf]);
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(4, 4)), Is.Null);
        Assert.That(map.PlaceCapital(WorldRace.Elf, new WorldPosition(38, 4)), Is.Null);
        Assert.That(map.PlaceVillage(new WorldPosition(20, 12), 1, WorldRace.Neutral, "Northgate").Error, Is.Null);
        Assert.That(map.PlaceRuin(new WorldPosition(12, 20), 0, "Old ruin").Error, Is.Null);
        Assert.That(map.PlaceStack(new WorldPosition(12, 10), WorldRace.Human, Slots(HumanLeader)).Error, Is.Null);
        Assert.That(map.PlaceStack(new WorldPosition(30, 16), WorldRace.Neutral, Slots(NeutralSoldier)).Error, Is.Null);
        map.PlaceLocation(new WorldPosition(14, 10), 1, "North Gate");
        map.PlaceLocation(new WorldPosition(13, 14), 1, "South Gate");

        return map;
    }

    // Events 1-5 are saveable (7 game events: 1 and 4 have two "or" groups), 6 points at a missing village, 7
    // switches 6 on (so it drops out too) and 8 is for a race that isn't on the map.
    private void AddEvents(CustomMap map)
    {
        CustomMapEvent Event(string name, WorldRace race) => CustomMapEvent.Create(name, race);

        map.AddEvent(Event("Gates", WorldRace.Human) with
        {
            Groups = [[new CustomMapEnterZoneCondition(1)], [new CustomMapEnterZoneCondition(2)]],
            Effects =
            [
                new CustomMapMessageEffect(CustomMapText.Of("The gates!").WithTranslation("ru", "Ворота!"), CustomMapMessageAudience.Triggerer, null, true),
                new CustomMapObjectiveEffect(CustomMapText.Of("Hold the gates."))
            ]
        });
        map.AddEvent(Event("Turn three", WorldRace.Human) with
        {
            Once = false,
            Groups = [[new CustomMapTurnCondition(3, Repeat: false)]],
            Effects = [new CustomMapWinEffect(WorldRace.Human)]
        });
        map.AddEvent(Event("Every second turn", WorldRace.Elf) with
        {
            Once = false,
            Groups = [[new CustomMapTurnCondition(2, Repeat: true)]],
            Effects = [new CustomMapRevealZoneEffect(2, [])]
        });
        map.AddEvent(Event("Northgate", WorldRace.Human) with
        {
            ConcernsRaces = [WorldRace.Human, WorldRace.Elf],
            Chance = 50,
            Order = 2,
            Groups =
            [
                [new CustomMapOwnCityCondition(new CustomMapCity.Village(1))],
                [new CustomMapEnterCityCondition(new CustomMapCity.Capital(WorldRace.Elf))]
            ],
            Effects = [new CustomMapGiveItemEffect(FirstItem, ToCapital: true), new CustomMapSwitchEventEffect(5, On: true)]
        });
        map.AddEvent(Event("Ruin", WorldRace.Human) with
        {
            Enabled = false,
            Groups = [[new CustomMapLootRuinCondition(1), new CustomMapDestroyArmyCondition(2), new CustomMapArmyExistsCondition(1, Exists: true)]],
            // Lose comes first here; the writer saves it last.
            Effects = [new CustomMapLoseEffect(), new CustomMapMessageEffect(CustomMapText.Of("Too late."), CustomMapMessageAudience.AllPlayers, "G000UU5008", false)]
        });
        map.AddEvent(Event("Broken", WorldRace.Human) with { Groups = [[new CustomMapOwnCityCondition(new CustomMapCity.Village(99))]] });
        map.AddEvent(Event("Leads to broken", WorldRace.Human) with
        {
            Groups = [[new CustomMapTurnCondition(5, Repeat: true)]],
            Effects = [new CustomMapSwitchEventEffect(6, On: true)]
        });
        map.AddEvent(Event("Undead only", WorldRace.Undead) with { Groups = [[new CustomMapTurnCondition(1, Repeat: true)]] });
    }

    [Test]
    public void EventRulesTest()
    {
        CustomMap map = CreateMap();
        AddEvents(map);

        Assert.That(map.Events.Select(ev => ev.Id), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.That(map.GetEventProblems(map.GetEvent(1)!), Is.Empty);
        Assert.That(map.GetEventProblems(map.GetEvent(6)!).Select(problem => problem.Kind), Is.EqualTo(new[] { CustomMapEventProblemKind.MissingTarget }));
        Assert.That(map.GetEventProblems(map.GetEvent(7)!), Is.Empty, "Its own blocks are fine; it drops out only because its target does.");
        Assert.That(map.GetEventProblems(map.GetEvent(8)!).Select(problem => problem.Kind), Is.EqualTo(new[] { CustomMapEventProblemKind.NoRace }));
        Assert.That(map.GetSaveableEvents().Select(ev => ev.Id), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));

        // A new event with nothing in it has no condition; two objectives in one event can't be saved.
        CustomMapEvent empty = map.AddEvent(CustomMapEvent.Create("", WorldRace.Elf));
        Assert.That(empty.Name, Is.EqualTo("Event 9"));
        Assert.That(map.GetEventProblems(empty).Select(problem => problem.Kind), Is.EqualTo(new[] { CustomMapEventProblemKind.NoCondition }));
        CustomMapEvent twoObjectives = empty with
        {
            Groups = [[new CustomMapTurnCondition(1, true)]],
            Effects = [new CustomMapObjectiveEffect(CustomMapText.Of("A")), new CustomMapObjectiveEffect(CustomMapText.Of("B"))]
        };
        Assert.That(map.UpdateEvent(twoObjectives), Is.True);
        Assert.That(map.GetEventProblems(twoObjectives).Select(problem => problem.Kind), Is.EqualTo(new[] { CustomMapEventProblemKind.SecondObjective }));

        // Deleting what a block points at makes the event broken; putting a village back with the same ID fixes it.
        Assert.That(map.RemoveVillage(1), Is.True);
        Assert.That(map.GetEventProblems(map.GetEvent(4)!).Select(problem => problem.Kind), Is.EqualTo(new[] { CustomMapEventProblemKind.MissingTarget }));
        Assert.That(map.GetSaveableEvents().Select(ev => ev.Id), Is.EqualTo(new[] { 1, 2, 3, 5 }));
        Assert.That(map.RemoveEvent(9), Is.True);
        Assert.That(map.GetEvent(9), Is.Null);
    }

    // "Used by": which events point at an object, and what blocks can point at on a tile.
    [Test]
    public void EventsUsingTest()
    {
        CustomMap map = CreateMap();
        AddEvents(map);
        string Names(CustomMapEventTarget target) => string.Join(", ", map.GetEventsUsing(target).Select(ev => ev.Name));

        Assert.That(Names(new CustomMapEventTarget.Location(1)), Is.EqualTo("Gates"));
        Assert.That(Names(new CustomMapEventTarget.Location(2)), Is.EqualTo("Gates, Every second turn"), "Condition and reveal effect.");
        Assert.That(Names(new CustomMapEventTarget.Village(1)), Is.EqualTo("Northgate"));
        Assert.That(Names(new CustomMapEventTarget.Capital(WorldRace.Elf)), Is.EqualTo("Northgate"));
        Assert.That(Names(new CustomMapEventTarget.Capital(WorldRace.Human)), Is.Empty);
        Assert.That(Names(new CustomMapEventTarget.Ruin(1)), Is.EqualTo("Ruin"));
        Assert.That(Names(new CustomMapEventTarget.Army(1)), Is.EqualTo("Ruin"));
        Assert.That(Names(new CustomMapEventTarget.Army(2)), Is.EqualTo("Ruin"));
        Assert.That(Names(new CustomMapEventTarget.Event(5)), Is.EqualTo("Northgate"));
        Assert.That(Names(new CustomMapEventTarget.Event(6)), Is.EqualTo("Leads to broken"));

        Assert.That(map.GetEventTargetsAt(new WorldPosition(12, 10)), Is.EqualTo(new CustomMapEventTarget[] { new CustomMapEventTarget.Army(1) }));
        Assert.That(map.GetEventTargetsAt(new WorldPosition(13, 10)), Is.EqualTo(new CustomMapEventTarget[] { new CustomMapEventTarget.Location(1) }));
        Assert.That(map.GetEventTargetsAt(new WorldPosition(21, 13)), Is.EqualTo(new CustomMapEventTarget[] { new CustomMapEventTarget.Village(1) }));
        Assert.That(map.GetEventTargetsAt(new WorldPosition(39, 5)), Is.EqualTo(new CustomMapEventTarget[] { new CustomMapEventTarget.Capital(WorldRace.Elf) }));
        Assert.That(map.GetEventTargetsAt(new WorldPosition(40, 40)), Is.Empty);
    }

    [Test]
    public void EventSaveAndLoadTest()
    {
        CustomMap map = CreateMap();
        AddEvents(map);
        CustomMapStore store = Store;
        store.Save(map);
        ScenarioDatabase database = store.LoadDatabase(MapId);
        Db.ScenarioId scenarioId = new(MapId);

        Assert.That(database.ScenarioEventsByScenario[scenarioId].Select(row => row.Id), Is.EqualTo(Enumerable.Range(1, 7).Select(n => Id("EV", n))));

        Scenario scenario = ScenarioLoader.Load(globalDb, database, campaignDb, new ScenarioId(MapId), definitions, new GlobalVariables());
        Dictionary<string, ScenarioEventDefinition> loaded = scenario.Provider.Events.ToDictionary(ev => ev.Id.Value, StringComparer.OrdinalIgnoreCase);
        Assert.That(loaded.Keys, Is.EquivalentTo(Enumerable.Range(1, 7).Select(n => Id("EV", n))));

        ScenarioEventId Ev(int n) => new(Id("EV", n));
        WorldPlayerId empire = scenario.State.Players.Values.Single(player => player.Race == WorldRace.Human).Id;
        WorldPlayerId elves = scenario.State.Players.Values.Single(player => player.Race == WorldRace.Elf).Id;
        ScenarioTextId Text(int n, string textId, int sequence) => ScenarioTextId.MakeInternal(new Db.Id(Id("EV", n)), textId, sequence);

        // "Gates": two copies, each switches the other off first, then the popup and the objective.
        for (int copy = 1; copy <= 2; copy++)
        {
            int other = copy == 1 ? 2 : 1;
            Assert.That(loaded[Id("EV", copy)], Is.EqualTo(new ScenarioEventDefinition(
                Ev(copy),
                [WorldRace.Human],
                [WorldRace.Human],
                [new ScenarioEventEnteringZoneCondition(new ScenarioLocationId(Id("LO", copy)))],
                [
                    new ScenarioEventEnableEventEffect(Ev(other), false),
                    new ScenarioEventPopupEffect(Text(copy, Db.TextScenario.PopupTextId, 1), ScenarioEventPopupAudience.Triggerer, null, true, false),
                    new ScenarioEventChangeObjectiveEffect(Text(copy, Db.TextScenario.ObjectiveTextId, 0))
                ],
                Enabled: true,
                OccurOnce: true,
                Chance: 100,
                Order: 0)), $"Gates copy {copy}");
        }

        // "Turn three": every 3 turns in a copy that occurs once, Win last.
        Assert.That(loaded[Id("EV", 3)].Conditions, Is.EqualTo(new ScenarioEventCondition[] { new ScenarioEventFrequencyCondition(3) }));
        Assert.That(loaded[Id("EV", 3)].OccurOnce, Is.True);
        Assert.That(loaded[Id("EV", 3)].Effects, Is.EqualTo(new ScenarioEventEffect[] { new ScenarioEventWinEffect(empire) }));

        // "Every second turn": repeats; reveals South Gate for the event's race.
        Assert.That(loaded[Id("EV", 4)].OccurOnce, Is.False);
        Assert.That(loaded[Id("EV", 4)].VerifiedRaces, Is.EqualTo(new[] { WorldRace.Elf }));
        Assert.That(loaded[Id("EV", 4)].Effects, Is.EqualTo(new ScenarioEventEffect[] { new ScenarioEventUnfogEffect(new ScenarioLocationId(Id("LO", 2)), true, null, [elves]) }));

        // "Northgate": the concerned races differ from the triggering race; the switch reaches "Ruin" (EV0007).
        ScenarioEventDefinition northgate = loaded[Id("EV", 5)];
        Assert.That(northgate.VerifiedRaces, Is.EqualTo(new[] { WorldRace.Human }));
        Assert.That(northgate.AffectedRaces, Is.EquivalentTo(new[] { WorldRace.Human, WorldRace.Elf }));
        Assert.That((northgate.Chance, northgate.Order, northgate.OccurOnce), Is.EqualTo((50, 2, true)));
        Assert.That(northgate.Conditions, Is.EqualTo(new ScenarioEventCondition[] { new ScenarioEventOwningCityCondition(new WorldFortificationId(Id("FT", 0x101))) }));
        Assert.That(loaded[Id("EV", 6)].Conditions, Is.EqualTo(new ScenarioEventCondition[] { new ScenarioEventEnteringCityCondition(new WorldFortificationId(Id("FT", 2))) }));
        Assert.That(northgate.Effects, Is.EqualTo(new ScenarioEventEffect[]
        {
            new ScenarioEventEnableEventEffect(Ev(6), false),
            new ScenarioEventGiveItemEffect(FirstItem, ScenarioEventGiveItemTarget.TriggererCapital),
            new ScenarioEventEnableEventEffect(Ev(7), true)
        }));

        // "Ruin": switched off at the start; three conditions; the lose effect is saved last.
        ScenarioEventDefinition ruin = loaded[Id("EV", 7)];
        Assert.That(ruin.Enabled, Is.False);
        Assert.That(ruin.Conditions, Is.EqualTo(new ScenarioEventCondition[]
        {
            new ScenarioEventLootingRuinCondition(new WorldFortificationId(Id("RU", 1))),
            new ScenarioEventDestroyingStackCondition(new ScenarioStackReference.Stack(new WorldStackId(Id("KC", 0x102)))),
            new ScenarioEventStackExistenceCondition(new ScenarioStackReference.Stack(new WorldStackId(Id("KC", 0x101))), MustExist: true)
        }));
        Assert.That(ruin.Effects, Is.EqualTo(new ScenarioEventEffect[]
        {
            new ScenarioEventPopupEffect(Text(7, Db.TextScenario.PopupTextId, 0), ScenarioEventPopupAudience.AllPlayers, new ScenarioEventPortraitId("G000UU5008"), false, false),
            new ScenarioEventWinEffect(null)
        }));

        // Texts: names and messages in the scenario, the Russian message in the map's Russian text file.
        GameTextProvider english = new(globalDb, database, database.TextScenarios, LanguageDatabase.Empty);
        Assert.That(english.GetScenarioText(Text(1, Db.TextScenario.PopupTextId, 1)), Is.EqualTo("The gates!"));
        Assert.That(english.GetScenarioText(Text(2, Db.TextScenario.ObjectiveTextId, 0)), Is.EqualTo("Hold the gates."));
        Assert.That(english.GetScenarioText(ScenarioTextId.MakeInternal(new Db.Id(Id("EV", 5)), Db.TextScenario.NameTextId, 0)), Is.EqualTo("Northgate"));
        LanguageDatabase russianTexts = LanguageDatabaseLoader.Load(
            RiseOfTheElves.GetLanguageGlobalDatabasePath("ru"),
            CustomMapWriter.GetLanguageDirectory(mapsDirectory, "ru"),
            scenarioId);
        GameTextProvider russian = new(globalDb, database, database.TextScenarios, russianTexts);
        Assert.That(russian.GetScenarioText(Text(1, Db.TextScenario.PopupTextId, 1)), Is.EqualTo("Ворота!"));
        Assert.That(russian.GetScenarioText(Text(2, Db.TextScenario.ObjectiveTextId, 0)), Is.EqualTo("Hold the gates."), "No translation: the default text.");
        Assert.That(Directory.Exists(Path.Combine(CustomMapWriter.GetLanguageDirectory(mapsDirectory, "de"), MapId)), Is.False);

        // The editor reopens every event as built, broken ones included.
        CustomMap reopened = store.Open(MapId, definitions);
        Assert.That(reopened.Events, Is.EqualTo(map.Events));

        // Dropping the translation removes the language file on the next save; deleting the map removes the rest.
        CustomMapEvent gates = reopened.GetEvent(1)!;
        CustomMapMessageEffect message = (CustomMapMessageEffect)gates.Effects[0];
        reopened.UpdateEvent(gates with { Effects = gates.Effects.SetItem(0, message with { Text = message.Text.WithTranslation("ru", null) }) });
        store.Save(reopened);
        Assert.That(Directory.Exists(Path.Combine(CustomMapWriter.GetLanguageDirectory(mapsDirectory, "ru"), MapId)), Is.False);
        Assert.That(store.Open(MapId, definitions).Events, Is.EqualTo(reopened.Events));
        store.Delete(MapId);
        Assert.That(Directory.Exists(Path.Combine(mapsDirectory, MapId)), Is.False);
    }

    // Races are stored by race: removing the race before another one renumbers the players, and the events follow.
    [Test]
    public void EventPlayerNumbersTest()
    {
        CustomMap map = CustomMap.Create(MapId, "Races", 48, WorldMapTerrain.Neutral, [WorldRace.Undead, WorldRace.Elf]);
        Assert.That(map.PlaceCapital(WorldRace.Undead, new WorldPosition(4, 4)), Is.Null);
        Assert.That(map.PlaceCapital(WorldRace.Elf, new WorldPosition(38, 4)), Is.Null);
        map.AddEvent(CustomMapEvent.Create("Elves win", WorldRace.Elf) with
        {
            Groups = [[new CustomMapTurnCondition(10, false)]],
            Effects = [new CustomMapWinEffect(WorldRace.Elf)]
        });
        CustomMapStore store = Store;
        store.Save(map);
        Assert.That(store.LoadDatabase(MapId).ScenarioEventEffectsByScenarioAndId[new Db.ScenarioId(MapId)].SelectMany(rows => rows).Single().IdPlayer1?.Value, Is.EqualTo(Id("PL", 2)));

        CustomMap withoutUndead = CustomMap.Create(MapId, "Races", 48, WorldMapTerrain.Neutral, [WorldRace.Elf]);
        Assert.That(withoutUndead.PlaceCapital(WorldRace.Elf, new WorldPosition(38, 4)), Is.Null);
        withoutUndead.AddEvent(map.GetEvent(1)!);
        store.Save(withoutUndead);
        Scenario scenario = ScenarioLoader.Load(globalDb, store.LoadDatabase(MapId), campaignDb, new ScenarioId(MapId), definitions, new GlobalVariables());
        Assert.That(scenario.Provider.Events.Single().Effects, Is.EqualTo(new ScenarioEventEffect[] { new ScenarioEventWinEffect(scenario.State.Players.Values.Single(player => player.Race == WorldRace.Elf).Id) }));
    }

    // The saved events play in the game: entering either gate shows the message once, and turn 3 wins.
    [Test]
    public void EventPlayTest()
    {
        CustomMap map = CreateMap();
        AddEvents(map);
        CustomMapStore store = Store;
        store.Save(map);

        ScenarioConfiguration configuration = new(
            [
                new ScenarioPlayerConfiguration(WorldRace.Human, LordCategory.Warrior, new WorldObjectVariant(0), null, LeaderName: null),
                new ScenarioPlayerConfiguration(WorldRace.Elf, LordCategory.Warrior, new WorldObjectVariant(0), null, LeaderName: null)
            ],
            WorldDifficulty.Average,
            Leader: null);
        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Create(globalDb, store.LoadDatabase(MapId), campaignDb, new ScenarioId(MapId), definitions, globals, configuration);
        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        GameSession session = new(context, scenario.State);
        Assert.That(session.World.DispatchCommand(new WorldStartGameCommand()).Error, Is.Null);
        WorldStackId army = new(CustomMapWriter.GetArmyStackId(MapId, 1));
        List<string> popups = [];

        void Step(int x, int y)
        {
            var result = session.World.DispatchCommand(new WorldStepStackCommand(army, new WorldPosition(x, y)));
            Assert.That(result.Error, Is.Null, $"Step to {x}, {y}");
            popups.AddRange(result.Events.OfType<WorldScenarioPopupEvent>().Select(popup => popup.EventId.Value));
        }

        Step(13, 10);
        Assert.That(popups, Is.EqualTo(new[] { Id("EV", 1) }), "North Gate.");

        Step(13, 11);
        Step(13, 12);
        Step(13, 13);
        Assert.That(popups, Is.EqualTo(new[] { Id("EV", 1) }), "South Gate: only once in all.");
        Assert.That(session.World.State.ScenarioState.EventStates[new ScenarioEventId(Id("EV", 2))], Is.False, "The other copy is switched off.");

        List<WorldEvent> ended = [];

        while (!session.World.State.ScenarioState.IsEnded && session.World.State.TurnInfo.TurnNumber < 5)
        {
            ended.AddRange(session.World.DispatchCommand(new WorldEndTurnCommand()).Events);
        }

        Assert.That(session.World.State.TurnInfo.TurnNumber, Is.EqualTo(3));
        Assert.That(ended.OfType<WorldScenarioEndedEvent>().Single().WinnerId, Is.EqualTo(session.World.State.Players.Values.Single(player => player.Race == WorldRace.Human).Id));
    }
}

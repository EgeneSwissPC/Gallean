using System.Collections.Immutable;
using Gallean.Common;
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
            Groups = [[new CustomMapLootRuinCondition(1), new CustomMapDestroyArmyCondition(2), new CustomMapArmyExistsCondition(1, true)]],
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

    // A win event takes "last one standing wins" away from the races it concerns; the conquest event gives it back:
    // the game ends for the Empire once it holds the elven capital, through the game's own event code.
    [Test]
    public void ConquestWinTest()
    {
        CustomMap map = CreateMap();
        Assert.That(map.GetRacesNeedingConquestWin(), Is.Empty);
        map.AddEvent(CustomMapEvent.Create("Capture Northgate", WorldRace.Human) with
        {
            Groups = [[new CustomMapOwnCityCondition(new CustomMapCity.Village(1))]],
            Effects = [new CustomMapWinEffect(WorldRace.Human)]
        });
        map.AddEvent(CustomMapEvent.Create("Elves lose the village", WorldRace.Elf) with
        {
            Groups = [[new CustomMapOwnCityCondition(new CustomMapCity.Village(1))]],
            Effects = [new CustomMapLoseEffect()]
        });
        Assert.That(map.GetRacesNeedingConquestWin(), Is.EqualTo(new[] { WorldRace.Human, WorldRace.Elf }));

        CustomMapEvent conquest = map.AddConquestWin(WorldRace.Human, "Empire conquers all");
        Assert.That(map.IsConquestWin(conquest, WorldRace.Human), Is.True);
        Assert.That(map.IsConquestWin(conquest, WorldRace.Elf), Is.False);
        Assert.That(conquest.Groups.Single(), Is.EqualTo(new CustomMapCondition[] { new CustomMapOwnCityCondition(new CustomMapCity.Capital(WorldRace.Elf)) }));
        Assert.That(map.GetRacesNeedingConquestWin(), Is.EqualTo(new[] { WorldRace.Elf }));

        Assert.That(PlayWithElvenCapitalCaptured(map), Is.EqualTo(WorldRace.Human));
        map.RemoveEvent(conquest.Id);
        Assert.That(PlayWithElvenCapitalCaptured(map), Is.Null, "Without it the game goes on: the Empire has a win event.");
    }

    // "Hold out against attackers" as the recipe builds it: the attackers' win event makes the defender's capital
    // the computer's goal (the game's AI reads win events), a zone condition gives no goal.
    [Test]
    public void HoldOutGivesTheAiAGoalTest()
    {
        CustomMap map = CreateMap();
        map.AddEvent(CustomMapEvent.Create("Empire holds out", WorldRace.Human) with
        {
            Groups = [[new CustomMapTurnCondition(30, Repeat: false)]],
            Effects = [new CustomMapWinEffect(WorldRace.Human)]
        });
        map.AddEvent(CustomMapEvent.Create("Elves take the Empire capital", WorldRace.Elf) with
        {
            Groups = [[new CustomMapOwnCityCondition(new CustomMapCity.Capital(WorldRace.Human))]],
            Effects = [new CustomMapWinEffect(WorldRace.Elf)]
        });
        map.AddEvent(CustomMapEvent.Create("Elves reach the gate", WorldRace.Elf) with
        {
            Groups = [[new CustomMapEnterZoneCondition(1)]],
            Effects = [new CustomMapWinEffect(WorldRace.Elf)]
        });
        CustomMapStore store = Store;
        store.Save(map);
        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Load(globalDb, store.LoadDatabase(MapId), campaignDb, new ScenarioId(MapId), definitions, globals);
        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        WorldPlayerId elves = scenario.State.Players.Values.Single(player => player.Race == WorldRace.Elf).Id;
        WorldCapital empireCapital = scenario.State.Fortifications.Values.OfType<WorldCapital>().Single(capital => scenario.State.Players[capital.OwnerId].Race == WorldRace.Human);

        Assert.That(new WorldAiScenario(context).GetObjectiveTargets(scenario.State, elves), Is.EquivalentTo(new WorldAiTarget[] { new WorldAiTarget.Fortification(empireCapital.Id) }));
    }

    // Saves the map, gives the Empire the elven capital and plays three rounds: the race that won, or null.
    private WorldRace? PlayWithElvenCapitalCaptured(CustomMap map)
    {
        CustomMapStore store = Store;
        store.Save(map);
        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Load(globalDb, store.LoadDatabase(MapId), campaignDb, new ScenarioId(MapId), definitions, globals);
        WorldPlayerId empire = scenario.State.Players.Values.Single(player => player.Race == WorldRace.Human).Id;
        WorldCapital elvenCapital = scenario.State.Fortifications.Values.OfType<WorldCapital>().Single(capital => scenario.State.Players[capital.OwnerId].Race == WorldRace.Elf);
        WorldState captured = scenario.State with { Fortifications = scenario.State.Fortifications.SetItem(elvenCapital.Id, elvenCapital with { CapturedById = empire }) };
        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        GameSession session = new(context, captured);

        List<WorldEvent> events = [.. session.World.DispatchCommand(new WorldStartGameCommand()).Events];

        while (!session.World.State.ScenarioState.IsEnded && session.World.State.TurnInfo.TurnNumber < 3)
        {
            events.AddRange(session.World.DispatchCommand(new WorldEndTurnCommand()).Events);
        }

        return events.OfType<WorldScenarioEndedEvent>().SingleOrDefault() is { WinnerId: { } winner } ? scenario.State.Players[winner].Race : null;
    }

    // Raiders: neutral leader + soldier, defending South Gate when created.
    private CustomMapArmyTemplate Raiders()
    {
        CustomMapUnit?[] slots = new CustomMapUnit?[GridPosition.Count];
        slots[GridPosition.FrontCenter.ToIndex()] = NeutralSoldier;
        slots[GridPosition.BackCenter.ToIndex()] = CustomMapUnit.From(Store.ListUnits(definitions)
            .Where(unit => unit is { Category: UnitCategory.Soldier, Race: WorldRace.Neutral, IsLarge: false })
            .OrderBy(unit => unit.Id.Value, StringComparer.Ordinal)
            .First(), 3, definitions);

        return new CustomMapArmyTemplate(0, "Raiders", WorldRace.Neutral, [.. slots], new CustomMapArmyOrder(CustomMapOrderKind.DefendZone, LocationId: 2));
    }

    // Army templates and the army effects (phase 5) through the game's loader: SStkTemp rows and every effect kind.
    [Test]
    public void TemplateSaveAndLoadTest()
    {
        CustomMap map = CreateMap();
        CustomMapArmyTemplate raiders = map.AddTemplate(Raiders());
        Assert.That((raiders.Id, map.GetTemplateError(raiders), map.IsTemplateSaveable(raiders)), Is.EqualTo((1, (CustomMapStackError?)null, true)));
        map.AddTemplate(Raiders() with { Name = "No leader", Slots = [.. new CustomMapUnit?[GridPosition.Count]] });
        Assert.That(map.GetTemplateError(map.GetTemplate(2)!), Is.EqualTo(CustomMapStackError.NoLeader));

        CustomMapArmyRef template = new CustomMapArmyRef.Template(1);
        CustomMapArmyRef neutralArmy = new CustomMapArmyRef.Army(2);
        map.AddEvent(CustomMapEvent.Create("Ambush", WorldRace.Human) with
        {
            Groups = [[new CustomMapEnterZoneCondition(1)]],
            Effects =
            [
                new CustomMapCreateArmyEffect(1, 2),
                new CustomMapChangeOrderEffect(template, new CustomMapArmyOrder(CustomMapOrderKind.MoveToZone, LocationId: 1)),
                new CustomMapChangeOrderEffect(neutralArmy, new CustomMapArmyOrder(CustomMapOrderKind.AttackArmy, ArmyId: 1)),
                new CustomMapChangeOrderEffect(neutralArmy, new CustomMapArmyOrder(CustomMapOrderKind.DefendCity, City: new CustomMapCity.Village(1))),
                new CustomMapChangeOrderEffect(neutralArmy, new CustomMapArmyOrder(CustomMapOrderKind.SecureCity, City: new CustomMapCity.Village(1))),
                new CustomMapChangeOwnerEffect(neutralArmy, WorldRace.Elf),
                new CustomMapChangeOwnerEffect(template, null),
                new CustomMapMoveArmyEffect(new CustomMapArmyRef.Triggerer(), 2),
                new CustomMapMoveArmyEffect(neutralArmy, 1),
                new CustomMapMoveBesideEffect(template),
                new CustomMapRemoveArmyEffect(neutralArmy),
                new CustomMapBattleEffect(template)
            ]
        });
        map.AddEvent(CustomMapEvent.Create("Broken template", WorldRace.Human) with
        {
            Groups = [[new CustomMapTurnCondition(2, true)]],
            Effects = [new CustomMapCreateArmyEffect(2, 1)]
        });
        Assert.That(map.GetSaveableEvents().Select(ev => ev.Name), Is.EqualTo(new[] { "Ambush" }));
        Assert.That(map.GetEventsUsing(new CustomMapEventTarget.Template(1)).Select(ev => ev.Name), Is.EqualTo(new[] { "Ambush" }));
        Assert.That(map.GetEventsUsing(new CustomMapEventTarget.Army(1)).Select(ev => ev.Name), Is.EqualTo(new[] { "Ambush" }), "The attack order's target.");

        CustomMapStore store = Store;
        store.Save(map);
        Scenario scenario = ScenarioLoader.Load(globalDb, store.LoadDatabase(MapId), campaignDb, new ScenarioId(MapId), definitions, new GlobalVariables());

        // The template: one row (the broken one is left out), units and order as built.
        ScenarioStackTemplateDefinition loaded = scenario.Provider.StackTemplates.Single();
        Assert.That(loaded.Id, Is.EqualTo(new ScenarioStackTemplateId(Id("TM", 1))));
        Assert.That(scenario.State.Players[loaded.OwnerId].Race, Is.EqualTo(WorldRace.Neutral));
        Assert.That(loaded.Order, Is.EqualTo(new WorldStackOrder.DefendLocation(new ScenarioLocationId(Id("LO", 2)), new WorldPosition(13, 14), 1)));

        for (int p = 0; p < GridPosition.Count; p++)
        {
            GridPosition position = new(p % GridPosition.LineCount, p / GridPosition.LineCount);
            CustomMapUnit? unit = raiders.Slots[position.ToIndex()];
            Assert.That(loaded.Units[position] is { } placed ? (placed.DefinitionId, placed.Level) : default, Is.EqualTo(unit is null ? default : (unit.TypeId, unit.Level)), $"Slot {position}");
        }

        ScenarioStackReference templateReference = new ScenarioStackReference.Template(new ScenarioStackTemplateId(Id("TM", 1)));
        ScenarioStackReference armyReference = new ScenarioStackReference.Stack(new WorldStackId(Id("KC", 0x102)));
        WorldSubraceId elvenSubrace = new(new Db.Id(Id("SR", 2)));
        Assert.That(scenario.Provider.Events.Single().Effects, Is.EqualTo(new ScenarioEventEffect[]
        {
            new ScenarioEventCreateStackEffect(new ScenarioStackTemplateId(Id("TM", 1)), new ScenarioLocationId(Id("LO", 2))),
            new ScenarioEventChangeOrderEffect(templateReference, new WorldStackOrder.MoveToLocation(new ScenarioLocationId(Id("LO", 1)), new WorldPosition(14, 10), 1), false),
            new ScenarioEventChangeOrderEffect(armyReference, new WorldStackOrder.AttackStack(new WorldStackId(Id("KC", 0x101))), false),
            new ScenarioEventChangeOrderEffect(armyReference, new WorldStackOrder.DefendCity(new WorldFortificationId(Id("FT", 0x101))), false),
            new ScenarioEventChangeOrderEffect(armyReference, new WorldStackOrder.SecureCity(new WorldFortificationId(Id("FT", 0x101))), false),
            new ScenarioEventChangeOwnerEffect(armyReference, new ScenarioEventStackOwner.Subrace(elvenSubrace), false, true),
            new ScenarioEventChangeOwnerEffect(templateReference, ScenarioEventStackOwner.Triggerer.Instance, false, true),
            new ScenarioEventMoveStackEffect(null, new ScenarioLocationId(Id("LO", 2))),
            new ScenarioEventMoveStackEffect(armyReference, new ScenarioLocationId(Id("LO", 1))),
            new ScenarioEventMoveBesideEffect(templateReference),
            new ScenarioEventRemoveStackEffect(armyReference, false),
            new ScenarioEventBattleEffect(templateReference)
        }));

        CustomMap reopened = store.Open(MapId, definitions);
        Assert.That((reopened.Templates, reopened.Events), Is.EqualTo((map.Templates, map.Events)).Using<(IReadOnlyList<CustomMapArmyTemplate>, IReadOnlyList<CustomMapEvent>)>((a, b) => a.Item1.SequenceEqual(b.Item1) && a.Item2.SequenceEqual(b.Item2)));
    }

    // The ambush plays: entering North Gate creates the raiders at South Gate with the template's order.
    [Test]
    public void AmbushPlayTest()
    {
        CustomMap map = CreateMap();
        map.AddTemplate(Raiders());
        map.AddEvent(CustomMapEvent.Create("Ambush", WorldRace.Human) with
        {
            Groups = [[new CustomMapEnterZoneCondition(1)]],
            Effects = [new CustomMapCreateArmyEffect(1, 2)]
        });
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
        Assert.That(session.World.State.Stacks.Values.Where(stack => stack.SourceTemplateId is not null), Is.Empty);

        Assert.That(session.World.DispatchCommand(new WorldStepStackCommand(new WorldStackId(CustomMapWriter.GetArmyStackId(MapId, 1)), new WorldPosition(13, 10))).Error, Is.Null);

        WorldStack raiders = session.World.State.Stacks.Values.Single(stack => stack.SourceTemplateId == new ScenarioStackTemplateId(Id("TM", 1)));
        Assert.That(raiders.Position.GetDistance(new WorldPosition(13, 14)), Is.LessThanOrEqualTo(1), "Inside South Gate.");
        Assert.That(raiders.Order, Is.EqualTo(new WorldStackOrder.DefendLocation(new ScenarioLocationId(Id("LO", 2)), new WorldPosition(13, 14), 1)));
        Assert.That(session.World.State.Players[raiders.OwnerId].Race, Is.EqualTo(WorldRace.Neutral));
    }

    // The ambush recipe's chain: the created raiders attack the army that fired the event at once.
    [Test]
    public void AmbushBattleTest()
    {
        CustomMap map = CreateMap();
        map.AddTemplate(Raiders());
        map.AddEvent(CustomMapEvent.Create("Ambush", WorldRace.Human) with
        {
            Groups = [[new CustomMapEnterZoneCondition(1)]],
            Effects = [new CustomMapCreateArmyEffect(1, 2), new CustomMapBattleEffect(new CustomMapArmyRef.Template(1))]
        });
        CustomMapStore store = Store;
        store.Save(map);
        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Load(globalDb, store.LoadDatabase(MapId), campaignDb, new ScenarioId(MapId), definitions, globals);
        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        GameSession session = new(context, scenario.State);
        Assert.That(session.World.DispatchCommand(new WorldStartGameCommand()).Error, Is.Null);
        WorldStackId army = new(CustomMapWriter.GetArmyStackId(MapId, 1));

        var result = session.World.DispatchCommand(new WorldStepStackCommand(army, new WorldPosition(13, 10)));

        Assert.That(result.Events.OfType<WorldStackBattleStartedEvent>().Single().DefenderStackId, Is.EqualTo(army));
        Assert.That(session.World.State.Stacks.Values.Single(stack => stack.SourceTemplateId is not null).Position.GetDistance(session.World.State.Stacks[army].Position), Is.LessThanOrEqualTo(1), "The raiders moved next to the army.");
    }

    // Neutrals retaking a village: a neutral template with the "capture a city" order (the game's SecureCity) is
    // created next to an Empire village at the neutral player's turn start, and the neutral AI walks in and takes it.
    [Test]
    public void SecureCityPlayTest()
    {
        CustomMap map = CustomMap.Create(MapId, "Retake", 48, WorldMapTerrain.Neutral, [WorldRace.Human, WorldRace.Elf]);
        Assert.That(map.PlaceCapital(WorldRace.Human, new WorldPosition(4, 4)), Is.Null);
        Assert.That(map.PlaceCapital(WorldRace.Elf, new WorldPosition(38, 38)), Is.Null);
        (CustomMapVillage? village, CustomMapPlacementError? error) = map.PlaceVillage(new WorldPosition(20, 20), 1, WorldRace.Human, "Eastholm");
        Assert.That((village is not null, error), Is.EqualTo((true, (CustomMapPlacementError?)null)));
        CustomMapLocation camp = map.PlaceLocation(new WorldPosition(26, 21), 1, "Camp");
        CustomMapArmyTemplate retakers = map.AddTemplate(Raiders() with
        {
            Name = "Retakers",
            Order = new CustomMapArmyOrder(CustomMapOrderKind.SecureCity, City: new CustomMapCity.Village(village!.Id))
        });
        Assert.That(map.IsTemplateSaveable(retakers), Is.True);
        Assert.That(map.GetEventsUsing(new CustomMapEventTarget.Village(village.Id)), Is.Empty);
        map.AddEvent(CustomMapEvent.Create("Muster", WorldRace.Neutral) with
        {
            Groups = [[new CustomMapTurnCondition(1, Repeat: false)]],
            Effects = [new CustomMapCreateArmyEffect(retakers.Id, camp.Id)]
        });
        CustomMapStore store = Store;
        store.Save(map);
        Assert.That(store.Open(MapId, definitions).Templates.Single().Order, Is.EqualTo(retakers.Order));

        ScenarioConfiguration configuration = new(
            [new ScenarioPlayerConfiguration(WorldRace.Human, LordCategory.Warrior, new WorldObjectVariant(0), null, LeaderName: null)],
            WorldDifficulty.Average,
            Leader: null);
        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Create(globalDb, store.LoadDatabase(MapId), campaignDb, new ScenarioId(MapId), definitions, globals, configuration);
        WorldFortificationId villageId = new(Id("FT", 0x100 + village.Id));
        Assert.That(scenario.Provider.StackTemplates.Single().Order, Is.EqualTo(new WorldStackOrder.SecureCity(villageId)));
        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        GameSession session = new(context, scenario.State);
        Assert.That(session.World.DispatchCommand(new WorldStartGameCommand()).Error, Is.Null);
        TestWorldAiRunner runner = new(session, context);
        WorldRace OwnerRace() => session.World.State.Players[((WorldCity)session.World.State.Fortifications[villageId]).OwnerId].Race;
        Assert.That(OwnerRace(), Is.EqualTo(WorldRace.Human));

        for (int turn = 0; turn < 3 && OwnerRace() != WorldRace.Neutral; turn++)
        {
            Assert.That(session.World.DispatchCommand(new WorldEndTurnCommand()).Error, Is.Null);

            while (session.World.State.Players[session.World.State.TurnInfo.CurrentPlayerId].IsAi)
            {
                runner.PlayTurn();
            }
        }

        Assert.That(OwnerRace(), Is.EqualTo(WorldRace.Neutral), "The created army took the village back.");
        Assert.That(session.World.State.Stacks.Values.Count(stack => stack.SourceTemplateId is not null), Is.EqualTo(1), "Created once.");
    }

    // Phase 6: every remaining kind through the game's loader, and variables as SVars rows.
    [Test]
    public void RemainingKindsSaveAndLoadTest()
    {
        CustomMap map = CreateMap();
        CustomMapStore store = Store;
        Assert.That(map.PlaceSite(new WorldPosition(30, 20), CustomMapSiteType.Merchant, 0, "Market", "").Error, Is.Null);
        WorldLandmarkDefinition[] landmarks = [.. store.ListLandmarks(definitions).Where(landmark => landmark.Width == 1 && !landmark.Mountain).OrderBy(landmark => landmark.Id.Value, StringComparer.Ordinal)];
        Assert.That(map.PlaceLandmark(new WorldPosition(40, 30), landmarks[0]).Error, Is.Null);
        CustomMapVariable counter = map.AddVariable("Counter", 2);
        CustomMapVariable met = map.AddVariable("Met the elder");
        ItemDefinitionId item = FirstItem;
        WorldSpellDefinitionId spell = store.ListSpells(definitions).OrderBy(candidate => candidate.Id.Value, StringComparer.Ordinal).First().Id;
        map.AddTemplate(Raiders());
        CustomMapArmyRef raiders = new CustomMapArmyRef.Template(1);

        map.AddEvent(CustomMapEvent.Create("Kinds", WorldRace.Human) with
        {
            Groups =
            [
                [
                    new CustomMapOwnItemCondition(item),
                    new CustomMapLeaderItemCondition(new CustomMapArmyRef.Army(1), item),
                    new CustomMapRelationCondition(WorldRace.Human, WorldRace.Elf, CustomMapRelation.War),
                    new CustomMapAllianceCondition(WorldRace.Human, WorldRace.Elf),
                    new CustomMapLandCondition(40),
                    new CustomMapVisitSiteCondition(1),
                    new CustomMapArmyToZoneCondition(raiders, 1),
                    new CustomMapArmyToCityCondition(new CustomMapArmyRef.Army(1), new CustomMapCity.Village(1)),
                    new CustomMapItemToZoneCondition(item, 2),
                    new CustomMapVariableCondition(counter.Id, 1, 5, CustomMapVariableJoin.Or, met.Id, 1, 1),
                    new CustomMapDestroyArmyCondition(raiders),
                    new CustomMapArmyExistsCondition(raiders, false)
                ]
            ],
            Effects =
            [
                new CustomMapCastSpellEffect(spell, null),
                new CustomMapCastSpellZoneEffect(spell, 2, WorldRace.Elf),
                new CustomMapGiveSpellEffect(spell),
                new CustomMapAllyEffect(WorldRace.Human, WorldRace.Elf, true),
                new CustomMapRelationEffect(WorldRace.Human, WorldRace.Elf, CustomMapRelation.Neutral),
                new CustomMapRemoveMountainsEffect(1),
                new CustomMapChangeLandmarkEffect(1, landmarks[1].Id),
                new CustomMapRemoveLandmarkEffect(1),
                new CustomMapChangeTerrainEffect(2, WorldMapTerrain.Undead, 5),
                new CustomMapVariableEffect(counter.Id, ScenarioVariableOperation.Multiply, 3),
                new CustomMapDestroyItemEffect(item, true),
                new CustomMapRevealZoneEffect(1, [WorldRace.Elf])
            ]
        });
        Assert.That(map.GetEventProblems(map.Events.Single()), Is.Empty);
        Assert.That(map.GetEventsUsing(new CustomMapEventTarget.Variable(met.Id)), Has.Count.EqualTo(1));
        Assert.That(map.GetEventsUsing(new CustomMapEventTarget.Site(1)), Has.Count.EqualTo(1));
        Assert.That(map.GetEventsUsing(new CustomMapEventTarget.Landmark(1)), Has.Count.EqualTo(1));
        Assert.That(map.GetEventTargetsAt(new WorldPosition(40, 30)), Does.Contain(new CustomMapEventTarget.Landmark(1)));

        store.Save(map);
        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Load(globalDb, store.LoadDatabase(MapId), campaignDb, new ScenarioId(MapId), definitions, globals);
        ScenarioEventDefinition loaded = scenario.Provider.Events.Single();
        WorldPlayerId empire = scenario.State.Players.Values.Single(player => player.Race == WorldRace.Human).Id;
        WorldPlayerId elves = scenario.State.Players.Values.Single(player => player.Race == WorldRace.Elf).Id;
        ScenarioStackReference templateReference = new ScenarioStackReference.Template(new ScenarioStackTemplateId(Id("TM", 1)));
        ScenarioStackReference armyReference = new ScenarioStackReference.Stack(new WorldStackId(Id("KC", 0x101)));
        ScenarioLocationId north = new(Id("LO", 1));
        ScenarioLocationId south = new(Id("LO", 2));

        Assert.That(scenario.Provider.Variables.Select(variable => (variable.Id.Value, variable.Name, variable.InitialValue)), Is.EqualTo(new[] { (1, "Counter", 2), (2, "Met the elder", 0) }));
        Assert.That(loaded.Conditions, Is.EqualTo(new ScenarioEventCondition[]
        {
            new ScenarioEventOwningItemCondition(item),
            new ScenarioEventLeaderOwningItemCondition(armyReference, item),
            new ScenarioEventDiplomacyRelationsCondition(empire, elves, WorldRelationLevel.War),
            new ScenarioEventAllianceCondition(empire, elves),
            new ScenarioEventTransformingLandCondition(40),
            new ScenarioEventVisitingSiteCondition(new WorldSiteId(Id("SI", 1))),
            new ScenarioEventStackToLocationCondition(templateReference, north),
            new ScenarioEventStackToCityCondition(armyReference, new WorldFortificationId(Id("FT", 0x101))),
            new ScenarioEventItemToLocationCondition(item, south),
            new ScenarioEventVariableInRangeCondition(
                new ScenarioVariableRange(new ScenarioVariableId(1), 1, 5),
                new ScenarioVariableClause(ScenarioVariableOperator.Or, new ScenarioVariableRange(new ScenarioVariableId(2), 1, 1))),
            new ScenarioEventDestroyingStackCondition(templateReference),
            new ScenarioEventStackExistenceCondition(templateReference, MustExist: false)
        }));
        Assert.That(loaded.Effects, Is.EqualTo(new ScenarioEventEffect[]
        {
            new ScenarioEventCastSpellEffect(spell, null),
            new ScenarioEventCastSpellMapEffect(spell, south, elves),
            new ScenarioEventGiveSpellEffect(spell),
            new ScenarioEventAllyEffect(empire, elves, true),
            new ScenarioEventDiplomacyEffect(empire, elves, 49, null),
            new ScenarioEventRemoveMountainEffect(north),
            new ScenarioEventChangeLandmarkEffect(new WorldLandmarkId(Id("MM", 1)), landmarks[1].Id),
            new ScenarioEventRemoveLandmarkEffect(new WorldLandmarkId(Id("MM", 1))),
            new ScenarioEventChangeTerrainEffect(south, WorldMapTerrain.Undead, 5),
            new ScenarioEventModifyVariableEffect(new ScenarioVariableId(1), ScenarioVariableOperation.Multiply, 3),
            new ScenarioEventDestroyItemEffect(item, true),
            new ScenarioEventUnfogEffect(north, true, null, [elves])
        }));

        CustomMap reopened = store.Open(MapId, definitions);
        Assert.That(reopened.Events.Single(), Is.EqualTo(map.Events.Single()));
        Assert.That(reopened.Variables, Is.EqualTo(map.Variables));
    }

    // Variables play: every turn adds 1 to a counter starting at 0; when it reaches 3 the Empire wins (turn 3).
    [Test]
    public void VariablePlayTest()
    {
        CustomMap map = CreateMap();
        CustomMapVariable counter = map.AddVariable("Counter");
        map.AddEvent(CustomMapEvent.Create("Count", WorldRace.Human) with
        {
            Once = false,
            Groups = [[new CustomMapTurnCondition(1, Repeat: true)]],
            Effects = [new CustomMapVariableEffect(counter.Id, ScenarioVariableOperation.Add, 1)]
        });
        map.AddEvent(CustomMapEvent.Create("Three", WorldRace.Human) with
        {
            Groups = [[new CustomMapVariableCondition(counter.Id, 3, 99)]],
            Effects = [new CustomMapWinEffect(WorldRace.Human)]
        });
        CustomMapStore store = Store;
        store.Save(map);
        GlobalVariables globals = new();
        Scenario scenario = ScenarioLoader.Load(globalDb, store.LoadDatabase(MapId), campaignDb, new ScenarioId(MapId), definitions, globals);
        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        GameSession session = new(context, scenario.State);
        List<WorldEvent> events = [.. session.World.DispatchCommand(new WorldStartGameCommand()).Events];

        while (!session.World.State.ScenarioState.IsEnded && session.World.State.TurnInfo.TurnNumber < 6)
        {
            events.AddRange(session.World.DispatchCommand(new WorldEndTurnCommand()).Events);
        }

        Assert.That(session.World.State.TurnInfo.TurnNumber, Is.EqualTo(3));
        Assert.That(scenario.State.Players[events.OfType<WorldScenarioEndedEvent>().Single().WinnerId!.Value].Race, Is.EqualTo(WorldRace.Human));
    }

    // Chains: a quest (1 on -> 2 -> 3), an off switch, a back link, and an event that can never fire.
    [Test]
    public void EventChainsTest()
    {
        CustomMap map = CreateMap();
        CustomMapEvent Quest(string name, bool enabled, params CustomMapEffect[] effects) =>
            CustomMapEvent.Create(name, WorldRace.Human) with { Enabled = enabled, Groups = [[new CustomMapTurnCondition(1, true)]], Effects = [.. effects] };
        map.AddEvent(Quest("Elder asks", true, new CustomMapSwitchEventEffect(2, true)));
        map.AddEvent(Quest("Ruin looted", false, new CustomMapSwitchEventEffect(3, true), new CustomMapSwitchEventEffect(1, false)));
        map.AddEvent(Quest("Reward", false, new CustomMapSwitchEventEffect(1, true)));
        map.AddEvent(Quest("Forgotten", false, new CustomMapSwitchEventEffect(5, true)));
        map.AddEvent(Quest("Never reached", false));

        CustomMapEventChains chains = map.GetEventChains();

        Assert.That(chains.Links, Is.EquivalentTo(new[]
        {
            new CustomMapEventLink(1, 2, true), new CustomMapEventLink(2, 3, true), new CustomMapEventLink(2, 1, false),
            new CustomMapEventLink(3, 1, true), new CustomMapEventLink(4, 5, true)
        }));
        Assert.That(new[] { 1, 2, 3 }.Select(id => chains.Layers[id]), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(chains.NeverFires, Is.EqualTo(new[] { 4, 5 }));
        Assert.That((chains.Layers[4], chains.Layers[5]), Is.EqualTo((0, 1)), "Unreachable chains keep their own order.");
    }
}

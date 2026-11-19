using System.Collections.Immutable;
using Gallean.Common;

namespace Gallean.Engine;

// Events: the scenario's script, built in the editor as blocks (docs/map-editor-events.md). An event holds condition
// groups (the conditions inside a group must all hold, the groups are alternatives: "or") and effects that run in
// order. Blocks point at editor objects by their editor IDs and at races by race, never at scenario IDs, so moving
// objects or removing a race never makes an event point at the wrong thing; a block whose target is gone makes the
// event broken, and the writer leaves broken events out of the saved scenario (CustomMap.GetSaveableEvents).
public sealed partial class CustomMap
{
    public const int MaxEventChance = 100;

    public CustomMapEvent? GetEvent(int id)
    {
        return events.FirstOrDefault(ev => ev.Id == id);
    }

    // Adds an event with a new ID (the given ID is ignored) and returns it.
    public CustomMapEvent AddEvent(CustomMapEvent ev)
    {
        int id = events.Count == 0 ? 1 : events.Max(existing => existing.Id) + 1;
        CustomMapEvent added = ev with { Id = id, Name = string.IsNullOrWhiteSpace(ev.Name) ? $"Event {id}" : ev.Name.Trim() };
        events.Add(added);

        return added;
    }

    // Replaces the event with the same ID. False when there is no such event.
    public bool UpdateEvent(CustomMapEvent ev)
    {
        int index = events.FindIndex(existing => existing.Id == ev.Id);

        if (index < 0)
        {
            return false;
        }

        events[index] = ev;

        return true;
    }

    public bool RemoveEvent(int id)
    {
        return events.RemoveAll(ev => ev.Id == id) > 0;
    }

    // What keeps an event out of the saved scenario; empty = it is saved. Phase 3 shows these as red blocks.
    public IReadOnlyList<CustomMapEventProblem> GetEventProblems(CustomMapEvent ev)
    {
        List<CustomMapEventProblem> problems = [];

        if (GetEventRaces(ev.Races).IsEmpty)
        {
            problems.Add(new CustomMapEventProblem(CustomMapEventProblemKind.NoRace, null));
        }

        if (ev.Groups.All(group => group.IsEmpty))
        {
            problems.Add(new CustomMapEventProblem(CustomMapEventProblemKind.NoCondition, null));
        }

        foreach (CustomMapCondition condition in ev.Groups.SelectMany(group => group))
        {
            if (!ConditionTargetExists(condition))
            {
                problems.Add(new CustomMapEventProblem(CustomMapEventProblemKind.MissingTarget, condition));
            }
        }

        int objectives = 0;

        foreach (CustomMapEffect effect in ev.Effects)
        {
            if (effect is CustomMapObjectiveEffect && ++objectives > 1)
            {
                problems.Add(new CustomMapEventProblem(CustomMapEventProblemKind.SecondObjective, effect));
            }
            else if (!EffectTargetExists(effect))
            {
                problems.Add(new CustomMapEventProblem(CustomMapEventProblemKind.MissingTarget, effect));
            }
        }

        return problems;
    }

    // The events the writer saves: no problems, and every event they switch on or off is saved too (repeated until
    // nothing changes, so a chain that leads to a broken event drops out as a whole).
    public IReadOnlyList<CustomMapEvent> GetSaveableEvents()
    {
        List<CustomMapEvent> saveable = [.. events.Where(ev => GetEventProblems(ev).Count == 0)];
        bool changed = true;

        while (changed)
        {
            HashSet<int> ids = [.. saveable.Select(ev => ev.Id)];
            int before = saveable.Count;
            saveable.RemoveAll(ev => ev.Effects.OfType<CustomMapSwitchEventEffect>().Any(effect => !ids.Contains(effect.EventId)));
            changed = saveable.Count != before;
        }

        return saveable;
    }

    // The events with a block pointing at target ("used by" in the hover line, warnings before deleting).
    public IReadOnlyList<CustomMapEvent> GetEventsUsing(CustomMapEventTarget target)
    {
        return [.. events.Where(ev => ev.Groups.SelectMany(group => group).Any(condition => Uses(condition, target)) || ev.Effects.Any(effect => Uses(effect, target)))];
    }

    // What blocks can point at on a tile: the capital, village, ruin and army there and every location covering it.
    public IReadOnlyList<CustomMapEventTarget> GetEventTargetsAt(WorldPosition position)
    {
        List<CustomMapEventTarget> targets = [];

        if (GetCapitalAt(position) is { } capital)
        {
            targets.Add(new CustomMapEventTarget.Capital(capital.Race));
        }

        if (GetVillageAt(position) is { } village)
        {
            targets.Add(new CustomMapEventTarget.Village(village.Id));
        }

        if (GetRuinAt(position) is { } ruin)
        {
            targets.Add(new CustomMapEventTarget.Ruin(ruin.Id));
        }

        if (GetStackAt(position) is { } stack)
        {
            targets.Add(new CustomMapEventTarget.Army(stack.Id));
        }

        if (sites.FirstOrDefault(site => site.Footprint.Contains(position)) is { } site)
        {
            targets.Add(new CustomMapEventTarget.Site(site.Id));
        }

        if (GetLandmarkAt(position) is { } landmark)
        {
            targets.Add(new CustomMapEventTarget.Landmark(landmark.Id));
        }

        targets.AddRange(GetLocationsAt(position).Select(location => new CustomMapEventTarget.Location(location.Id)));

        return targets;
    }

    private static bool Uses(CustomMapCondition condition, CustomMapEventTarget target)
    {
        return (condition, target) switch
        {
            (CustomMapEnterZoneCondition zone, CustomMapEventTarget.Location location) => zone.LocationId == location.Id,
            (CustomMapOwnCityCondition own, _) => IsCity(own.City, target),
            (CustomMapEnterCityCondition enter, _) => IsCity(enter.City, target),
            (CustomMapLootRuinCondition loot, CustomMapEventTarget.Ruin ruin) => loot.RuinId == ruin.Id,
            (CustomMapDestroyArmyCondition destroy, _) => UsesArmy(destroy.Army, target),
            (CustomMapArmyExistsCondition exists, _) => UsesArmy(exists.Army, target),
            _ => KindUses(condition, target)
        };
    }

    private static bool Uses(CustomMapEffect effect, CustomMapEventTarget target)
    {
        return (effect, target) switch
        {
            (CustomMapRevealZoneEffect reveal, CustomMapEventTarget.Location location) => reveal.LocationId == location.Id,
            (CustomMapSwitchEventEffect switchEvent, CustomMapEventTarget.Event ev) => switchEvent.EventId == ev.Id,
            (CustomMapCreateArmyEffect create, CustomMapEventTarget.Template template) => create.TemplateId == template.Id,
            (CustomMapCreateArmyEffect create, CustomMapEventTarget.Location location) => create.LocationId == location.Id,
            (CustomMapMoveArmyEffect move, CustomMapEventTarget.Location location) => move.LocationId == location.Id,
            (CustomMapRemoveArmyEffect remove, _) => UsesArmy(remove.Army, target),
            (CustomMapMoveArmyEffect move, _) => UsesArmy(move.Army, target),
            (CustomMapMoveBesideEffect beside, _) => UsesArmy(beside.Army, target),
            (CustomMapChangeOwnerEffect owner, _) => UsesArmy(owner.Army, target),
            (CustomMapChangeOrderEffect order, _) => UsesArmy(order.Army, target) || UsesOrder(order.Order, target),
            (CustomMapBattleEffect battle, _) => UsesArmy(battle.Army, target),
            _ => KindUses(effect, target)
        };
    }

    private static bool UsesArmy(CustomMapArmyRef army, CustomMapEventTarget target)
    {
        return (army, target) switch
        {
            (CustomMapArmyRef.Army placed, CustomMapEventTarget.Army other) => placed.Id == other.Id,
            (CustomMapArmyRef.Template template, CustomMapEventTarget.Template other) => template.Id == other.Id,
            _ => false
        };
    }

    private static bool UsesOrder(CustomMapArmyOrder order, CustomMapEventTarget target)
    {
        return order.Kind switch
        {
            CustomMapOrderKind.MoveToZone or CustomMapOrderKind.DefendZone => target is CustomMapEventTarget.Location location && location.Id == order.LocationId,
            CustomMapOrderKind.DefendCity => order.City is { } city && IsCity(city, target),
            CustomMapOrderKind.AttackArmy => target is CustomMapEventTarget.Army army && army.Id == order.ArmyId,
            _ => false
        };
    }

    private static bool IsCity(CustomMapCity city, CustomMapEventTarget target)
    {
        return (city, target) switch
        {
            (CustomMapCity.Village village, CustomMapEventTarget.Village other) => village.Id == other.Id,
            (CustomMapCity.Capital capital, CustomMapEventTarget.Capital other) => capital.Race == other.Race,
            _ => false
        };
    }

    // In the game a race that an event ending in a win or lose concerns no longer wins by being the last one standing
    // (PlayerDefeatedEndScenarioRule.HasWinEvent). These playable races are in that state and have no conquest event
    // yet, so conquering everyone would no longer end the game for them.
    public IReadOnlyList<WorldRace> GetRacesNeedingConquestWin()
    {
        if (players.Count < 2)
        {
            return [];
        }

        return
        [
            .. players.Select(player => player.Race).Where(race =>
                events.Any(ev => ev.Effects.Any(effect => effect is CustomMapWinEffect or CustomMapLoseEffect) && GetEventRaces(ev.Concerns).Contains(race)) &&
                !events.Any(ev => IsConquestWin(ev, race)))
        ];
    }

    // "Race wins when it owns every other race's capital": one condition set of "owns <capital>" blocks, then Win.
    public CustomMapEvent AddConquestWin(WorldRace race, string name)
    {
        ImmutableArray<CustomMapCondition> capitals = [.. players.Where(player => player.Race != race).Select(player => (CustomMapCondition)new CustomMapOwnCityCondition(new CustomMapCity.Capital(player.Race)))];

        return AddEvent(CustomMapEvent.Create(name, race) with { Groups = [capitals], Effects = [new CustomMapWinEffect(race)] });
    }

    public bool IsConquestWin(CustomMapEvent ev, WorldRace race)
    {
        HashSet<WorldRace> others = [.. players.Select(player => player.Race).Where(other => other != race)];

        return ev.Races.SequenceEqual([race]) &&
               ev.ConcernsRaces is null &&
               ev.Groups.Length == 1 &&
               ev.Groups[0].Length == others.Count &&
               ev.Groups[0].All(condition => condition is CustomMapOwnCityCondition { City: CustomMapCity.Capital capital } && others.Contains(capital.Race)) &&
               ev.Effects.SequenceEqual([new CustomMapWinEffect(race)]);
    }

    // The races of a race set that are on the map (Neutral always is); races removed from the map drop out.
    public ImmutableArray<WorldRace> GetEventRaces(ImmutableArray<WorldRace> races)
    {
        return [.. races.Distinct().Where(IsEventRaceOnMap)];
    }

    private bool IsEventRaceOnMap(WorldRace race)
    {
        return race == WorldRace.Neutral || players.Any(player => player.Race == race);
    }

    private bool ConditionTargetExists(CustomMapCondition condition)
    {
        return condition switch
        {
            CustomMapTurnCondition turn => turn.Turn > 0,
            CustomMapEnterZoneCondition zone => GetLocation(zone.LocationId) is not null,
            CustomMapOwnCityCondition own => CityExists(own.City),
            CustomMapEnterCityCondition enter => CityExists(enter.City),
            CustomMapLootRuinCondition loot => ruins.Any(ruin => ruin.Id == loot.RuinId),
            CustomMapDestroyArmyCondition destroy => ArmyRefExists(destroy.Army, triggererAllowed: false),
            CustomMapArmyExistsCondition exists => ArmyRefExists(exists.Army, triggererAllowed: false),
            _ => KindConditionTargetExists(condition)
        };
    }

    private bool EffectTargetExists(CustomMapEffect effect)
    {
        return effect switch
        {
            CustomMapMessageEffect message => !string.IsNullOrWhiteSpace(message.Text.Default),
            CustomMapWinEffect win => players.Any(player => player.Race == win.Winner),
            CustomMapLoseEffect => true,
            CustomMapSwitchEventEffect switchEvent => GetEvent(switchEvent.EventId) is not null,
            CustomMapRevealZoneEffect reveal => GetLocation(reveal.LocationId) is not null,
            CustomMapGiveItemEffect give => !string.IsNullOrEmpty(give.Item.Value),
            CustomMapObjectiveEffect objective => !string.IsNullOrWhiteSpace(objective.Text.Default),
            CustomMapCreateArmyEffect create => GetTemplate(create.TemplateId) is { } template && IsTemplateSaveable(template) && GetLocation(create.LocationId) is not null,
            CustomMapRemoveArmyEffect remove => ArmyRefExists(remove.Army, triggererAllowed: false),
            CustomMapMoveArmyEffect move => ArmyRefExists(move.Army, triggererAllowed: true) && GetLocation(move.LocationId) is not null,
            CustomMapMoveBesideEffect beside => ArmyRefExists(beside.Army, triggererAllowed: false),
            CustomMapChangeOwnerEffect owner => ArmyRefExists(owner.Army, triggererAllowed: false) && (owner.NewOwner is not { } race || race == WorldRace.Neutral || players.Any(player => player.Race == race)),
            CustomMapChangeOrderEffect order => ArmyRefExists(order.Army, triggererAllowed: false) && OrderTargetExists(order.Order),
            CustomMapBattleEffect battle => ArmyRefExists(battle.Army, triggererAllowed: false),
            _ => KindEffectTargetExists(effect)
        };
    }

    private bool CityExists(CustomMapCity city)
    {
        return city switch
        {
            CustomMapCity.Village village => villages.Any(candidate => candidate.Id == village.Id),
            CustomMapCity.Capital capital => players.Any(player => player.Race == capital.Race),
            _ => false
        };
    }
}

// One event. Races = "triggered by" (whose turn or army checks it); ConcernsRaces = the races the conditions talk
// about ("an army of X enters", "X owns the city"), null = the same as Races. Once: switched off after it fires.
// Chance 1-100 is rolled at every check. Order: events are checked by Order, then by ID.
public sealed record CustomMapEvent(
    int Id,
    string Name,
    ImmutableArray<WorldRace> Races,
    ImmutableArray<WorldRace>? ConcernsRaces,
    bool Once,
    bool Enabled,
    int Chance,
    int Order,
    ImmutableArray<ImmutableArray<CustomMapCondition>> Groups,
    ImmutableArray<CustomMapEffect> Effects)
{
    public static CustomMapEvent Create(string name, WorldRace race)
    {
        return new CustomMapEvent(0, name, [race], null, Once: true, Enabled: true, Chance: CustomMap.MaxEventChance, Order: 0, [[]], []);
    }

    public ImmutableArray<WorldRace> Concerns => ConcernsRaces ?? Races;

    public bool Equals(CustomMapEvent? other)
    {
        return other is not null &&
               Id == other.Id &&
               Name == other.Name &&
               Races.SequenceEqual(other.Races) &&
               ConcernsRaces.HasValue == other.ConcernsRaces.HasValue &&
               (!ConcernsRaces.HasValue || ConcernsRaces.Value.SequenceEqual(other.ConcernsRaces!.Value)) &&
               Once == other.Once &&
               Enabled == other.Enabled &&
               Chance == other.Chance &&
               Order == other.Order &&
               Groups.Length == other.Groups.Length &&
               Groups.Zip(other.Groups).All(pair => pair.First.SequenceEqual(pair.Second)) &&
               Effects.SequenceEqual(other.Effects);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Id);
        hash.Add(Name);
        hash.AddSequence(Races);
        hash.Add(Once);
        hash.Add(Enabled);
        hash.Add(Chance);
        hash.Add(Order);
        hash.Add(Groups.Length);
        hash.AddSequence(Effects);

        return hash.ToHashCode();
    }
}

public enum CustomMapEventProblemKind
{
    NoRace,
    NoCondition,
    MissingTarget,
    SecondObjective
}

// Block = the condition or effect with the problem (null for the event itself).
public sealed record CustomMapEventProblem(CustomMapEventProblemKind Kind, object? Block);

// A text the player reads: the default (English) text plus optional translations keyed by the game's language code
// (CustomMapText.Languages). A language without a translation shows the default.
public sealed record CustomMapText(string Default, ImmutableSortedDictionary<string, string> Translations)
{
    public static readonly ImmutableArray<string> Languages = ["cs", "de", "es", "fr", "it", "pl", "ru"];

    public static CustomMapText Of(string text)
    {
        return new CustomMapText(text, ImmutableSortedDictionary<string, string>.Empty);
    }

    public string Get(string language)
    {
        return Translations.TryGetValue(language, out string? text) && !string.IsNullOrWhiteSpace(text) ? text : Default;
    }

    public CustomMapText WithTranslation(string language, string? text)
    {
        if (!Languages.Contains(language))
        {
            throw new ArgumentOutOfRangeException(nameof(language), language, "Not one of the game's languages.");
        }

        return this with { Translations = text is null ? Translations.Remove(language) : Translations.SetItem(language, text) };
    }

    public bool Equals(CustomMapText? other)
    {
        return other is not null && Default == other.Default && Translations.SequenceEqual(other.Translations);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Default);
        hash.AddSequence(Translations);

        return hash.ToHashCode();
    }
}

// Something an event block can point at.
public abstract record CustomMapEventTarget
{
    public sealed record Location(int Id) : CustomMapEventTarget;

    public sealed record Village(int Id) : CustomMapEventTarget;

    public sealed record Capital(WorldRace Race) : CustomMapEventTarget;

    public sealed record Ruin(int Id) : CustomMapEventTarget;

    public sealed record Army(int Id) : CustomMapEventTarget;

    public sealed record Event(int Id) : CustomMapEventTarget;

    public sealed record Template(int Id) : CustomMapEventTarget;

    public sealed record Site(int Id) : CustomMapEventTarget;

    public sealed record Landmark(int Id) : CustomMapEventTarget;

    public sealed record Variable(int Id) : CustomMapEventTarget;
}

// A city a block points at: a village by its editor ID or a race's capital.
public abstract record CustomMapCity
{
    public sealed record Village(int Id) : CustomMapCity;

    public sealed record Capital(WorldRace Race) : CustomMapCity;
}

public abstract record CustomMapCondition;

// Turn N (Repeat = false: once, on turn N; saved as "every N turns" in a copy that occurs once) or every N turns.
public sealed record CustomMapTurnCondition(int Turn, bool Repeat) : CustomMapCondition;

// An army of a concerned race enters the zone (armies inside villages and uncaptured capitals don't count).
public sealed record CustomMapEnterZoneCondition(int LocationId) : CustomMapCondition;

// A concerned race owns the city (a capital counts once a concerned race has captured it).
public sealed record CustomMapOwnCityCondition(CustomMapCity City) : CustomMapCondition;

// The city belongs to a concerned race and an army stands in it.
public sealed record CustomMapEnterCityCondition(CustomMapCity City) : CustomMapCondition;

public sealed record CustomMapLootRuinCondition(int RuinId) : CustomMapCondition;

// A concerned race destroyed the army (or any army made from the template).
public sealed record CustomMapDestroyArmyCondition(CustomMapArmyRef Army) : CustomMapCondition
{
    public CustomMapDestroyArmyCondition(int armyId)
        : this(new CustomMapArmyRef.Army(armyId))
    {
    }
}

// The army (or one made from the template) exists, or none does.
public sealed record CustomMapArmyExistsCondition(CustomMapArmyRef Army, bool Exists) : CustomMapCondition
{
    public CustomMapArmyExistsCondition(int armyId, bool exists)
        : this(new CustomMapArmyRef.Army(armyId), exists)
    {
    }
}

public abstract record CustomMapEffect;

public enum CustomMapMessageAudience
{
    // The player whose turn or army fired the event.
    Triggerer,
    AllPlayers,
    ConcernedRaces
}

// The game's story popup. Portrait = a unit type ID (the original maps use unit portraits), null = none.
public sealed record CustomMapMessageEffect(CustomMapText Text, CustomMapMessageAudience Audience, string? Portrait, bool PortraitLeft) : CustomMapEffect;

// Saved as the event's last effect (the game only treats an event as a win event when Win is last).
public sealed record CustomMapWinEffect(WorldRace Winner) : CustomMapEffect;

// The scenario ends with no winner: the human players lose.
public sealed record CustomMapLoseEffect : CustomMapEffect;

public sealed record CustomMapSwitchEventEffect(int EventId, bool On) : CustomMapEffect;

// Reveals the zone for the given races (empty = the event's races). The game keeps one audience per event, so all
// reveal blocks of an event are saved with the union of their races.
public sealed record CustomMapRevealZoneEffect(int LocationId, ImmutableArray<WorldRace> Races) : CustomMapEffect
{
    public bool Equals(CustomMapRevealZoneEffect? other)
    {
        return other is not null && LocationId == other.LocationId && Races.SequenceEqual(other.Races);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(LocationId);
        hash.AddSequence(Races);

        return hash.ToHashCode();
    }
}

// Gives the item to the army that fired the event, or to its owner's capital.
public sealed record CustomMapGiveItemEffect(ItemDefinitionId Item, bool ToCapital) : CustomMapEffect;

// Replaces the scenario objective text (one per event: the game keys the text by event).
public sealed record CustomMapObjectiveEffect(CustomMapText Text) : CustomMapEffect;

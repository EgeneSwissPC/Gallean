using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gallean.Engine;

// The editor's own copy of the events (EditorEvents.json in the map folder, next to the scenario tables). The game
// never reads it: it plays the SEvent/SEvCond/SEvEffec rows the writer builds from it. The editor reads it back so an
// event opens exactly as it was built: "or" groups, "turn N" blocks, translations, and broken events, which the
// scenario tables leave out. Written with JsonNode by hand, so it stays AOT-safe without a serializer context.
public static class CustomMapEventsFile
{
    public const string FileName = "EditorEvents.json";

    private const int FormatVersion = 1;

    public static void Write(string path, IReadOnlyList<CustomMapEvent> events, IReadOnlyList<CustomMapArmyTemplate>? templates = null, IReadOnlyList<CustomMapVariable>? variables = null)
    {
        JsonArray array = [];

        foreach (CustomMapEvent ev in events.OrderBy(ev => ev.Id))
        {
            JsonObject node = new()
            {
                ["id"] = ev.Id,
                ["name"] = ev.Name,
                ["races"] = Races(ev.Races),
                ["once"] = ev.Once,
                ["enabled"] = ev.Enabled,
                ["chance"] = ev.Chance,
                ["order"] = ev.Order,
                ["groups"] = new JsonArray([.. ev.Groups.Select(group => (JsonNode)new JsonArray([.. group.Select(WriteCondition)]))]),
                ["effects"] = new JsonArray([.. ev.Effects.Select(WriteEffect)])
            };

            if (ev.ConcernsRaces is { } concerns)
            {
                node["concerns"] = Races(concerns);
            }

            array.Add((JsonNode)node);
        }

        JsonArray templateArray = [];

        foreach (CustomMapArmyTemplate template in (templates ?? []).OrderBy(template => template.Id))
        {
            templateArray.Add((JsonNode)new JsonObject
            {
                ["id"] = template.Id,
                ["name"] = template.Name,
                ["owner"] = template.Owner.ToString(),
                ["slots"] = new JsonArray([.. template.Slots.Select(unit => unit is null ? null : (JsonNode)new JsonObject
                {
                    ["type"] = unit.TypeId.Value,
                    ["level"] = unit.Level,
                    ["hitpoints"] = unit.Hitpoints,
                    ["large"] = unit.IsLarge,
                    ["leadership"] = unit.Leadership,
                    ["movement"] = unit.MovementPoints,
                    ["guardian"] = unit.IsGuardian,
                    ["waterOnly"] = unit.IsWaterOnly
                })]),
                ["order"] = WriteOrder(template.Order)
            });
        }

        JsonArray variableArray = [.. (variables ?? []).OrderBy(variable => variable.Id).Select(variable => (JsonNode)new JsonObject { ["id"] = variable.Id, ["name"] = variable.Name, ["initial"] = variable.Initial })];
        JsonObject root = new() { ["formatVersion"] = FormatVersion, ["events"] = array, ["templates"] = templateArray, ["variables"] = variableArray };
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static IReadOnlyList<CustomMapArmyTemplate> ReadTemplates(string path)
    {
        if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not { } root)
        {
            return [];
        }

        List<CustomMapArmyTemplate> result = [];

        foreach (JsonNode? node in root["templates"]?.AsArray() ?? [])
        {
            if (node is null)
            {
                continue;
            }

            result.Add(new CustomMapArmyTemplate(
                Int(node, "id"),
                Str(node, "name"),
                Enum.Parse<WorldRace>(Str(node, "owner")),
                [.. Array(node, "slots").Select(unit => unit is null ? null : new CustomMapUnit(
                    new UnitDefinitionId(Str(unit, "type")),
                    Int(unit, "level"),
                    Int(unit, "hitpoints"),
                    Bool(unit, "large"),
                    Int(unit, "leadership"),
                    Int(unit, "movement"),
                    Bool(unit, "guardian"),
                    unit["waterOnly"]?.GetValue<bool>() ?? false))],
                ReadOrder(node["order"])));
        }

        return result;
    }

    public static IReadOnlyList<CustomMapEvent> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        JsonNode root = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidDataException($"'{path}' is empty.");
        List<CustomMapEvent> result = [];

        foreach (JsonNode? node in root["events"]?.AsArray() ?? [])
        {
            if (node is null)
            {
                continue;
            }

            result.Add(new CustomMapEvent(
                Int(node, "id"),
                Str(node, "name"),
                ReadRaces(node["races"]),
                node["concerns"] is { } concerns ? ReadRaces(concerns) : null,
                Bool(node, "once"),
                Bool(node, "enabled"),
                Int(node, "chance"),
                Int(node, "order"),
                [.. Array(node, "groups").Select(group => group!.AsArray().Select(ReadCondition).ToImmutableArray())],
                [.. Array(node, "effects").Select(ReadEffect)]));
        }

        return result;
    }

    private static JsonNode WriteCondition(CustomMapCondition condition)
    {
        return condition switch
        {
            CustomMapTurnCondition turn => new JsonObject { ["kind"] = "turn", ["turn"] = turn.Turn, ["repeat"] = turn.Repeat },
            CustomMapEnterZoneCondition zone => new JsonObject { ["kind"] = "enterZone", ["location"] = zone.LocationId },
            CustomMapOwnCityCondition own => new JsonObject { ["kind"] = "ownCity", ["city"] = WriteCity(own.City) },
            CustomMapEnterCityCondition enter => new JsonObject { ["kind"] = "enterCity", ["city"] = WriteCity(enter.City) },
            CustomMapLootRuinCondition loot => new JsonObject { ["kind"] = "lootRuin", ["ruin"] = loot.RuinId },
            CustomMapDestroyArmyCondition destroy => WithArmy(new JsonObject { ["kind"] = "destroyArmy" }, destroy.Army),
            CustomMapArmyExistsCondition exists => WithArmy(new JsonObject { ["kind"] = "armyExists", ["exists"] = exists.Exists }, exists.Army),
            CustomMapOwnItemCondition own => new JsonObject { ["kind"] = "ownItem", ["item"] = own.Item.Value },
            CustomMapLeaderItemCondition leader => new JsonObject { ["kind"] = "leaderItem", ["army"] = WriteArmy(leader.Army), ["item"] = leader.Item.Value },
            CustomMapRelationCondition relation => new JsonObject { ["kind"] = "relation", ["race1"] = relation.Race1.ToString(), ["race2"] = relation.Race2.ToString(), ["level"] = relation.Level.ToString() },
            CustomMapAllianceCondition alliance => new JsonObject { ["kind"] = "alliance", ["race1"] = alliance.Race1.ToString(), ["race2"] = alliance.Race2.ToString() },
            CustomMapLandCondition land => new JsonObject { ["kind"] = "land", ["percent"] = land.Percent },
            CustomMapVisitSiteCondition visit => new JsonObject { ["kind"] = "visitSite", ["site"] = visit.SiteId },
            CustomMapArmyToZoneCondition toZone => new JsonObject { ["kind"] = "armyToZone", ["army"] = WriteArmy(toZone.Army), ["location"] = toZone.LocationId },
            CustomMapArmyToCityCondition toCity => new JsonObject { ["kind"] = "armyToCity", ["army"] = WriteArmy(toCity.Army), ["city"] = WriteCity(toCity.City) },
            CustomMapItemToZoneCondition item => new JsonObject { ["kind"] = "itemToZone", ["item"] = item.Item.Value, ["location"] = item.LocationId },
            CustomMapVariableCondition variable => new JsonObject
            {
                ["kind"] = "variable",
                ["variable"] = variable.VariableId,
                ["min"] = variable.Minimum,
                ["max"] = variable.Maximum,
                ["join"] = variable.Join.ToString(),
                ["variable2"] = variable.SecondVariableId,
                ["min2"] = variable.SecondMinimum,
                ["max2"] = variable.SecondMaximum
            },
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, null)
        };
    }

    private static CustomMapCondition ReadCondition(JsonNode? node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return Str(node, "kind") switch
        {
            "turn" => new CustomMapTurnCondition(Int(node, "turn"), Bool(node, "repeat")),
            "enterZone" => new CustomMapEnterZoneCondition(Int(node, "location")),
            "ownCity" => new CustomMapOwnCityCondition(ReadCity(node["city"])),
            "enterCity" => new CustomMapEnterCityCondition(ReadCity(node["city"])),
            "lootRuin" => new CustomMapLootRuinCondition(Int(node, "ruin")),
            "destroyArmy" => new CustomMapDestroyArmyCondition(ReadArmy(node)),
            "armyExists" => new CustomMapArmyExistsCondition(ReadArmy(node), Bool(node, "exists")),
            "ownItem" => new CustomMapOwnItemCondition(new ItemDefinitionId(Str(node, "item"))),
            "leaderItem" => new CustomMapLeaderItemCondition(ReadArmy(node["army"]), new ItemDefinitionId(Str(node, "item"))),
            "relation" => new CustomMapRelationCondition(Race(node, "race1"), Race(node, "race2"), Enum.Parse<CustomMapRelation>(Str(node, "level"))),
            "alliance" => new CustomMapAllianceCondition(Race(node, "race1"), Race(node, "race2")),
            "land" => new CustomMapLandCondition(Int(node, "percent")),
            "visitSite" => new CustomMapVisitSiteCondition(Int(node, "site")),
            "armyToZone" => new CustomMapArmyToZoneCondition(ReadArmy(node["army"]), Int(node, "location")),
            "armyToCity" => new CustomMapArmyToCityCondition(ReadArmy(node["army"]), ReadCity(node["city"])),
            "itemToZone" => new CustomMapItemToZoneCondition(new ItemDefinitionId(Str(node, "item")), Int(node, "location")),
            "variable" => new CustomMapVariableCondition(
                Int(node, "variable"),
                Int(node, "min"),
                Int(node, "max"),
                Enum.Parse<CustomMapVariableJoin>(Str(node, "join")),
                Int(node, "variable2"),
                Int(node, "min2"),
                Int(node, "max2")),
            string kind => throw new InvalidDataException($"Unknown event condition '{kind}'.")
        };
    }

    private static JsonNode WriteEffect(CustomMapEffect effect)
    {
        return effect switch
        {
            CustomMapMessageEffect message => new JsonObject
            {
                ["kind"] = "message",
                ["text"] = WriteText(message.Text),
                ["audience"] = message.Audience.ToString(),
                ["portrait"] = message.Portrait,
                ["portraitLeft"] = message.PortraitLeft
            },
            CustomMapWinEffect win => new JsonObject { ["kind"] = "win", ["winner"] = win.Winner.ToString() },
            CustomMapLoseEffect => new JsonObject { ["kind"] = "lose" },
            CustomMapSwitchEventEffect switchEvent => new JsonObject { ["kind"] = "switchEvent", ["event"] = switchEvent.EventId, ["on"] = switchEvent.On },
            CustomMapRevealZoneEffect reveal => new JsonObject { ["kind"] = "revealZone", ["location"] = reveal.LocationId, ["races"] = Races(reveal.Races) },
            CustomMapGiveItemEffect give => new JsonObject { ["kind"] = "giveItem", ["item"] = give.Item.Value, ["toCapital"] = give.ToCapital },
            CustomMapObjectiveEffect objective => new JsonObject { ["kind"] = "objective", ["text"] = WriteText(objective.Text) },
            CustomMapCreateArmyEffect create => new JsonObject { ["kind"] = "createArmy", ["template"] = create.TemplateId, ["location"] = create.LocationId },
            CustomMapRemoveArmyEffect remove => new JsonObject { ["kind"] = "removeArmy", ["army"] = WriteArmy(remove.Army) },
            CustomMapMoveArmyEffect move => new JsonObject { ["kind"] = "moveArmy", ["army"] = WriteArmy(move.Army), ["location"] = move.LocationId },
            CustomMapMoveBesideEffect beside => new JsonObject { ["kind"] = "moveBeside", ["army"] = WriteArmy(beside.Army) },
            CustomMapChangeOwnerEffect owner => new JsonObject { ["kind"] = "changeOwner", ["army"] = WriteArmy(owner.Army), ["owner"] = owner.NewOwner?.ToString() },
            CustomMapChangeOrderEffect order => new JsonObject { ["kind"] = "changeOrder", ["army"] = WriteArmy(order.Army), ["order"] = WriteOrder(order.Order) },
            CustomMapBattleEffect battle => new JsonObject { ["kind"] = "battle", ["army"] = WriteArmy(battle.Army) },
            CustomMapCastSpellEffect cast => new JsonObject { ["kind"] = "castSpell", ["spell"] = cast.Spell.Value, ["caster"] = cast.Caster?.ToString() },
            CustomMapCastSpellZoneEffect cast => new JsonObject { ["kind"] = "castSpellZone", ["spell"] = cast.Spell.Value, ["location"] = cast.LocationId, ["caster"] = cast.Caster?.ToString() },
            CustomMapGiveSpellEffect give => new JsonObject { ["kind"] = "giveSpell", ["spell"] = give.Spell.Value },
            CustomMapAllyEffect ally => new JsonObject { ["kind"] = "ally", ["race1"] = ally.Race1.ToString(), ["race2"] = ally.Race2.ToString(), ["permanent"] = ally.Permanent },
            CustomMapRelationEffect relation => new JsonObject { ["kind"] = "setRelation", ["race1"] = relation.Race1.ToString(), ["race2"] = relation.Race2.ToString(), ["level"] = relation.Level.ToString() },
            CustomMapRemoveMountainsEffect mountains => new JsonObject { ["kind"] = "removeMountains", ["location"] = mountains.LocationId },
            CustomMapRemoveLandmarkEffect remove => new JsonObject { ["kind"] = "removeLandmark", ["landmark"] = remove.LandmarkId },
            CustomMapChangeLandmarkEffect change => new JsonObject { ["kind"] = "changeLandmark", ["landmark"] = change.LandmarkId, ["type"] = change.Type.Value },
            CustomMapChangeTerrainEffect terrain => new JsonObject { ["kind"] = "changeTerrain", ["location"] = terrain.LocationId, ["terrain"] = terrain.Terrain.ToString(), ["size"] = terrain.Size },
            CustomMapVariableEffect variable => new JsonObject { ["kind"] = "setVariable", ["variable"] = variable.VariableId, ["operation"] = variable.Operation.ToString(), ["value"] = variable.Value },
            CustomMapDestroyItemEffect destroy => new JsonObject { ["kind"] = "destroyItem", ["item"] = destroy.Item.Value, ["triggererOnly"] = destroy.TriggererOnly },
            _ => throw new ArgumentOutOfRangeException(nameof(effect), effect, null)
        };
    }

    private static CustomMapEffect ReadEffect(JsonNode? node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return Str(node, "kind") switch
        {
            "message" => new CustomMapMessageEffect(
                ReadText(node["text"]),
                Enum.Parse<CustomMapMessageAudience>(Str(node, "audience")),
                node["portrait"]?.GetValue<string>(),
                Bool(node, "portraitLeft")),
            "win" => new CustomMapWinEffect(Enum.Parse<WorldRace>(Str(node, "winner"))),
            "lose" => new CustomMapLoseEffect(),
            "switchEvent" => new CustomMapSwitchEventEffect(Int(node, "event"), Bool(node, "on")),
            "revealZone" => new CustomMapRevealZoneEffect(Int(node, "location"), ReadRaces(node["races"])),
            "giveItem" => new CustomMapGiveItemEffect(new ItemDefinitionId(Str(node, "item")), Bool(node, "toCapital")),
            "objective" => new CustomMapObjectiveEffect(ReadText(node["text"])),
            "createArmy" => new CustomMapCreateArmyEffect(Int(node, "template"), Int(node, "location")),
            "removeArmy" => new CustomMapRemoveArmyEffect(ReadArmy(node["army"])),
            "moveArmy" => new CustomMapMoveArmyEffect(ReadArmy(node["army"]), Int(node, "location")),
            "moveBeside" => new CustomMapMoveBesideEffect(ReadArmy(node["army"])),
            "changeOwner" => new CustomMapChangeOwnerEffect(ReadArmy(node["army"]), node["owner"] is { } owner ? Enum.Parse<WorldRace>(owner.GetValue<string>()) : null),
            "changeOrder" => new CustomMapChangeOrderEffect(ReadArmy(node["army"]), ReadOrder(node["order"])),
            "battle" => new CustomMapBattleEffect(ReadArmy(node["army"])),
            "castSpell" => new CustomMapCastSpellEffect(new WorldSpellDefinitionId(Str(node, "spell")), OptionalRace(node, "caster")),
            "castSpellZone" => new CustomMapCastSpellZoneEffect(new WorldSpellDefinitionId(Str(node, "spell")), Int(node, "location"), OptionalRace(node, "caster")),
            "giveSpell" => new CustomMapGiveSpellEffect(new WorldSpellDefinitionId(Str(node, "spell"))),
            "ally" => new CustomMapAllyEffect(Race(node, "race1"), Race(node, "race2"), Bool(node, "permanent")),
            "setRelation" => new CustomMapRelationEffect(Race(node, "race1"), Race(node, "race2"), Enum.Parse<CustomMapRelation>(Str(node, "level"))),
            "removeMountains" => new CustomMapRemoveMountainsEffect(Int(node, "location")),
            "removeLandmark" => new CustomMapRemoveLandmarkEffect(Int(node, "landmark")),
            "changeLandmark" => new CustomMapChangeLandmarkEffect(Int(node, "landmark"), new WorldLandmarkDefinitionId(Str(node, "type"))),
            "changeTerrain" => new CustomMapChangeTerrainEffect(Int(node, "location"), Enum.Parse<WorldMapTerrain>(Str(node, "terrain")), Int(node, "size")),
            "setVariable" => new CustomMapVariableEffect(Int(node, "variable"), Enum.Parse<ScenarioVariableOperation>(Str(node, "operation")), Int(node, "value")),
            "destroyItem" => new CustomMapDestroyItemEffect(new ItemDefinitionId(Str(node, "item")), Bool(node, "triggererOnly")),
            string kind => throw new InvalidDataException($"Unknown event effect '{kind}'.")
        };
    }

    // Condition nodes keep the army inline ("army": id or "template": id), as phase 1 wrote it.
    private static JsonObject WithArmy(JsonObject node, CustomMapArmyRef army)
    {
        switch (army)
        {
            case CustomMapArmyRef.Army placed:
                node["army"] = placed.Id;
                break;
            case CustomMapArmyRef.Template template:
                node["template"] = template.Id;
                break;
        }

        return node;
    }

    private static WorldRace Race(JsonNode node, string name) => Enum.Parse<WorldRace>(Str(node, name));

    private static WorldRace? OptionalRace(JsonNode node, string name) => node[name] is { } race ? Enum.Parse<WorldRace>(race.GetValue<string>()) : null;

    public static IReadOnlyList<CustomMapVariable> ReadVariables(string path)
    {
        if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not { } root)
        {
            return [];
        }

        return [.. (root["variables"]?.AsArray() ?? []).OfType<JsonNode>().Select(node => new CustomMapVariable(Int(node, "id"), Str(node, "name"), Int(node, "initial")))];
    }

    private static JsonNode WriteArmy(CustomMapArmyRef army)
    {
        return army switch
        {
            CustomMapArmyRef.Army placed => new JsonObject { ["army"] = placed.Id },
            CustomMapArmyRef.Template template => new JsonObject { ["template"] = template.Id },
            _ => new JsonObject { ["triggerer"] = true }
        };
    }

    private static CustomMapArmyRef ReadArmy(JsonNode? node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node["army"] is { } army ? new CustomMapArmyRef.Army(army.GetValue<int>())
            : node["template"] is { } template ? new CustomMapArmyRef.Template(template.GetValue<int>())
            : new CustomMapArmyRef.Triggerer();
    }

    private static JsonNode WriteOrder(CustomMapArmyOrder order)
    {
        JsonObject node = new() { ["kind"] = order.Kind.ToString(), ["location"] = order.LocationId, ["army"] = order.ArmyId };

        if (order.City is { } city)
        {
            node["city"] = WriteCity(city);
        }

        return node;
    }

    private static CustomMapArmyOrder ReadOrder(JsonNode? node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return new CustomMapArmyOrder(
            Enum.Parse<CustomMapOrderKind>(Str(node, "kind")),
            Int(node, "location"),
            node["city"] is { } city ? ReadCity(city) : null,
            Int(node, "army"));
    }

    private static JsonNode WriteCity(CustomMapCity city)
    {
        return city switch
        {
            CustomMapCity.Village village => new JsonObject { ["village"] = village.Id },
            CustomMapCity.Capital capital => new JsonObject { ["capital"] = capital.Race.ToString() },
            _ => throw new ArgumentOutOfRangeException(nameof(city), city, null)
        };
    }

    private static CustomMapCity ReadCity(JsonNode? node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node["village"] is { } village
            ? new CustomMapCity.Village(village.GetValue<int>())
            : new CustomMapCity.Capital(Enum.Parse<WorldRace>(Str(node, "capital")));
    }

    private static JsonNode WriteText(CustomMapText text)
    {
        JsonObject translations = [];

        foreach ((string language, string value) in text.Translations)
        {
            translations[language] = value;
        }

        return new JsonObject { ["default"] = text.Default, ["translations"] = translations };
    }

    private static CustomMapText ReadText(JsonNode? node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ImmutableSortedDictionary<string, string>.Builder translations = ImmutableSortedDictionary.CreateBuilder<string, string>();

        foreach ((string language, JsonNode? value) in node["translations"]?.AsObject() ?? [])
        {
            if (value is not null)
            {
                translations[language] = value.GetValue<string>();
            }
        }

        return new CustomMapText(Str(node, "default"), translations.ToImmutable());
    }

    private static JsonArray Races(ImmutableArray<WorldRace> races)
    {
        return [.. races.Select(race => (JsonNode)JsonValue.Create(race.ToString()))];
    }

    private static ImmutableArray<WorldRace> ReadRaces(JsonNode? node)
    {
        return [.. (node?.AsArray() ?? []).Select(race => Enum.Parse<WorldRace>(race!.GetValue<string>()))];
    }

    private static JsonArray Array(JsonNode node, string name)
    {
        return node[name]?.AsArray() ?? [];
    }

    private static int Int(JsonNode node, string name)
    {
        return node[name]?.GetValue<int>() ?? throw new InvalidDataException($"Missing '{name}'.");
    }

    private static bool Bool(JsonNode node, string name)
    {
        return node[name]?.GetValue<bool>() ?? throw new InvalidDataException($"Missing '{name}'.");
    }

    private static string Str(JsonNode node, string name)
    {
        return node[name]?.GetValue<string>() ?? throw new InvalidDataException($"Missing '{name}'.");
    }
}

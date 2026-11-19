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

    public static void Write(string path, IReadOnlyList<CustomMapEvent> events)
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

        JsonObject root = new() { ["formatVersion"] = FormatVersion, ["events"] = array };
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
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
            CustomMapDestroyArmyCondition destroy => new JsonObject { ["kind"] = "destroyArmy", ["army"] = destroy.ArmyId },
            CustomMapArmyExistsCondition exists => new JsonObject { ["kind"] = "armyExists", ["army"] = exists.ArmyId, ["exists"] = exists.Exists },
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
            "destroyArmy" => new CustomMapDestroyArmyCondition(Int(node, "army")),
            "armyExists" => new CustomMapArmyExistsCondition(Int(node, "army"), Bool(node, "exists")),
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
            string kind => throw new InvalidDataException($"Unknown event effect '{kind}'.")
        };
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

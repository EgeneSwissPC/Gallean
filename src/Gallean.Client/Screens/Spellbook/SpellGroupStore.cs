using System.Collections.Immutable;
using System.Text.Json;
using Gallean.Configuration;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: a named list of spells cast one after another, in this order.
internal sealed record SpellGroup(string Name, ImmutableArray<WorldSpellDefinitionId> Spells);

// Gallean Upgrade: spell groups are a player preference, not game state: they live in one file next to the
// settings (spell-groups.json), one list per race, so they work in every save and quest of that race.
// Read with JsonDocument and written with Utf8JsonWriter only: the client project sets PublishAot, which turns
// reflection-based JSON off even in plain builds (JsonNode's generic Add<T>/Create<T> throws NotSupportedException
// there, and the tests don't see it because the test project keeps reflection on).
internal static class SpellGroupStore
{
    public const int MaxNameLength = 32;

    // Tests point this at a temporary file.
    public static string FilePath { get; set; } = Path.Combine(AppDirectories.Root, "spell-groups.json");

    public static IReadOnlyList<SpellGroup> Load(WorldRace race)
    {
        return ReadAll().TryGetValue(race.ToString(), out List<SpellGroup>? groups) ? groups : [];
    }

    public static void Save(WorldRace race, IReadOnlyList<SpellGroup> groups)
    {
        Dictionary<string, List<SpellGroup>> all = ReadAll();
        all[race.ToString()] = [.. groups];

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        using FileStream stream = File.Create(FilePath);
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        foreach ((string raceName, List<SpellGroup> raceGroups) in all)
        {
            writer.WriteStartArray(raceName);

            foreach (SpellGroup group in raceGroups)
            {
                writer.WriteStartObject();
                writer.WriteString("name", group.Name);
                writer.WriteStartArray("spells");

                foreach (WorldSpellDefinitionId spell in group.Spells)
                {
                    writer.WriteStringValue(spell.Value);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    public static void Add(WorldRace race, SpellGroup group)
    {
        Save(race, [.. Load(race), group]);
    }

    public static void Remove(WorldRace race, int index)
    {
        List<SpellGroup> groups = [.. Load(race)];

        if (index >= 0 && index < groups.Count)
        {
            groups.RemoveAt(index);
            Save(race, groups);
        }
    }

    private static Dictionary<string, List<SpellGroup>> ReadAll()
    {
        Dictionary<string, List<SpellGroup>> all = [];

        try
        {
            if (!File.Exists(FilePath))
            {
                return all;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(FilePath));

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return all;
            }

            foreach (JsonProperty race in document.RootElement.EnumerateObject())
            {
                if (race.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                List<SpellGroup> groups = [];

                foreach (JsonElement group in race.Value.EnumerateArray())
                {
                    if (group.ValueKind == JsonValueKind.Object &&
                        group.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String &&
                        group.TryGetProperty("spells", out JsonElement spells) && spells.ValueKind == JsonValueKind.Array)
                    {
                        groups.Add(new SpellGroup(
                            name.GetString()!,
                            [.. spells.EnumerateArray().Where(spell => spell.ValueKind == JsonValueKind.String).Select(spell => new WorldSpellDefinitionId(spell.GetString()!))]));
                    }
                }

                all[race.Name] = groups;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken file is ignored (and replaced on the next save) rather than breaking the spellbook.
        }

        return all;
    }
}

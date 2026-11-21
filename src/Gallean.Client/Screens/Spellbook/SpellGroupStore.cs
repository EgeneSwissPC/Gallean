using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gallean.Configuration;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: a named list of spells cast one after another, in this order.
internal sealed record SpellGroup(string Name, ImmutableArray<WorldSpellDefinitionId> Spells);

// Gallean Upgrade: spell groups are a player preference, not game state: they live in one file next to the
// settings (spell-groups.json), one list per race, so they work in every save and quest of that race.
// Written with JsonNode (no reflection), so it works in the AOT build.
internal static class SpellGroupStore
{
    public const int MaxNameLength = 32;

    // Tests point this at a temporary file.
    public static string FilePath { get; set; } = Path.Combine(AppDirectories.Root, "spell-groups.json");

    public static IReadOnlyList<SpellGroup> Load(WorldRace race)
    {
        JsonObject root = ReadRoot();

        if (root[race.ToString()] is not JsonArray groups)
        {
            return [];
        }

        List<SpellGroup> result = [];

        foreach (JsonNode? node in groups)
        {
            if (node?["name"]?.GetValue<string>() is { } name && node["spells"] is JsonArray spells)
            {
                result.Add(new SpellGroup(name, [.. spells.Select(spell => new WorldSpellDefinitionId(spell!.GetValue<string>()))]));
            }
        }

        return result;
    }

    public static void Save(WorldRace race, IReadOnlyList<SpellGroup> groups)
    {
        JsonObject root = ReadRoot();
        JsonArray list = [];

        foreach (SpellGroup group in groups)
        {
            JsonArray spells = [];

            foreach (WorldSpellDefinitionId spell in group.Spells)
            {
                spells.Add(spell.Value);
            }

            list.Add(new JsonObject { ["name"] = group.Name, ["spells"] = spells });
        }

        root[race.ToString()] = list;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        using FileStream stream = File.Create(FilePath);
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });
        root.WriteTo(writer);
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

    private static JsonObject ReadRoot()
    {
        try
        {
            if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject root)
            {
                return root;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            // A broken file is ignored (and replaced on the next save) rather than breaking the spellbook.
        }

        return [];
    }
}

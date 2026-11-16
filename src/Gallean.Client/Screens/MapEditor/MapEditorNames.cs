using System.Text.Json;
using Gallean.Entities.GameData;

namespace Gallean.Client;

internal sealed record MapEditorName(string Name, string Greeting);

// The game's lists of names for new map objects (RiseOfTheElves/ScenData): cities, ruins, and the sites with the
// greeting their owners say.
internal sealed record MapEditorNames(
    IReadOnlyList<MapEditorName> Villages,
    IReadOnlyList<MapEditorName> Ruins,
    IReadOnlyList<MapEditorName> Merchants,
    IReadOnlyList<MapEditorName> MageTowers,
    IReadOnlyList<MapEditorName> MercenaryCamps,
    IReadOnlyList<MapEditorName> Trainers)
{
    public static MapEditorNames Empty { get; } = new([], [], [], [], [], []);

    public static MapEditorNames Load()
    {
        return Load(Path.Combine(RiseOfTheElves.RootPath, "ScenData"));
    }

    public static MapEditorNames Load(string directory)
    {
        return new MapEditorNames(
            LoadFile(Path.Combine(directory, "Cityname.json")),
            LoadFile(Path.Combine(directory, "Ruinname.json")),
            LoadFile(Path.Combine(directory, "Mercname.json")),
            LoadFile(Path.Combine(directory, "Magename.json")),
            LoadFile(Path.Combine(directory, "Campname.json")),
            LoadFile(Path.Combine(directory, "Trainame.json")));
    }

    // Reads NAME/DESC rows with JsonDocument (AOT-safe; these tables have no Db record).
    private static IReadOnlyList<MapEditorName> LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);
            List<MapEditorName> names = [];

            foreach (JsonElement row in document.RootElement.GetProperty("rows").EnumerateArray())
            {
                if (GetText(row, "NAME") is { Length: > 0 } name && !names.Any(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    names.Add(new MapEditorName(name, GetText(row, "DESC") ?? string.Empty));
                }
            }

            return names;
        }
        catch (Exception ex) when (ex is JsonException or IOException or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }

    private static string? GetText(JsonElement row, string property)
    {
        return row.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
    }
}

using System.Text.Json;
using Gallean.Entities.GameData;

namespace Gallean.Client;

// The game's list of city names (ScenData/Cityname.json), which the map editor uses for new villages.
internal static class MapEditorVillageNames
{
    public static IReadOnlyList<string> Load()
    {
        return Load(Path.Combine(RiseOfTheElves.RootPath, "ScenData", "Cityname.json"));
    }

    public static IReadOnlyList<string> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);
            List<string> names = [];

            foreach (JsonElement row in document.RootElement.GetProperty("rows").EnumerateArray())
            {
                if (row.TryGetProperty("NAME", out JsonElement name) && name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } text)
                {
                    names.Add(text.Trim());
                }
            }

            return [.. names.Distinct(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is JsonException or IOException or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }
}

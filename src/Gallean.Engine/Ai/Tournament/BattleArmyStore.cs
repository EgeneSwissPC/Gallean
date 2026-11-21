using System.Collections.Immutable;
using System.Text.Json;

namespace Gallean.Engine;

// Gallean Upgrade (docs/battle-ai-designer.md): army presets as JSON files, one per army (<name>.json).
// Written with Utf8JsonWriter and read with JsonDocument, because the client is native AOT (no
// reflection-based JSON). Unknown properties are ignored, so files from newer builds still load.
public sealed class BattleArmyStore(string directory)
{
    public const int FormatVersion = 1;
    private const string Extension = ".json";

    public string Directory { get; } = directory;

    public IReadOnlyList<BattleArmy> LoadAll()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        List<BattleArmy> result = [];

        foreach (string path in System.IO.Directory.EnumerateFiles(Directory, "*" + Extension).Order(StringComparer.Ordinal))
        {
            BattleArmy? army = TryLoadFile(path);

            if (army is not null)
            {
                result.Add(army);
            }
        }

        return result;
    }

    public void Save(BattleArmy army)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string path = Path.Combine(Directory, GetFileName(army.Name) + Extension);
        string temporary = path + ".tmp";

        File.WriteAllBytes(temporary, Write(army));
        File.Move(temporary, path, overwrite: true);
    }

    public void Delete(string name)
    {
        string path = Path.Combine(Directory, GetFileName(name) + Extension);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    // A file name from the army name: characters a file name can't hold become '_'.
    public static string GetFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string result = new([.. name.Select(character => invalid.Contains(character) ? '_' : character)]);

        return string.IsNullOrWhiteSpace(result) ? "army" : result.Trim();
    }

    public static BattleArmy? TryLoadFile(string path)
    {
        try
        {
            return Read(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    public static byte[] Write(BattleArmy army)
    {
        using MemoryStream stream = new();

        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", FormatVersion);
            writer.WriteString("name", army.Name);

            if (army.AllowAnyArmy)
            {
                writer.WriteBoolean("allowAnyArmy", true);
            }

            writer.WriteStartArray("units");

            foreach (BattleArmyUnit unit in army.Units)
            {
                writer.WriteStartObject();
                writer.WriteString("unit", unit.TypeId.Value);
                writer.WriteNumber("level", unit.Level);
                writer.WriteNumber("line", unit.Position.Line);
                writer.WriteNumber("flank", unit.Position.Flank);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            if (!army.Equipment.IsEmpty)
            {
                writer.WriteStartArray("equipment");

                foreach (BattleArmyItem item in army.Equipment)
                {
                    writer.WriteStartObject();
                    writer.WriteString("slot", item.Slot.ToString());
                    writer.WriteString("item", item.ItemId.Value);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            if (!army.LeaderUpgrades.IsEmpty)
            {
                writer.WriteStartArray("leaderUpgrades");

                foreach (UnitModifierDefinitionId upgrade in army.LeaderUpgrades)
                {
                    writer.WriteStringValue(upgrade.Value);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static BattleArmy Read(byte[] json, string fallbackName)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string name = root.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString()!
            : fallbackName;
        bool allowAny = root.TryGetProperty("allowAnyArmy", out JsonElement allowElement) && allowElement.ValueKind == JsonValueKind.True;
        ImmutableArray<BattleArmyUnit>.Builder units = ImmutableArray.CreateBuilder<BattleArmyUnit>();

        if (root.TryGetProperty("units", out JsonElement unitsElement) && unitsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in unitsElement.EnumerateArray())
            {
                units.Add(new BattleArmyUnit(
                    new UnitDefinitionId(element.GetProperty("unit").GetString() ?? throw new FormatException("unit")),
                    element.TryGetProperty("level", out JsonElement level) ? level.GetInt32() : 1,
                    new GridPosition(element.GetProperty("line").GetInt32(), element.GetProperty("flank").GetInt32())));
            }
        }

        ImmutableArray<BattleArmyItem>.Builder equipment = ImmutableArray.CreateBuilder<BattleArmyItem>();

        if (root.TryGetProperty("equipment", out JsonElement equipmentElement) && equipmentElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in equipmentElement.EnumerateArray())
            {
                string slotName = element.GetProperty("slot").GetString() ?? "";

                // A slot this build doesn't know is skipped (files from newer builds still load).
                if (Enum.TryParse(slotName, ignoreCase: true, out LeaderEquipmentSlot slot) && Enum.IsDefined(slot))
                {
                    equipment.Add(new BattleArmyItem(slot, new ItemDefinitionId(element.GetProperty("item").GetString() ?? throw new FormatException("item"))));
                }
            }
        }

        ImmutableArray<UnitModifierDefinitionId>.Builder upgrades = ImmutableArray.CreateBuilder<UnitModifierDefinitionId>();

        if (root.TryGetProperty("leaderUpgrades", out JsonElement upgradesElement) && upgradesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in upgradesElement.EnumerateArray())
            {
                upgrades.Add(new UnitModifierDefinitionId(element.GetString() ?? throw new FormatException("leaderUpgrades")));
            }
        }

        return new BattleArmy(name, units.ToImmutable())
        {
            AllowAnyArmy = allowAny,
            Equipment = equipment.ToImmutable(),
            LeaderUpgrades = upgrades.ToImmutable()
        };
    }
}

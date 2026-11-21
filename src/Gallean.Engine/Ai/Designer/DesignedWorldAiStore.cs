using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace Gallean.Engine;

// Gallean Upgrade (AI designer): designed map AIs as JSON files, one per AI (<name>.json; the ID is
// "custom:" + the file name). Written with Utf8JsonWriter and read with JsonDocument, because the client
// is native AOT (no reflection-based JSON). Unknown condition or action kinds are skipped, so files from
// newer builds still load.
public sealed class DesignedWorldAiStore(string directory)
{
    public const int FormatVersion = 1;
    private const string Extension = ".json";

    public string Directory { get; } = directory;

    public IReadOnlyList<DesignedWorldAi> LoadAll()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        List<DesignedWorldAi> result = [];

        foreach (string path in System.IO.Directory.EnumerateFiles(Directory, "*" + Extension).Order(StringComparer.Ordinal))
        {
            DesignedWorldAi? design = TryLoad(path);

            if (design is not null)
            {
                result.Add(design);
            }
        }

        return result;
    }

    public void Save(DesignedWorldAi design)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string path = Path.Combine(Directory, GetFileName(design.Id) + Extension);
        string temporary = path + ".tmp";

        File.WriteAllBytes(temporary, Write(design));
        File.Move(temporary, path, overwrite: true);
    }

    public void Delete(string id)
    {
        string path = Path.Combine(Directory, GetFileName(id) + Extension);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static string GetFileName(string id)
    {
        return id.StartsWith(DesignedWorldAi.IdPrefix, StringComparison.Ordinal) ? id[DesignedWorldAi.IdPrefix.Length..] : id;
    }

    public static DesignedWorldAi? TryLoad(string path)
    {
        try
        {
            return Read(Path.GetFileNameWithoutExtension(path), File.ReadAllBytes(path));
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            return null;
        }
    }

    public static byte[] Write(DesignedWorldAi design)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", FormatVersion);
            writer.WriteString("name", design.Name);
            writer.WriteStartArray("stages");

            foreach (DesignedWorldStage stage in design.Stages)
            {
                writer.WriteStartObject();
                writer.WriteString("name", stage.Name);

                writer.WriteStartArray("when");
                foreach (ImmutableArray<DesignedStageCondition> group in stage.When.IsDefault ? [] : stage.When)
                {
                    writer.WriteStartArray();
                    foreach (DesignedStageCondition condition in group)
                    {
                        WriteCondition(writer, condition);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndArray();

                WriteTiers(writer, "economy", stage.Economy);
                WriteTiers(writer, "army", stage.Army);

                writer.WriteStartObject("rules");
                WriteOptional(writer, "hurtPercent", stage.Rules.HurtPercent);
                WriteOptional(writer, "healedPercent", stage.Rules.HealedPercent);
                WriteOptional(writer, "reinforcePercent", stage.Rules.ReinforcePercent);
                WriteOptional(writer, "attackPowerPercent", stage.Rules.AttackPowerPercent);
                WriteOptional(writer, "leaderSavingsGold", stage.Rules.LeaderSavingsGold);
                if (stage.Rules.WaitForFreeCapital is { } wait)
                {
                    writer.WriteBoolean("waitForFreeCapital", wait);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static DesignedWorldAi Read(string fileName, byte[] json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string name = root.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? fileName : fileName;
        ImmutableArray<DesignedWorldStage>.Builder stages = ImmutableArray.CreateBuilder<DesignedWorldStage>();

        if (root.TryGetProperty("stages", out JsonElement stagesElement))
        {
            foreach (JsonElement stageElement in stagesElement.EnumerateArray())
            {
                stages.Add(ReadStage(stageElement));
            }
        }

        return new DesignedWorldAi(DesignedWorldAi.IdPrefix + fileName, name, stages.ToImmutable());
    }

    private static DesignedWorldStage ReadStage(JsonElement element)
    {
        string name = element.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? "" : "";
        ImmutableArray<ImmutableArray<DesignedStageCondition>>.Builder when = ImmutableArray.CreateBuilder<ImmutableArray<DesignedStageCondition>>();

        if (element.TryGetProperty("when", out JsonElement whenElement))
        {
            foreach (JsonElement groupElement in whenElement.EnumerateArray())
            {
                ImmutableArray<DesignedStageCondition>.Builder group = ImmutableArray.CreateBuilder<DesignedStageCondition>();

                foreach (JsonElement conditionElement in groupElement.EnumerateArray())
                {
                    if (ReadCondition(conditionElement) is { } condition)
                    {
                        group.Add(condition);
                    }
                }

                if (group.Count > 0)
                {
                    when.Add(group.ToImmutable());
                }
            }
        }

        DesignedWorldRules rules = DesignedWorldRules.Classic;

        if (element.TryGetProperty("rules", out JsonElement rulesElement))
        {
            rules = new DesignedWorldRules(
                ReadOptional(rulesElement, "hurtPercent"),
                ReadOptional(rulesElement, "healedPercent"),
                ReadOptional(rulesElement, "reinforcePercent"),
                ReadOptional(rulesElement, "attackPowerPercent"),
                ReadOptional(rulesElement, "leaderSavingsGold"),
                rulesElement.TryGetProperty("waitForFreeCapital", out JsonElement wait) ? wait.GetBoolean() : null);
        }

        return new DesignedWorldStage(name, when.ToImmutable(), ReadTiers(element, "economy"), ReadTiers(element, "army"), rules);
    }

    private static void WriteCondition(Utf8JsonWriter writer, DesignedStageCondition condition)
    {
        writer.WriteStartObject();

        switch (condition)
        {
            case DesignedStageCondition.FromTurn fromTurn:
                writer.WriteString("kind", "fromTurn");
                writer.WriteNumber("turn", fromTurn.Turn);
                break;
            case DesignedStageCondition.GoldAbove goldAbove:
                writer.WriteString("kind", "goldAbove");
                writer.WriteNumber("gold", goldAbove.Gold);
                break;
            case DesignedStageCondition.CitiesBelow citiesBelow:
                writer.WriteString("kind", "citiesBelow");
                writer.WriteNumber("count", citiesBelow.Count);
                break;
            case DesignedStageCondition.EnemyNearCapital near:
                writer.WriteString("kind", "enemyNearCapital");
                writer.WriteNumber("tiles", near.Tiles);
                break;
            case DesignedStageCondition.LostCity:
                writer.WriteString("kind", "lostCity");
                break;
        }

        writer.WriteEndObject();
    }

    private static DesignedStageCondition? ReadCondition(JsonElement element)
    {
        int Number(string property) => element.GetProperty(property).GetInt32();

        return element.TryGetProperty("kind", out JsonElement kind) ? kind.GetString() switch
        {
            "fromTurn" => new DesignedStageCondition.FromTurn(Number("turn")),
            "goldAbove" => new DesignedStageCondition.GoldAbove(Number("gold")),
            "citiesBelow" => new DesignedStageCondition.CitiesBelow(Number("count")),
            "enemyNearCapital" => new DesignedStageCondition.EnemyNearCapital(Number("tiles")),
            "lostCity" => new DesignedStageCondition.LostCity(),
            _ => null
        } : null;
    }

    private static void WriteTiers(Utf8JsonWriter writer, string property, ImmutableArray<ImmutableArray<WorldAiActionKind>> tiers)
    {
        writer.WriteStartArray(property);

        foreach (ImmutableArray<WorldAiActionKind> tier in tiers.IsDefault ? [] : tiers)
        {
            writer.WriteStartArray();
            foreach (WorldAiActionKind kind in tier)
            {
                writer.WriteStringValue(kind.ToString());
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }

    private static ImmutableArray<ImmutableArray<WorldAiActionKind>> ReadTiers(JsonElement element, string property)
    {
        ImmutableArray<ImmutableArray<WorldAiActionKind>>.Builder tiers = ImmutableArray.CreateBuilder<ImmutableArray<WorldAiActionKind>>();

        if (element.TryGetProperty(property, out JsonElement tiersElement))
        {
            foreach (JsonElement tierElement in tiersElement.EnumerateArray())
            {
                ImmutableArray<WorldAiActionKind>.Builder tier = ImmutableArray.CreateBuilder<WorldAiActionKind>();

                foreach (JsonElement kindElement in tierElement.EnumerateArray())
                {
                    if (Enum.TryParse(kindElement.GetString(), ignoreCase: false, out WorldAiActionKind kind) && Enum.IsDefined(kind))
                    {
                        tier.Add(kind);
                    }
                }

                if (tier.Count > 0)
                {
                    tiers.Add(tier.ToImmutable());
                }
            }
        }

        return tiers.ToImmutable();
    }

    private static void WriteOptional(Utf8JsonWriter writer, string property, int? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(property, number);
        }
    }

    private static int? ReadOptional(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) ? value.GetInt32() : null;
    }
}

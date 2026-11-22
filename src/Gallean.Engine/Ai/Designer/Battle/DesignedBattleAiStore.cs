using System.Collections.Immutable;
using System.Text.Json;

namespace Gallean.Engine;

// Gallean Upgrade (battle AI designer): designed battle AIs as JSON files, one per AI (<name>.json; the ID is
// "custom-battle:" + the file name). Utf8JsonWriter / JsonDocument only (the client is native AOT). Unknown
// condition kinds are skipped and missing tactics keep their Classic value, so files from other builds load.
public sealed class DesignedBattleAiStore(string directory)
{
    public const int FormatVersion = 1;
    private const string Extension = ".json";

    public string Directory { get; } = directory;

    public IReadOnlyList<DesignedBattleAi> LoadAll()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        List<DesignedBattleAi> result = [];

        foreach (string path in System.IO.Directory.EnumerateFiles(Directory, "*" + Extension).Order(StringComparer.Ordinal))
        {
            if (TryLoad(path) is { } design)
            {
                result.Add(design);
            }
        }

        return result;
    }

    public void Save(DesignedBattleAi design)
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
        return id.StartsWith(DesignedBattleAi.IdPrefix, StringComparison.Ordinal) ? id[DesignedBattleAi.IdPrefix.Length..] : id;
    }

    public static DesignedBattleAi? TryLoad(string path)
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

    public static byte[] Write(DesignedBattleAi design)
    {
        using MemoryStream stream = new();

        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", FormatVersion);
            writer.WriteString("name", design.Name);
            writer.WriteStartArray("stages");

            foreach (DesignedBattleStage stage in design.Stages)
            {
                writer.WriteStartObject();
                writer.WriteString("name", stage.Name);
                writer.WriteStartArray("when");

                foreach (ImmutableArray<DesignedBattleCondition> group in stage.When.IsDefault ? [] : stage.When)
                {
                    writer.WriteStartArray();

                    foreach (DesignedBattleCondition condition in group)
                    {
                        WriteCondition(writer, condition);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndArray();

                DesignedBattleTactics tactics = stage.Tactics;
                writer.WriteStartObject("tactics");
                writer.WriteNumber("healersFirst", tactics.HealersFirst);
                writer.WriteNumber("castersFirst", tactics.CastersFirst);
                writer.WriteNumber("leaderFirst", tactics.LeaderFirst);
                writer.WriteNumber("hardestHitterFirst", tactics.HardestHitterFirst);
                writer.WriteNumber("woundedFirst", tactics.WoundedFirst);
                writer.WriteNumber("focusFire", tactics.FocusFire);
                writer.WriteBoolean("protectLeader", tactics.ProtectLeader);
                writer.WriteBoolean("neverFlee", tactics.NeverFlee);
                writer.WriteNumber("fleeBelowPercent", tactics.FleeBelowPercent);
                writer.WriteBoolean("canDefend", tactics.CanDefend);
                writer.WriteNumber("leaderDefendsBelowPercent", tactics.LeaderDefendsBelowPercent);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static DesignedBattleAi Read(string fileName, byte[] json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string name = root.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? fileName : fileName;
        ImmutableArray<DesignedBattleStage>.Builder stages = ImmutableArray.CreateBuilder<DesignedBattleStage>();

        if (root.TryGetProperty("stages", out JsonElement stagesElement))
        {
            foreach (JsonElement stageElement in stagesElement.EnumerateArray())
            {
                stages.Add(ReadStage(stageElement));
            }
        }

        return new DesignedBattleAi(DesignedBattleAi.IdPrefix + fileName, name, stages.ToImmutable());
    }

    private static DesignedBattleStage ReadStage(JsonElement element)
    {
        string name = element.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? "" : "";
        ImmutableArray<ImmutableArray<DesignedBattleCondition>>.Builder when = ImmutableArray.CreateBuilder<ImmutableArray<DesignedBattleCondition>>();

        if (element.TryGetProperty("when", out JsonElement whenElement))
        {
            foreach (JsonElement groupElement in whenElement.EnumerateArray())
            {
                ImmutableArray<DesignedBattleCondition>.Builder group = ImmutableArray.CreateBuilder<DesignedBattleCondition>();

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

        DesignedBattleTactics tactics = DesignedBattleTactics.Classic;

        if (element.TryGetProperty("tactics", out JsonElement t))
        {
            int Number(string property, int fallback) => t.TryGetProperty(property, out JsonElement value) ? value.GetInt32() : fallback;
            bool Flag(string property, bool fallback) => t.TryGetProperty(property, out JsonElement value) ? value.GetBoolean() : fallback;

            tactics = new DesignedBattleTactics(
                Number("healersFirst", 0),
                Number("castersFirst", 0),
                Number("leaderFirst", 0),
                Number("hardestHitterFirst", 0),
                Number("woundedFirst", 0),
                Number("focusFire", 0),
                Flag("protectLeader", false),
                Flag("neverFlee", false),
                Number("fleeBelowPercent", 25),
                Flag("canDefend", true),
                Number("leaderDefendsBelowPercent", 0));
        }

        return new DesignedBattleStage(name, when.ToImmutable(), tactics);
    }

    private static void WriteCondition(Utf8JsonWriter writer, DesignedBattleCondition condition)
    {
        writer.WriteStartObject();

        switch (condition)
        {
            case DesignedBattleCondition.FromRound fromRound:
                writer.WriteString("kind", "fromRound");
                writer.WriteNumber("round", fromRound.Round);
                break;
            case DesignedBattleCondition.PowerShareBelow below:
                writer.WriteString("kind", "powerShareBelow");
                writer.WriteNumber("percent", below.Percent);
                break;
            case DesignedBattleCondition.PowerShareAbove above:
                writer.WriteString("kind", "powerShareAbove");
                writer.WriteNumber("percent", above.Percent);
                break;
            case DesignedBattleCondition.LeaderHitpointsBelow leader:
                writer.WriteString("kind", "leaderHitpointsBelow");
                writer.WriteNumber("percent", leader.Percent);
                break;
            case DesignedBattleCondition.CargoAbove cargo:
                writer.WriteString("kind", "cargoAbove");
                writer.WriteNumber("gold", cargo.Gold);
                writer.WriteBoolean("includeEquipped", cargo.IncludeEquipped);
                break;
            case DesignedBattleCondition.CarriesQuestItem:
                writer.WriteString("kind", "carriesQuestItem");
                break;
        }

        writer.WriteEndObject();
    }

    private static DesignedBattleCondition? ReadCondition(JsonElement element)
    {
        int Number(string property) => element.GetProperty(property).GetInt32();

        return element.TryGetProperty("kind", out JsonElement kind) ? kind.GetString() switch
        {
            "fromRound" => new DesignedBattleCondition.FromRound(Number("round")),
            "powerShareBelow" => new DesignedBattleCondition.PowerShareBelow(Number("percent")),
            "powerShareAbove" => new DesignedBattleCondition.PowerShareAbove(Number("percent")),
            "leaderHitpointsBelow" => new DesignedBattleCondition.LeaderHitpointsBelow(Number("percent")),
            "cargoAbove" => new DesignedBattleCondition.CargoAbove(Number("gold"), element.TryGetProperty("includeEquipped", out JsonElement equipped) && equipped.GetBoolean()),
            "carriesQuestItem" => new DesignedBattleCondition.CarriesQuestItem(),
            _ => null
        } : null;
    }
}

using System.Collections.Immutable;

namespace Gallean.Engine;

// Custom maps live in their own folder (the user's data directory), laid out like the game's Scens folder:
// <maps>/Sinfo.json lists them and <maps>/<id>/ holds each map's tables. IDs use the S600-S899 range.
public sealed class CustomMapStore(string mapsDirectory, GlobalDatabase globalDb, CampaignDatabase campaignDb)
{
    public const int FirstIdNumber = 600;
    public const int LastIdNumber = 899;

    private const string PreviewDirectoryName = ".preview";

    public string MapsDirectory => mapsDirectory;

    public static bool IsCustomMapId(string scenarioId)
    {
        return scenarioId.Length == 10 &&
               scenarioId[0] is 'S' or 's' &&
               int.TryParse(scenarioId.AsSpan(1, 3), out int number) &&
               number is >= FirstIdNumber and <= LastIdNumber;
    }

    public IReadOnlyList<Db.ScenarioInfo> List()
    {
        return [.. CustomMapWriter.ReadIndex(mapsDirectory).Where(info => Directory.Exists(Path.Combine(mapsDirectory, info.ScenarioId.Value)))];
    }

    public bool Contains(string mapId)
    {
        return List().Any(info => string.Equals(info.ScenarioId.Value, mapId, StringComparison.OrdinalIgnoreCase));
    }

    public string NextId()
    {
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);

        foreach (Db.ScenarioInfo info in CustomMapWriter.ReadIndex(mapsDirectory))
        {
            used.Add(info.ScenarioId.Value);
        }

        if (Directory.Exists(mapsDirectory))
        {
            foreach (string directory in Directory.EnumerateDirectories(mapsDirectory))
            {
                used.Add(Path.GetFileName(directory));
            }
        }

        for (int number = FirstIdNumber; number <= LastIdNumber; number++)
        {
            string id = $"S{number:D3}SC0000";

            if (!used.Contains(id))
            {
                return id;
            }
        }

        throw new InvalidOperationException($"No free custom map IDs left (S{FirstIdNumber}-S{LastIdNumber}).");
    }

    public void Save(CustomMap map)
    {
        IReadOnlyList<string> errors = map.Validate();

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        Directory.CreateDirectory(mapsDirectory);
        new CustomMapWriter(globalDb).Write(map, mapsDirectory);
    }

    public void Delete(string mapId)
    {
        CustomMapWriter.Delete(mapsDirectory, mapId);
    }

    public ScenarioDatabase LoadDatabase(string mapId)
    {
        return ScenarioDatabaseLoader.Load(mapsDirectory, new Db.ScenarioId(mapId));
    }

    public CustomMap Open(string mapId, IGameDefinitionProvider definitions)
    {
        Db.ScenarioInfo info = List().Single(candidate => string.Equals(candidate.ScenarioId.Value, mapId, StringComparison.OrdinalIgnoreCase));
        Scenario scenario = ScenarioLoader.Load(globalDb, LoadDatabase(mapId), campaignDb, new ScenarioId(info.ScenarioId.Value), definitions, new GlobalVariables());

        return FromState(info.ScenarioId.Value, info.Name, info.Description ?? string.Empty, scenario.State, definitions);
    }

    // Builds the world state the editor renders by saving the map to a scratch folder and loading it through the
    // regular scenario loader, so the editor shows exactly what the game will load.
    public Scenario BuildPreview(CustomMap map, IGameDefinitionProvider definitions)
    {
        return BuildPreviewWithDatabase(map, definitions).Scenario;
    }

    // Also returns the loaded tables, which hold the scenario texts (player and capital names).
    public (Scenario Scenario, ScenarioDatabase Database) BuildPreviewWithDatabase(CustomMap map, IGameDefinitionProvider definitions)
    {
        string previewDirectory = Path.Combine(mapsDirectory, PreviewDirectoryName, Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(previewDirectory);
            new CustomMapWriter(globalDb).Write(map, previewDirectory);

            ScenarioDatabase database = ScenarioDatabaseLoader.Load(previewDirectory, new Db.ScenarioId(map.Id));

            return (ScenarioLoader.Load(globalDb, database, campaignDb, new ScenarioId(map.Id), definitions, new GlobalVariables()), database);
        }
        finally
        {
            if (Directory.Exists(previewDirectory))
            {
                Directory.Delete(previewDirectory, recursive: true);
            }
        }
    }

    // Adds the custom maps to the bundled scenario index, so they appear in the quest list next to the
    // original scenarios.
    public ScenarioDatabaseIndex MergeInto(ScenarioDatabaseIndex bundled)
    {
        if (!File.Exists(Path.Combine(mapsDirectory, CustomMapWriter.IndexFileName)))
        {
            return bundled;
        }

        ScenarioDatabaseIndex custom = ScenarioDatabaseLoader.LoadIndex(mapsDirectory);
        HashSet<Db.ScenarioId> bundledIds = [.. bundled.ScenarioInfos.Select(info => info.ScenarioId)];
        Db.ScenarioInfo[] added = [.. custom.ScenarioInfos.Where(info => !bundledIds.Contains(info.ScenarioId) && IsCustomMapId(info.ScenarioId.Value))];
        Db.ScenarioInfo[] infos = [.. bundled.ScenarioInfos, .. added];

        return bundled with
        {
            ScenarioInfos = infos,
            ScenarioInfosByScenario = infos.ToLookup(info => info.ScenarioId),
            ScenariosWithEvents = new HashSet<Db.ScenarioId>(bundled.ScenariosWithEvents.Concat(added.Select(info => info.ScenarioId).Where(custom.ScenariosWithEvents.Contains)))
        };
    }

    // Updates terrain and mountains of a preview state in place of a full rebuild; capitals need BuildPreview.
    public static WorldState ApplyTerrain(WorldState state, CustomMap map)
    {
        return state with
        {
            Map = map.ToWorldMap(),
            Mountains = map.Mountains
                .Select(mountain => new WorldMountain(new WorldMountainId(mountain.Id), mountain.Footprint, WorldRace.Neutral, new WorldObjectVariant(mountain.Variant)))
                .ToImmutableSortedDictionary(mountain => mountain.Id, mountain => mountain)
        };
    }

    public static CustomMap FromState(string mapId, string name, string description, WorldState state, IGameDefinitionProvider definitions)
    {
        if (state.Map.Width != state.Map.Height)
        {
            throw new NotSupportedException("The map editor only supports square maps.");
        }

        List<CustomMapPlayer> players = [];

        foreach (WorldPlayer player in state.Players.Values)
        {
            if (!WorldHelpers.IsRacePlayable(player.Race))
            {
                continue;
            }

            WorldPosition? capital = state.Fortifications.Values
                .OfType<WorldCapital>()
                .Where(candidate => candidate.OwnerId == player.Id)
                .Select(candidate => (WorldPosition?)candidate.Footprint.TopLeft)
                .FirstOrDefault();

            players.Add(new CustomMapPlayer(player.Race, capital));
        }

        ImmutableArray<CustomMapMountain> mountains =
        [
            .. state.Mountains.Values.Select(mountain => new CustomMapMountain(
                mountain.Id.Value,
                mountain.Footprint.TopLeft,
                mountain.Footprint.Width,
                mountain.Variant.Value))
        ];

        WorldFootprint[] capitals = [.. state.Fortifications.Values.OfType<WorldCapital>().Select(capital => capital.Footprint)];
        List<CustomMapStack> stacks = [];

        foreach (WorldStack stack in state.Stacks.Values)
        {
            // Capital garrisons belong to the capitals; everything else is an army placed in the editor.
            if (capitals.Any(footprint => footprint.Contains(stack.Position)))
            {
                continue;
            }

            CustomMapUnit?[] slots = new CustomMapUnit?[GridPosition.Count];

            foreach ((WorldUnit unit, GridPosition position) in stack.Units.GetUnits())
            {
                UnitDefinition definition = definitions.GetUnit(unit.DefinitionId);
                int slot = definition.IsLarge ? GridPosition.AtFrontLine(position.Flank).ToIndex() : position.ToIndex();
                slots[slot] = CustomMapUnit.From(definition) with { Level = unit.Level, Hitpoints = unit.Hitpoints };
            }

            string idText = stack.Id.Value;
            int id = idText.Length == 10 && int.TryParse(idText.AsSpan(6), System.Globalization.NumberStyles.HexNumber, null, out int number) && number > CustomMapWriter.FirstArmyNumber
                ? number - CustomMapWriter.FirstArmyNumber
                : stacks.Count + 1;
            stacks.Add(new CustomMapStack(id, stack.Position, state.Players[stack.OwnerId].Race, [.. slots]));
        }

        return new CustomMap(mapId, name, description, state.Map.Width, state.Map.Cells, mountains, players, stacks);
    }

    // Units the editor offers for armies: regular and neutral soldiers and leaders that can walk on land.
    public IReadOnlyList<UnitDefinition> ListUnits(IGameDefinitionProvider definitions)
    {
        List<UnitDefinition> result = [];

        foreach (Db.Id id in globalDb.Units.Keys)
        {
            UnitDefinition? definition = definitions.TryGetUnit(new UnitDefinitionId(id.Value));

            if (definition is { IsWaterOnly: false, Category: UnitCategory.Soldier or UnitCategory.Leader or UnitCategory.NeutralSoldier or UnitCategory.NeutralLeader })
            {
                result.Add(definition);
            }
        }

        return result;
    }
}

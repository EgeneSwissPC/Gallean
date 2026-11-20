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
        ScenarioDatabase database = LoadDatabase(mapId);
        Scenario scenario = ScenarioLoader.Load(globalDb, database, campaignDb, new ScenarioId(info.ScenarioId.Value), definitions, new GlobalVariables());
        Dictionary<string, string> texts = database.TextScenariosByScenario[info.ScenarioId]
            .Where(text => text.Text is not null)
            .GroupBy(text => $"{text.IdObject.Value}/{text.IdString}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Text!, StringComparer.OrdinalIgnoreCase);

        CustomMap map = FromState(info.ScenarioId.Value, info.Name, info.Description ?? string.Empty, scenario.State, definitions, (objectId, textId) => texts.GetValueOrDefault($"{objectId}/{textId}"), scenario.Provider.Locations,
            CustomMapEventsFile.Read(Path.Combine(mapsDirectory, info.ScenarioId.Value, CustomMapEventsFile.FileName)),
            CustomMapEventsFile.ReadTemplates(Path.Combine(mapsDirectory, info.ScenarioId.Value, CustomMapEventsFile.FileName)),
            CustomMapEventsFile.ReadVariables(Path.Combine(mapsDirectory, info.ScenarioId.Value, CustomMapEventsFile.FileName)));
        map.SetForeignSpellMarkup(scenario.Provider.Definition.ForeignSpellMarkup);
        map.NeutralKinds = database.ScenarioRulesByScenario[info.ScenarioId].FirstOrDefault()?.NeutralKinds ?? false;

        return map;
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
            ScenarioInfosByScenario = infos.ToLookup(info => info.ScenarioId)
        };
    }

    // Updates terrain, mountains, resources, landmarks and rods of a preview state in place of a full rebuild;
    // capitals, armies and villages need BuildPreview.
    public static WorldState ApplyTerrain(WorldState state, CustomMap map)
    {
        return state with
        {
            Map = map.ToWorldMap(),
            Mountains = map.Mountains
                .Select(mountain => new WorldMountain(new WorldMountainId(mountain.Id), mountain.Footprint, WorldRace.Neutral, new WorldObjectVariant(mountain.Variant)))
                .ToImmutableSortedDictionary(mountain => mountain.Id, mountain => mountain),
            ResourceNodes = map.Resources
                .Select(resource => new WorldResourceNode(new WorldResourceNodeId(CustomMapWriter.GetResourceId(map.Id, resource.Id)), resource.Position, resource.Type, AiPriority: 3))
                .ToImmutableSortedDictionary(node => node.Id, node => node),
            Landmarks = map.Landmarks
                .Select(landmark => new WorldLandmark(new WorldLandmarkId(CustomMapWriter.GetLandmarkId(map.Id, landmark.Id)), landmark.Type, landmark.Footprint))
                .ToImmutableSortedDictionary(landmark => landmark.Id, landmark => landmark),
            Rods = map.Rods
                .Where(rod => state.Players.Values.Any(player => player.Race == rod.Owner))
                .Select(rod => new WorldRod(
                    new WorldRodId(CustomMapWriter.GetRodId(map.Id, rod.Id)),
                    state.Players.Values.First(player => player.Race == rod.Owner).Id,
                    rod.Position))
                .ToImmutableSortedDictionary(rod => rod.Id, rod => rod)
        };
    }

    // getText returns a scenario text of an object (scenario object ID, text ID such as NAME_TXT), when known.
    // locations are the scenario's SLoc zones (Scenario.Provider.Locations); they aren't part of the world state.
    // events come from the editor's EditorEvents.json (the scenario's event tables are only the game's copy).
    public static CustomMap FromState(string mapId, string name, string description, WorldState state, IGameDefinitionProvider definitions, Func<string, string, string?>? getText = null, IReadOnlyList<ScenarioLocationDefinition>? locations = null, IReadOnlyList<CustomMapEvent>? events = null, IReadOnlyList<CustomMapArmyTemplate>? templates = null, IReadOnlyList<CustomMapVariable>? variables = null)
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

            WorldCapital? capital = state.Fortifications.Values
                .OfType<WorldCapital>()
                .FirstOrDefault(candidate => candidate.OwnerId == player.Id);

            // A garrison of just the guardian where the writer puts it is what an unset garrison saves as.
            CustomMapUnit?[] garrison = capital is null ? [] : ToSlots(capital.Garrison, definitions);
            bool isDefault = garrison.Length == GridPosition.Count &&
                             garrison.Count(unit => unit is not null) == 1 &&
                             garrison[CustomMapWriter.GuardianSlot.ToIndex()] is { IsGuardian: true } guardian &&
                             guardian == CustomMapUnit.From(definitions.GetUnit(guardian.TypeId));

            // The loader scales a human player's bank by difficulty; custom maps are saved as Average (100 %).
            players.Add(new CustomMapPlayer(player.Race, capital?.Footprint.TopLeft) { Garrison = isDefault ? [] : [.. garrison], StartingBank = player.Resources });
        }

        ImmutableArray<CustomMapMountain> mountains =
        [
            .. state.Mountains.Values.Select(mountain => new CustomMapMountain(
                mountain.Id.Value,
                mountain.Footprint.TopLeft,
                mountain.Footprint.Width,
                mountain.Variant.Value))
        ];

        WorldCapital[] capitals = [.. state.Fortifications.Values.OfType<WorldCapital>()];
        List<CustomMapStack> stacks = [];
        List<WorldStack> unnumbered = [];

        CustomMapStack ToStack(WorldStack stack, int id)
        {
            return new CustomMapStack(id, stack.Position, state.Players[stack.OwnerId].Race, [.. ToSlots(stack.Units, definitions)]);
        }

        foreach (WorldStack stack in state.Stacks.Values)
        {
            // Inside a capital only the army on its entrance is kept: it is stationed there.
            if (capitals.Any(capital => capital.Footprint.Contains(stack.Position) && capital.Entrance != stack.Position))
            {
                continue;
            }

            if (ParseObjectNumber(stack.Id.Value, CustomMapWriter.FirstArmyNumber) is { } id)
            {
                stacks.Add(ToStack(stack, id));
            }
            else
            {
                unnumbered.Add(stack);
            }
        }

        // Armies from outside the editor's numbering get the next free IDs, except a capital's starting leader: the
        // writer adds that one when no army was set for the capital.
        foreach (WorldStack stack in unnumbered)
        {
            if (capitals.Any(capital => capital.Entrance == stack.Position))
            {
                continue;
            }

            stacks.Add(ToStack(stack, stacks.Count == 0 ? 1 : stacks.Max(existing => existing.Id) + 1));
        }

        List<CustomMapVillage> villages = [];

        foreach (WorldCity city in state.Fortifications.Values.OfType<WorldCity>())
        {
            int id = ParseObjectNumber(city.Id.Value, CustomMapWriter.FirstVillageNumber) ?? villages.Count + 1;
            string villageName = getText?.Invoke(city.Id.Value, Db.TextScenario.NameTextId) ?? string.Empty;
            villages.Add(new CustomMapVillage(id, city.Footprint.TopLeft, city.DefinitionId.Level, state.Players[city.OwnerId].Race, villageName, [.. ToSlots(city.Garrison, definitions)]));
        }

        List<CustomMapResource> resources = [];

        foreach (WorldResourceNode node in state.ResourceNodes.Values)
        {
            int id = ParseObjectNumber(node.Id.Value, 0) ?? resources.Count + 1;
            resources.Add(new CustomMapResource(id, node.Position, node.ResourceType));
        }

        List<CustomMapRuin> ruins = [];

        foreach (WorldRuin ruin in state.Fortifications.Values.OfType<WorldRuin>())
        {
            int id = ParseObjectNumber(ruin.Id.Value, 0) ?? ruins.Count + 1;
            string ruinName = getText?.Invoke(ruin.Id.Value, Db.TextScenario.RuinTitleTextId) ?? string.Empty;
            ruins.Add(new CustomMapRuin(id, ruin.Footprint.TopLeft, ruin.Variant.Value, ruinName, ruin.Resources.Gold, ruin.Item, [.. ToSlots(ruin.Garrison, definitions)]));
        }

        List<CustomMapBag> bags = [];

        foreach (WorldBag bag in state.Bags.Values)
        {
            int id = ParseObjectNumber(bag.Id.Value, 0) ?? bags.Count + 1;
            bags.Add(new CustomMapBag(id, bag.Position, bag.Variant.Value, [.. bag.Inventory.Items.Select(item => item.DefinitionId)]));
        }

        List<CustomMapSite> sites = [];

        foreach (WorldSite site in state.Sites.Values)
        {
            int id = ParseObjectNumber(site.Id.Value, 0) ?? sites.Count + 1;
            string siteName = getText?.Invoke(site.Id.Value, Db.TextScenario.SiteTitleTextId) ?? string.Empty;
            string greeting = getText?.Invoke(site.Id.Value, Db.TextScenario.SiteDescriptionTextId) ?? string.Empty;
            CustomMapSite converted = site switch
            {
                WorldMerchantSite merchant => new CustomMapSite(id, site.Footprint.TopLeft, CustomMapSiteType.Merchant, site.Variant.Value, siteName, greeting,
                    [.. merchant.Items.Select(item => new CustomMapMerchantItem(item.ItemId, item.Count))], [], []),
                WorldMageSite mage => new CustomMapSite(id, site.Footprint.TopLeft, CustomMapSiteType.MageTower, site.Variant.Value, siteName, greeting,
                    [], [.. mage.Spells], []),
                WorldMercenarySite camp => new CustomMapSite(id, site.Footprint.TopLeft, CustomMapSiteType.Mercenaries, site.Variant.Value, siteName, greeting,
                    [], [], [.. camp.Units.Select(unit => new CustomMapMercenary(unit.DefinitionId, unit.Level))]),
                _ => new CustomMapSite(id, site.Footprint.TopLeft, CustomMapSiteType.Trainer, site.Variant.Value, siteName, greeting, [], [], [])
            };
            sites.Add(converted);
        }

        List<CustomMapLandmark> landmarks = [];
        WorldMapCell[] cells = [.. state.Map.Cells];

        foreach (WorldLandmark landmark in state.Landmarks.Values)
        {
            int id = ParseObjectNumber(landmark.Id.Value, 0) ?? landmarks.Count + 1;
            bool mountainType = definitions.GetLandmark(landmark.DefinitionId).Mountain;
            landmarks.Add(new CustomMapLandmark(id, landmark.Footprint.TopLeft, landmark.DefinitionId, landmark.Footprint.Width, landmark.Footprint.Height, mountainType));

            // The loader turns the tiles of a mountain-type landmark into mountains; the map keeps them free, so the
            // tiles don't stay blocked once the landmark is removed. The writer marks them again.
            if (mountainType)
            {
                foreach (WorldPosition tile in landmark.Footprint.EnumerateTiles())
                {
                    int index = (tile.Y * state.Map.Width) + tile.X;

                    if (state.Map.IsValidPosition(tile) && cells[index].Feature == WorldMapFeature.Mountain &&
                        !mountains.Any(mountain => mountain.Footprint.Contains(tile)))
                    {
                        cells[index] = cells[index] with { Feature = WorldMapFeature.None, FeatureVariant = new WorldObjectVariant(0) };
                    }
                }
            }
        }

        List<CustomMapRod> rods = [];

        foreach (WorldRod rod in state.Rods.Values)
        {
            WorldRace owner = state.Players[rod.OwnerId].Race;

            // Only playable races' rods spread land; a rod of anyone else can't be edited, so it isn't kept.
            if (WorldHelpers.IsRacePlayable(owner))
            {
                int id = ParseObjectNumber(rod.Id.Value, 0) ?? rods.Count + 1;
                rods.Add(new CustomMapRod(id, rod.Position, owner));
            }
        }

        List<CustomMapLocation> mapLocations = [];

        foreach (ScenarioLocationDefinition location in locations ?? [])
        {
            int id = ParseObjectNumber(location.Id.Value, 0) ?? mapLocations.Count + 1;
            string locationName = getText?.Invoke(location.Id.Value, Db.TextScenario.NameTextId) is { } text && !string.IsNullOrWhiteSpace(text)
                ? text
                : $"Location {id}";

            // The editor keeps radius 0-3 (all the original maps use); larger zones are clamped.
            mapLocations.Add(new CustomMapLocation(id, locationName, location.Position, Math.Clamp(location.Radius, 0, CustomMap.MaxLocationRadius)));
        }

        return new CustomMap(mapId, name, description, state.Map.Width, cells, mountains, players, stacks, villages, resources, ruins, bags, sites, landmarks, rods, mapLocations, events, templates, variables);
    }

    // The editor's object number from a saved ID such as S600KC0101 (first = 0x100 gives 1).
    private static int? ParseObjectNumber(string id, int first)
    {
        return id.Length == 10 && int.TryParse(id.AsSpan(6), System.Globalization.NumberStyles.HexNumber, null, out int number) && number > first
            ? number - first
            : null;
    }

    private static CustomMapUnit?[] ToSlots(UnitGrid<WorldUnit> units, IGameDefinitionProvider definitions)
    {
        CustomMapUnit?[] slots = new CustomMapUnit?[GridPosition.Count];

        foreach ((WorldUnit unit, GridPosition position) in units.GetUnits())
        {
            UnitDefinition definition = definitions.GetUnit(unit.DefinitionId);
            int slot = definition.IsLarge ? GridPosition.AtFrontLine(position.Flank).ToIndex() : position.ToIndex();
            slots[slot] = CustomMapUnit.From(definition, unit.Level, definitions) with { Level = unit.Level, Hitpoints = unit.Hitpoints };
        }

        return slots;
    }

    // What a new capital starts with: the race's guardian in the garrison and its first leader stationed inside.
    public (CustomMapUnit?[] Garrison, CustomMapUnit?[] Army) GetCapitalDefaults(WorldRace race, IGameDefinitionProvider definitions)
    {
        Db.RaceType raceType = CustomMapWriter.ToRaceType(race);
        Db.Race dbRace = globalDb.Races.Values.Single(candidate => candidate.RaceType == raceType);
        Db.Id guardianId = dbRace.GuardianId ?? throw new InvalidDataException($"Race {race} has no guardian.");
        Db.Id leaderId = dbRace.Leader1Id ?? throw new InvalidDataException($"Race {race} has no leader.");
        CustomMapUnit?[] garrison = new CustomMapUnit?[GridPosition.Count];
        CustomMapUnit?[] army = new CustomMapUnit?[GridPosition.Count];

        garrison[CustomMapWriter.GuardianSlot.ToIndex()] = CustomMapUnit.From(definitions.GetUnit(new UnitDefinitionId(guardianId.Value)));
        army[GridPosition.FrontCenter.ToIndex()] = CustomMapUnit.From(definitions.GetUnit(new UnitDefinitionId(leaderId.Value)));

        return (garrison, army);
    }

    // Items the editor offers for bags, ruins and merchants: every item of the game.
    public IReadOnlyList<ItemDefinition> ListItems(IGameDefinitionProvider definitions)
    {
        return [.. globalDb.Items.Keys.Select(id => definitions.GetItem(new ItemDefinitionId(id.Value)))];
    }

    // Landmarks the editor offers: every landmark of the game.
    public IReadOnlyList<WorldLandmarkDefinition> ListLandmarks(IGameDefinitionProvider definitions)
    {
        return [.. globalDb.Landmarks.Keys.Select(id => definitions.GetLandmark(new WorldLandmarkDefinitionId(id.Value)))];
    }

    // Spells the editor offers for mage towers: every spell of the game.
    public IReadOnlyList<WorldSpellDefinition> ListSpells(IGameDefinitionProvider definitions)
    {
        return [.. globalDb.Spells.Keys.Select(id => definitions.GetSpell(new WorldSpellDefinitionId(id.Value)))];
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

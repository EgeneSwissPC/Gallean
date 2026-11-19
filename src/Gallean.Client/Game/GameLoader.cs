using Gallean.Common;
using Gallean.Engine;
using Gallean.Entities;
using Gallean.Entities.GameData;

namespace Gallean.Client;

// customMapsDirectory holds the map editor's maps; null disables custom maps (tests).
internal sealed class GameLoader(IRandomProvider random, string? customMapsDirectory = null)
{
    private delegate Scenario ScenarioLoadFunc(
        ScenarioDatabase scenarioDatabase,
        IGameDefinitionProvider definitions,
        GlobalVariables globals);

    public const string TutorialScenarioId = "S253SC0000";

    private readonly Lazy<CampaignDatabase> campaignDatabase = new(() => CampaignDatabaseLoader.Load(RiseOfTheElves.CampaignDatabasePath));
    private readonly Lazy<GlobalDatabase> globalDatabase = new(() => GlobalDatabaseLoader.Load(RiseOfTheElves.GlobalDatabasePath));
    private readonly Lazy<GlobalDatabaseIndex> globalDatabaseIndex = new(() => GlobalDatabaseLoader.LoadIndex(RiseOfTheElves.GlobalDatabasePath));
    private readonly Lazy<ScenarioDatabaseIndex> scenarioDatabaseIndex = new(() => ScenarioDatabaseLoader.LoadIndex(RiseOfTheElves.ScenarioDatabasePath));

    public (IReadOnlyList<GameCampaign> Campaigns, IGameTextProvider Texts, IGameDefinitionProvider Definitions) LoadCampaigns(Language language)
    {
        ScenarioDatabaseIndex scenarioIndex = CreateMapStore()?.MergeInto(scenarioDatabaseIndex.Value) ?? scenarioDatabaseIndex.Value;
        IReadOnlyList<ScenarioCampaign> campaigns = ScenarioLoader.EnumerateCampaigns(
            globalDatabaseIndex.Value,
            scenarioIndex,
            campaignDatabase.Value);
        LanguageDatabase languageDatabase = LanguageDatabaseLoader.LoadIndex(
            RiseOfTheElves.GetLanguageGlobalDatabasePath(language.Code),
            RiseOfTheElves.GetLanguageScenarioDatabasePath(language.Code));
        IGameTextProvider texts = new GameTextProvider(globalDatabaseIndex.Value, scenarioIndex, [], languageDatabase);
        IGameDefinitionProvider definitions = new GameDefinitionProvider(globalDatabase.Value);

        List<GameCampaign> result = [];

        foreach (ScenarioCampaign campaign in campaigns)
        {
            List<GameScenario> scenarios = [];

            foreach (ScenarioMetadata scenario in campaign.Scenarios)
            {
                scenarios.Add(new GameScenario(
                    scenario.Id.Value,
                    texts.GetScenarioText(scenario.InfoTexts.Name),
                    GetOptionalText(texts, scenario.InfoTexts.Description),
                    texts.GetScenarioText(scenario.InfoTexts.Objective),
                    scenario.SuggestedLevel,
                    scenario.MapSize,
                    scenario.CityCount,
                    GetOptionalText(texts, scenario.InfoTexts.Creator),
                    campaign.IsSkirmish,
                    campaign.CanImportLeader,
                    scenario.PlayableRaces));
            }

            result.Add(new GameCampaign(
                campaign.Id.Value,
                texts.GetGlobalText(campaign.NameTextId).Text,
                campaign.PlayRace,
                campaign.IsSkirmish,
                scenarios));
        }

        return (result, texts, definitions);
    }

    private static string? GetOptionalText(IGameTextProvider texts, ScenarioTextId? id)
    {
        if (id is not null)
        {
            return texts.GetScenarioText(id.Value);
        }
        else
        {
            return null;
        }
    }

    public (GameSession Session, IGameTextProvider Texts) LoadScenario(string scenarioId, WorldState? worldState, Language language)
    {
        return LoadInternal(
            scenarioId,
            (scenarioDatabase, definitions, globals) => ScenarioLoader.Load(
                globalDatabase.Value,
                scenarioDatabase,
                campaignDatabase.Value,
                new ScenarioId(scenarioId),
                definitions,
                globals),
            worldState,
            language);
    }

    public (GameSession Session, IGameTextProvider Texts) CreateScenario(string scenarioId, ScenarioConfiguration configuration, Language language)
    {
        return LoadInternal(
            scenarioId,
            (scenarioDatabase, definitions, globals) => ScenarioLoader.Create(
                globalDatabase.Value,
                scenarioDatabase,
                campaignDatabase.Value,
                new ScenarioId(scenarioId),
                definitions,
                globals,
                configuration),
            worldState: null,
            language);
    }

    public CustomMapStore? CreateMapStore()
    {
        return customMapsDirectory is null ? null : new CustomMapStore(customMapsDirectory, globalDatabase.Value, campaignDatabase.Value);
    }

    public IGameTextProvider CreateTexts(ScenarioDatabase scenarioDatabase, string scenarioId, Language language)
    {
        return CreateTextProvider(scenarioDatabase, scenarioId, language);
    }

    public IGameTextProvider LoadGlobalTexts(Language language)
    {
        LanguageDatabase languageDatabase = LanguageDatabaseLoader.LoadIndex(
            RiseOfTheElves.GetLanguageGlobalDatabasePath(language.Code),
            RiseOfTheElves.GetLanguageScenarioDatabasePath(language.Code));

        return new GameTextProvider(globalDatabaseIndex.Value, scenarioDatabaseIndex.Value, [], languageDatabase);
    }

    public IGameDefinitionProvider CreateDefinitions()
    {
        return new GameDefinitionProvider(globalDatabase.Value);
    }

    // Custom maps (S600-S899) are read from the map editor's folder, everything else from the game data.
    private string GetScenarioDirectory(string scenarioId)
    {
        if (customMapsDirectory is not null &&
            CustomMapStore.IsCustomMapId(scenarioId) &&
            Directory.Exists(Path.Combine(customMapsDirectory, scenarioId)))
        {
            return customMapsDirectory;
        }

        return RiseOfTheElves.ScenarioDatabasePath;
    }

    public IGameTextProvider LoadTexts(string scenarioId, Language language)
    {
        ScenarioDatabase scenarioDatabase = ScenarioDatabaseLoader.Load(GetScenarioDirectory(scenarioId), scenarioId);

        return CreateTextProvider(scenarioDatabase, scenarioId, language);
    }

    private (GameSession Session, IGameTextProvider Texts) LoadInternal(
        string scenarioId,
        ScenarioLoadFunc loadFunc,
        WorldState? worldState,
        Language language)
    {
        IGameDefinitionProvider definitions = new GameDefinitionProvider(globalDatabase.Value);
        ScenarioDatabase scenarioDatabase = ScenarioDatabaseLoader.Load(GetScenarioDirectory(scenarioId), scenarioId);
        IGameTextProvider texts = CreateTextProvider(scenarioDatabase, scenarioId, language);
        GlobalVariables globals = new();
        Scenario scenario = loadFunc(scenarioDatabase, definitions, globals);

        UnitStatsProvider unitStats = new(definitions, globals, scenario.Provider.Definition);
        EngineContext engineContext = new(random, definitions, unitStats, scenario.Provider, globals);
        GameSession session = new(engineContext, worldState ?? scenario.State);

        return (session, texts);
    }

    private IGameTextProvider CreateTextProvider(ScenarioDatabase scenarioDatabase, string scenarioId, Language language)
    {
        // Custom maps keep their translations (map editor events) under <maps>/Languages/<code>/<map id>.
        string languageScenarios = GetScenarioDirectory(scenarioId) == customMapsDirectory && customMapsDirectory is not null
            ? CustomMapWriter.GetLanguageDirectory(customMapsDirectory, language.Code)
            : RiseOfTheElves.GetLanguageScenarioDatabasePath(language.Code);
        LanguageDatabase languageDatabase = LanguageDatabaseLoader.Load(
            RiseOfTheElves.GetLanguageGlobalDatabasePath(language.Code),
            languageScenarios,
            scenarioId);

        return new GameTextProvider(globalDatabase.Value, scenarioDatabase, scenarioDatabase.TextScenarios, languageDatabase);
    }
}

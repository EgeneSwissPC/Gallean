using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;
using Gallean.Entities;

namespace Gallean.Client;

internal sealed partial class GameViewModel
{
    [RelayCommand]
    private void ShowMapEditor()
    {
        CustomMapStore? store = gameLoader.CreateMapStore();

        if (store is null)
        {
            ShowError(Tr("Custom maps are not available."));
            return;
        }

        StopGame();
        ShowScreen(new MapEditorMenuScreenViewModel(this, store));
    }

    public async Task OpenMapEditorAsync(CustomMapStore store, Func<IGameDefinitionProvider, CustomMap> createMap)
    {
        OpenScreen(new LoadingScreenViewModel(Tr("Loading map…")));

        try
        {
            (CustomMap map, GameModel editorGame, IReadOnlyList<UnitDefinition> units, IReadOnlyList<string> villageNames) = await RunAsync(() =>
            {
                IGameDefinitionProvider definitions = gameLoader.CreateDefinitions();
                CustomMap map = createMap(definitions);

                return (map, CreateEditorGame(store, map, definitions), store.ListUnits(definitions), MapEditorVillageNames.Load());
            });

            ShowScreen(new MapEditorScreenViewModel(this, store, editorGame, map, units, villageNames));
        }
        catch (Exception ex)
        {
            log.Error("Failed to open map in the editor.", ex);
            ShowScreen(new MapEditorMenuScreenViewModel(this, store));
            ShowError(Tr("Failed to open map: {0}", ex.Message));
        }
    }

    public Task<GameModel> BuildMapPreviewAsync(CustomMapStore store, CustomMap map, IGameDefinitionProvider definitions)
    {
        return RunAsync(() => CreateEditorGame(store, map, definitions));
    }

    // The editor's view of its map as a game: the preview scenario loaded by the regular loader, with the first
    // player human so game screens and info panels can be built from it.
    private GameModel CreateEditorGame(CustomMapStore store, CustomMap map, IGameDefinitionProvider definitions)
    {
        (Scenario preview, ScenarioDatabase database) = store.BuildPreviewWithDatabase(map, definitions);
        IGameTextProvider texts = gameLoader.CreateTexts(database, map.Id, Settings.EffectiveLanguage);
        GlobalVariables globals = new();
        UnitStatsProvider unitStats = new(definitions, globals, preview.Provider.Definition);
        EngineContext context = new(random, definitions, unitStats, preview.Provider, globals);
        WorldPlayer human = preview.State.Players.Values.First(player => player.Race != WorldRace.Neutral);
        WorldState state = preview.State with { Players = preview.State.Players.SetItem(human.Id, human with { IsAi = false }) };

        return new GameModel(new GameSession(context, state), texts, log, scheduler);
    }
}

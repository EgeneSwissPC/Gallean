using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

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
            (CustomMap map, Scenario preview, IGameDefinitionProvider definitions) = await RunAsync(() =>
            {
                IGameDefinitionProvider definitions = gameLoader.CreateDefinitions();
                CustomMap map = createMap(definitions);

                return (map, store.BuildPreview(map, definitions), definitions);
            });

            ShowScreen(new MapEditorScreenViewModel(this, store, definitions, map, preview));
        }
        catch (Exception ex)
        {
            log.Error("Failed to open map in the editor.", ex);
            ShowScreen(new MapEditorMenuScreenViewModel(this, store));
            ShowError(Tr("Failed to open map: {0}", ex.Message));
        }
    }

    public Task<Scenario> BuildMapPreviewAsync(CustomMapStore store, CustomMap map, IGameDefinitionProvider definitions)
    {
        return RunAsync(() => store.BuildPreview(map, definitions));
    }
}

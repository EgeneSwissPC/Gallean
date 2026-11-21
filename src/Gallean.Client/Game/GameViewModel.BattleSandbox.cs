using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (battle sandbox, docs/battle-ai-designer.md phase 3): the Battle sandbox mode.
internal sealed partial class GameViewModel
{
    [RelayCommand]
    private async Task ShowBattleSandboxAsync()
    {
        CustomMapStore? store = gameLoader.CreateMapStore();

        if (store is null)
        {
            ShowError(Tr("Custom maps are not available."));
            return;
        }

        StopGame();
        OpenScreen(new LoadingScreenViewModel(Tr("Loading…")));

        try
        {
            (GameModel editorGame, IReadOnlyList<UnitDefinition> units, AiBattleBench bench) = await RunAsync(() =>
            {
                IGameDefinitionProvider definitions = gameLoader.CreateDefinitions();
                CustomMap host = AiBattleBench.CreateHostMap(definitions);

                return (CreateEditorGame(store, host, definitions), MapEditorLibrary.Load(store, definitions).Units, gameLoader.CreateBattleBench());
            });

            ShowScreen(new BattleSandboxScreenViewModel(this, editorGame, units, bench, new BattleArmyStore(BattleSandboxScreenViewModel.ArmiesDirectory)));
        }
        catch (Exception ex)
        {
            log.Error("Failed to open the battle sandbox.", ex);
            ShowMainMenuCommand.Execute(null);
            ShowError(Tr("Failed to open the battle sandbox: {0}", ex.Message));
        }
    }
}

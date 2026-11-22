using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (battle sandbox, docs/battle-ai-designer.md phase 3): the Battle sandbox mode.
internal sealed partial class GameViewModel
{
    [RelayCommand]
    private Task ShowBattleSandboxAsync()
    {
        return OpenBattleSandboxAsync(returnTo: null, testAiId: null);
    }

    // Gallean Upgrade (battle AI designer phase 9): the designer's Test button. The sandbox opens with the AI
    // under test on the attacker side and closes back to returnTo.
    public Task OpenBattleSandboxAsync(AbstractScreenViewModel? returnTo, string? testAiId)
    {
        return OpenBattleSandboxCoreAsync(returnTo, testAiId);
    }

    private async Task OpenBattleSandboxCoreAsync(AbstractScreenViewModel? returnTo, string? testAiId)
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
            (GameModel editorGame, MapEditorLibrary library, AiBattleBench bench) = await RunAsync(() =>
            {
                IGameDefinitionProvider definitions = gameLoader.CreateDefinitions();
                CustomMap host = AiBattleBench.CreateHostMap(definitions);

                return (CreateEditorGame(store, host, definitions), MapEditorLibrary.Load(store, definitions), gameLoader.CreateBattleBench());
            });

            BattleSandboxScreenViewModel sandbox = new(this, editorGame, library, bench, new BattleArmyStore(BattleSandboxScreenViewModel.ArmiesDirectory)) { ReturnTo = returnTo };
            sandbox.SelectTestAi(testAiId);
            ShowScreen(sandbox);
        }
        catch (Exception ex)
        {
            log.Error("Failed to open the battle sandbox.", ex);
            if (returnTo is null)
            {
                ShowMainMenuCommand.Execute(null);
            }
            else
            {
                ShowScreen(returnTo);
            }

            ShowError(Tr("Failed to open the battle sandbox: {0}", ex.Message));
        }
    }

    // Watch: the battle on the real battle screen, both sides played by their chosen AI one unit action at a time
    // with the auto-resolve rules, from the seed (the same battle Run fought with that seed). The screen opens over
    // the sandbox and closes back to it after the battle.
    public async Task<BattleScreenViewModel?> WatchSandboxBattleAsync(AiBattleBench bench, IGameTextProvider texts, BattleBenchSide attacker, BattleBenchSide defender, BattleBenchSetup setup, int seed)
    {
        GameModel? gameModel = null;
        BattleScreenViewModel? screen = null;

        try
        {
            GameSession session = bench.CreateWatchSession(attacker, defender, setup, seed);
            gameModel = new GameModel(session, texts, log, scheduler);
            AiBattleBench.StartWatch(session);
            BattleModel battle = gameModel.ActiveBattle ?? throw new InvalidOperationException("The battle did not start.");

            screen = new BattleScreenViewModel(gameModel, battle, this, assets, AssetPreloader, random)
            {
                SandboxWatch = new BattleSandboxWatch(attacker.AiId, defender.AiId, bench.WatchStartRound)
            };

            await screen.LoadAsync();
            OpenScreen(screen);

            return screen;
        }
        catch (Exception ex)
        {
            log.Error("Failed to watch the sandbox battle.", ex);

            if (screen is not null)
            {
                CloseScreen(screen);
            }

            gameModel?.Dispose();
            ShowError(Tr("Failed to load battle.\n{0}", ex.Message));

            return null;
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Gallean.Client;

internal sealed partial class GameMenuViewModel : AbstractGameScreenViewModel
{
    public GameMenuViewModel(GameModel gameModel, GameViewModel gameViewModel)
        : base(gameModel, gameViewModel)
    {
        SaveTip = FormatSaveTip(Snapshot);

        RegisterSnapshotGatedCommands(SaveGameCommand, OpenAiPlayersCommand);
    }

    public override bool BlurBackdrop => true;

    [ObservableProperty]
    public partial string? SaveTip { get; private set; }

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }

    protected override void OnSnapshotChanged(GameSnapshot current)
    {
        SaveTip = FormatSaveTip(current);
    }

    [RelayCommand(CanExecute = nameof(CanSaveGame))]
    private async Task SaveGameAsync()
    {
        await Game.ShowSaveGameAsync(GameModel, CloseSelf);
    }

    private bool CanSaveGame()
    {
        return Snapshot.IsSaveAllowed;
    }

    // Gallean Upgrade: own AIs for computer players during the game (docs/ai-switching.md).
    [RelayCommand(CanExecute = nameof(CanOpenAiPlayers))]
    private void OpenAiPlayers()
    {
        Game.OpenScreen(new AiPlayersScreenViewModel(AiPlayersPanel.CreateRows(GameModel), rows => AiPlayersPanel.ApplyAsync(GameModel, rows)));
    }

    private bool CanOpenAiPlayers()
    {
        return Snapshot.BattleState is null;
    }

    [RelayCommand]
    private void ExportLeader()
    {
        Game.OpenScreen(new LeaderExportSelectScreenViewModel(GameModel, Game, _ => CloseSelf()));
    }

    private static string? FormatSaveTip(GameSnapshot snapshot)
    {
        if (snapshot.BattleState is not null)
        {
            return Tr("Saving is not allowed during battle");
        }
        else if (!snapshot.IsViewPlayerTurn)
        {
            return Tr("Saving is not allowed during another player's turn");
        }
        else
        {
            return null;
        }
    }
}

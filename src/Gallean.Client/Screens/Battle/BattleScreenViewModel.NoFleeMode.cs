using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Common;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: the "Refuse to flee" battle mode (button and F key). Like Auto-play, but
// the player's units never flee, until the button is pressed again. Instant resolve in this
// mode resolves the rest of the battle without the player's army fleeing.
internal sealed partial class BattleScreenViewModel
{
    private bool isNoFleeInstantRequested;

    [ObservableProperty]
    public partial bool IsNoFleeMode { get; private set; }

    [RelayCommand(CanExecute = nameof(CanToggleNoFleeMode), AllowConcurrentExecutions = true)]
    private async Task ToggleNoFleeModeAsync()
    {
        IsNoFleeMode = !IsNoFleeMode;

        await PlayAiTurnAsync();
    }

    private bool CanToggleNoFleeMode()
    {
        return !IsFinished && battle.ViewerSide != BattleViewerSide.None;
    }

    private void OpenNoFleeResolvePrompt()
    {
        Game.OpenScreen(new ConfirmPromptViewModel(
            Tr("Instant resolve"),
            TrSpans("Would you like to resolve the rest of the battle automatically, without your army fleeing?"),
            Tr("Resolve"),
            ConfirmPromptType.Primary,
            RequestNoFleeInstantResolveAsync));
    }

    private async Task RequestNoFleeInstantResolveAsync()
    {
        if (IsFinished)
        {
            return;
        }
        else if (!IsNoFleeMode)
        {
            // The mode was switched off while the prompt was open.
            await AutoResolveConfirmedAsync();
            return;
        }

        // The mode's loop picks the request up after its current animation, so the
        // instant resolve never runs alongside an animated step.
        isNoFleeInstantRequested = true;

        await PlayAiTurnAsync();
    }

    private async Task ResolveNoFleeInstantlyAsync()
    {
        isNoFleeInstantRequested = false;

        BattleSide? side = battle.ViewerSide switch
        {
            BattleViewerSide.Attacker => BattleSide.Attacker,
            BattleViewerSide.Defender => BattleSide.Defender,
            BattleViewerSide.None => null
        };

        isActing = true;
        try
        {
            await battle.DispatchCommandAsync(new BattleAutoResolveCommand { NoRetreatSide = side });
        }
        finally
        {
            isActing = false;
            IsNoFleeMode = false;
        }

        ResetTarget();
        UpdateState();
    }
}

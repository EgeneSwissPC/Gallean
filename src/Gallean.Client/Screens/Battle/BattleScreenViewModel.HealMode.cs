using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Common;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: the "Heal and defend" battle mode (button and H key). While it is on,
// the player's healers heal the most wounded ally and every other unit defends, turn after
// turn, until the button is pressed again. Instant resolve in this mode does the same
// without animations until the army is fully healed, then resolves the rest of the battle.
internal sealed partial class BattleScreenViewModel
{
    // Stop healing instantly after this many rounds if the enemy hurts the army faster
    // than the healers can heal it, and resolve the rest of the battle normally.
    private const int MaxInstantHealRounds = 30;

    private bool isHealModeInstantRequested;

    [ObservableProperty]
    public partial bool IsHealMode { get; private set; }

    [RelayCommand(CanExecute = nameof(CanToggleHealMode), AllowConcurrentExecutions = true)]
    private async Task ToggleHealModeAsync()
    {
        IsHealMode = !IsHealMode;

        await PlayAiTurnAsync();
    }

    private bool CanToggleHealMode()
    {
        return !IsFinished && battle.ViewerSide != BattleViewerSide.None;
    }

    // Auto-play, Heal and defend and Refuse to flee switch each other off.
    partial void OnIsHealModeChanged(bool value)
    {
        if (value)
        {
            IsAutoBattle = false;
            IsNoFleeMode = false;
        }
    }

    partial void OnIsAutoBattleChanged(bool value)
    {
        if (value)
        {
            IsHealMode = false;
            IsNoFleeMode = false;
        }
    }

    partial void OnIsNoFleeModeChanged(bool value)
    {
        if (value)
        {
            IsAutoBattle = false;
            IsHealMode = false;
        }
    }

    private async Task PlayAiStepAsync()
    {
        if (isHealModeInstantRequested)
        {
            await ResolveHealModeInstantlyAsync();
        }
        else if (isNoFleeInstantRequested)
        {
            await ResolveNoFleeInstantlyAsync();
        }
        else if (IsHealMode && !battle.IsAiTurn)
        {
            await DispatchBattleActionAsync(new BattleHealOrDefendCommand(State.CurrentUnitId), animate: true);
        }
        else if (IsNoFleeMode && !battle.IsAiTurn)
        {
            await DispatchBattleActionAsync(new BattleAiActionCommand(CanUseItems: false, CanRetreat: false), animate: true);
        }
        else
        {
            await DispatchBattleActionAsync(new BattleAiActionCommand(CanUseItems: battle.IsAiTurn) { BrainId = battle.IsAiTurn ? GetBattleAiId(State.CurrentUnit.Side) : null }, animate: true);
        }
    }

    private void OpenHealModeResolvePrompt()
    {
        Game.OpenScreen(new ConfirmPromptViewModel(
            Tr("Instant resolve"),
            TrSpans("Would you like to heal the army instantly and then resolve the rest of the battle automatically?"),
            Tr("Resolve"),
            ConfirmPromptType.Primary,
            RequestHealModeInstantResolveAsync));
    }

    private async Task RequestHealModeInstantResolveAsync()
    {
        if (IsFinished)
        {
            return;
        }
        else if (!IsHealMode)
        {
            // Heal mode was switched off while the prompt was open.
            await AutoResolveConfirmedAsync();
            return;
        }

        // The heal mode loop picks the request up after its current animation, so the
        // instant steps never run alongside an animated one.
        isHealModeInstantRequested = true;

        await PlayAiTurnAsync();
    }

    private async Task ResolveHealModeInstantlyAsync()
    {
        isHealModeInstantRequested = false;

        int startRound = State.RoundNumber;

        isActing = true;
        try
        {
            while (!IsFinished)
            {
                IBattleCommand<Empty>? command = ResolveInstantHealCommand(startRound);

                if (command is null || (await battle.DispatchCommandAsync(command)).Failed)
                {
                    await battle.DispatchCommandAsync(WithBattleAis(new BattleAutoResolveCommand()));
                    break;
                }
            }
        }
        finally
        {
            isActing = false;
            IsHealMode = false;
        }

        ResetTarget();
        UpdateState();
    }

    private IBattleCommand<Empty>? ResolveInstantHealCommand(int startRound)
    {
        if (battle.IsAiTurn)
        {
            return new BattleAiActionCommand(CanUseItems: true) { BrainId = GetBattleAiId(State.CurrentUnit.Side) };
        }

        if (State.RoundNumber - startRound >= MaxInstantHealRounds)
        {
            return null;
        }

        ICommandResult<BattleCommandError, BattleEvent, BattleGetHealModeStatusCommand.Result> status =
            battle.DryRunCommand(new BattleGetHealModeStatusCommand(State.CurrentUnit.Side));

        if (status.Failed || !status.Data.CanKeepHealing)
        {
            return null;
        }

        return new BattleHealOrDefendCommand(State.CurrentUnitId);
    }
}

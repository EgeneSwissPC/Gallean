using CommunityToolkit.Mvvm.Input;

namespace Gallean.Client;

// Gallean Upgrade: move orders, phase 3 (docs/move-orders.md). The game's attack prompt with a
// third answer, asked during End turn for each army whose order would fight this turn.
internal enum MoveOrderPromptChoice
{
    Attack,
    PlanAnotherPath,
    CancelOrder
}

// The target is shown like the world's right-click info (WorldStackInfoViewModel /
// WorldFortificationInfoViewModel): its units when the player may see them (own, neutral, spied
// race), otherwise the game's note that a thief must spy on it.
internal sealed partial class MoveOrderPromptViewModel(
    WorldPositionInfoViewModel? targetInfo,
    string armyName,
    string targetName,
    bool isTargetKnown) : AbstractScreenViewModel
{
    private readonly TaskCompletionSource<MoveOrderPromptChoice> choice = new();

    public WorldPositionInfoViewModel? TargetInfo { get; } = targetInfo;
    public TextSpans Message { get; } = TrSpans("{0}: the path leads into battle with {1:*}.", armyName, targetName);
    public bool IsTargetKnown { get; } = isTargetKnown;

    public Task<MoveOrderPromptChoice> Choice => choice.Task;

    // Escape (and closing the prompt any other way) = plan another path: nothing happens.
    protected override bool TryClose()
    {
        Choose(MoveOrderPromptChoice.PlanAnotherPath);

        return true;
    }

    [RelayCommand]
    private void Attack()
    {
        Choose(MoveOrderPromptChoice.Attack);
    }

    [RelayCommand]
    private void PlanAnotherPath()
    {
        Choose(MoveOrderPromptChoice.PlanAnotherPath);
    }

    [RelayCommand]
    private void CancelOrder()
    {
        Choose(MoveOrderPromptChoice.CancelOrder);
    }

    private void Choose(MoveOrderPromptChoice value)
    {
        CloseSelf();
        choice.TrySetResult(value);
    }
}

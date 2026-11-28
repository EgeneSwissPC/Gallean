using CommunityToolkit.Mvvm.Input;

namespace Gallean.Client;

// Gallean Upgrade: auto rod (docs/auto-rod.md, decision B3). Asked when a rod planter on auto rod
// stands next to a spot where it can plant a rod.
internal enum RodPlantPromptChoice
{
    Plant,
    Skip,
    Stop
}

internal sealed partial class RodPlantPromptViewModel(string armyName) : AbstractScreenViewModel
{
    private readonly TaskCompletionSource<RodPlantPromptChoice> choice = new();

    public string Message { get; } = Tr("{0} can plant a rod here.", armyName);

    public Task<RodPlantPromptChoice> Choice => choice.Task;

    // Escape (and closing the prompt any other way) = skip this spot.
    protected override bool TryClose()
    {
        Choose(RodPlantPromptChoice.Skip);

        return true;
    }

    [RelayCommand]
    private void Plant()
    {
        Choose(RodPlantPromptChoice.Plant);
    }

    [RelayCommand]
    private void Skip()
    {
        Choose(RodPlantPromptChoice.Skip);
    }

    [RelayCommand]
    private void Stop()
    {
        Choose(RodPlantPromptChoice.Stop);
    }

    private void Choose(RodPlantPromptChoice value)
    {
        CloseSelf();
        choice.TrySetResult(value);
    }
}

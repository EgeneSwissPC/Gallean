using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: gives single computer players their own world/battle AI (docs/ai-switching.md).
// Used on the race screen (before the game) and from the game menu (during the game); the
// caller applies the rows when the panel closes.
internal sealed class AiPlayersScreenViewModel : AbstractScreenViewModel
{
    private readonly Func<IReadOnlyList<AiPlayerRowViewModel>, Task> onClosed;

    public AiPlayersScreenViewModel(IReadOnlyList<AiPlayerRowViewModel> players, Func<IReadOnlyList<AiPlayerRowViewModel>, Task> onClosed)
    {
        this.onClosed = onClosed;

        Players = players;
    }

    public IReadOnlyList<AiPlayerRowViewModel> Players { get; }

    public IReadOnlyList<string> WorldAis { get; } = [AiPlayerRowViewModel.SettingsChoice, .. AiBrainRegistry.WorldBrains.Select(brain => brain.Id)];
    public IReadOnlyList<string> BattleAis { get; } = [AiPlayerRowViewModel.SettingsChoice, .. AiBrainRegistry.BattleBrains.Select(brain => brain.Id)];

    public bool HasPlayers => Players.Count > 0;

    protected override bool TryClose()
    {
        CloseSelf();
        onClosed(Players).FileAndForget();

        return true;
    }
}

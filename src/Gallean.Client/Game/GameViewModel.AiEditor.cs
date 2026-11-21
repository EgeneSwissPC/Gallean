using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (AI editor, docs/ai-designer.md): the AI editor mode.
internal sealed partial class GameViewModel
{
    [RelayCommand]
    private void ShowAiEditor()
    {
        StopGame();
        ShowScreen(new AiEditorMenuScreenViewModel(this));
    }

    public IReadOnlyList<(string Id, string Name, int Size)> ListAiTestMaps()
    {
        return gameLoader.ListAiTestMaps(Settings.EffectiveLanguage);
    }

    public AiTournamentRunner CreateAiTournament(string scenarioId, int maxTurns)
    {
        return gameLoader.CreateAiTournament(scenarioId, maxTurns);
    }
}

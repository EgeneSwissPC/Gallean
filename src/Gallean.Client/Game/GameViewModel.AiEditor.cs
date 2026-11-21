using CommunityToolkit.Mvvm.Input;

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
}

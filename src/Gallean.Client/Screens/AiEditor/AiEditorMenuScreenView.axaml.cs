using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class AiEditorMenuScreenView : UserControl
{
    public AiEditorMenuScreenView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnAiDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control { DataContext: AiEditorItem } && DataContext is AiEditorMenuScreenViewModel { EditCommand: var edit } && edit.CanExecute(null))
        {
            edit.Execute(null);
            e.Handled = true;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class MapEditorMenuScreenView : UserControl
{
    public MapEditorMenuScreenView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private MapEditorMenuScreenViewModel ViewModel => (MapEditorMenuScreenViewModel)DataContext!;

    private void OnMapDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control { DataContext: MapEditorMapItem } && ViewModel.OpenCommand.CanExecute(null))
        {
            ViewModel.OpenCommand.Execute(null);
            e.Handled = true;
        }
    }
}

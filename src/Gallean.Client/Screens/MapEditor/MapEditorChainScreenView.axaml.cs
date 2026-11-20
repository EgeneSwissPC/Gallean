using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class MapEditorChainScreenView : UserControl
{
    public MapEditorChainScreenView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnNodeTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: MapEditorChainNodeViewModel node })
        {
            node.OpenCommand.Execute(null);
        }
    }
}

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class MapEditorBagScreenView : UserControl
{
    public MapEditorBagScreenView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

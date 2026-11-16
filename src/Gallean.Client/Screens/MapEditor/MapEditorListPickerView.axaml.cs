using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class MapEditorListPickerView : UserControl
{
    public MapEditorListPickerView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

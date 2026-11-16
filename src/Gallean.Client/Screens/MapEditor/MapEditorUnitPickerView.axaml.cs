using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class MapEditorUnitPickerView : UserControl
{
    public MapEditorUnitPickerView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

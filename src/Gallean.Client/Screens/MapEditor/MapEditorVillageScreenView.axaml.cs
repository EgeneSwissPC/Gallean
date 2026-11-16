using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class MapEditorVillageScreenView : UserControl
{
    public MapEditorVillageScreenView()
    {
        AvaloniaXamlLoader.Load(this);
        DragDropBehavior.Attach(this);
    }
}

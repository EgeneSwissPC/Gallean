using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class MapEditorCapitalScreenView : UserControl
{
    public MapEditorCapitalScreenView()
    {
        AvaloniaXamlLoader.Load(this);
        DragDropBehavior.Attach(this);
    }
}

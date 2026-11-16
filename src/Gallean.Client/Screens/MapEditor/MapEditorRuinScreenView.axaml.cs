using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class MapEditorRuinScreenView : UserControl
{
    public MapEditorRuinScreenView()
    {
        AvaloniaXamlLoader.Load(this);
        DragDropBehavior.Attach(this);
    }
}

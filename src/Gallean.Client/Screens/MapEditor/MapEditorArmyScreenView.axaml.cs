using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class MapEditorArmyScreenView : UserControl
{
    public MapEditorArmyScreenView()
    {
        AvaloniaXamlLoader.Load(this);
        DragDropBehavior.Attach(this);
    }
}

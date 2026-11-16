using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class MapEditorSiteScreenView : UserControl
{
    public MapEditorSiteScreenView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

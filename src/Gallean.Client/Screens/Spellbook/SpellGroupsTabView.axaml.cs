using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class SpellGroupsTabView : UserControl
{
    public SpellGroupsTabView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

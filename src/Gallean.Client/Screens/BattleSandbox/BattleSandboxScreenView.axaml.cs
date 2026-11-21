using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class BattleSandboxScreenView : UserControl
{
    public BattleSandboxScreenView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class BattleSandboxRunView : UserControl
{
    public BattleSandboxRunView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

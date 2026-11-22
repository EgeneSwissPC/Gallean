using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class MoveOrderPromptView : UserControl
{
    public MoveOrderPromptView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

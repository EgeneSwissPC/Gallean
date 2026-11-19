using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class MapEditorVariablesScreenView : UserControl
{
    public MapEditorVariablesScreenView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Enter commits the box like leaving it does (the bindings update on lost focus).
    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            this.FindControl<ItemsControl>("VariableList")?.Focus();
            e.Handled = true;
        }
    }
}

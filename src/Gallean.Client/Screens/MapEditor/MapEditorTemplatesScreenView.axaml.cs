using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class MapEditorTemplatesScreenView : UserControl
{
    public MapEditorTemplatesScreenView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Enter commits the name like leaving the box does (the binding updates on lost focus).
    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            // Moving the focus to the list commits the name, like leaving the box.
            this.FindControl<ItemsControl>("TemplateList")?.Focus();
            e.Handled = true;
        }
    }
}

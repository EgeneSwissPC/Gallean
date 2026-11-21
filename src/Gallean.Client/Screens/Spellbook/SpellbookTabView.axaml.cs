using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Gallean.Client;

internal sealed partial class SpellbookTabView : UserControl
{
    public SpellbookTabView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private SpellbookTabViewModel ViewModel => (SpellbookTabViewModel)DataContext!;

    private void OnContentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel.SelectedSpell is not null)
        {
            ViewModel.ClearSelectionCommand.Execute(null);
        }
    }

    // Gallean Upgrade: Enter in the group name saves the group.
    private void OnGroupNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (ViewModel.SaveGroupCommand.CanExecute(null))
            {
                ViewModel.SaveGroupCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    // Gallean Upgrade: while the name box has focus, the screen's letter hotkeys (Q and E switch tabs) are off.
    // Avalonia checks key bindings before the text box sees the key, so they took the box away mid-word.
    private void OnGroupNameFocusChanged(object? sender, FocusChangedEventArgs e)
    {
        ViewModel.IsTyping = sender is TextBox { IsFocused: true };
    }

    private void OnSpellDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel.CastSelectedCommand.CanExecute(null))
        {
            ViewModel.CastSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }
}

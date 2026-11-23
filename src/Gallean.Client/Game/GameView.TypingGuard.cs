using Avalonia.Controls;
using Avalonia.Input;

namespace Gallean.Client;

// Gallean Upgrade: since upstream 0.17 (ea83f3f3) an unhandled key runs the key bindings of every visible part of the
// top screen, and a text box doesn't handle letter keys. So while a text box has focus, single-key hotkeys that type
// a character (letters, digits, space, punctuation) are skipped; Enter, Escape, F-keys and modified keys still work.
internal sealed partial class GameView
{
    private bool IsTypedIntoTextBox(KeyBinding binding)
    {
        return FocusManager?.GetFocusedElement() is TextBox && TypesACharacter(binding.Gesture);
    }

    internal static bool TypesACharacter(KeyGesture? gesture)
    {
        return gesture is not null &&
               (gesture.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0 &&
               IsCharacterKey(gesture.Key);
    }

    private static bool IsCharacterKey(Key key)
    {
        return key is (>= Key.A and <= Key.Z) or (>= Key.D0 and <= Key.D9) or (>= Key.NumPad0 and <= Key.Divide) or
            Key.Space or (>= Key.OemSemicolon and <= Key.OemBackslash) or Key.OemPlus or Key.OemMinus or Key.OemComma or Key.OemPeriod;
    }
}

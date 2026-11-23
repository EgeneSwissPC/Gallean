using Avalonia.Input;

namespace Gallean.Client.Tests;

// Gallean Upgrade: single-key hotkeys that type a character are skipped while a text box has focus.
public sealed class GameViewTypingGuardTests
{
    [TestCase(Key.H, KeyModifiers.None, true)]
    [TestCase(Key.R, KeyModifiers.Shift, true)]
    [TestCase(Key.D5, KeyModifiers.None, true)]
    [TestCase(Key.Space, KeyModifiers.None, true)]
    [TestCase(Key.OemPeriod, KeyModifiers.None, true)]
    [TestCase(Key.S, KeyModifiers.Control, false)]
    [TestCase(Key.Escape, KeyModifiers.None, false)]
    [TestCase(Key.Enter, KeyModifiers.None, false)]
    [TestCase(Key.F11, KeyModifiers.None, false)]
    [TestCase(Key.Tab, KeyModifiers.None, false)]
    public void TypesACharacterTest(Key key, KeyModifiers modifiers, bool skipped)
    {
        Assert.That(GameView.TypesACharacter(new KeyGesture(key, modifiers)), Is.EqualTo(skipped));
    }

    [Test]
    public void NoGestureTest()
    {
        Assert.That(GameView.TypesACharacter(null), Is.False);
    }
}

namespace Gallean.Client.Tests;

public sealed class ClientTextIndexTests
{
    [Test]
    public void SettingsTest()
    {
        IReadOnlyList<string> settings = ClientTextIndex.All
            .Where(entry => entry.File == "Screens/Settings/SettingsScreenView.axaml")
            .Select(entry => entry.Text)
            .ToList();

        Assert.That(settings, Is.EquivalentTo([
            "Autosave limit",
            "Autosave interval",
            "Battle speed",
            "Battle AI",
            "Close",
            "Display",
            "Sound effects",
            "Game",
            "Interface size",
            "Language",
            "Lock cursor to window",
            "Map AI",
            "Movement speed",
            "Music",
            "Render resolution",
            "Settings",
            "Show the army path under the cursor",
            "Sound and music",
            "Tab skips armies with orders",
            "When this is on, Tab skips armies with a move order; use O to go through them. When it is off, Tab visits every army, ordered ones last.",
            "Windowed mode (F11)"
        ]));
    }

    [Test]
    public void EntriesTest()
    {
        foreach (ClientTextEntry entry in ClientTextIndex.All)
        {
            Assert.That(entry.Text, Is.Not.Empty);
            Assert.That(entry.File, Is.Not.Empty);
            Assert.That(entry.Line, Is.GreaterThan(0));
        }
    }
}

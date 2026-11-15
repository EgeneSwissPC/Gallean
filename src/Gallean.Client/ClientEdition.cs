using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Gallean.Client;

// Marks this build as the personal gallean-upgrade edition (main menu label and window title), so it can't be
// mistaken for the official game when both are installed.
internal static class ClientEdition
{
    public const string Name = "Gallean Upgrade";

    public static IBrush LabelBrush { get; } = new ImmutableSolidColorBrush(Color.FromRgb(242, 201, 76));
}

using System.Globalization;
using Avalonia.Data.Converters;

namespace Gallean.Client;

// Gallean Upgrade: an AI players panel choice: empty = "Settings choice", else the AI's name.
internal sealed class AiChoiceConverter : IValueConverter
{
    public static AiChoiceConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string id)
        {
            throw new ArgumentException("Unexpected value type.", nameof(value));
        }

        return id == AiPlayerRowViewModel.SettingsChoice ? Tr("Settings choice") : AiBrainFormatter.FormatName(id);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

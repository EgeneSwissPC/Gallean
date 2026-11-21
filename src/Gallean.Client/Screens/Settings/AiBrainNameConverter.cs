using System.Globalization;
using Avalonia.Data.Converters;

namespace Gallean.Client;

// Gallean Upgrade: shows an AiBrainRegistry ID as its translated name.
internal sealed class AiBrainNameConverter : IValueConverter
{
    public static AiBrainNameConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string id)
        {
            throw new ArgumentException("Unexpected value type.", nameof(value));
        }

        return AiBrainFormatter.FormatName(id);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

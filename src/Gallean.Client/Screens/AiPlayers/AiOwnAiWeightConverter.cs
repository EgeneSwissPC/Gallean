using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Gallean.Client;

// Gallean Upgrade: players with their own AI stand out in the AI players panel (bold name).
internal sealed class AiOwnAiWeightConverter : IValueConverter
{
    public static AiOwnAiWeightConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? FontWeight.Bold : FontWeight.Normal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

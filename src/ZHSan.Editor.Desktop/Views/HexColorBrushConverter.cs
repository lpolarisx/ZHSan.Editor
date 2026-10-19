using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ZHSan.Editor.Desktop.Views;

/// <summary>Turns a "#RRGGBB" text, such as a colour record's name, into a brush for its swatch.</summary>
public sealed class HexColorBrushConverter : IValueConverter
{
    public static readonly HexColorBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && Color.TryParse(text, out var color) ? new SolidColorBrush(color) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

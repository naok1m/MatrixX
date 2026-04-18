using System.Globalization;
using Avalonia.Data.Converters;

namespace InputBusX.UI.Converters;

public class StickToCanvasConverter : IValueConverter
{
    public double CanvasSize { get; set; } = 100;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double v)
        {
            bool invert = parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase);
            // Map [-1, 1] → [0, CanvasSize]; subtract half the dot size (6px) so the
            // dot centre, not its top-left corner, tracks the stick position.
            return (invert ? 1.0 - v : v + 1.0) / 2.0 * CanvasSize - 6;
        }
        return CanvasSize / 2.0 - 6;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class PercentageConverter : IValueConverter
{
    public double MaxHeight { get; set; } = 100;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double v)
            return v * MaxHeight;
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

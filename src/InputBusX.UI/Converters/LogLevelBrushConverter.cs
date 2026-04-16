using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace InputBusX.UI.Converters;

public class LogLevelBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value as string;
        if (level == "ERROR" || level == "FATAL")
            return new SolidColorBrush(Color.Parse("#FF6B6B"));
        if (level == "WARN")
            return new SolidColorBrush(Color.Parse("#FFD93D"));
        if (level == "DEBUG" || level == "TRACE")
            return new SolidColorBrush(Color.Parse("#4A4F5A"));
        return new SolidColorBrush(Color.Parse("#00FF9C"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

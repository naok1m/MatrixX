using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace InputBusX.UI.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public IBrush? TrueBrush { get; set; } = new SolidColorBrush(Color.Parse("#00FF95"));
    public IBrush? FalseBrush { get; set; } = new SolidColorBrush(Color.Parse("#2A3345"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueBrush : FalseBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

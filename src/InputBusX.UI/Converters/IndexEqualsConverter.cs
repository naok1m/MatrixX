using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace InputBusX.UI.Converters;

public sealed class IndexEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int current)
        {
            return false;
        }

        return int.TryParse(parameter?.ToString(), out var expected) && current == expected;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

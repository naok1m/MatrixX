using System.Globalization;
using Avalonia.Data.Converters;

namespace InputBusX.UI.Converters;

/// <summary>Generic bool → two-string converter. Use TrueValue/FalseValue to configure.</summary>
public class BoolToStringConverter : IValueConverter
{
    public string TrueValue { get; set; } = "Yes";
    public string FalseValue { get; set; } = "No";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueValue : FalseValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Pos.Desktop.Converters;

/// <summary>
/// Compares the bound value to ConverterParameter. Returns Visible when equal,
/// Collapsed when not. Pass "Invert" as ConverterParameter prefix to reverse.
/// Used by the Reports page to show/hide the active tab's content.
/// </summary>
public sealed class EqualsConverterToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? expected = parameter?.ToString();
        if (expected is null)
        {
            return Visibility.Collapsed;
        }

        bool invert = expected.StartsWith("Invert:", StringComparison.Ordinal);
        if (invert)
        {
            expected = expected.Substring("Invert:".Length);
        }

        bool equal = string.Equals(value?.ToString(), expected, StringComparison.Ordinal);
        if (invert)
        {
            equal = !equal;
        }

        return equal ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

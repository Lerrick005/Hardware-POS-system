using System.Globalization;
using System.Windows.Data;

namespace Pos.Desktop.Converters;

/// <summary>Converts integer cents to a shillings display string with thousand separators.</summary>
public sealed class CentsToShillingsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long cents)
        {
            return (cents / 100m).ToString("#,##0.##", CultureInfo.InvariantCulture);
        }
        if (value is int i)
        {
            return (i / 100m).ToString("#,##0.##", CultureInfo.InvariantCulture);
        }
        if (value is null)
        {
            return "";
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

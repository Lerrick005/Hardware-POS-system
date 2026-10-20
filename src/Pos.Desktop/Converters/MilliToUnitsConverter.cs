using System.Globalization;
using System.Windows.Data;

namespace Pos.Desktop.Converters;

/// <summary>Converts milli-units (1000 = 1) to a display string like "5" or "2.5".</summary>
public sealed class MilliToUnitsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long milli)
        {
            return (milli / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
        }
        if (value is int i)
        {
            return (i / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

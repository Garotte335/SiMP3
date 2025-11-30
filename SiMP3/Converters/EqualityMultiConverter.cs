using System.Globalization;
using Microsoft.Maui.Controls;

namespace SiMP3.Converters;

public class EqualityMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return false;

        var first = values[0]?.ToString();
        for (var i = 1; i < values.Length; i++)
        {
            if (!string.Equals(first, values[i]?.ToString(), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return !string.IsNullOrWhiteSpace(first);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
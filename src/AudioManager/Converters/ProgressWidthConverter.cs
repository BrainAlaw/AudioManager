using System;
using System.Globalization;
using System.Windows.Data;

namespace AudioManager.Converters;

public sealed class ProgressWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return 0d;
        }

        if (values[0] is not double totalWidth || values[1] is not double value)
        {
            return 0d;
        }

        var clampedValue = Math.Clamp(value, 0d, 1d);
        return totalWidth * clampedValue;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

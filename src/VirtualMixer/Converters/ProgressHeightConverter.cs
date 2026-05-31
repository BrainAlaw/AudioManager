using System;
using System.Globalization;
using System.Windows.Data;

namespace VirtualMixer.Converters;

public sealed class ProgressHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return 0d;
        }

        if (values[0] is not double totalHeight || values[1] is not double value)
        {
            return 0d;
        }

        var clampedValue = Math.Clamp(value, 0d, 1d);
        return totalHeight * clampedValue;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

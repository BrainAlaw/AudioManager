using System;
using System.Globalization;
using System.Windows.Data;
using WpfBinding = System.Windows.Data.Binding;

namespace AudioManager.Converters;

public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return false;
        }

        if (parameter is string parameterText && value.GetType().IsEnum)
        {
            return string.Equals(value.ToString(), parameterText, StringComparison.OrdinalIgnoreCase);
        }

        return Equals(value, parameter);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        WpfBinding.DoNothing;
}

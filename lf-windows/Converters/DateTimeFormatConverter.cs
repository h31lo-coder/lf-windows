using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LfWindows.Converters;

public class DateTimeFormatConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 3 && values[0] is DateTime date && values[1] is string fmtNew && values[2] is string fmtOld)
        {
            if (date.Year == DateTime.Now.Year)
            {
                return date.ToString(fmtNew, culture);
            }
            else
            {
                return date.ToString(fmtOld, culture);
            }
        }
        return values.FirstOrDefault()?.ToString();
    }
}

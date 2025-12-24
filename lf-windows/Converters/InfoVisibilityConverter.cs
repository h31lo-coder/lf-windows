using Avalonia.Data.Converters;
using LfWindows.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LfWindows.Converters;

public class InfoVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IEnumerable<InfoType> infoList && parameter is string typeStr && Enum.TryParse<InfoType>(typeStr, true, out var type))
        {
            return infoList.Contains(type);
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

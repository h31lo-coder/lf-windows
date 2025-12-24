using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LfWindows.Converters;

public class ColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorStr)
        {
            try
            {
                return Brush.Parse(colorStr);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

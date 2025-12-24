using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LfWindows.Converters;

public class ColorStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorStr && !string.IsNullOrEmpty(colorStr))
        {
            try
            {
                if (Color.TryParse(colorStr, out var color))
                {
                    return color;
                }
            }
            catch
            {
                // Fallback
            }
        }
        return Colors.White; // Default
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            return color.ToString();
        }
        return null;
    }
}

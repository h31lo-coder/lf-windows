using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LfWindows.Converters;

public class ActiveInactiveColorConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 3)
            return null;

        // Handle UnsetValue
        if (values[0] == Avalonia.AvaloniaProperty.UnsetValue || values[0] == null)
            return null;

        bool isActive = false;
        if (values[0] is bool b)
        {
            isActive = b;
        }

        var activeColor = values[1];
        var inactiveColor = values[2];

        var colorToUse = isActive ? activeColor : inactiveColor;

        // Always try to convert string to Brush if it looks like a color
        if (colorToUse is string colorStr)
        {
            if (Color.TryParse(colorStr, out var color))
            {
                return new SolidColorBrush(color);
            }
        }
        
        return colorToUse;
    }
}

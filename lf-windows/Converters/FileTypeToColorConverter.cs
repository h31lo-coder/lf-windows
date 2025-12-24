using Avalonia.Data.Converters;
using Avalonia.Media;
using LfWindows.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace LfWindows.Converters;

public class FileTypeToColorConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3) return Brushes.Gray;

        if (values[0] is FileType type && 
            values[1] is string fileColorStr && 
            values[2] is string dirColorStr)
        {
            string colorStr = type == FileType.Directory ? dirColorStr : fileColorStr;
            
            if (Color.TryParse(colorStr, out var color))
            {
                return new SolidColorBrush(color);
            }
        }

        return Brushes.Gray;
    }
}

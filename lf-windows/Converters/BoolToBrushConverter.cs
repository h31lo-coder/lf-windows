using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LfWindows.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public object? TrueBrush { get; set; }
    public object? FalseBrush { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? TrueBrush : FalseBrush;
        }
        return FalseBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

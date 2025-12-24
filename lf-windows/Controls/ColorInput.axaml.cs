using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace LfWindows.Controls;

public partial class ColorInput : UserControl
{
    public static readonly StyledProperty<string> ColorProperty =
        AvaloniaProperty.Register<ColorInput, string>(nameof(Color), defaultBindingMode: BindingMode.TwoWay);

    public string Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public ColorInput()
    {
        InitializeComponent();
    }
}
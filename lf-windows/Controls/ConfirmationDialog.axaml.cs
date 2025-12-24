using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LfWindows.Controls;

public partial class ConfirmationDialog : UserControl
{
    public ConfirmationDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

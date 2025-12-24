using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LfWindows.Models;
using System;

namespace LfWindows.Controls;

public partial class ExcelPreviewControl : UserControl
{
    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<ExcelPreviewControl, double>(nameof(ZoomLevel), 1.0);

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    public ExcelPreviewControl()
    {
        InitializeComponent();
        
        var zoomOutBtn = this.FindControl<Button>("ZoomOutButton");
        var zoomInBtn = this.FindControl<Button>("ZoomInButton");
        
        if (zoomOutBtn != null) zoomOutBtn.Click += OnZoomOutClick;
        if (zoomInBtn != null) zoomInBtn.Click += OnZoomInClick;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
    {
        ZoomLevel = Math.Max(0.5, ZoomLevel - 0.1);
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e)
    {
        ZoomLevel = Math.Min(2.0, ZoomLevel + 0.1);
    }
}

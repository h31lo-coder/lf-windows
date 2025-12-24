using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using LfWindows.Models;
using System;
using System.Threading.Tasks;

namespace LfWindows.Controls;

public partial class VideoPreviewControl : UserControl
{
    public static readonly StyledProperty<double> RotationAngleProperty =
        AvaloniaProperty.Register<VideoPreviewControl, double>(nameof(RotationAngle));

    public double RotationAngle
    {
        get => GetValue(RotationAngleProperty);
        set => SetValue(RotationAngleProperty, value);
    }

    public VideoPreviewControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;

        // Manually attach handlers with Tunnel strategy to ensure we catch events before Slider consumes them
        var slider = this.FindControl<Slider>("ProgressSlider");
        if (slider != null)
        {
            slider.AddHandler(PointerPressedEvent, OnSliderPointerPressed, RoutingStrategies.Tunnel);
            slider.AddHandler(PointerMovedEvent, OnSliderPointerMoved, RoutingStrategies.Tunnel);
            slider.AddHandler(PointerReleasedEvent, OnSliderPointerReleased, RoutingStrategies.Tunnel);
            slider.AddHandler(PointerCaptureLostEvent, OnSliderPointerCaptureLost, RoutingStrategies.Tunnel);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // In Avalonia 11, use VisualRoot to check attachment or just rely on the fact that if we are here, we might be attached.
        // But better to check if we are effectively attached.
        // Actually, IsAttachedToVisualTree is a property of Visual in some versions, or StyledElement.
        // Let's check StyledElement.IsAttachedToVisualTree (protected?) or Visual.IsAttachedToVisualTree (deprecated?)
        // In Avalonia 11, it's Visual.IsAttachedToVisualTree (bool).
        // Wait, the error says 'VideoPreviewControl' does not contain a definition for 'IsAttachedToVisualTree'.
        // It might be because it's a UserControl which inherits from StyledElement -> Visual.
        // Maybe I need to cast or use a different property.
        // Actually, let's just check if VisualRoot is not null.
        
        if (this.VisualRoot != null && DataContext is VideoPreviewModel viewModel)
        {
            viewModel.Initialize();
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is VideoPreviewModel viewModel)
        {
            viewModel.Initialize();
            // Subscribe to property changes to invalidate visual if needed
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is VideoPreviewModel viewModel)
        {
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
    }

    private bool _isDraggingLocal = false;

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoPreviewModel.VideoFrame))
        {
            // Force redraw
            this.InvalidateVisual();
            var image = this.FindControl<Image>("VideoImage");
            image?.InvalidateVisual();
        }
        else if (e.PropertyName == nameof(VideoPreviewModel.Progress))
        {
            if (!_isDraggingLocal && DataContext is VideoPreviewModel vm)
            {
                var slider = this.FindControl<Slider>("ProgressSlider");
                if (slider != null)
                {
                    // Only update if difference is significant to avoid infinite loops
                    if (Math.Abs(slider.Value - vm.Progress) > 0.001)
                    {
                        slider.Value = vm.Progress;
                    }
                }
            }
        }
    }

    private void OnPanelPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (DataContext is VideoPreviewModel viewModel)
        {
            viewModel.TogglePlayCommand.Execute(null);
        }
    }

    private void OnSliderPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (DataContext is VideoPreviewModel vm && sender is Slider slider)
        {
            var point = e.GetCurrentPoint(slider);
            if (point.Properties.IsLeftButtonPressed)
            {
                _isDraggingLocal = true;
                vm.IsDragging = true;
                e.Pointer.Capture(slider);
                e.Handled = true; // Prevent default Slider behavior

                UpdateSliderValue(slider, point.Position.X, vm);
            }
        }
    }

    private void OnSliderPointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (DataContext is VideoPreviewModel vm && sender is Slider slider && _isDraggingLocal)
        {
            var point = e.GetCurrentPoint(slider);
            if (point.Properties.IsLeftButtonPressed)
            {
                UpdateSliderValue(slider, point.Position.X, vm);
                e.Handled = true;
            }
        }
    }

    private void UpdateSliderValue(Slider slider, double relativeX, VideoPreviewModel vm)
    {
        var width = slider.Bounds.Width;
        if (width > 0)
        {
            var ratio = relativeX / width;
            ratio = Math.Max(0, Math.Min(1, ratio));
            var newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * ratio;
            
            if (Math.Abs(slider.Value - newValue) > 0.001)
            {
                slider.Value = newValue;
                vm.Seek(newValue);
            }
        }
    }

    private async void OnSliderPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (DataContext is VideoPreviewModel vm && sender is Slider slider)
        {
            if (_isDraggingLocal)
            {
                e.Pointer.Capture(null);
                e.Handled = true;
                
                vm.Seek(slider.Value);
                
                // Wait for seek to apply to prevent "jump back" glitch
                await Task.Delay(500);
                _isDraggingLocal = false;
                vm.IsDragging = false;
            }
        }
    }

    private async void OnSliderPointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        if (DataContext is VideoPreviewModel vm)
        {
            if (_isDraggingLocal)
            {
                // Wait for seek to apply to prevent "jump back" glitch
                await Task.Delay(500);
                _isDraggingLocal = false;
                vm.IsDragging = false;
                
                if (sender is Slider slider)
                {
                    vm.Seek(slider.Value);
                }
            }
        }
    }
}

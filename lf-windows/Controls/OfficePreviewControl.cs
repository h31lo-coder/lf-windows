using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LfWindows.Models;

namespace LfWindows.Controls;

public class OfficePreviewControl : UserControl
{
    private PreviewHandlerHost _host;
    private Image _thumbnailImage;
    private Grid _contentGrid;
    private Border _debugBorder;

    public OfficePreviewControl()
    {
        // 1. Create the Host
        _host = new PreviewHandlerHost();
        _host.Bind(PreviewHandlerHost.PathProperty, new Avalonia.Data.Binding("Path"));
        _host.Bind(PreviewHandlerHost.BackgroundBrushProperty, this.GetObservable(BackgroundBrushProperty));
        _host.IsActive = false; // Start hidden
        _host.HandlerLoaded += OnHandlerLoaded;

        // 2. Create the Thumbnail Image
        _thumbnailImage = new Image
        {
            Stretch = Stretch.Uniform, // Use Uniform to prevent distortion blur
            UseLayoutRounding = true   // Ensure pixel alignment
        };
        RenderOptions.SetBitmapInterpolationMode(_thumbnailImage, BitmapInterpolationMode.HighQuality);
        _thumbnailImage.Bind(Image.SourceProperty, new Avalonia.Data.Binding("Thumbnail"));

        // 3. Create a container for both
        _contentGrid = new Grid();
        // Enable LayoutRounding to ensure pixel-perfect alignment with Win32 host
        _contentGrid.UseLayoutRounding = true;
        _contentGrid.Children.Add(_host);
        _contentGrid.Children.Add(_thumbnailImage);

        // Wrap in container (formerly debug border)
        _debugBorder = new Border
        {
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Child = _contentGrid
        };
        
        // Configure Grid to center content
        _contentGrid.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        _contentGrid.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;

        // 4. Set Content
        Content = _debugBorder;
    }

    public static readonly StyledProperty<IBrush?> BackgroundBrushProperty =
        AvaloniaProperty.Register<OfficePreviewControl, IBrush?>(nameof(BackgroundBrush));

    public IBrush? BackgroundBrush
    {
        get => GetValue(BackgroundBrushProperty);
        set => SetValue(BackgroundBrushProperty, value);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        
        // Explicitly clean up the host when the control is removed
        if (_host != null)
        {
            _host.HandlerLoaded -= OnHandlerLoaded;
            // Force unload of preview handler
            // We can't access UnloadPreviewHandler directly as it is private, 
            // but setting Path to null triggers it via OnPropertyChanged
            _host.Path = null;
        }
    }

    // Override ArrangeOverride to implement "Uniform" layout logic manually.
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Arrange the Debug Border to fill the entire available space
        var availableRect = new Rect(0, 0, finalSize.Width, finalSize.Height);
        _debugBorder.Arrange(availableRect);

        // Now we need to size the _contentGrid inside the _debugBorder
        var model = DataContext as OfficePreviewModel;
        
        // For Word documents, we want to fill the available space (especially height)
        // For PPT, we want to maintain aspect ratio (usually 16:9)
        bool isWord = model?.Extension == ".docx" || model?.Extension == ".doc";
        
        if (isWord)
        {
            _contentGrid.Width = finalSize.Width;
            _contentGrid.Height = finalSize.Height;
        }
        else
        {
            Size targetSize = new Size(16, 9); // Default 16:9

            if (model?.Thumbnail != null)
            {
                targetSize = model.Thumbnail.Size;
            }

            if (targetSize.Width <= 0 || targetSize.Height <= 0) targetSize = new Size(16, 9);

            // Calculate the size that fits 'targetSize' into 'finalSize' while maintaining aspect ratio
            double scaleX = finalSize.Width / targetSize.Width;
            double scaleY = finalSize.Height / targetSize.Height;
            double scale = Math.Min(scaleX, scaleY);

            double width = targetSize.Width * scale;
            double height = targetSize.Height * scale;
            
            _contentGrid.Width = width;
            _contentGrid.Height = height;
        }

        return finalSize;
    }

    // Invalidate layout when DataContext changes (new file loaded)
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        // Listen for Thumbnail changes on the new ViewModel
        if (DataContext is OfficePreviewModel model)
        {
            model.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(OfficePreviewModel.Thumbnail))
                {
                    Dispatcher.UIThread.Post(InvalidateArrange);
                }
            };
        }
        
        InvalidateArrange();
    }

    private async void OnHandlerLoaded(object? sender, EventArgs e)
    {
        // Delay showing the preview to allow the ActiveX control to fully initialize and settle its layout.
        // This prevents the "flash and shift" effect where the control loads at a default size and then snaps to the correct size.
        // The user reported a visible shift/stutter, so we keep the thumbnail visible until this process is likely complete.
        await System.Threading.Tasks.Task.Delay(600);

        // Show Host
        _host.IsActive = true;
        
        // Wait a small amount of time to ensure the Host window is actually painted 
        // behind the thumbnail before we hide the thumbnail.
        // Since the Thumbnail is on top (Z-index), this is safe.
        await System.Threading.Tasks.Task.Delay(50);

        // Hide Thumbnail
        _thumbnailImage.IsVisible = false;
    }
}

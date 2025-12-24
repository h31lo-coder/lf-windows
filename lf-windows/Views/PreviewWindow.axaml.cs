using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Platform;
using LfWindows.Models;
using LfWindows.ViewModels;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace LfWindows.Views;

public partial class PreviewWindow : Window
{
    public object? PreviewContent
    {
        get => (DataContext as PreviewWindowViewModel)?.PreviewContent;
        set
        {
            if (DataContext is PreviewWindowViewModel vm)
            {
                vm.PreviewContent = value;
            }
        }
    }

    private Control? _contentContainer;
    private bool _isClosingOrClosed;

    public PreviewWindow()
    {
        InitializeComponent();
        var vm = new PreviewWindowViewModel();
        DataContext = vm;

        this.Closing += (_, _) => _isClosingOrClosed = true;
        
        // Debug Logging
        this.Opened += (s, e) =>
        {
            var transformControl = this.FindControl<LayoutTransformControl>("PreviewTransformControl");
            var contentControl = this.FindControl<ContentControl>("PreviewContentControl");
        };

        this.SizeChanged += (s, e) =>
        {
        };

        this.LayoutUpdated += (s, e) =>
        {
        };

        // Use Tunnel strategy for global shortcuts (Ctrl+T) to ensure they work even if a child control has focus
        this.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        
        this.KeyDown += OnKeyDown;

        // Sync Topmost manually when ViewModel property changes
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PreviewWindowViewModel.IsTopMost))
            {
                this.Topmost = vm.IsTopMost;
            }
            else if (e.PropertyName == nameof(PreviewWindowViewModel.RotationAngle))
            {
                // Swap Width and Height when rotation changes (assuming 90 degree steps)
                // This ensures the window adapts to the new content orientation
                Dispatcher.UIThread.Post(() =>
                {
                    if (_isClosingOrClosed || !IsVisible || TryGetPlatformHandle() == null) return;

                    Screen? screen = null;
                    try
                    {
                        screen = Screens.ScreenFromWindow(this);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    if (screen == null) return;

                    var scaling = RenderScaling;
                    var workingArea = screen.WorkingArea; // Physical pixels

                    // Max dimensions in logical pixels (80% of screen)
                    var maxW = (workingArea.Width / scaling) * 0.8;
                    var maxH = (workingArea.Height / scaling) * 0.8;

                    var currentW = Bounds.Width;
                    var currentH = Bounds.Height;

                    if (currentW <= 0 || currentH <= 0) return;

                    // Target aspect ratio (swapped because of 90-degree rotation)
                    // We want the new H/W to be proportional to the old W/H
                    var targetRatio = currentW / currentH;

                    // Calculate dimensions to maximize within 80% bounds
                    // Try fitting to Max Width first
                    var targetW = maxW;
                    var targetH = targetW * targetRatio;

                    // If Height exceeds Max Height, fit to Max Height instead
                    if (targetH > maxH)
                    {
                        targetH = maxH;
                        targetW = targetH / targetRatio;
                    }

                    // Calculate new position to keep center constant
                    var currentPos = Position;
                    
                    // Center in physical pixels
                    var centerX = currentPos.X + (currentW * scaling) / 2;
                    var centerY = currentPos.Y + (currentH * scaling) / 2;
                    
                    var newX = centerX - (targetW * scaling) / 2;
                    var newY = centerY - (targetH * scaling) / 2;
                    
                    Position = new PixelPoint((int)Math.Round(newX), (int)Math.Round(newY));
                    Width = targetW;
                    Height = targetH;
                });
            }
        };

        // Handle dragging on the main content container
        _contentContainer = this.FindControl<Control>("ContentContainer");
        if (_contentContainer != null)
        {
            _contentContainer.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    BeginMoveDrag(e);
                }
            };
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = DataContext as PreviewWindowViewModel;
        if (vm == null) return;

        // Close: Esc or Ctrl+W (Handle in Tunneling phase to ensure it works even if child has focus)
        if (e.Key == Key.Escape || (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.W))
        {
            Close();
            e.Handled = true;
            return;
        }

        // Toggle TopMost: Ctrl+T
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.T)
        {
            vm.IsTopMost = !vm.IsTopMost;
            e.Handled = true;
            return;
        }

        // Zoom In: = or +
        if (e.Key == Key.OemPlus || e.Key == Key.Add)
        {
            ScaleWindow(1.1);
            e.Handled = true;
            return;
        }

        // Zoom Out: - or Subtract
        if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
        {
            ScaleWindow(0.9);
            e.Handled = true;
            return;
        }

        // Reset: 0 or NumPad0
        if (e.Key == Key.D0 || e.Key == Key.NumPad0)
        {
            if (vm != null)
            {
                vm.RotationAngle = 0;
            }
            AdjustWindowSize(true);
            e.Handled = true;
            return;
        }
    }

    private bool _isAnimating;
    private System.Diagnostics.Stopwatch _animationStopwatch = new System.Diagnostics.Stopwatch();
    // Adjusted duration to 300ms for a balance of smoothness and responsiveness (~18 frames at 60fps)
    private const double AnimationDuration = 300.0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOCOPYBITS = 0x0100; // Required to prevent OS from copying old bits to wrong location

    private Rect _animStartRect;
    private Rect _animTargetRect;
    private Size _animTargetFinalSize;
    private PixelPoint _animTargetFinalPos;

    private void Log(string message)
    {
        // Logging disabled
    }
    private async void AnimateWindowTo(double targetWidth, double targetHeight, PixelPoint targetPosition)
    {
        Log($"AnimateWindowTo Start: TargetSize={targetWidth}x{targetHeight}, TargetPos={targetPosition}");
        _isAnimating = false;
        _animationStopwatch.Stop();

        // 1. Capture Start State
        double startW = this.Width;
        double startH = this.Height;
        PixelPoint startPos = this.Position;
        double scaling = this.RenderScaling;

        Log($"Start State: Size={startW}x{startH}, Pos={startPos}, Scaling={scaling}");

        if (double.IsNaN(startW) || double.IsNaN(startH) || scaling <= 0)
        {
             SetWindowSizeAndPosition(targetWidth, targetHeight, targetPosition);
             return;
        }

        // Convert everything to DIPs for calculation
        double startX = startPos.X / scaling;
        double startY = startPos.Y / scaling;
        double targetX = targetPosition.X / scaling;
        double targetY = targetPosition.Y / scaling;

        // Define Rects
        Rect startRect = new Rect(startX, startY, startW, startH);
        Rect targetRect = new Rect(targetX, targetY, targetWidth, targetHeight);
        
        // 2. Setup Animation State
        _animStartRect = startRect;
        _animTargetRect = targetRect;
        _animTargetFinalSize = new Size(targetWidth, targetHeight);
        _animTargetFinalPos = targetPosition;

        // 3. Start Loop
        _isAnimating = true;
        _animationStopwatch.Restart();
        this.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan timestamp)
    {
        if (!_isAnimating) return;

        double elapsed = _animationStopwatch.Elapsed.TotalMilliseconds;
        double t = elapsed / AnimationDuration;

        if (t >= 1.0)
        {
            Log("Animation Finished. Snapping to final state.");
            // Finish
            _isAnimating = false;
            _animationStopwatch.Stop();
            
            // Snap to final exact state using SetWindowPos for atomicity
            if (OperatingSystem.IsWindows())
            {
                 var handle = this.TryGetPlatformHandle()?.Handle;
                 if (handle.HasValue)
                 {
                     var scaling = this.RenderScaling;
                     SetWindowPos(handle.Value, IntPtr.Zero, 
                         _animTargetFinalPos.X, _animTargetFinalPos.Y, 
                         (int)(_animTargetFinalSize.Width * scaling), (int)(_animTargetFinalSize.Height * scaling), 
                         SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOCOPYBITS);
                 }
            }

            this.Position = _animTargetFinalPos;
            this.Width = _animTargetFinalSize.Width;
            this.Height = _animTargetFinalSize.Height;
            return;
        }

        // EaseOutCubic
        t--;
        double ease = (t * t * t + 1);

        // Interpolate Window Rect directly
        double curX = _animStartRect.X + (_animTargetRect.X - _animStartRect.X) * ease;
        double curY = _animStartRect.Y + (_animTargetRect.Y - _animStartRect.Y) * ease;
        double curW = _animStartRect.Width + (_animTargetRect.Width - _animStartRect.Width) * ease;
        double curH = _animStartRect.Height + (_animTargetRect.Height - _animStartRect.Height) * ease;

        // Apply to Window
        if (OperatingSystem.IsWindows())
        {
             var handle = this.TryGetPlatformHandle()?.Handle;
             if (handle.HasValue)
             {
                 var scaling = this.RenderScaling;
                 SetWindowPos(handle.Value, IntPtr.Zero, 
                     (int)(curX * scaling), (int)(curY * scaling), 
                     (int)(curW * scaling), (int)(curH * scaling), 
                     SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOCOPYBITS);
             }
        }
        else
        {
            this.Position = new PixelPoint((int)(curX * this.RenderScaling), (int)(curY * this.RenderScaling));
            this.Width = curW;
            this.Height = curH;
        }

        this.RequestAnimationFrame(OnAnimationFrame);
    }

    private void UpdateContentScale(double currentW, double currentH)
    {
        // Deprecated
    }

    private void SetWindowSizeAndPosition(double width, double height, PixelPoint position)
    {
        if (OperatingSystem.IsWindows())
        {
             var handle = this.TryGetPlatformHandle()?.Handle;
             if (handle.HasValue)
             {
                 // Convert DIPs to Physical for SetWindowPos
                 var scaling = this.RenderScaling;
                 int w = (int)(width * scaling);
                 int h = (int)(height * scaling);
                 
                 SetWindowPos(handle.Value, IntPtr.Zero, 
                     position.X, position.Y, 
                     w, h, 
                     SWP_NOZORDER | SWP_NOACTIVATE);
                 
                 // Continue to update Avalonia properties below to ensure layout sync
             }
        }

        this.Width = width;
        this.Height = height;
        this.Position = position;
    }

    private void ScaleWindow(double factor)
    {
        var scaling = this.RenderScaling;
        
        double currentWidth = this.Width;
        double currentHeight = this.Height;
        
        if (double.IsNaN(currentWidth) || double.IsNaN(currentHeight)) return;

        double newWidth = currentWidth * factor;
        double newHeight = currentHeight * factor;
        
        // Min size check (e.g. 100x100)
        if (newWidth < 100 || newHeight < 100) return;

        // Calculate center in pixels based on current position and size
        var currentRect = new PixelRect(this.Position, PixelSize.FromSize(new Size(currentWidth, currentHeight), scaling));
        var centerX = currentRect.X + currentRect.Width / 2.0;
        var centerY = currentRect.Y + currentRect.Height / 2.0;
        
        // Calculate new position to keep center
        var newWidthPixels = newWidth * scaling;
        var newHeightPixels = newHeight * scaling;
        
        var newX = centerX - newWidthPixels / 2.0;
        var newY = centerY - newHeightPixels / 2.0;
        
        AnimateWindowTo(newWidth, newHeight, new PixelPoint((int)newX, (int)newY));
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = DataContext as PreviewWindowViewModel;
        if (vm == null) return;

        // Close logic moved to OnPreviewKeyDown

        // PDF/Office Page Navigation
        if (vm.IsPdf)
        {
            if (e.Key == Key.J || e.Key == Key.Right || e.Key == Key.PageDown)
            {
                vm.NextPageCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.K || e.Key == Key.Left || e.Key == Key.PageUp)
            {
                vm.PrevPageCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        // Rotate: h, l (Only if Rotatable - Image/Video)
        if (vm.IsRotatable)
        {
            if (e.Key == Key.H)
            {
                vm.RotationAngle -= 90;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.L)
            {
                vm.RotationAngle += 90;
                e.Handled = true;
                return;
            }
        }

        // Scroll: h, j, k, l (Only if not PDF or handled above)
        if (e.Key == Key.J || e.Key == Key.K || e.Key == Key.H || e.Key == Key.L)
        {
            HandleScrolling(e.Key);
            e.Handled = true;
        }
    }

    private void HandleScrolling(Key key)
    {
        var scrollViewer = this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer != null)
        {
            double offset = 50;
            if (key == Key.J) scrollViewer.Offset = new Vector(scrollViewer.Offset.X, scrollViewer.Offset.Y + offset);
            if (key == Key.K) scrollViewer.Offset = new Vector(scrollViewer.Offset.X, scrollViewer.Offset.Y - offset);
            if (key == Key.H) scrollViewer.Offset = new Vector(scrollViewer.Offset.X - offset, scrollViewer.Offset.Y);
            if (key == Key.L) scrollViewer.Offset = new Vector(scrollViewer.Offset.X + offset, scrollViewer.Offset.Y);
        }
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        var vm = DataContext as PreviewWindowViewModel;
        if (vm?.PreviewContent is VideoPreviewModel videoVm)
        {
             videoVm.PropertyChanged += VideoVm_PropertyChanged;
        }

        AdjustWindowSize(false);
        this.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        var vm = DataContext as PreviewWindowViewModel;
        if (vm?.PreviewContent is VideoPreviewModel videoVm)
        {
             videoVm.PropertyChanged -= VideoVm_PropertyChanged;
        }

        // Ensure preview content is disposed immediately when window closes
        if (vm?.PreviewContent is IDisposable disposable)
        {
            try { disposable.Dispose(); } catch { }
            vm.PreviewContent = null;
        }

        base.OnClosed(e);
    }

    private void VideoVm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoPreviewModel.VideoWidth) || e.PropertyName == nameof(VideoPreviewModel.VideoHeight))
        {
             Avalonia.Threading.Dispatcher.UIThread.Post(() => AdjustWindowSize(true));
        }
    }

    private void AdjustWindowSize(bool animate = false)
    {
        var screen = Screens.Primary ?? Screens.All[0];
        var scaling = this.RenderScaling;
        
        // WorkingArea is in physical pixels. Convert to DIPs for size calculations.
        var screenWidthDips = screen.WorkingArea.Width / scaling;
        var screenHeightDips = screen.WorkingArea.Height / scaling;
        
        double maxW = screenWidthDips * 0.8;
        double maxH = screenHeightDips * 0.8;

        double targetWidth = maxW;
        double targetHeight = maxH;

        var vm = DataContext as PreviewWindowViewModel;
        if (vm?.PreviewContent is ImagePreviewModel imgModel && imgModel.Image is Bitmap bitmap)
        {
            // bitmap.Size is in DIPs (96 DPI)
            double w = bitmap.Size.Width;
            double h = bitmap.Size.Height;
            
            // No extra padding needed for borderless window
            // w += 0; 
            // h += 0; 

            if (w > maxW || h > maxH)
            {
                double ratio = Math.Min(maxW / w, maxH / h);
                w *= ratio;
                h *= ratio;
            }
            
            targetWidth = w;
            targetHeight = h;
        }
        else if (vm?.PreviewContent is CodePreviewModel)
        {
             targetWidth = screenWidthDips * 0.6;
             targetHeight = screenHeightDips * 0.7;
        }
        else if (vm?.PreviewContent is VideoPreviewModel videoVm)
        {
             if (videoVm.VideoWidth > 0 && videoVm.VideoHeight > 0)
             {
                 // Convert video pixels to DIPs
                 double w = videoVm.VideoWidth / scaling;
                 double h = videoVm.VideoHeight / scaling;
                 
                 if (w > maxW || h > maxH)
                 {
                     double ratio = Math.Min(maxW / w, maxH / h);
                     w *= ratio;
                     h *= ratio;
                 }
                 targetWidth = w;
                 targetHeight = h;
             }
             else
             {
                 targetWidth = screenWidthDips * 0.6;
                 targetHeight = screenHeightDips * 0.6;
             }
        }
        else if (vm?.IsPdf == true && vm.CurrentPdfPage != null)
        {
            // PDF/Office Sizing Logic
            var page = vm.CurrentPdfPage;
            // Use default dimensions if not loaded yet (PdfPageModel constructor sets these)
            double pageW = page.Width;
            double pageH = page.Height;

            if (pageW > 0 && pageH > 0)
            {
                double ratio = pageW / pageH;
                
                // Determine if it's likely a slide (Landscape) or document (Portrait)
                // PPTX usually > 1.3 (4:3) or > 1.7 (16:9)
                // A4 Portrait is ~0.7
                
                // Target Height: 80% of screen height
                double targetH = screenHeightDips * 0.8;
                double targetW = targetH * ratio;

                // If width exceeds 80% of screen width, constrain by width instead
                if (targetW > screenWidthDips * 0.8)
                {
                    targetW = screenWidthDips * 0.8;
                    targetH = targetW / ratio;
                }

                // No extra padding needed for borderless window
                targetWidth = targetW;
                targetHeight = targetH;
            }
            else
            {
                // Fallback
                targetWidth = screenWidthDips * 0.5;
                targetHeight = screenHeightDips * 0.8;
            }
        }

        // Position is in Physical Pixels
        var targetWidthPhysical = targetWidth * scaling;
        var targetHeightPhysical = targetHeight * scaling;

        var x = screen.WorkingArea.X + (screen.WorkingArea.Width - targetWidthPhysical) / 2;
        var y = screen.WorkingArea.Y + (screen.WorkingArea.Height - targetHeightPhysical) / 2;
        
        if (animate)
        {
            AnimateWindowTo(targetWidth, targetHeight, new PixelPoint((int)x, (int)y));
        }
        else
        {
            _isAnimating = false;
            _animationStopwatch.Stop();

            this.Width = targetWidth;
            this.Height = targetHeight;
            this.Position = new PixelPoint((int)x, (int)y);
        }
    }
}

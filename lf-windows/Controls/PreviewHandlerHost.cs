using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using LfWindows.Interop;

namespace LfWindows.Controls;

public class PreviewHandlerHost : NativeControlHost
{
    public static readonly StyledProperty<string?> PathProperty =
        AvaloniaProperty.Register<PreviewHandlerHost, string?>(nameof(Path));

    public string? Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    public static readonly StyledProperty<IBrush?> BackgroundBrushProperty =
        AvaloniaProperty.Register<PreviewHandlerHost, IBrush?>(nameof(BackgroundBrush));

    public IBrush? BackgroundBrush
    {
        get => GetValue(BackgroundBrushProperty);
        set => SetValue(BackgroundBrushProperty, value);
    }

    public static readonly StyledProperty<Size> ThumbnailSizeProperty =
        AvaloniaProperty.Register<PreviewHandlerHost, Size>(nameof(ThumbnailSize));

    public Size ThumbnailSize
    {
        get => GetValue(ThumbnailSizeProperty);
        set => SetValue(ThumbnailSizeProperty, value);
    }

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<PreviewHandlerHost, bool>(nameof(IsActive), defaultValue: true);

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public event EventHandler? HandlerLoaded;

    private IntPtr _hwndHost;
    private IntPtr _hwndClip;
    private IntPtr _hwndContent;
    private object? _previewHandler; // Keep reference to prevent GC
    private IPreviewHandler? _currentHandler;
    private IntPtr _hBackgroundBrush = IntPtr.Zero;

    public PreviewHandlerHost()
    {
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PathProperty)
        {
            if (_hwndHost != IntPtr.Zero)
            {
                UnloadPreviewHandler();
                Avalonia.Threading.Dispatcher.UIThread.Post(LoadPreviewHandler, Avalonia.Threading.DispatcherPriority.Background);
            }
        }
        else if (change.Property == BackgroundBrushProperty)
        {
            UpdateWindowBackground();
        }
        else if (change.Property == IsActiveProperty)
        {
            UpdateWindowVisibility();
        }
        else if (change.Property == ThumbnailSizeProperty)
        {
            UpdatePreviewLayout();
        }
    }

    private void UpdateWindowVisibility()
    {
        if (_hwndHost != IntPtr.Zero)
        {
            bool show = IsActive;
            
            if (_hwndHost != IntPtr.Zero)
            {
                // Always keep the window "Shown" so it processes paint messages
                User32.ShowWindow(_hwndHost, User32.SW_SHOW);

                // Control visibility using Alpha transparency
                // 0 = Fully Transparent (Invisible), 255 = Fully Opaque (Visible)
                byte alpha = show ? (byte)255 : (byte)0;
                User32.SetLayeredWindowAttributes(_hwndHost, 0, alpha, User32.LWA_ALPHA);
                
                // Ensure children layout/visibility is updated when showing
                if (show)
                {
                    UpdatePreviewLayout();
                }
            }
        }
    }

    private void UpdateWindowBackground()
    {
        if (BackgroundBrush is ISolidColorBrush solidBrush)
        {
            var color = solidBrush.Color;
            // COLORREF is 0x00BBGGRR
            uint colorRef = (uint)((color.B << 16) | (color.G << 8) | color.R);

            // 1. Try to set background on the Preview Handler itself (if supported)
            if (_previewHandler is IPreviewHandlerVisuals visuals)
            {
                try
                {
                    visuals.SetBackgroundColor(colorRef);
                }
                catch { /* Not supported or failed */ }
            }

            // 2. Set background on the host window
            if (_hwndHost != IntPtr.Zero)
            {
                if (_hBackgroundBrush != IntPtr.Zero)
                {
                    User32.DeleteObject(_hBackgroundBrush);
                    _hBackgroundBrush = IntPtr.Zero;
                }

                _hBackgroundBrush = User32.CreateSolidBrush(colorRef);
                
                if (_hBackgroundBrush != IntPtr.Zero)
                {
                    User32.SetClassLongPtr(_hwndHost, User32.GCLP_HBRBACKGROUND, _hBackgroundBrush);
                    if (_hwndClip != IntPtr.Zero)
                        User32.SetClassLongPtr(_hwndClip, User32.GCLP_HBRBACKGROUND, _hBackgroundBrush);
                    
                    // Force repaint
                    User32.InvalidateRect(_hwndHost, IntPtr.Zero, true);
                    User32.UpdateWindow(_hwndHost);
                }
            }
        }
    }

    private static bool _classRegistered = false;
    private static readonly string _className = "LfPreviewHost";
    private static User32.WndProc? _wndProcDelegate; // Keep reference to prevent GC

    private static void EnsureWindowClass()
    {
        if (_classRegistered) return;

        _wndProcDelegate = User32.DefWindowProc;

        var wc = new User32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<User32.WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = User32.GetModuleHandle(null!),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero, // Will set later
            lpszMenuName = null!,
            lpszClassName = _className,
            hIconSm = IntPtr.Zero
        };

        if (User32.RegisterClassEx(ref wc) == 0)
        {
            // If failed, maybe already registered?
            int err = Marshal.GetLastWin32Error();
            if (err != 1410) // ERROR_CLASS_ALREADY_EXISTS
            {
                // Log or ignore?
            }
        }
        _classRegistered = true;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        EnsureWindowClass();

        // 1. Create the main host window (fills the Avalonia control)
        // Note: We respect IsActive for initial visibility
        uint style = User32.WS_CHILD | User32.WS_CLIPCHILDREN;
        // Always make it visible so it can paint, but we control opacity via Layered attributes
        style |= User32.WS_VISIBLE;

        // Use WS_EX_LAYERED to control opacity (0 = invisible, 255 = visible)
        // This allows the window to be "Visible" to the OS (so it paints) but invisible to the user.
        _hwndHost = User32.CreateWindowEx(
            User32.WS_EX_LAYERED, _className, "Host",
            style,
            0, 0, 0, 0,
            parent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_hwndHost != IntPtr.Zero)
        {
            // Initialize as transparent (Alpha = 0)
            User32.SetLayeredWindowAttributes(_hwndHost, 0, 0, User32.LWA_ALPHA);
        }

        if (_hwndHost == IntPtr.Zero)
        {
            throw new Exception($"Failed to create host window. Error: {Marshal.GetLastWin32Error()}");
        }

        // 2. Create the clipping window (centered, 16:9)
        _hwndClip = User32.CreateWindowEx(
            0, _className, "Clip",
            User32.WS_CHILD | User32.WS_VISIBLE | User32.WS_CLIPCHILDREN,
            0, 0, 0, 0,
            _hwndHost,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        // 3. Create the content window (wider than clip to hide scrollbar)
        _hwndContent = User32.CreateWindowEx(
            0, _className, "Content",
            User32.WS_CHILD | User32.WS_VISIBLE | User32.WS_CLIPCHILDREN,
            0, 0, 0, 0,
            _hwndClip,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        UpdateWindowBackground();
        UpdateWindowVisibility(); // Ensure visibility state is correct

        // Load handler asynchronously to avoid blocking UI
        Avalonia.Threading.Dispatcher.UIThread.Post(LoadPreviewHandler, Avalonia.Threading.DispatcherPriority.Background);

        // Force visibility update to ensure it matches IsActive
        UpdateWindowVisibility();

        return new PlatformHandle(_hwndHost, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        UnloadPreviewHandler();
        
        if (_hwndContent != IntPtr.Zero)
        {
            User32.DestroyWindow(_hwndContent);
            _hwndContent = IntPtr.Zero;
        }
        if (_hwndClip != IntPtr.Zero)
        {
            User32.DestroyWindow(_hwndClip);
            _hwndClip = IntPtr.Zero;
        }
        if (_hwndHost != IntPtr.Zero)
        {
            User32.DestroyWindow(_hwndHost);
            _hwndHost = IntPtr.Zero;
        }

        if (_hBackgroundBrush != IntPtr.Zero)
        {
            User32.DeleteObject(_hBackgroundBrush);
            _hBackgroundBrush = IntPtr.Zero;
        }

        base.DestroyNativeControlCore(control);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdatePreviewLayout();
    }

    private async void LoadPreviewHandler()
    {
        string? targetPath = Path;
        if (string.IsNullOrEmpty(targetPath)) return;

        // Debounce/Delay to prevent blocking UI during rapid navigation
        // and allow UI to render first
        await System.Threading.Tasks.Task.Delay(50);

        // Check if we are still attached and valid
        if (_hwndHost == IntPtr.Zero) return;
        
        // If path changed while we were waiting, abort.
        if (Path != targetPath) return;

        try
        {
            Guid clsid = Guid.Empty;
            
            // Run Registry lookup on background thread to avoid blocking UI
            await System.Threading.Tasks.Task.Run(() => 
            {
                clsid = GetPreviewHandlerCLSID(targetPath);
            });

            if (Path != targetPath) return; // Check again
            if (clsid == Guid.Empty) return;
            if (_hwndHost == IntPtr.Zero) return; // Check again after await

            Type? type = Type.GetTypeFromCLSID(clsid);
            if (type == null) return;

            // Ensure any previous handler is unloaded before creating a new one
            // This is a safety check, normally UnloadPreviewHandler is called on PropertyChanged
            UnloadPreviewHandler();

            // Activator.CreateInstance can be slow for COM objects, but must run on UI thread (STA)
            // We can't easily move this to background thread.
            _previewHandler = Activator.CreateInstance(type);
            _currentHandler = _previewHandler as IPreviewHandler;

            if (_currentHandler == null) return;

            // Initialize
            bool initialized = false;
            
            // Initialization might involve I/O, but usually fast for files
            if (_previewHandler is IInitializeWithFile initFile)
            {
                try 
                {
                    initFile.Initialize(targetPath, 0);
                    initialized = true;
                }
                catch { /* Try next method */ }
            }
            
            if (!initialized && _previewHandler is IInitializeWithStream initStream)
            {
                try
                {
                    // Create stream on background? No, SHCreateStreamOnFileEx is fast enough usually
                    Shell32.SHCreateStreamOnFileEx(targetPath, 0, 0x80, false, null, out var stream);
                    if (stream != null)
                    {
                        initStream.Initialize(stream, 0);
                        initialized = true;
                    }
                }
                catch { /* Log */ }
            }

            if (!initialized)
            {
                // If neither worked, we can't preview
                UnloadPreviewHandler();
                return;
            }

            // Apply background color if possible
            UpdateWindowBackground();

            // Ensure layout is updated before setting window
            bool layoutReady = false;
            Avalonia.Threading.Dispatcher.UIThread.Invoke(() => 
            {
                layoutReady = UpdatePreviewLayout();
            });

            if (!layoutReady)
            {
                // Layout not ready (size is 0), defer DoPreview
                _pendingPreview = true;
                // Console.WriteLine("[PreviewHandlerHost] Layout not ready, deferring DoPreview.");
                return;
            }

            // Setup Window
            // We use _hwndContent for the handler
            User32.GetClientRect(_hwndContent, out var rect);
            _currentHandler.SetWindow(_hwndContent, ref rect);
            _currentHandler.DoPreview();
            // Console.WriteLine("[PreviewHandlerHost] DoPreview called.");

            // Notify that handler is loaded and ready
            Avalonia.Threading.Dispatcher.UIThread.Post(() => 
            {
                HandlerLoaded?.Invoke(this, EventArgs.Empty);
                // Force a layout update after handler is loaded to ensure correct sizing
                UpdatePreviewLayout();
            });

            // Try to set background color on child windows
            Avalonia.Threading.Dispatcher.UIThread.Post(async () => 
            {
                await System.Threading.Tasks.Task.Delay(500);
                if (_hwndHost != IntPtr.Zero)
                {
                    User32.EnumChildWindows(_hwndHost, (hwnd, lParam) =>
                    {
                        // 1. Try to set background color on child windows
                        if (_hBackgroundBrush != IntPtr.Zero)
                        {
                            User32.SetClassLongPtr(hwnd, User32.GCLP_HBRBACKGROUND, _hBackgroundBrush);
                            User32.InvalidateRect(hwnd, IntPtr.Zero, true);
                            User32.UpdateWindow(hwnd);
                        }
                        return true;
                    }, IntPtr.Zero);

                    // Notify that handler is loaded and ready
                    // HandlerLoaded?.Invoke(this, EventArgs.Empty); // Moved to StartPreview/DoPreview
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load preview handler: {ex.Message}");
            UnloadPreviewHandler();
        }
    }

    private bool _pendingPreview = false;

    private bool UpdatePreviewLayout()
    {
        if (_hwndHost != IntPtr.Zero && _hwndClip != IntPtr.Zero && _hwndContent != IntPtr.Zero)
        {
            User32.GetClientRect(_hwndHost, out var clientRect);

            if (clientRect.right - clientRect.left <= 0 || clientRect.bottom - clientRect.top <= 0) return false;

            // The Host is now already sized correctly by the parent container (OfficePreviewControl).
            // So we just fill the Host with the Clip window.
            int clipWidth = clientRect.right - clientRect.left;
            int clipHeight = clientRect.bottom - clientRect.top;
            
            // Always show children, visibility is controlled by Parent Host Alpha
            uint swpFlags = User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_SHOWWINDOW;

            User32.SetWindowPos(_hwndClip, IntPtr.Zero, 
                0, 0, 
                clipWidth, clipHeight, 
                swpFlags);

            // 2. Position Content Window inside Clip Window
            
            int scrollbarWidth = User32.GetSystemMetrics(User32.SM_CXVSCROLL);
            if (scrollbarWidth <= 0) scrollbarWidth = 20;

            int contentWidth = clipWidth;
            int contentHeight = clipHeight;
            int contentX = 0;
            int contentY = 0;

            // Identify File Type
            string ext = "";
            if (!string.IsNullOrEmpty(Path))
            {
                ext = System.IO.Path.GetExtension(Path).ToLower();
            }

            bool isWord = ext == ".docx" || ext == ".doc";
            bool isExcel = ext == ".xlsx" || ext == ".xls" || ext == ".csv";
            bool isPowerPoint = ext == ".pptx" || ext == ".ppt";

            // --- Strategy per File Type ---

            if (isPowerPoint)
            {
                // PowerPoint:
                // Extend width to hide the right-side scrollbar.
                // Keep height as clipHeight to maintain aspect ratio and avoid black bars/vertical scrolling issues.
                contentWidth = clipWidth + scrollbarWidth;
                contentHeight = clipHeight;
            }
            else if (isExcel)
            {
                // Excel:
                // Standard fit. Excel handles its own scrolling.
                contentWidth = clipWidth;
                contentHeight = clipHeight;
            }
            else if (isWord)
            {
                // Word:
                // Strategy: "Fit Width" with Zoom.
                // We set the window size exactly to the clip size.
                // Then we use Zoom to make the content fit.
                
                contentWidth = clipWidth;
                contentHeight = clipHeight;
                contentX = 0;
                contentY = 0;
            }
            else
            {
                // Default (PDF, Text, etc.)
                contentWidth = clipWidth;
                contentHeight = clipHeight;
            }

            // Ensure we don't have negative dimensions
            if (contentWidth <= 0) contentWidth = 1;
            if (contentHeight <= 0) contentHeight = 1;

            User32.SetWindowPos(_hwndContent, IntPtr.Zero, 
                contentX, contentY, 
                contentWidth, contentHeight, 
                swpFlags);

            // 3. Apply Zoom for Word documents to fit width
            if (isWord)
            {
                // Calculate required zoom based on ACTUAL page width from the file.
                double scaling = 1.0;
                if (Avalonia.VisualTree.VisualExtensions.GetVisualRoot(this) is Avalonia.Controls.TopLevel tl)
                {
                    scaling = tl.RenderScaling;
                }

                // 1. Get Page Width from Docx (in Pixels)
                double? rawPageWidth = GetDocxPageWidthInPixels(Path, scaling);
                double pageWidthPixels = rawPageWidth ?? (816.0 * scaling); // Default to 8.5 inches if parsing fails

                // 2. Calculate Zoom
                // We want the Page to fit exactly in the 'clipWidth' (the visible area).
                // Zoom = (VisibleWidth / PageWidth) * 100
                
                // Note: We use clipWidth, NOT contentWidth, because contentWidth includes hidden areas.
                // FIX: Subtract a safety margin (e.g. 24px) to account for Word's page shadow and borders.
                // This prevents the content from being slightly wider than the view, which causes horizontal scrollbars and cropping.
                double safeWidth = clipWidth - (24 * scaling);
                if (safeWidth < 100) safeWidth = 100;

                int zoom = (int)((safeWidth / pageWidthPixels) * 100);
                
                // Clamp zoom to reasonable limits (10% to 500%)
                if (zoom < 10) zoom = 10;
                if (zoom > 500) zoom = 500;

                SetZoom(zoom);
            }

            // 3. Update Handler Rect
            if (_currentHandler != null)
            {
                var contentRect = new Interop.RECT(0, 0, contentWidth, contentHeight);
                _currentHandler.SetRect(ref contentRect);
            }
            
            // Force repaint
            User32.InvalidateRect(_hwndHost, IntPtr.Zero, true);

            // Check if we have a pending preview start
            if (_pendingPreview && _currentHandler != null)
            {
                _pendingPreview = false;
                StartPreview(contentWidth, contentHeight);
            }

            return true;
        }
        return false;
    }

    private void StartPreview(int width, int height)
    {
        if (_currentHandler == null) return;

        try
        {
            var rect = new Interop.RECT(0, 0, width, height);
            _currentHandler.SetWindow(_hwndContent, ref rect);
            _currentHandler.DoPreview();
            // Console.WriteLine("[PreviewHandlerHost] DoPreview called (Delayed).");
            
            // Notify that handler is loaded and ready
            HandlerLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start preview: {ex.Message}");
        }
    }

    private void UnloadPreviewHandler()
    {
        bool unloaded = false;
        if (_currentHandler != null)
        {
            try { _currentHandler.Unload(); } catch { }
            try { Marshal.FinalReleaseComObject(_currentHandler); } catch { }
            _currentHandler = null;
            unloaded = true;
        }
        if (_previewHandler != null)
        {
            try { Marshal.FinalReleaseComObject(_previewHandler); } catch { }
            _previewHandler = null;
            unloaded = true;
        }

        if (unloaded)
        {
            // Force GC to clean up RCWs and release unmanaged memory held by COM objects
            // This is often necessary for Office Interop to prevent memory leaks.
            // However, running this synchronously on UI thread causes massive lag.
            // We schedule it on a background thread or low priority.
            System.Threading.Tasks.Task.Run(() => 
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });
        }
    }

    public static Guid GetPreviewHandlerCLSID(string filename)
    {
        string ext = System.IO.Path.GetExtension(filename);
        if (string.IsNullOrEmpty(ext)) return Guid.Empty;

        Guid previewHandlerGuid = new Guid("8895b1c6-b41f-4c1c-a562-0d564250836f");

        try
        {
            // 1. Check HKCR\.ext\shellex\{GUID}
            if (TryGetClsidFromKey(Microsoft.Win32.Registry.ClassesRoot, $"{ext}\\shellex\\{{8895b1c6-b41f-4c1c-a562-0d564250836f}}", out var clsid))
            {
                return clsid;
            }

            // 2. Check ProgID
            // Read default value of HKCR\.ext
            using var extKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(ext);
            if (extKey != null)
            {
                var progId = extKey.GetValue(null) as string;
                if (!string.IsNullOrEmpty(progId))
                {
                    // Check HKCR\ProgID\shellex\{GUID}
                    if (TryGetClsidFromKey(Microsoft.Win32.Registry.ClassesRoot, $"{progId}\\shellex\\{{8895b1c6-b41f-4c1c-a562-0d564250836f}}", out clsid))
                    {
                        return clsid;
                    }
                }
            }
        }
        catch { }

        return Guid.Empty;
    }

    private double? GetDocxPageWidthInPixels(string? path, double scaling)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

        try
        {
            // Only try for .docx
            string ext = System.IO.Path.GetExtension(path).ToLower();
            if (ext != ".docx") return null;

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var entry = archive.GetEntry("word/document.xml");
                if (entry != null)
                {
                    using (var stream = entry.Open())
                    using (var reader = XmlReader.Create(stream))
                    {
                        while (reader.Read())
                        {
                            // Look for <w:pgSz ... w:w="11906" ... />
                            // Namespace is usually http://schemas.openxmlformats.org/wordprocessingml/2006/main
                            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "pgSz")
                            {
                                string? w = reader.GetAttribute("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
                                if (w == null) w = reader.GetAttribute("w"); // Try without namespace
                                
                                if (long.TryParse(w, out long twips))
                                {
                                    // Twips to Pixels
                                    // 1440 twips = 1 inch
                                    // 96 pixels = 1 inch (standard DPI)
                                    // Formula: Pixels = (Twips / 1440) * 96 * Scaling
                                    return (twips / 1440.0) * 96.0 * scaling;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse docx width: {ex.Message}");
        }
        return null;
    }

    private void SetZoom(int zoomPercent)
    {
        // Note: IOleCommandTarget is defined inside User32 class in Interop
        if (_previewHandler is User32.IOleCommandTarget cmdTarget)
        {
            try
            {
                // OLECMDID_OPTICAL_ZOOM = 63
                // Input is a VT_I4 (int) wrapped in a Variant
                
                // VARIANT size: 16 bytes on 32-bit, 24 bytes on 64-bit
                int variantSize = IntPtr.Size == 8 ? 24 : 16;
                IntPtr pIn = Marshal.AllocCoTaskMem(variantSize); 
                try
                {
                    // Clear memory first
                    byte[] empty = new byte[variantSize];
                    Marshal.Copy(empty, 0, pIn, variantSize);

                    // Create Variant from int using Marshal helper
                    Marshal.GetNativeVariantForObject(zoomPercent, pIn);

                    // Use IntPtr.Zero for NULL GUID (Standard Group)
                    IntPtr pGroup = IntPtr.Zero;
                    
                    // Try OLECMDID_OPTICAL_ZOOM (63) first as it is the correct one for "Zoom Level"
                    int hr = cmdTarget.Exec(pGroup, User32.OLECMDID_OPTICAL_ZOOM, User32.OLECMDEXECOPT_DONTPROMPTUSER, pIn, IntPtr.Zero);
                    
                    // If 63 fails, try ZOOM (19)
                    if (hr != 0)
                    {
                         hr = cmdTarget.Exec(pGroup, User32.OLECMDID_ZOOM, User32.OLECMDEXECOPT_DONTPROMPTUSER, pIn, IntPtr.Zero);
                    }

                    // If both fail, try simulating Ctrl+MouseWheel
                    if (hr != 0)
                    {
                        SimulateZoomWithMouseWheel(zoomPercent);
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pIn);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetZoom failed: {ex.Message}");
                Console.WriteLine($"[WordPreviewDebug] SetZoom Exception: {ex.Message}");
            }
        }
        else
        {
             Console.WriteLine($"[WordPreviewDebug] Handler does not implement IOleCommandTarget");
        }
    }

    private void SimulateZoomWithMouseWheel(int targetZoom)
    {
        // Assume starting at 100%
        int currentZoom = 100;
        int diff = targetZoom - currentZoom;
        
        // Word usually zooms 10% per wheel tick
        int steps = diff / 10; 
        
        if (steps == 0) return;

        // Retry loop to find the correct window
        IntPtr targetHwnd = IntPtr.Zero;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            // Strategy 1: QueryFocus
            if (_currentHandler != null)
            {
                try
                {
                    // Try to set focus to the handler first, so it knows which window is active
                    _currentHandler.SetFocus();
                    
                    _currentHandler.QueryFocus(out IntPtr focusHwnd);
                    if (focusHwnd != IntPtr.Zero && focusHwnd != _hwndContent)
                    {
                        targetHwnd = focusHwnd;
                        Console.WriteLine($"[WordPreviewDebug] QueryFocus returned valid child: {targetHwnd}");
                        break;
                    }
                }
                catch { }
            }

            // Strategy 2: EnumChildWindows to find _WwG
            User32.EnumChildWindows(_hwndContent, (hwnd, lParam) => 
            {
                StringBuilder sb = new StringBuilder(256);
                User32.GetClassName(hwnd, sb, sb.Capacity);
                string className = sb.ToString();
                
                if (className == "_WwG" || className.Contains("Word"))
                {
                    targetHwnd = hwnd;
                    return false; // Found it
                }
                return true;
            }, IntPtr.Zero);

            if (targetHwnd != IntPtr.Zero) break;

            // Wait a bit for the window to be created
            System.Threading.Thread.Sleep(200);
        }

        if (targetHwnd == IntPtr.Zero) 
        {
            targetHwnd = _hwndContent;
            Console.WriteLine($"[WordPreviewDebug] No child found after retries. Sending to Content Window: {targetHwnd}");
        }
        else
        {
             StringBuilder sb = new StringBuilder(256);
             User32.GetClassName(targetHwnd, sb, sb.Capacity);
             Console.WriteLine($"[WordPreviewDebug] Found Target Window: {targetHwnd}, Class: {sb}");
        }

        int delta = steps > 0 ? 120 : -120;
        int count = Math.Abs(steps);

        Console.WriteLine($"[WordPreviewDebug] Simulating Zoom: {steps} steps (Target: {targetZoom}%)");

        // Ensure window has focus before sending input
        User32.SetFocus(targetHwnd);

        // Use PostMessage for input simulation as it is more reliable for input queues
        
        // Simulate Ctrl Key Down
        User32.PostMessage(targetHwnd, User32.WM_KEYDOWN, (IntPtr)User32.VK_CONTROL, (IntPtr)0x001D0001); // Scan code for Ctrl

        // Small delay after KeyDown
        System.Threading.Thread.Sleep(50);

        for (int i = 0; i < count; i++)
        {
            // WM_MOUSEWHEEL = 0x020A
            // wParam: High word is delta, Low word is flags (MK_CONTROL = 0x0008)
            IntPtr wParam = (IntPtr)((delta << 16) | 0x0008);
            
            // lParam: Coordinates (center of window)
            User32.GetWindowRect(targetHwnd, out var rect);
            int centerX = (rect.left + rect.right) / 2;
            int centerY = (rect.top + rect.bottom) / 2;
            IntPtr lParam = (IntPtr)((centerY << 16) | (centerX & 0xFFFF));

            User32.PostMessage(targetHwnd, 0x020A, wParam, lParam);
            
            // Increase delay to ensure Word processes the messages
            System.Threading.Thread.Sleep(100);
        }

        // Small delay before KeyUp
        System.Threading.Thread.Sleep(50);

        // Simulate Ctrl Key Up
        User32.PostMessage(targetHwnd, User32.WM_KEYUP, (IntPtr)User32.VK_CONTROL, (IntPtr)unchecked((int)0xC01D0001));
    }

    private static bool TryGetClsidFromKey(Microsoft.Win32.RegistryKey root, string subKeyPath, out Guid clsid)
    {
        clsid = Guid.Empty;
        try
        {
            using var key = root.OpenSubKey(subKeyPath);
            if (key != null)
            {
                var val = key.GetValue(null) as string;
                if (Guid.TryParse(val, out var result))
                {
                    clsid = result;
                    return true;
                }
            }
        }
        catch { }
        return false;
    }
}

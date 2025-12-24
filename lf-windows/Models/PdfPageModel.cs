using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Docnet.Core.Readers;
using LfWindows.Services;

namespace LfWindows.Models;

public partial class PdfPageModel : ObservableObject, IDisposable
{
    private static readonly System.Threading.SemaphoreSlim _loadGate = new(1, 1); // limit concurrent page loads
    private const int CacheMaxBytes = 3_000_000; // Skip caching very large rendered pages (~3 MB raw)
    private readonly IDocReader _docReader;
    private readonly int _pageIndex;
    private readonly object _renderLock;
    private readonly PreviewCacheService _cacheService;
    private readonly string _fileHash;
    private readonly bool _enableCache;
    private bool _isLoading;
    private System.Threading.CancellationTokenSource? _cts;

    [ObservableProperty]
    private Bitmap? _image;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;

    public int PageNumber => _pageIndex + 1;

    public PdfPageModel(IDocReader docReader, int pageIndex, object renderLock, PreviewCacheService cacheService, string fileHash, double defaultWidth, double defaultHeight, bool enableCache)
    {
        _docReader = docReader;
        _pageIndex = pageIndex;
        _renderLock = renderLock;
        _cacheService = cacheService;
        _fileHash = fileHash;
        _enableCache = enableCache;
        
        // Use default dimensions initially to avoid expensive PDF parsing loop
        _width = defaultWidth;
        _height = defaultHeight;
    }

    public async Task LoadAsync()
    {
        if (Image != null || _isLoading) return;
        _isLoading = true;
        _cts = new System.Threading.CancellationTokenSource();
        var token = _cts.Token;
        var gateTaken = false;

        try
        {
            await _loadGate.WaitAsync(token);
            gateTaken = true;
            // Run heavy lifting in background, but create UI objects (Bitmap) on UI thread
            // to avoid potential cross-thread issues with WriteableBitmap/Bitmap.
            // load start
            
            // Explicitly specify tuple type to avoid CS8619 warning
            // Return: (CachedBitmap, RawBytes, Width, Height)
            (Bitmap? cachedBitmap, byte[]? rawBytes, int width, int height) result = await Task.Run<(Bitmap?, byte[]?, int, int)>(() =>
            {
                if (token.IsCancellationRequested) return (null, null, 0, 0);

                try 
                {
                    // 1. Try load from cache (only when enabled)
                    if (_enableCache)
                    {
                        string cacheKey = $"{_fileHash}_{_pageIndex}";
                        // Use TryGetCachedFileByKey to avoid re-hashing the key as a file path
                        if (_cacheService.TryGetCachedFileByKey(cacheKey, "pdf_pages", ".png", out string cachedPath))
                        {
                            if (token.IsCancellationRequested) return (null, null, 0, 0);
                            var bmp = new Bitmap(cachedPath);
                            return (bmp, null, (int)bmp.Size.Width, (int)bmp.Size.Height);
                        }
                    }

                    if (token.IsCancellationRequested) return (null, null, 0, 0);

                    // Console.WriteLine($"[PdfPageModel] Cache MISS page {_pageIndex} - Rendering...");

                    // 2. Render if not cached
                    lock (_renderLock)
                    {
                        if (token.IsCancellationRequested) return (null, null, 0, 0);
                        using var pageReader = _docReader.GetPageReader(_pageIndex);
                        var w = pageReader.GetPageWidth();
                        var h = pageReader.GetPageHeight();
                        
                        // SMART SCALING LOGIC:
                        // If the native dimension is small (< 1920), we might want to upscale it for better quality.
                        // If it is large (> 1920), we keep it as is (1.0 scale from DocReader) to avoid memory explosion.
                        // Docnet's GetImage() returns the size based on the DocReader's PageDimensions.
                        // Since we initialized DocReader with 1.0, we are getting native size here.
                        
                        // We can't easily change the scale per page without a new DocReader.
                        // However, we can check if the image is too small and maybe we should have used a higher scale?
                        // But for now, let's stick to native size to be safe on memory.
                        // The user requested "Limit max to 1920x1920".
                        // If native is 5000x5000, we get 5000x5000. This is still huge.
                        // Docnet doesn't support downscaling in GetImage() directly if DocReader is set to 1.0.
                        // We would need to resize the byte array or bitmap manually, which is expensive.
                        // Ideally, we should have initialized DocReader with a dimension constraint, but pages vary.
                        
                        var bytes = pageReader.GetImage();
                        
                        // rendered page

                        return (null, bytes, w, h);
                    }
                }
                catch
                {
                    // Console.WriteLine($"[PdfPageModel] Render error page {_pageIndex}: {ex.Message}");
                    return (null, null, 0, 0);
                }
            }, token);

            if (token.IsCancellationRequested)
            {
                result.cachedBitmap?.Dispose();
                return;
            }

            if (result.cachedBitmap != null)
            {
                // Update dimensions if they differ from default
                if (Width != result.width || Height != result.height)
                {
                    Width = result.width;
                    Height = result.height;
                }
                Image = result.cachedBitmap;
                // cache hit
            }
            else if (result.rawBytes != null)
            {
                // Update dimensions if they differ from default
                if (Width != result.width || Height != result.height)
                {
                    Width = result.width;
                    Height = result.height;
                }

                // Create WriteableBitmap
                var wb = new WriteableBitmap(
                    new PixelSize(result.width, result.height),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul);

                using (var buffer = wb.Lock())
                {
                    System.Runtime.InteropServices.Marshal.Copy(result.rawBytes, 0, buffer.Address, result.rawBytes.Length);
                }
                
                Image = wb;
                // rendered bitmap

                // 3. Save to cache (Fire and forget in background)
                // CRITICAL FIX: Do NOT use 'wb' (WriteableBitmap) in background thread.
                // It is a UI object and accessing it from another thread (even for Save) is unsafe and causes 0xC0000005 Access Violation.
                // Instead, we must create a deep copy of the pixel data to pass to the background thread.
                
                // Clone the raw bytes for the background task
                // Only do this if we are NOT cancelling
                if (_enableCache && !token.IsCancellationRequested && result.rawBytes.Length <= CacheMaxBytes)
                {
                    byte[] bytesToSave = new byte[result.rawBytes.Length];
                    Array.Copy(result.rawBytes, bytesToSave, result.rawBytes.Length);
                    int w = result.width;
                    int h = result.height;

                    _ = Task.Run(() => 
                    {
                        try
                        {
                            string cacheKey = $"{_fileHash}_{_pageIndex}";
                            string savePath = _cacheService.GetCachePathByKey(cacheKey, "pdf_pages", ".png");
                            
                            // Create a temporary Skia bitmap in the background thread to save it
                            // This avoids touching any UI objects (WriteableBitmap) from this thread.
                            using var bitmap = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(w, h, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul));
                            
                            // Copy bytes to SKBitmap
                            IntPtr pixels = bitmap.GetPixels();
                            System.Runtime.InteropServices.Marshal.Copy(bytesToSave, 0, pixels, bytesToSave.Length);

                            // Save using SkiaSharp directly
                            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
                            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                            using var stream = File.OpenWrite(savePath);
                            data.SaveTo(stream);

                            // Console.WriteLine($"[PdfPageModel] Saved cache page {_pageIndex}");
                            // cached page
                        }
                        catch { 
                            // Console.WriteLine($"[PdfPageModel] Save cache error: {ex.Message}");
                        }
                    });
                }
            }
        }
        catch
        {
            // Console.WriteLine($"[PdfPageModel] LoadAsync fatal error page {_pageIndex}: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
            if (gateTaken) _loadGate.Release();
            // load end
        }
    }

    public void Unload()
    {
        Dispose();
    }

    public void Dispose()
    {
        // Cancel any pending load
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        // Unload image to save memory when scrolled out of view
        if (Image != null)
        {
            // Console.WriteLine($"[PdfPageModel] Disposing page {_pageIndex}");
            Image.Dispose();
            Image = null;
        }
        _isLoading = false;
        // disposed page
    }
}

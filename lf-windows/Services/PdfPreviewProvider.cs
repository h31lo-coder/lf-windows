using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers; // Added for IDocReader
using LfWindows.Models;
using LfWindows.Controls;

namespace LfWindows.Services;

public class PdfPreviewProvider : IPreviewProvider
{
    private static readonly bool DebugLogging = true; // Toggle for memory/scale diagnostics
    private const int MaxRenderEdge = 1920;           // Cap for adaptive upscale
    private readonly PreviewCacheService _cacheService;
    private readonly System.Threading.SemaphoreSlim _semaphore = new(1, 1);
    private readonly object _ctsLock = new();
    private CancellationTokenSource? _currentRenderCts;

    public PdfPreviewProvider(PreviewCacheService cacheService)
    {
        _cacheService = cacheService;
    }

    public PdfPreviewProvider() : this(new PreviewCacheService()) { }

    public bool CanPreview(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLower();
        return ext == ".pdf";
    }

    public async Task<object> GeneratePreviewAsync(string filePath, System.Threading.CancellationToken token = default)
    {
        // We want to use our own renderer (Docnet) to support multi-page preview 
        // and consistent UI with Office-converted PDFs.
        // So we skip the native Preview Handler check.

        /*
        // 1. Try Native Preview Handler (WebView2 / Adobe)
        if (PreviewHandlerHost.GetPreviewHandlerCLSID(filePath) != Guid.Empty)
        {
             return new OfficePreviewModel(filePath);
        }
        */

        // Preemptive: cancel any in-flight render so navigation stays responsive
        CancellationTokenSource? toCancel = null;
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (_ctsLock)
        {
            toCancel = _currentRenderCts;
            _currentRenderCts = linkedCts;
        }
        if (toCancel != null)
        {
            try
            {
                toCancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed; safe to ignore
            }
        }

        return await Task.Run<object>(async () =>
        {
            var linkedToken = linkedCts.Token;
            var acquired = false;
            try
            {
                // Try immediate acquire; if busy, bail to keep UI unblocked
                acquired = await _semaphore.WaitAsync(0, linkedToken);
                if (!acquired)
                {
                    return "Cancelled";
                }

                if (linkedToken.IsCancellationRequested) return "Cancelled";

                // Use Docnet (PDFium) exclusively for reliable rendering
                // Adaptive scale: small pages are upscaled up to MaxRenderEdge, large pages stay at 1.0.
                IDocReader docReader;
                double scale = 1.0;
                
                if (ArchiveFileSystemHelper.IsArchivePath(filePath, out string archiveRoot, out string internalPath) && !string.IsNullOrEmpty(internalPath))
                {
                    using var stream = ArchiveFileSystemHelper.OpenStream(filePath);
                    if (stream == null) return (object)"Could not read PDF from archive";
                    
                    if (linkedToken.IsCancellationRequested) return "Cancelled";

                    // Docnet supports byte array
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    if (linkedToken.IsCancellationRequested) return "Cancelled";
                    
                    byte[] fileBytes = ms.ToArray();

                    scale = ProbeAdaptiveScale(fileBytes);
                    docReader = DocLib.Instance.GetDocReader(fileBytes, new PageDimensions(scale));
                    Log($"[PdfPreviewProvider] Archive load scale={scale:F2} path={filePath}");

                    if (linkedToken.IsCancellationRequested)
                    {
                        docReader.Dispose();
                        return "Cancelled";
                    }

                    // Compute hash for archive file
                    string fileHash = ComputeArchiveFileHash(filePath, archiveRoot, internalPath);

                    if (docReader.GetPageCount() == 0) 
                    {
                        docReader.Dispose();
                        return (object)"Empty PDF";
                    }

                    return new PdfPreviewModel(docReader, _cacheService, filePath, fileHash);
                }
                else
                {
                    scale = ProbeAdaptiveScale(filePath);
                    Log($"[PdfPreviewProvider] File load scale={scale:F2} path={filePath}");
                    docReader = DocLib.Instance.GetDocReader(filePath, new PageDimensions(scale));
                }

                if (linkedToken.IsCancellationRequested)
                {
                    docReader.Dispose();
                    return "Cancelled";
                }
                
                if (docReader.GetPageCount() == 0) 
                {
                    docReader.Dispose();
                    return (object)"Empty PDF";
                }

                var model = new PdfPreviewModel(docReader, _cacheService, filePath);
                return model;
            }
            catch (Exception ex)
            {
                if (linkedToken.IsCancellationRequested)
                {
                    return "Cancelled";
                }
                return (object)$"Error rendering PDF: {ex.Message}";
            }
            finally
            {
                if (acquired)
                {
                    _semaphore.Release();
                }
                lock (_ctsLock)
                {
                    if (ReferenceEquals(_currentRenderCts, linkedCts))
                    {
                        _currentRenderCts = null;
                    }
                }
                linkedCts.Dispose();
            }
        });
    }

    private string ComputeArchiveFileHash(string fullPath, string archiveRoot, string internalPath)
    {
        var item = ArchiveFileSystemHelper.GetArchiveItem(fullPath, archiveRoot, internalPath);
        long size = item?.Size ?? 0;
        long ticks = item?.Modified.Ticks ?? 0;
        
        string input = $"{fullPath}|{ticks}|{size}";
        
        using var md5 = MD5.Create();
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = md5.ComputeHash(inputBytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }

    private static void Log(string message)
    {
        if (!DebugLogging) return;
        DebugLogger.Log(message);
    }

    // Memory logging removed per request

    /// <summary>
    /// Probe first page to decide scale: upscale small pages up to MaxRenderEdge, keep large pages at 1.0.
    /// Only used at doc open; per-page scaling remains consistent.
    /// </summary>
    private double ProbeAdaptiveScale(string filePath)
    {
        try
        {
            using var probeReader = DocLib.Instance.GetDocReader(filePath, new PageDimensions(1.0));
            return ComputeScaleFromReader(probeReader);
        }
        catch
        {
            return 1.0;
        }
    }

    private double ProbeAdaptiveScale(byte[] fileBytes)
    {
        try
        {
            using var probeReader = DocLib.Instance.GetDocReader(fileBytes, new PageDimensions(1.0));
            return ComputeScaleFromReader(probeReader);
        }
        catch
        {
            return 1.0;
        }
    }

    private double ComputeScaleFromReader(IDocReader reader)
    {
        try
        {
            if (reader.GetPageCount() == 0) return 1.0;
            using var page = reader.GetPageReader(0);
            int w = page.GetPageWidth();
            int h = page.GetPageHeight();
            int maxEdge = Math.Max(w, h);
            if (maxEdge <= 0) return 1.0;
            // Adaptive upscale for 150% DPI: small pages get boosted up to MaxRenderEdge, capped at 1.5x to avoid runaway memory.
            if (maxEdge < MaxRenderEdge)
            {
                double upscale = (double)MaxRenderEdge / maxEdge; // scale needed to reach target edge
                double dpiBoost = 1.5; // user display at 150%
                double target = Math.Min(upscale, dpiBoost);
                return target > 0 ? target : 1.0;
            }
            // Large pages stay at 1.0 to prevent huge bitmaps
            return 1.0;
        }
        catch
        {
            return 1.0;
        }
    }
}

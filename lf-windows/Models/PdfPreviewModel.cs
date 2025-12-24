using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;
using LfWindows.Services;

namespace LfWindows.Models;

public partial class PdfPreviewModel : ObservableObject, IDisposable
{
    private readonly IDocReader _docReader;
    private readonly object _renderLock = new();
    private readonly bool _enablePageCache;
    
    [ObservableProperty]
    private ObservableCollection<PdfPageModel> _pages = new();

    [ObservableProperty]
    private int _pageCount;

    public PdfPreviewModel(IDocReader docReader, PreviewCacheService cacheService, string filePath, string? precomputedHash = null)
    {
        _docReader = docReader;
        PageCount = _docReader.GetPageCount();
        _enablePageCache = true; // Always allow per-page PNG cache, even for large documents
        
        // Compute file hash once for all pages
        string fileHash = precomputedHash ?? cacheService.ComputeFileHash(filePath);

        // Get default dimensions from first page to avoid opening every page reader in loop
        double defW = 0, defH = 0;
        if (PageCount > 0)
        {
            lock (_renderLock)
            {
                using var pageReader = _docReader.GetPageReader(0);
                defW = pageReader.GetPageWidth();
                defH = pageReader.GetPageHeight();
            }
        }

        // Initialize all page models (lightweight wrappers)
        // This runs in background thread (from PdfPreviewProvider), so it's safe to take some time.
        for (int i = 0; i < PageCount; i++)
        {
            Pages.Add(new PdfPageModel(_docReader, i, _renderLock, cacheService, fileHash, defW, defH, _enablePageCache));
        }

    }

    public void Dispose()
    {
        // Only dispose loaded bitmaps; avoid long UI-blocking loops on large PDFs
        if (Pages != null)
        {
            var loadedPages = Pages.Where(p => p.Image != null).ToList();
            Pages.Clear();

            if (loadedPages.Count > 0)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var page in loadedPages)
                    {
                        page.Dispose();
                    }
                });
            }
        }

        lock (_renderLock)
        {
            _docReader?.Dispose();
        }

        // Best-effort aggressive cleanup after heavy PDF cycles; run in background to avoid UI hitch
        _ = Task.Run(() =>
        {
            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
            catch { }
        });
    }

}

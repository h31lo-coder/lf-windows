using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using LfWindows.Controls;
using LfWindows.Models;
using SharpCompress.Archives;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AvaloniaEdit.Highlighting;
using Avalonia.Media.Imaging;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;
using Word = NetOffice.WordApi;
using PowerPoint = NetOffice.PowerPointApi;
using NetOffice.PowerPointApi.Enums;
using Excel = NetOffice.ExcelApi;
using NetOffice.ExcelApi.Enums;
using ExcelDataReader;
using System.Data;
using NetOffice.OfficeApi.Enums;
using Docnet.Core;
using Docnet.Core.Models;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using Task = System.Threading.Tasks.Task;

namespace LfWindows.Services;

public class OfficePreviewProvider : IPreviewProvider
{
    private static readonly bool DebugLogging = true; // Toggle for memory/scale diagnostics
    private const int MaxRenderEdge = 1920;
    private const int ExcelMaxSheets = 1;           // Limit sheets to reduce load
    private const int ExcelMaxRows = 30;            // Limit rows per sheet
    private const int ExcelMaxColumns = 10;         // Limit columns per sheet
    private readonly PreviewCacheService _cacheService;
    private readonly ConcurrentDictionary<string, DateTime> _emptyFiles = new();
    private readonly System.Threading.SemaphoreSlim _semaphore = new(1, 1);

    public OfficePreviewProvider(PreviewCacheService cacheService)
    {
        _cacheService = cacheService;
    }

    public OfficePreviewProvider() : this(new PreviewCacheService()) { }

    public bool CanPreview(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLower();

        // Disable legacy Office formats (doc/ppt/xls) per request; keep modern OpenXML and other supported types.
        if (ext == ".doc" || ext == ".ppt" || ext == ".xls")
        {
            return false;
        }

        return ext == ".docx" || ext == ".xlsx" || ext == ".pptx" ||
               ext == ".msg" || ext == ".vsd" || ext == ".vsdx";
    }

    private static void Log(string message)
    {
        if (!DebugLogging) return;
        DebugLogger.Log(message);
    }

    public async Task<object> GeneratePreviewAsync(string filePath, PreviewMode mode, System.Threading.CancellationToken token = default)
    {
        try
        {
            await _semaphore.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return "Cancelled";
        }

        try
        {
            if (token.IsCancellationRequested) return "Cancelled";

            // Check for Archive Path first
            if (ArchiveFileSystemHelper.IsArchivePath(filePath, out _, out string internalPath) && !string.IsNullOrEmpty(internalPath))
            {
                return await GenerateArchivePreviewAsync(filePath, token);
            }

        string ext = Path.GetExtension(filePath).ToLower();
        bool isWord = ext == ".docx";
        bool isPpt = ext == ".pptx";
        bool isExcel = ext == ".xlsx";

        // Legacy Office formats are not supported for preview; return message immediately.
        if (ext == ".doc" || ext == ".ppt" || ext == ".xls")
        {
            return "Preview unavailable (legacy Office formats disabled)";
        }

        if (isExcel)
        {
            try
            {
                var excelModel = await Task.Run<ExcelPreviewModel?>(() =>
                {
                    if (token.IsCancellationRequested) return null;

                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                    using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    
                    if (token.IsCancellationRequested) return null;

                    using var reader = ExcelReaderFactory.CreateReader(stream);
                    var config = new ExcelDataSetConfiguration
                    {
                        ConfigureDataTable = (_) => new ExcelDataTableConfiguration
                        {
                            UseHeaderRow = true,
                            // Trim data size to avoid UI stalls and high memory on large sheets
                            FilterRow = rowReader => rowReader.Depth < ExcelMaxRows,
                            FilterColumn = (_, columnIndex) => columnIndex < ExcelMaxColumns
                        }
                    };

                    var result = reader.AsDataSet(config);

                    if (token.IsCancellationRequested) return null;

                    if (result.Tables.Count > 0)
                    {
                        var sheets = new List<System.Data.DataTable>();
                        foreach (System.Data.DataTable table in result.Tables)
                        {
                            sheets.Add(table);
                            if (sheets.Count >= ExcelMaxSheets) break;
                        }

                        if (sheets.Count > 0)
                        {
                            Log($"[ExcelPreview] sheets={result.Tables.Count} loaded={sheets.Count} rows<={ExcelMaxRows} cols<={ExcelMaxColumns}");
                            return new ExcelPreviewModel(sheets);
                        }
                    }
                    return null;
                });

                if (excelModel != null)
                {
                    return excelModel;
                }
            }
            catch
            {
                // Excel read failed
            }
        }

        // Strategy: For Word and PowerPoint documents, try to convert to PDF first to ensure consistent rendering
        // and avoid the Zoom/Layout issues of the native Preview Handler.
        if (isWord || isPpt)
        {
            // Check empty file cache to avoid repeated COM calls for known empty files
            if (_emptyFiles.TryGetValue(filePath, out DateTime cachedTime))
            {
                try
                {
                    if (File.GetLastWriteTime(filePath) == cachedTime)
                    {
                        return "No preview available";
                    }
                    else
                    {
                        // File changed, remove from cache
                        _emptyFiles.TryRemove(filePath, out _);
                    }
                }
                catch { }
            }

            try
            {
                string? pdfPath = null;
                
                // Check cache first
                if (_cacheService.TryGetCachedFile(filePath, "office", ".pdf", out string cachedPath))
                {
                    pdfPath = cachedPath;
                }
                else
                {
                    // If not cached, convert
                    // Use Task.Run to avoid blocking UI thread
                    pdfPath = await Task.Run(() => 
                    {
                        if (token.IsCancellationRequested) return null;

                        string targetPath = _cacheService.GetCachePath(filePath, "office", ".pdf");
                        if (isWord) return ConvertWordToPdf(filePath, targetPath);
                        if (isPpt) return ConvertPptToPdf(filePath, targetPath);
                        return null;
                    }, token);
                }

                if (!string.IsNullOrEmpty(pdfPath) && File.Exists(pdfPath))
                {
                    try
                    {
                        var model = await Task.Run(() => CreatePdfPreviewModelWithCap(pdfPath, filePath, token), token);

                        if (token.IsCancellationRequested)
                        {
                            if (model is IDisposable disposable) disposable.Dispose();
                            return "Cancelled";
                        }

                        if (model != null)
                        {
                            return model;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return "Cancelled";
                    }
                }
            }
            catch
            {
                // Office to PDF conversion failed
            }

            // If PDF conversion failed or returned null (e.g. empty file), 
            // for PPTX we prefer "No preview available" over the buggy/laggy PreviewHandler fallback.
            if (isPpt)
            {
                return "No preview available";
            }
        }

        // 1. Check if Preview Handler is available in Registry
        // We prefer OfficePreviewModel even for Default mode to ensure smooth transition within the same control
        if (PreviewHandlerHost.GetPreviewHandlerCLSID(filePath) != Guid.Empty)
        {
            try
            {
                // Try to get a thumbnail to pass to the model for smooth transition
                Bitmap? thumb = null;
                
                // For Word/PPT files, we skip thumbnail to avoid "Image 1" flash and layout jump
                // because Word preview is flow-layout (full size) while thumbnail is fixed aspect ratio.
                if (!isWord && !isPpt)
                {
                    try 
                    { 
                        if (!token.IsCancellationRequested)
                        {
                            thumb = await Task.Run(() => ExtractThumbnail(filePath, token)); 
                        }
                    } 
                    catch { }
                }

                if (token.IsCancellationRequested)
                {
                    thumb?.Dispose();
                    return "Cancelled";
                }

                return new OfficePreviewModel(filePath, thumb);
            }
            catch
            {
                // Fallback if instantiation fails immediately
            }
        }

        // 2. Fallback: Try to extract text for OpenXML formats
        if (ext == ".docx" || ext == ".xlsx" || ext == ".pptx")
        {
            try 
            {
                if (token.IsCancellationRequested) return "Cancelled";
                string text = await Task.Run(() => ExtractTextFromOpenXml(filePath, token));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Return as text document
                    return new CodePreviewModel(text, null); 
                }
            }
            catch
            {
                // Ignore extraction errors
            }
        }

            return "Preview unavailable (No handler found)";
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task<object> GeneratePreviewAsync(string filePath, System.Threading.CancellationToken token = default) => GeneratePreviewAsync(filePath, PreviewMode.Default, token);

    private PdfPreviewModel? CreatePdfPreviewModelWithCap(string pdfPath, string originPath, System.Threading.CancellationToken token)
    {
        if (token.IsCancellationRequested) return null;

        double scale = 1.0;
        int probeWidth = 0;
        int probeHeight = 0;

        try
        {
            using var probeReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(1.0));
            int pageCount = probeReader.GetPageCount();
            if (pageCount == 0) return null;

            using var probePage = probeReader.GetPageReader(0);
            probeWidth = probePage.GetPageWidth();
            probeHeight = probePage.GetPageHeight();

            int maxEdge = Math.Max(probeWidth, probeHeight);

            // Adaptive scaling: only upscale small pages up to MaxRenderEdge, keep large pages at 1.0 to avoid blur
            if (maxEdge > 0 && maxEdge < MaxRenderEdge)
            {
                scale = Math.Min(1.5, MaxRenderEdge / (double)maxEdge); // cap upscale to 1.5x
            }
        }
        catch
        {
            // Fallback to scale=1 when probing fails
        }

        if (token.IsCancellationRequested) return null;

        var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(scale));

        if (token.IsCancellationRequested)
        {
            docReader.Dispose();
            return null;
        }

        int pageCountAfterScale = docReader.GetPageCount();
        if (pageCountAfterScale == 0)
        {
            docReader.Dispose();
            return null;
        }

        int scaledWidth = probeWidth;
        int scaledHeight = probeHeight;
        try
        {
            using var firstPage = docReader.GetPageReader(0);
            scaledWidth = firstPage.GetPageWidth();
            scaledHeight = firstPage.GetPageHeight();
        }
        catch
        {
            // Ignore logging dimensions if we can't read the first page
        }

        Log($"[OfficePreviewProvider] PDF-from-Office pages={pageCountAfterScale} scale={scale:F2} firstSize=({scaledWidth}x{scaledHeight}) path={originPath}");
        return new PdfPreviewModel(docReader, _cacheService, pdfPath);
    }

    private Bitmap? ExtractThumbnail(string filePath, System.Threading.CancellationToken token)
    {
        if (token.IsCancellationRequested) return null;

        // 1. Try OpenXML Thumbnail first (Internal) as requested
        try
        {
            using var archive = ArchiveFactory.Open(filePath);
            if (token.IsCancellationRequested) return null;

            var entry = archive.Entries.FirstOrDefault(e => e.Key != null && e.Key.Equals("docProps/thumbnail.jpeg", StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                using var ms = new MemoryStream();
                entry.WriteTo(ms);
                ms.Position = 0;
                
                if (token.IsCancellationRequested) return null;

                // Enhance the internal thumbnail
                using var originalBmp = System.Drawing.Image.FromStream(ms);
                using var enhancedBmp = EnhanceImage(originalBmp);
                
                if (token.IsCancellationRequested) return null;

                using var enhancedMs = new MemoryStream();
                enhancedBmp.Save(enhancedMs, System.Drawing.Imaging.ImageFormat.Bmp);
                enhancedMs.Position = 0;
                
                return new Bitmap(enhancedMs);
            }
        }
        catch { /* Ignore extraction errors */ }

        if (token.IsCancellationRequested) return null;

        // 2. Try Windows Shell Thumbnail (High Quality)
        try
        {
            var guid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"); // IShellItemImageFactory
            Interop.Shell32.SHCreateItemFromParsingName(filePath, IntPtr.Zero, guid, out var factory);
            
            if (factory != null)
            {
                if (token.IsCancellationRequested) return null;

                // Request a higher-quality thumbnail for A4 text clarity (still bounded to avoid huge allocations)
                var size = new Interop.SIZE { cx = 1600, cy = 1600 }; 
                
                // Use RESIZETOFIT (0) to ask the shell to generate/scale the image to fit this size.
                factory.GetImage(size, Interop.SIIGBF.SIIGBF_RESIZETOFIT, out var hBitmap);
                
                if (token.IsCancellationRequested)
                {
                    if (hBitmap != IntPtr.Zero) Interop.User32.DeleteObject(hBitmap);
                    return null;
                }

                if (hBitmap != IntPtr.Zero)
                {
                    try
                    {
                        using var sysBitmap = System.Drawing.Image.FromHbitmap(hBitmap);
                        
                        // Enhance the shell thumbnail
                        using var enhancedBmp = EnhanceImage(sysBitmap);

                        using var ms = new MemoryStream();
                        // Use BMP to avoid any potential PNG compression artifacts and overhead
                        enhancedBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                        ms.Position = 0;
                        return new Bitmap(ms);
                    }
                    finally
                    {
                        Interop.User32.DeleteObject(hBitmap);
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private System.Drawing.Bitmap EnhanceImage(System.Drawing.Image source)
    {
        const int target = 1600; // Higher target for A4 text clarity

        // If source is already near target, just clone without extra processing
        if (source.Width >= target || source.Height >= target)
        {
            return new System.Drawing.Bitmap(source);
        }

        // Step-wise upscaling only (no sharpen/contrast)
        using var step1 = Resize(source, Math.Min(source.Width * 2, target), Math.Min(source.Height * 2, target));

        if (step1.Width < target && step1.Height < target)
        {
            int nextW = Math.Min(step1.Width * 2, target);
            int nextH = Math.Min(step1.Height * 2, target);
            return Resize(step1, nextW, nextH);
        }

        return new System.Drawing.Bitmap(step1);
    }

    private System.Drawing.Bitmap Resize(System.Drawing.Image image, int width, int height)
    {
        var destRect = new System.Drawing.Rectangle(0, 0, width, height);
        var destImage = new System.Drawing.Bitmap(width, height);

        destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

        using (var graphics = System.Drawing.Graphics.FromImage(destImage))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            using (var wrapMode = new System.Drawing.Imaging.ImageAttributes())
            {
                wrapMode.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, System.Drawing.GraphicsUnit.Pixel, wrapMode);
            }
        }

        return destImage;
    }

    private System.Drawing.Bitmap AdjustContrast(System.Drawing.Bitmap image, float value)
    {
        // Deprecated: kept for compatibility if needed in future; currently unused
        return new System.Drawing.Bitmap(image);
    }

    private System.Drawing.Bitmap Sharpen(System.Drawing.Bitmap image)
    {
        // Deprecated: kept for compatibility if needed in future; currently unused
        return new System.Drawing.Bitmap(image);
    }

    private string? ConvertWordToPdf(string sourcePath, string pdfPath)
    {
        Word.Document? doc = null;
        try
        {
            var wordApp = OfficeInteropService.Instance.GetWordApp();

            // Open with ReadOnly to avoid locking the file or creating temp files in the source dir
            // NetOffice uses object parameters for optional arguments
            object confirmConversions = false;
            object readOnly = true;
            object visible = false;
            object addToRecent = false;
            
            // NetOffice.WordApi.Documents.Open(object fileName, object confirmConversions, object readOnly, ...)
            doc = wordApp.Documents.Open(sourcePath, confirmConversions, readOnly, addToRecent, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, visible);
            
            if (doc != null)
            {
                doc.SaveAs2(pdfPath, WdSaveFormat.wdFormatPDF);
                return pdfPath;
            }
        }
        catch
        {
            // Word Conversion failed
        }
        finally
        {
            if (doc != null)
            {
                object saveChanges = false;
                try { doc.Close(saveChanges); } catch { }
                doc.Dispose();
            }
            // App lifecycle managed by OfficeInteropService
        }
        return null;
    }

    private string? ConvertPptToPdf(string sourcePath, string pdfPath)
    {
        PowerPoint.Presentation? pres = null;
        bool wasAlreadyOpen = false;
        try
        {
            var pptApp = OfficeInteropService.Instance.GetPptApp();
            
            // Check if the application is visible (User is interacting)
            bool isAppVisible = false;
            try { isAppVisible = pptApp.Visible == MsoTriState.msoTrue; } catch { }

            if (isAppVisible)
            {
                // Check if the file is already open
                foreach (PowerPoint.Presentation p in pptApp.Presentations)
                {
                    if (p.FullName.Equals(sourcePath, StringComparison.OrdinalIgnoreCase))
                    {
                        pres = p;
                        wasAlreadyOpen = true;
                        break;
                    }
                }

                if (pres == null)
                {
                    return null;
                }
            }
            else
            {
                // Open ReadOnly, Untitled=False, WithWindow=False
                pres = pptApp.Presentations.Open(sourcePath, MsoTriState.msoTrue, MsoTriState.msoTrue, MsoTriState.msoFalse);
            }
            
            if (pres != null)
            {
                // Handle 0-slide presentations (e.g. newly created files)
                // User Request: If PPT has no slides, skip preview generation entirely to avoid issues.
                if (pres.Slides.Count == 0)
                {
                    // Cache this as empty to avoid repeated COM calls
                    try { _emptyFiles.TryAdd(sourcePath, File.GetLastWriteTime(sourcePath)); } catch { }
                    return null;
                }

                if (pres.Slides.Count > 0)
                {
                    // Reset print options to ensure we print all slides and avoid "selection not found" errors
                    try 
                    {
                        pres.PrintOptions.RangeType = PpPrintRangeType.ppPrintAll;
                        pres.PrintOptions.NumberOfCopies = 1;
                    } 
                    catch { }

                    try
                    {
                        // Try SaveAs first as it is often more reliable for background processing
                        pres.SaveAs(pdfPath, NetOffice.PowerPointApi.Enums.PpSaveAsFileType.ppSaveAsPDF, MsoTriState.msoFalse);
                    }
                    catch
                    {
                        // Fallback to ExportAsFixedFormat
                        pres.ExportAsFixedFormat(pdfPath, PpFixedFormatType.ppFixedFormatTypePDF, 
                            PpFixedFormatIntent.ppFixedFormatIntentScreen, MsoTriState.msoFalse, 
                            PpPrintHandoutOrder.ppPrintHandoutVerticalFirst, PpPrintOutputType.ppPrintOutputSlides, 
                            MsoTriState.msoFalse, null, PpPrintRangeType.ppPrintAll, string.Empty, false, false, false, false, false);
                    }
                    
                    return pdfPath;
                }
            }
        }
        catch
        {
            // PPT Conversion failed
        }
        finally
        {
            if (pres != null && !wasAlreadyOpen)
            {
                try 
                { 
                    // Mark as saved to avoid prompts and avoid saving changes (like our temp slide)
                    pres.Saved = MsoTriState.msoTrue;
                    pres.Close(); 
                } catch { }
                pres.Dispose();
            }

            // Force quit PPT app to avoid interfering with user's interactive session
            // This fixes the issue where opening the file manually results in a blank screen
            // OfficeInteropService.Instance.QuitPptApp(); // Disabled for performance
        }
        return null;
    }

    private string? ConvertExcelToPdf(string sourcePath, string pdfPath)
    {
        Excel.Workbook? wb = null;
        try
        {
            var excelApp = OfficeInteropService.Instance.GetExcelApp();
            
            // Open ReadOnly
            // Open(Filename, UpdateLinks, ReadOnly, Format, Password, WriteResPassword, IgnoreReadOnlyRecommended, Origin, Delimiter, Editable, Notify, Converter, AddToMru, Local, CorruptLoad)
            wb = excelApp.Workbooks.Open(sourcePath, Type.Missing, true, Type.Missing, Type.Missing, Type.Missing, true, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, false);
            
            if (wb != null)
            {
                // ExportAsFixedFormat(Type, Filename, Quality, IncludeDocProperties, IgnorePrintAreas, From, To, OpenAfterPublish, FixedFormatExtClassPtr)
                wb.ExportAsFixedFormat(XlFixedFormatType.xlTypePDF, pdfPath, XlFixedFormatQuality.xlQualityStandard, true, true, Type.Missing, Type.Missing, false, Type.Missing);
                return pdfPath;
            }
        }
        catch
        {
            // Excel Conversion failed
        }
        finally
        {
            if (wb != null)
            {
                try { wb.Close(false); } catch { }
                wb.Dispose();
            }
            // App lifecycle managed by OfficeInteropService
        }
        return null;
    }



    private async Task<object> GenerateArchivePreviewAsync(string filePath, System.Threading.CancellationToken token)
    {
        // Run heavy extraction on background thread
        var extractionResult = await Task.Run<(bool Success, string Content, string Error)>(() =>
        {
            if (token.IsCancellationRequested) return (false, "", "Cancelled");

            try
            {
                string ext = Path.GetExtension(filePath).ToLower();
                using var stream = ArchiveFileSystemHelper.OpenStream(filePath);
                if (stream == null) return (false, "", "Could not read file from archive");

                if (token.IsCancellationRequested) return (false, "", "Cancelled");

                // Excel: Full Preview (DataSet is thread-safe enough to be created here and passed back?)
                // Actually ExcelPreviewModel takes a List<DataTable>. DataTable is not thread-safe but if not attached to UI yet, it's fine.
                // But let's keep Excel logic as is for now since user said it works.
                if (ext == ".xlsx" || ext == ".xls")
                {
                    // We can't easily return the Excel model through this tuple structure designed for text.
                    // So we will handle Excel separately or just return it from here if we refactor.
                    // To minimize changes, let's handle Excel inside here and return it, 
                    // BUT we must be careful. The user said Excel works. 
                    // So let's only change the Text/Code path.
                    return (false, "", "EXCEL_HANDLED_INTERNALLY"); 
                }

                // Word/PPT: Text Preview (OpenXML)
                if (ext == ".docx" || ext == ".pptx")
                {
                    try
                    {
                        // Open the stream as a zip archive
                        using var archive = ArchiveFactory.Open(stream);
                        if (archive == null) return (false, "", "Failed to open document structure");

                        if (token.IsCancellationRequested) return (false, "", "Cancelled");

                        string text = ExtractTextFromArchive(archive, token);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return (true, text, "");
                        }
                    }
                    catch (Exception ex)
                    {
                        return (false, "", $"Text extraction failed: {ex.Message}");
                    }
                }

                return (false, "", "Preview not supported for this file type in archive");
            }
            catch (Exception ex)
            {
                return (false, "", $"Error reading archive file: {ex.Message}");
            }
        });

        // Handle the result on the UI thread
        if (extractionResult.Error == "EXCEL_HANDLED_INTERNALLY")
        {
            // Re-run the Excel logic (it was skipped above). 
            // Or better, just copy the Excel logic here but run it in Task.Run.
            // Since Excel works, let's just revert to the old pattern for Excel ONLY.
            return await Task.Run<object>(() => 
            {
                try 
                {
                    using var stream = ArchiveFileSystemHelper.OpenStream(filePath);
                    if (stream == null) return "Could not read file";
                    
                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                    using var reader = ExcelReaderFactory.CreateReader(stream);
                    var result = reader.AsDataSet(new ExcelDataSetConfiguration() { ConfigureDataTable = (_) => new ExcelDataTableConfiguration() { UseHeaderRow = true } });
                    if (result.Tables.Count > 0)
                    {
                        var sheets = new List<System.Data.DataTable>();
                        foreach (System.Data.DataTable table in result.Tables) sheets.Add(table);
                        return new ExcelPreviewModel(sheets);
                    }
                    return "Empty Excel file";
                }
                catch (Exception ex) { return $"Excel error: {ex.Message}"; }
            });
        }

        if (extractionResult.Success)
        {
            // CRITICAL FIX: Create the CodePreviewModel (and its internal TextDocument) on the UI thread.
            // Creating TextDocument on a background thread and moving it to UI thread can cause issues in AvaloniaEdit.
            return new CodePreviewModel(extractionResult.Content, null);
        }

        return extractionResult.Error;
    }

    private string ExtractTextFromOpenXml(string filePath, System.Threading.CancellationToken token)
    {
        if (token.IsCancellationRequested) return string.Empty;
        try
        {
            using var archive = ArchiveFactory.Open(filePath);
            if (token.IsCancellationRequested) return string.Empty;
            return ExtractTextFromArchive(archive, token);
        }
        catch { }
        return string.Empty;
    }

    private string ExtractTextFromArchive(IArchive archive, System.Threading.CancellationToken token)
    {
        if (archive == null) return string.Empty;
        if (token.IsCancellationRequested) return string.Empty;
        try
        {
            if (archive.Entries == null) return string.Empty;

            var sb = new StringBuilder();

            // Helper to clean XML and avoid massive single lines
            string CleanXml(string xml)
            {
                if (string.IsNullOrEmpty(xml)) return "";
                if (token.IsCancellationRequested) return "";
                
                // Replace block tags with newlines to prevent single-line text explosion
                string text = Regex.Replace(xml, @"</(p|div|tr|br|h\d)>|<br\s*\/?>", "\n", RegexOptions.IgnoreCase);
                
                if (token.IsCancellationRequested) return "";

                // Remove all other tags
                text = Regex.Replace(text, "<[^>]+>", " ");
                
                if (token.IsCancellationRequested) return "";

                // Decode entities
                text = System.Net.WebUtility.HtmlDecode(text);
                
                if (token.IsCancellationRequested) return "";

                // Normalize whitespace (collapse multiple spaces, but keep newlines)
                text = Regex.Replace(text, @"[ \t]+", " ");
                text = Regex.Replace(text, @"\n\s+", "\n");
                text = Regex.Replace(text, @"\n+", "\n");
                
                if (token.IsCancellationRequested) return "";

                // Remove control characters that might crash the editor (keep \n, \r, \t)
                text = new string(text.Where(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t').ToArray());
                
                return text.Trim();
            }

            // DOCX
            var docEntry = archive.Entries.FirstOrDefault(e => e != null && e.Key != null && e.Key.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase));
            if (docEntry != null)
            {
                if (token.IsCancellationRequested) return string.Empty;
                
                // Limit size to avoid OOM on huge docs
                if (docEntry.Size > 10 * 1024 * 1024) // 10MB limit for XML
                {
                     return "--- DOCX Text Content (Fallback) ---\n\n(Document too large for text preview)";
                }

                using var stream = docEntry.OpenEntryStream();
                using var reader = new StreamReader(stream);
                string xml = reader.ReadToEnd();
                
                if (token.IsCancellationRequested) return string.Empty;

                string text = CleanXml(xml);
                if (text.Length > 50000) text = text.Substring(0, 50000) + "\n... (Truncated)";
                return "--- DOCX Text Content (Fallback) ---\n\n" + text;
            }

            // XLSX (Shared Strings)
            var xlsEntry = archive.Entries.FirstOrDefault(e => e != null && e.Key != null && e.Key.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase));
            if (xlsEntry != null)
            {
                using var stream = xlsEntry.OpenEntryStream();
                using var reader = new StreamReader(stream);
                string xml = reader.ReadToEnd();
                string text = CleanXml(xml);
                if (text.Length > 50000) text = text.Substring(0, 50000) + "\n... (Truncated)";
                return "--- XLSX Text Content (Fallback) ---\n\n" + text;
            }

            // PPTX (Slides)
            // Use ToList to materialize and avoid deferred execution issues if archive is disposed or modified
            var slides = archive.Entries
                .Where(e => e != null && e.Key != null && e.Key.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) && e.Key.EndsWith(".xml"))
                .OrderBy(e => e.Key)
                .ToList();

            if (slides.Any())
            {
                sb.AppendLine("--- PPTX Text Content (Fallback) ---");
                int totalLength = 0;
                foreach (var slide in slides)
                {
                    if (token.IsCancellationRequested) return string.Empty;

                    if (slide == null) continue;
                    if (totalLength > 50000) 
                    {
                        sb.AppendLine("\n... (Truncated)");
                        break;
                    }

                    try
                    {
                        using var stream = slide.OpenEntryStream();
                        using var reader = new StreamReader(stream);
                        string xml = reader.ReadToEnd();
                        string text = CleanXml(xml);
                        sb.AppendLine($"\n[Slide {slide.Key}]\n{text}");
                        totalLength += text.Length;
                    }
                    catch
                    {
                        sb.AppendLine($"\n[Slide {slide?.Key ?? "Unknown"}] (Error reading content)");
                    }
                }
                return sb.ToString();
            }
        }
        catch (Exception ex)
        {
            return $"Error extracting text: {ex.Message}";
        }
        return string.Empty;
    }

}

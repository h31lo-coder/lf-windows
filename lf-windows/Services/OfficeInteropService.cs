using System;
using System.Threading.Tasks;
using NetOffice.WordApi;
using NetOffice.PowerPointApi;
using NetOffice.ExcelApi;
using NetOffice.OfficeApi.Enums;
using Word = NetOffice.WordApi;
using PowerPoint = NetOffice.PowerPointApi;
using Excel = NetOffice.ExcelApi;
using System.Runtime.InteropServices;

namespace LfWindows.Services;

public class OfficeInteropService : IDisposable
{
    private static OfficeInteropService? _instance;
    public static OfficeInteropService Instance => _instance ??= new OfficeInteropService();

    private Word.Application? _wordApp;
    private PowerPoint.Application? _pptApp;
    private Excel.Application? _excelApp;
    private readonly object _wordLock = new();
    private readonly object _pptLock = new();
    private readonly object _excelLock = new();

    private OfficeInteropService() { }

    public void Preload()
    {
        // Pre-warm Office processes in background
        System.Threading.Tasks.Task.Run(() => 
        {
            try { GetWordApp(); } catch { }
            try { GetPptApp(); } catch { }
        });
    }

    public Word.Application GetWordApp()
    {
        lock (_wordLock)
        {
            if (_wordApp == null)
            {
                try
                {
                    _wordApp = new Word.Application();
                    _wordApp.Visible = false;
                    _wordApp.ScreenUpdating = false;
                    _wordApp.DisplayAlerts = NetOffice.WordApi.Enums.WdAlertLevel.wdAlertsNone;
                }
                catch
                {
                    throw;
                }
            }
            return _wordApp;
        }
    }

    public PowerPoint.Application GetPptApp()
    {
        lock (_pptLock)
        {
            if (_pptApp == null)
            {
                try
                {
                    _pptApp = new PowerPoint.Application();
                    try 
                    {
                        _pptApp.DisplayAlerts = NetOffice.PowerPointApi.Enums.PpAlertLevel.ppAlertsNone;
                    } 
                    catch { }
                }
                catch
                {
                    throw;
                }
            }
            else
            {
                // Reusing existing PowerPoint.Application instance
            }
            return _pptApp;
        }
    }

    public Excel.Application GetExcelApp()
    {
        lock (_excelLock)
        {
            if (_excelApp == null)
            {
                try
                {
                    _excelApp = new Excel.Application();
                    _excelApp.Visible = false;
                    _excelApp.ScreenUpdating = false;
                    _excelApp.DisplayAlerts = false;
                }
                catch
                {
                    throw;
                }
            }
            return _excelApp;
        }
    }

    public void QuitPptApp()
    {
        // Keep process alive for performance (Process Pre-warming)
        // Only quit on application exit
        return;
    }

    public void Dispose()
    {
        lock (_wordLock)
        {
            if (_wordApp != null)
            {
                try { _wordApp.Quit(false); } catch { }
                _wordApp.Dispose();
                _wordApp = null;
            }
        }

        QuitPptApp();

        lock (_excelLock)
        {
            if (_excelApp != null)
            {
                try { _excelApp.Quit(); } catch { }
                _excelApp.Dispose();
                _excelApp = null;
            }
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LfWindows.Models;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace LfWindows.ViewModels;

public partial class PreviewWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private Thickness _borderThickness = new Thickness(0);

    [ObservableProperty]
    private IBrush _borderBrush = Brushes.Transparent;

    [ObservableProperty]
    private FontFamily _fontFamily = new("Cascadia Code, Consolas, Monospace");

    [ObservableProperty]
    private double _fontSize = 14.0;

    [ObservableProperty]
    private object? _previewContent;

    [ObservableProperty]
    private string _title = "Preview";

    [ObservableProperty]
    private bool _isTopMost;

    [ObservableProperty]
    private string _customBackgroundColor = "Transparent";

    [ObservableProperty]
    private bool _useCustomBackground;

    // PDF/Office Pagination Support
    [ObservableProperty]
    private bool _isPdf;

    [ObservableProperty]
    private int _pdfPageCount;

    [ObservableProperty]
    private int _currentPdfPageIndex;

    [ObservableProperty]
    private PdfPageModel? _currentPdfPage;

    [ObservableProperty]
    private string _pageStatusText = string.Empty;

    [ObservableProperty]
    private double _rotationAngle = 0;

    [ObservableProperty]
    private bool _isRotatable;

    partial void OnPreviewContentChanged(object? value)
    {
        RotationAngle = 0;
        IsRotatable = value is ImagePreviewModel || value is VideoPreviewModel;

        if (value is PdfPreviewModel pdf)
        {
            IsPdf = true;
            PdfPageCount = pdf.PageCount;
            CurrentPdfPageIndex = 0;
            if (pdf.Pages.Count > 0)
            {
                CurrentPdfPage = pdf.Pages[0];
            }
            UpdatePageStatus();
        }
        else
        {
            IsPdf = false;
            CurrentPdfPage = null;
            PageStatusText = string.Empty;
        }
    }

    [RelayCommand]
    public void RotateLeft()
    {
        RotationAngle -= 90;
    }

    [RelayCommand]
    public void RotateRight()
    {
        RotationAngle += 90;
    }

    [RelayCommand]
    public void NextPage()
    {
        if (IsPdf && PreviewContent is PdfPreviewModel pdf && CurrentPdfPageIndex < PdfPageCount - 1)
        {
            CurrentPdfPageIndex++;
            CurrentPdfPage = pdf.Pages[CurrentPdfPageIndex];
            UpdatePageStatus();
        }
    }

    [RelayCommand]
    public void PrevPage()
    {
        if (IsPdf && PreviewContent is PdfPreviewModel pdf && CurrentPdfPageIndex > 0)
        {
            CurrentPdfPageIndex--;
            CurrentPdfPage = pdf.Pages[CurrentPdfPageIndex];
            UpdatePageStatus();
        }
    }

    private void UpdatePageStatus()
    {
        if (IsPdf)
        {
            PageStatusText = $"{CurrentPdfPageIndex + 1} / {PdfPageCount}";
        }
    }

    [RelayCommand]
    private void ToggleTopMost()
    {
        IsTopMost = !IsTopMost;
    }
}

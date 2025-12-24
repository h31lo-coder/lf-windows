using System;
using Avalonia;
using Avalonia.Controls;
using LfWindows.Models;

namespace LfWindows.Controls;

public partial class PdfPageView : UserControl
{
    private bool _isAttached;

    public PdfPageView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        TriggerLoad();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        if (DataContext is PdfPageModel model)
        {
            model.Unload();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_isAttached)
        {
            TriggerLoad();
        }
    }

    private void TriggerLoad()
    {
        if (DataContext is PdfPageModel model)
        {
            // Fire and forget
            _ = model.LoadAsync();
        }
    }
}

using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LfWindows.Models;

public partial class OfficePreviewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private string _path;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    public string Extension => System.IO.Path.GetExtension(Path).ToLower();

    public OfficePreviewModel(string path, Bitmap? thumbnail = null)
    {
        _path = path;
        _thumbnail = thumbnail;
    }

    public void Dispose()
    {
        if (Thumbnail != null)
        {
            Thumbnail.Dispose();
            Thumbnail = null;
        }
    }
}

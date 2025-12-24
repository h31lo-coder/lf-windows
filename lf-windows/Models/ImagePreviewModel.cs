using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace LfWindows.Models;

public class ImagePreviewModel : IDisposable
{
    public IImage Image { get; set; }
    public Dictionary<string, string> Metadata { get; set; }

    public ImagePreviewModel(IImage image, Dictionary<string, string> metadata)
    {
        Image = image;
        Metadata = metadata;
    }

    public void Dispose()
    {
        if (Image is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

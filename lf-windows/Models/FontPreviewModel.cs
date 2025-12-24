using Avalonia.Media.Imaging;

namespace LfWindows.Models;

public class FontPreviewModel
{
    public string FontFamilyName { get; }
    public Bitmap PreviewImage { get; }

    public FontPreviewModel(string fontFamilyName, Bitmap previewImage)
    {
        FontFamilyName = fontFamilyName;
        PreviewImage = previewImage;
    }
}

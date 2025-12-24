using Avalonia.Media.Imaging;
using System.Threading.Tasks;

namespace LfWindows.Services;

public enum IconSize
{
    Small,      // 16x16
    Large,      // 32x32
    ExtraLarge, // 48x48
    Jumbo       // 256x256
}

public interface IIconProvider
{
    Task<Bitmap?> GetFileIconAsync(string filePath, IconSize size);
}

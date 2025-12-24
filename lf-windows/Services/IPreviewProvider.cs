using System.Threading;
using System.Threading.Tasks;

namespace LfWindows.Services;

public enum PreviewMode
{
    Default,    // 智能选择（优先缩略图）
    Thumbnail,  // 强制缩略图
    Full        // 强制完整预览
}

public interface IPreviewProvider
{
    bool CanPreview(string filePath);
    Task<object> GeneratePreviewAsync(string filePath, CancellationToken token = default);
    // 新增重载，支持指定模式
    Task<object> GeneratePreviewAsync(string filePath, PreviewMode mode, CancellationToken token = default) => GeneratePreviewAsync(filePath, token);
}

using System;
using System.IO;
using System.Threading.Tasks;
using LfWindows.Models;

namespace LfWindows.Services;

public class VideoPreviewProvider : IPreviewProvider
{
    private readonly PreviewCacheService _cacheService;
    private readonly System.Threading.SemaphoreSlim _semaphore = new(1, 1);

    public VideoPreviewProvider(PreviewCacheService cacheService)
    {
        _cacheService = cacheService;
    }

    public VideoPreviewProvider() : this(new PreviewCacheService()) { }

    public bool CanPreview(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLower();
        return ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".mov" || ext == ".webm" || ext == ".wmv" || ext == ".flv";
    }

    public async Task<object> GeneratePreviewAsync(string filePath, System.Threading.CancellationToken token = default)
    {
        return await Task.Run<object>(async () =>
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
                return (object)new VideoPreviewModel(filePath);
            }
            catch (Exception ex)
            {
                return (object)$"Error initializing video preview: {ex.Message}";
            }
            finally
            {
                _semaphore.Release();
            }
        });
    }
}

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace LfWindows.Services;

public class PreviewCacheService
{
    private readonly string _cacheRoot;
    private const long MaxCacheSize = 500 * 1024 * 1024; // 500 MB

    public PreviewCacheService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cacheRoot = Path.Combine(localAppData, "lf-windows", "cache");
        
        if (!Directory.Exists(_cacheRoot))
        {
            Directory.CreateDirectory(_cacheRoot);
        }

        // Run cleanup in background
        Task.Run(CleanupCacheAsync);
    }

    private async Task CleanupCacheAsync()
    {
        try
        {
            var dirInfo = new DirectoryInfo(_cacheRoot);
            if (!dirInfo.Exists) return;

            // Calculate total size
            var files = dirInfo.GetFiles("*", SearchOption.AllDirectories)
                             .OrderBy(f => f.LastAccessTimeUtc) // Oldest accessed first
                             .ToList();

            long currentSize = files.Sum(f => f.Length);

            if (currentSize > MaxCacheSize)
            {
                long sizeToRemove = currentSize - (long)(MaxCacheSize * 0.8); // Reduce to 80%
                long removedSize = 0;

                foreach (var file in files)
                {
                    if (removedSize >= sizeToRemove) break;

                    try
                    {
                        long len = file.Length;
                        file.Delete();
                        removedSize += len;
                    }
                    catch
                    {
                        // Ignore file lock errors
                    }
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    public string GetCachePath(string filePath, string category, string extension)
    {
        string hash = ComputeFileHash(filePath);
        return GetCachePathByKey(hash, category, extension);
    }

    public string GetCachePathByKey(string key, string category, string extension)
    {
        string categoryDir = Path.Combine(_cacheRoot, category);
        
        if (!Directory.Exists(categoryDir))
        {
            Directory.CreateDirectory(categoryDir);
        }

        return Path.Combine(categoryDir, $"{key}{extension}");
    }

    public bool TryGetCachedFile(string filePath, string category, string extension, out string cachedPath)
    {
        cachedPath = GetCachePath(filePath, category, extension);
        return File.Exists(cachedPath);
    }

    public bool TryGetCachedFileByKey(string key, string category, string extension, out string cachedPath)
    {
        cachedPath = GetCachePathByKey(key, category, extension);
        return File.Exists(cachedPath);
    }

    public string ComputeFileHash(string filePath)
    {
        // Hash based on path + last write time + size
        // This ensures that if the file is modified, the hash changes
        var info = new FileInfo(filePath);
        string input = $"{filePath}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
        
        using var md5 = MD5.Create();
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = md5.ComputeHash(inputBytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }
}

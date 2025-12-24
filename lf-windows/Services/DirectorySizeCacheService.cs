using System;
using System.Collections.Generic;
using System.Linq;

namespace LfWindows.Services;

public class DirectorySizeCacheService
{
    private class CacheEntry
    {
        public long Size { get; set; }
        public DateTime LastAccessTime { get; set; }
    }

    private readonly Dictionary<string, CacheEntry> _cache = new();
    private readonly object _lock = new();
    
    // Maximum number of folders to keep in cache
    private const int MaxCapacity = 5000;
    // Number of items to remove when limit is reached
    private const int TrimCount = 1000;

    public bool TryGetSize(string path, out long size)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var entry))
            {
                // Update access time (LRU)
                entry.LastAccessTime = DateTime.UtcNow;
                size = entry.Size;
                return true;
            }
            size = -1;
            return false;
        }
    }

    public void UpdateSize(string path, long size)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var entry))
            {
                entry.Size = size;
                entry.LastAccessTime = DateTime.UtcNow;
            }
            else
            {
                if (_cache.Count >= MaxCapacity)
                {
                    TrimCache();
                }
                _cache[path] = new CacheEntry { Size = size, LastAccessTime = DateTime.UtcNow };
            }
        }
    }

    private void TrimCache()
    {
        // Remove the oldest items based on LastAccessTime
        var itemsToRemove = _cache.OrderBy(kvp => kvp.Value.LastAccessTime)
                                  .Take(TrimCount)
                                  .Select(kvp => kvp.Key)
                                  .ToList();

        foreach (var key in itemsToRemove)
        {
            _cache.Remove(key);
        }
    }

    public void Invalidate(string path)
    {
        lock (_lock)
        {
            _cache.Remove(path);
        }
    }
    
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
    }
}

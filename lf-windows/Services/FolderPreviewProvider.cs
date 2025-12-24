using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LfWindows.Models;

namespace LfWindows.Services;

public class FolderPreviewProvider : IPreviewProvider
{
    private readonly IIconProvider _iconProvider;

    public FolderPreviewProvider(IIconProvider iconProvider)
    {
        _iconProvider = iconProvider;
    }

    public bool CanPreview(string filePath)
    {
        return Directory.Exists(filePath);
    }

    public async Task<object> GeneratePreviewAsync(string filePath, CancellationToken token = default)
    {
        return await GeneratePreviewAsync(filePath, PreviewMode.Default, token);
    }

    public async Task<object> GeneratePreviewAsync(string filePath, PreviewMode mode, CancellationToken token = default)
    {
        return await Task.Run<object>(async () =>
        {
            try
            {
                var items = new List<FileSystemItem>();
                var dirInfo = new DirectoryInfo(filePath);
                var comparer = new NaturalStringComparer();

                foreach (var entry in dirInfo.GetFileSystemInfos())
                {
                    if (token.IsCancellationRequested) break;

                    var item = new FileSystemItem
                    {
                        Name = entry.Name,
                        Path = entry.FullName,
                        Type = entry is DirectoryInfo ? FileType.Directory : FileType.File,
                        Size = entry is FileInfo f ? f.Length : 0,
                        Modified = entry.LastWriteTime,
                        Extension = entry.Extension
                    };

                    // Load icon
                    // Note: IconProvider might need to run on UI thread depending on implementation, 
                    // but usually Bitmap creation is fine on background thread if not attached to visual tree yet.
                    // However, if IconProvider uses Shell API that requires STA, we might have issues.
                    // Assuming IconProvider is thread-safe or handles marshaling.
                    item.Icon = await _iconProvider.GetFileIconAsync(entry.FullName, IconSize.Small);

                    items.Add(item);
                }

                // Sort: Directories first, then files. Alphabetical.
                items.Sort((a, b) =>
                {
                    if (a.Type == b.Type)
                        return comparer.Compare(a.Name, b.Name);
                    return a.Type == FileType.Directory ? -1 : 1;
                });

                return (object)new FolderPreviewResult(items, filePath);
            }
            catch (Exception ex)
            {
                return (object)$"Error reading directory: {ex.Message}";
            }
        }, token);
    }
}

public class FolderPreviewResult
{
    public List<FileSystemItem> Items { get; }
    public string Path { get; }

    public FolderPreviewResult(List<FileSystemItem> items, string path)
    {
        Items = items;
        Path = path;
    }
}

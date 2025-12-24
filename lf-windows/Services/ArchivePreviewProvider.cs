using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LfWindows.Models;
using LfWindows.ViewModels;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace LfWindows.Services;

public class ArchivePreviewProvider : IPreviewProvider
{
    private readonly IIconProvider _iconProvider;

    public ArchivePreviewProvider(IIconProvider iconProvider)
    {
        _iconProvider = iconProvider;
    }

    public bool CanPreview(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLower();
        return ext == ".zip" || ext == ".7z" || ext == ".rar" || ext == ".tar" || ext == ".gz" || ext == ".bz2";
    }

    public async Task<object> GeneratePreviewAsync(string filePath, System.Threading.CancellationToken token = default)
    {
        return await Task.Run<object>(() =>
        {
            try
            {
                var items = new List<FileSystemItem>();
                
                // Use SharpCompress to read archive headers only (fast)
                using var archive = ArchiveFactory.Open(filePath);
                
                // We want to show a flat list or a simple tree. 
                // For simplicity in preview, let's show the top-level items and maybe flatten the structure 
                // or just show all entries if count is small.
                // Let's try to simulate a "root" directory view of the archive.
                
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    // For now, let's just list all files to see what's inside.
                    // In a real "virtual file system" we would handle hierarchy.
                    // Here we just want to see contents.
                    
                    var item = new FileSystemItem
                    {
                        Name = entry.Key ?? string.Empty, // This contains the full path inside zip
                        Path = "archive://" + (entry.Key ?? string.Empty), // Virtual path
                        Type = FileType.File,
                        Size = entry.Size,
                        Modified = entry.LastModifiedTime ?? DateTime.MinValue,
                        Extension = Path.GetExtension(entry.Key ?? string.Empty) ?? string.Empty
                    };
                    
                    // Try to get icon based on extension
                    // We can't get real system icon for a virtual file easily without extracting,
                    // but we can try to get icon for a dummy file with same extension.
                    // For now, let's skip icon or use a generic one to avoid UI thread issues here 
                    // (IconProvider usually needs UI thread or Dispatcher if it uses UI objects).
                    // Actually our IconProvider returns Bitmap, which is UI object. 
                    // We should probably load icons on UI thread or use a helper.
                    
                    items.Add(item);
                }

                // Sort by name
                items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                // Create a ViewModel to reuse the ListBox template
                // We can reuse FileListViewModel or create a simple list.
                // Since MainWindow expects a FileListViewModel for the preview content in DataTemplate:
                // <DataTemplate DataType="vm:FileListViewModel">
                
                // We need to create the VM on UI thread usually, but let's see.
                // We can return a list of items, but the View expects specific types.
                
                // Let's return a FileListViewModel populated with these items.
                // We need to mock the services since this VM won't do real navigation.
                
                return (object)new ArchivePreviewResult(items);
            }
            catch (Exception ex)
            {
                return (object)$"Error reading archive: {ex.Message}";
            }
        });
    }
}

// Simple wrapper to hold items, we will convert this to VM in the View/ViewModel layer if needed
// or we can just return FileListViewModel directly if we can instantiate it.
public class ArchivePreviewResult : IDisposable
{
    public List<FileSystemItem> Items { get; }
    public ArchivePreviewResult(List<FileSystemItem> items)
    {
        Items = items;
    }

    public void Dispose()
    {
        if (Items != null)
        {
            foreach (var item in Items)
            {
                if (item.Icon != null)
                {
                    item.Icon.Dispose();
                    item.Icon = null;
                }
            }
        }
    }
}

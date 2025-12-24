using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LfWindows.Models;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace LfWindows.Services;

public static class ArchiveFileSystemHelper
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2"
    };

    public static bool IsArchiveExtension(string path)
    {
        return SupportedExtensions.Contains(Path.GetExtension(path));
    }

    /// <summary>
    /// Determines if the path points to an archive file that should be treated as a directory,
    /// or if it points to a file/folder inside an archive.
    /// </summary>
    public static bool IsArchivePath(string path, out string archiveRootPath, out string internalPath)
    {
        archiveRootPath = string.Empty;
        internalPath = string.Empty;

        try
        {
            if (string.IsNullOrEmpty(path)) return false;

            // Case 1: The path itself is an archive file on disk
            if (File.Exists(path) && IsArchiveExtension(path))
            {
                archiveRootPath = path;
                internalPath = string.Empty;
                return true;
            }

            // Case 2: The path is inside an archive
            // We need to walk up the path to find the archive root
            string current = path;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(current) && IsArchiveExtension(current))
                {
                    archiveRootPath = current;
                    // internalPath is the rest of the original path
                    // e.g. path = C:\a.zip\folder\file, root = C:\a.zip
                    // internalPath = folder\file
                    if (path.Length > current.Length)
                    {
                        internalPath = path.Substring(current.Length + 1); // +1 for separator
                    }
                    return true;
                }
                current = Path.GetDirectoryName(current) ?? string.Empty;
            }
        }
        catch
        {
            // Ignore errors
        }

        return false;
    }

    public static List<FileSystemItem> ListArchiveDirectory(string fullPath, string archiveRootPath, string internalPath)
    {
        var items = new List<FileSystemItem>();

        try
        {
            using var archive = ArchiveFactory.Open(archiveRootPath);
            
            // Normalize internal path for comparison
            // SharpCompress usually uses forward slashes or matches the entry key
            // We should handle both separators
            string targetDir = internalPath.Replace('\\', '/').Trim('/');
            
            // If targetDir is empty, we are at root
            
            var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
            var directories = new HashSet<string>();

            foreach (var entry in entries)
            {
                string entryKey = entry.Key?.Replace('\\', '/') ?? string.Empty;
                
                // Check if this entry is inside the target directory
                if (IsInDirectory(entryKey, targetDir, out string childName, out bool isDirectChild))
                {
                    if (isDirectChild)
                    {
                        // It's a file in the current directory
                        items.Add(new FileSystemItem
                        {
                            Name = childName,
                            Path = Path.Combine(fullPath, childName),
                            Type = FileType.File,
                            Size = entry.Size,
                            Modified = entry.LastModifiedTime ?? DateTime.MinValue,
                            Extension = Path.GetExtension(childName) ?? string.Empty,
                            Permissions = "r--------" // Read-only for now
                        });
                    }
                    else
                    {
                        // It's a file in a subdirectory, so we need to add the subdirectory
                        // childName will be "subdir/file.txt", we want "subdir"
                        string subDirName = childName.Split('/')[0];
                        if (!directories.Contains(subDirName))
                        {
                            directories.Add(subDirName);
                            items.Add(new FileSystemItem
                            {
                                Name = subDirName,
                                Path = Path.Combine(fullPath, subDirName),
                                Type = FileType.Directory,
                                Modified = entry.LastModifiedTime ?? DateTime.MinValue,
                                Permissions = "dr-xr-xr-x"
                            });
                        }
                    }
                }
            }
            
            // Also check for explicit directory entries if the archive format supports them
            foreach (var entry in archive.Entries.Where(e => e.IsDirectory))
            {
                string entryKey = entry.Key?.Replace('\\', '/').Trim('/') ?? string.Empty;
                 if (IsInDirectory(entryKey, targetDir, out string childName, out bool isDirectChild))
                 {
                     if (isDirectChild)
                     {
                         if (!directories.Contains(childName))
                         {
                             directories.Add(childName);
                             items.Add(new FileSystemItem
                             {
                                 Name = childName,
                                 Path = Path.Combine(fullPath, childName),
                                 Type = FileType.Directory,
                                 Modified = entry.LastModifiedTime ?? DateTime.MinValue,
                                 Permissions = "dr-xr-xr-x"
                             });
                         }
                     }
                 }
            }
        }
        catch (Exception ex)
        {
            // Log or handle error
            System.Diagnostics.Debug.WriteLine($"Error listing archive: {ex.Message}");
        }

        return items;
    }

    private static bool IsInDirectory(string entryPath, string targetDir, out string childName, out bool isDirectChild)
    {
        childName = string.Empty;
        isDirectChild = false;

        if (string.IsNullOrEmpty(targetDir))
        {
            // We are at root, so everything is a child
            childName = entryPath;
            isDirectChild = !childName.Contains('/');
            return true;
        }

        // Check if entryPath starts with targetDir + /
        string prefix = targetDir + "/";
        if (entryPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            childName = entryPath.Substring(prefix.Length);
            isDirectChild = !childName.Contains('/');
            return true;
        }

        return false;
    }

    public static FileSystemItem? GetArchiveItem(string fullPath, string archiveRootPath, string internalPath)
    {
        try
        {
            using var archive = ArchiveFactory.Open(archiveRootPath);
            string targetKey = internalPath.Replace('\\', '/').Trim('/');

            // Try to find exact match
            var entry = archive.Entries.FirstOrDefault(e => 
                (e.Key?.Replace('\\', '/').Trim('/') ?? string.Empty).Equals(targetKey, StringComparison.OrdinalIgnoreCase));

            if (entry != null)
            {
                return new FileSystemItem
                {
                    Name = Path.GetFileName(fullPath),
                    Path = fullPath,
                    Type = entry.IsDirectory ? FileType.Directory : FileType.File,
                    Size = entry.Size,
                    Modified = entry.LastModifiedTime ?? DateTime.MinValue,
                    Extension = Path.GetExtension(fullPath) ?? string.Empty,
                    Permissions = entry.IsDirectory ? "dr-xr-xr-x" : "r--------"
                };
            }
            
            // If not found as an entry, it might be a virtual directory implied by files
            // e.g. zip contains "folder/file.txt" but no explicit "folder/" entry.
            // We check if any file starts with this path
            string prefix = targetKey + "/";
            if (archive.Entries.Any(e => (e.Key?.Replace('\\', '/').Trim('/') ?? string.Empty).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                 return new FileSystemItem
                {
                    Name = Path.GetFileName(fullPath),
                    Path = fullPath,
                    Type = FileType.Directory,
                    Modified = DateTime.Now, // Unknown
                    Permissions = "dr-xr-xr-x"
                };
            }
        }
        catch
        {
            // Ignore
        }
        return null;
    }

    public static Stream? OpenStream(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return null;

        if (!IsArchivePath(fullPath, out string archiveRootPath, out string internalPath) || string.IsNullOrEmpty(internalPath))
            return null;

        try
        {
            var archive = ArchiveFactory.Open(archiveRootPath);
            if (archive == null) return null;

            string targetKey = internalPath.Replace('\\', '/').Trim('/');
            
            if (archive.Entries == null)
            {
                archive.Dispose();
                return null;
            }

            var entry = archive.Entries.FirstOrDefault(e => 
                e != null &&
                !e.IsDirectory && 
                (e.Key?.Replace('\\', '/').Trim('/') ?? string.Empty).Equals(targetKey, StringComparison.OrdinalIgnoreCase));

            if (entry != null)
            {
                var ms = new MemoryStream();
                using (var entryStream = entry.OpenEntryStream())
                {
                    entryStream.CopyTo(ms);
                }
                ms.Position = 0;
                archive.Dispose(); // Dispose archive after reading
                return ms;
            }
            archive.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening archive stream: {ex.Message}");
        }
        return null;
    }
}

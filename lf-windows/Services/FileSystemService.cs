using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LfWindows.Models;

namespace LfWindows.Services;

public class FileSystemService : IFileSystemService
{
    public async Task<IEnumerable<FileSystemItem>> ListDirectoryAsync(string path)
    {
        return await Task.Run(() =>
        {
            // Check for archive navigation
            if (ArchiveFileSystemHelper.IsArchivePath(path, out string archiveRoot, out string internalPath))
            {
                return ArchiveFileSystemHelper.ListArchiveDirectory(path, archiveRoot, internalPath);
            }

            var items = new List<FileSystemItem>();
            try
            {
                var dirInfo = new DirectoryInfo(path);
                
                foreach (var dir in dirInfo.GetDirectories())
                {
                    // Skip system junctions (Hidden + System + ReparsePoint) which are usually legacy compatibility links
                    if ((dir.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    items.Add(new FileSystemItem
                    {
                        Name = dir.Name,
                        Path = dir.FullName,
                        Type = FileType.Directory,
                        Modified = dir.LastWriteTime,
                        CreationTime = dir.CreationTime,
                        LastAccessTime = dir.LastAccessTime,
                        IsHidden = (dir.Attributes & FileAttributes.Hidden) != 0,
                        IsSystem = (dir.Attributes & FileAttributes.System) != 0,
                        Permissions = GetPermissionsString(dir)
                    });
                }

                foreach (var file in dirInfo.GetFiles())
                {
                    items.Add(new FileSystemItem
                    {
                        Name = file.Name,
                        Path = file.FullName,
                        Type = FileType.File,
                        Size = file.Length,
                        Modified = file.LastWriteTime,
                        CreationTime = file.CreationTime,
                        LastAccessTime = file.LastAccessTime,
                        Extension = file.Extension,
                        IsHidden = (file.Attributes & FileAttributes.Hidden) != 0,
                        IsSystem = (file.Attributes & FileAttributes.System) != 0,
                        Permissions = GetPermissionsString(file)
                    });
                }
            }
            catch (Exception)
            {
                // Handle access denied etc.
            }
            return items;
        });
    }

    private string GetPermissionsString(FileSystemInfo info)
    {
        var attr = info.Attributes;
        char d = (attr & FileAttributes.Directory) != 0 ? 'd' : '-';
        // Logic: If ReadOnly attribute is set, show 'r'. Otherwise show '-'.
        char r = (attr & FileAttributes.ReadOnly) != 0 ? 'r' : '-';
        char w = (attr & FileAttributes.ReadOnly) != 0 ? '-' : 'w';
        char h = (attr & FileAttributes.Hidden) != 0 ? 'h' : '-';
        char s = (attr & FileAttributes.System) != 0 ? 's' : '-';
        char a = (attr & FileAttributes.Archive) != 0 ? 'a' : '-';
        return $"{d}{r}{w}{h}{s}{a}";
    }

    public Task<FileSystemItem?> GetItemAsync(string path)
    {
        return Task.Run<FileSystemItem?>(() =>
        {
            if (ArchiveFileSystemHelper.IsArchivePath(path, out string archiveRoot, out string internalPath))
            {
                // If internalPath is empty, it means we are pointing to the archive file itself (e.g. C:\a.zip)
                // We can treat it as a directory if we are "inside" it, but here we are getting the item itself.
                // If the caller wants the "directory" view of the zip, they call ListDirectoryAsync.
                // If they want the item properties, we return the file properties if it's the root,
                // or the internal item properties if it's inside.
                
                if (string.IsNullOrEmpty(internalPath))
                {
                    // It is the archive file itself. Return as file (or directory? usually file properties)
                    // But wait, if we are navigating "Up" to it, we might want it to look like a directory?
                    // No, on disk it is a file.
                    // However, if we are inside it, and we ask for "C:\a.zip", we might be asking for the "root" folder item.
                    // But usually GetItemAsync is used to get info about a child.
                    // Let's fall through to standard file handling for the root archive file, 
                    // unless we want to force it to be Type=Directory?
                    // For now, let's let standard file handling take care of the archive file itself,
                    // and only handle internal paths here.
                }
                else
                {
                    return ArchiveFileSystemHelper.GetArchiveItem(path, archiveRoot, internalPath);
                }
            }

            if (Directory.Exists(path))
            {
                var dir = new DirectoryInfo(path);
                return new FileSystemItem
                {
                    Name = dir.Name,
                    Path = dir.FullName,
                    Type = FileType.Directory,
                    Modified = dir.LastWriteTime,
                    CreationTime = dir.CreationTime,
                    LastAccessTime = dir.LastAccessTime,
                    IsHidden = (dir.Attributes & FileAttributes.Hidden) != 0,
                    IsSystem = (dir.Attributes & FileAttributes.System) != 0,
                    Permissions = GetPermissionsString(dir)
                };
            }
            else if (File.Exists(path))
            {
                var file = new FileInfo(path);
                return new FileSystemItem
                {
                    Name = file.Name,
                    Path = file.FullName,
                    Type = FileType.File,
                    Size = file.Length,
                    Modified = file.LastWriteTime,
                    CreationTime = file.CreationTime,
                    LastAccessTime = file.LastAccessTime,
                    Extension = file.Extension,
                    IsHidden = (file.Attributes & FileAttributes.Hidden) != 0,
                    IsSystem = (file.Attributes & FileAttributes.System) != 0,
                    Permissions = GetPermissionsString(file)
                };
            }
            return null;
        });
    }

    public bool Exists(string path)
    {
        return Directory.Exists(path) || File.Exists(path);
    }

    public bool IsDirectory(string path)
    {
        return Directory.Exists(path);
    }

    public string? GetParentPath(string path)
    {
        // Handle virtual paths
        // If path is C:\a.zip\folder, parent is C:\a.zip
        // If path is C:\a.zip, parent is C:\
        // Path.GetDirectoryName handles this string manipulation correctly for both cases
        return Path.GetDirectoryName(path);
    }

    public async Task CopyAsync(string sourcePath, string destinationPath)
    {
        await Task.Run(() =>
        {
            if (IsDirectory(sourcePath))
            {
                CopyDirectory(sourcePath, destinationPath);
            }
            else
            {
                File.Copy(sourcePath, destinationPath, true);
            }
        });
    }

    private void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

        Directory.CreateDirectory(destinationDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }

    public async Task MoveAsync(string sourcePath, string destinationPath)
    {
        await Task.Run(() =>
        {
            if (IsDirectory(sourcePath))
            {
                Directory.Move(sourcePath, destinationPath);
            }
            else
            {
                File.Move(sourcePath, destinationPath);
            }
        });
    }

    public async Task DeleteAsync(string path)
    {
        await Task.Run(() =>
        {
            try 
            {
                if (IsDirectory(path))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                else
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception ex)
            {
                // Fallback or rethrow if recycle bin fails (e.g. network drive)
                // For now, we just rethrow to let the caller handle it
                throw new IOException($"Failed to delete {path} to recycle bin.", ex);
            }
        });
    }

    public async Task RenameAsync(string path, string newName)
    {
        await Task.Run(() =>
        {
            string? parent = Path.GetDirectoryName(path);
            if (parent == null) return;
            
            string newPath = Path.Combine(parent, newName);
            if (IsDirectory(path))
            {
                Directory.Move(path, newPath);
            }
            else
            {
                File.Move(path, newPath);
            }
        });
    }

    public async Task CreateDirectoryAsync(string path)
    {
        await Task.Run(() =>
        {
            Directory.CreateDirectory(path);
        });
    }

    public async Task<long> GetDirectorySizeAsync(string path)
    {
        return await Task.Run(() =>
        {
            try
            {
                var dir = new DirectoryInfo(path);
                return dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        });
    }
}

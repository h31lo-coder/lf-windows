using System.Collections.Generic;
using System.Threading.Tasks;
using LfWindows.Models;

namespace LfWindows.Services;

public interface IFileSystemService
{
    Task<IEnumerable<FileSystemItem>> ListDirectoryAsync(string path);
    Task<FileSystemItem?> GetItemAsync(string path);
    bool Exists(string path);
    bool IsDirectory(string path);
    string? GetParentPath(string path);

    // File Operations
    Task CopyAsync(string sourcePath, string destinationPath);
    Task MoveAsync(string sourcePath, string destinationPath);
    Task DeleteAsync(string path);
    Task RenameAsync(string path, string newName);
    Task CreateDirectoryAsync(string path);
    Task<long> GetDirectorySizeAsync(string path);
}

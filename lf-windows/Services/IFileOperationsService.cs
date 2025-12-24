using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LfWindows.Models;

namespace LfWindows.Services;

public interface IFileOperationsService
{
    Task CopyAsync(IEnumerable<string> sourcePaths, string destinationDir, CancellationToken cancellationToken = default);
    Task MoveAsync(IEnumerable<string> sourcePaths, string destinationDir, CancellationToken cancellationToken = default);
    Task DeleteAsync(IEnumerable<string> paths, bool permanent = false, CancellationToken cancellationToken = default);
    Task RenameAsync(string path, string newName);
    Task CreateDirectoryAsync(string path);
    Task CreateFileAsync(string path);
    Task CreateShortcutAsync(IEnumerable<string> sourcePaths, string destinationDir, CancellationToken cancellationToken = default);
    
    void ClearUndoHistory();
    bool CanUndoLastDelete(string currentDirectory);
    Task UndoLastDeleteAsync();
}

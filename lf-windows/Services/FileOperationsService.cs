using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using LfWindows.Interop;
using LfWindows.Models;
using Excel = NetOffice.ExcelApi;
using Word = NetOffice.WordApi;
using PowerPoint = NetOffice.PowerPointApi;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace LfWindows.Services;

public class FileOperationsService : IFileOperationsService
{
    private class DeleteOperationSnapshot
    {
        public string OriginalDirectory { get; set; } = string.Empty;
        public List<string> DeletedFileNames { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    private DeleteOperationSnapshot? _lastDeleteSnapshot;
    private readonly RecycleBinService _recycleBinService = new();
    private readonly IConfigService _configService;

    public FileOperationsService(IConfigService configService)
    {
        _configService = configService;
    }

    public void ClearUndoHistory()
    {
        _lastDeleteSnapshot = null;
    }

    public bool CanUndoLastDelete(string currentDirectory)
    {
        bool canUndo = _lastDeleteSnapshot != null && 
               string.Equals(_lastDeleteSnapshot.OriginalDirectory, currentDirectory, StringComparison.OrdinalIgnoreCase);
        return canUndo;
    }

    public async Task UndoLastDeleteAsync()
    {
        if (_lastDeleteSnapshot == null) 
        {
            return;
        }

        foreach (var fileName in _lastDeleteSnapshot.DeletedFileNames)
        {
            bool success = await _recycleBinService.RestoreFileAsync(fileName, _lastDeleteSnapshot.OriginalDirectory);
            
            // Verify existence
            string fullPath = Path.Combine(_lastDeleteSnapshot.OriginalDirectory, fileName);
        }
        
        ClearUndoHistory();
    }

    public async Task CopyAsync(IEnumerable<string> sourcePaths, string destinationDir, CancellationToken cancellationToken = default)
    {
        ClearUndoHistory();
        foreach (var sourcePath in sourcePaths)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string name = Path.GetFileName(sourcePath);
            string destPath = Path.Combine(destinationDir, name);

            // Check for Archive Path
            if (ArchiveFileSystemHelper.IsArchivePath(sourcePath, out string archiveRoot, out string internalPath) && !string.IsNullOrEmpty(internalPath))
            {
                 await CopyFromArchiveAsync(sourcePath, destPath, cancellationToken);
                 continue;
            }

            if (File.Exists(sourcePath))
            {
                await CopyFileAsync(sourcePath, destPath, cancellationToken);
            }
            else if (Directory.Exists(sourcePath))
            {
                await CopyDirectoryAsync(sourcePath, destPath, cancellationToken);
            }
        }
    }

    private async Task CopyFromArchiveAsync(string sourcePath, string destPath, CancellationToken token)
    {
        if (!ArchiveFileSystemHelper.IsArchivePath(sourcePath, out string archiveRoot, out string internalPath)) return;

        destPath = EnsureUniquePath(destPath);

        await Task.Run(() => 
        {
            try
            {
                using var archive = ArchiveFactory.Open(archiveRoot);
                string targetKey = internalPath.Replace('\\', '/').Trim('/');
                
                // Check for exact file match
                var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && (e.Key?.Replace('\\', '/').Trim('/') ?? "").Equals(targetKey, StringComparison.OrdinalIgnoreCase));
                
                if (entry != null)
                {
                    // It's a file
                    using var entryStream = entry.OpenEntryStream();
                    using var destStream = File.Create(destPath);
                    entryStream.CopyTo(destStream);
                    return;
                }
                
                // Check for directory (either explicit entry or implied)
                string prefix = targetKey + "/";
                var childEntries = archive.Entries.Where(e => (e.Key?.Replace('\\', '/').Trim('/') ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                
                if (childEntries.Any())
                {
                    // It's a directory
                    Directory.CreateDirectory(destPath);
                    
                    foreach (var child in childEntries)
                    {
                        if (child.IsDirectory || child.Key == null) continue; 
                        
                        string childKey = child.Key.Replace('\\', '/').Trim('/');
                        string relativePath = childKey.Substring(prefix.Length);
                        string childDestPath = Path.Combine(destPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                        
                        string? childDir = Path.GetDirectoryName(childDestPath);
                        if (!string.IsNullOrEmpty(childDir)) Directory.CreateDirectory(childDir);
                        
                        using var entryStream = child.OpenEntryStream();
                        using var destStream = File.Create(childDestPath);
                        entryStream.CopyTo(destStream);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error copying from archive: {ex.Message}");
            }
        }, token);
    }

    private async Task CopyFileAsync(string source, string dest, CancellationToken token)
    {
        // Simple copy for now. In production, we'd want progress reporting and conflict handling.
        // Ensure unique name if exists
        dest = EnsureUniquePath(dest);
        
        await Task.Run(() => File.Copy(source, dest), token);
    }

    private async Task CopyDirectoryAsync(string source, string dest, CancellationToken token)
    {
        dest = EnsureUniquePath(dest);
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
        {
            string destFile = Path.Combine(dest, Path.GetFileName(file));
            await CopyFileAsync(file, destFile, token);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            string destDir = Path.Combine(dest, Path.GetFileName(dir));
            await CopyDirectoryAsync(dir, destDir, token);
        }
    }

    public async Task MoveAsync(IEnumerable<string> sourcePaths, string destinationDir, CancellationToken cancellationToken = default)
    {
        ClearUndoHistory();
        foreach (var sourcePath in sourcePaths)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string name = Path.GetFileName(sourcePath);
            string destPath = Path.Combine(destinationDir, name);
            destPath = EnsureUniquePath(destPath);

            await Task.Run(() => 
            {
                if (File.Exists(sourcePath))
                {
                    File.Move(sourcePath, destPath);
                }
                else if (Directory.Exists(sourcePath))
                {
                    Directory.Move(sourcePath, destPath);
                }

                // Update Bookmarks
                UpdateBookmarks(sourcePath, destPath);
                UpdateYankHistory(sourcePath, destPath);
            }, cancellationToken);
        }
    }

    public async Task DeleteAsync(IEnumerable<string> paths, bool permanent = false, CancellationToken cancellationToken = default)
    {
        var pathList = paths.ToList();
        if (pathList.Count == 0) return;

        if (!permanent && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var tcs = new TaskCompletionSource<bool>();
            var thread = new Thread(() =>
            {
                try
                {
                    // Join paths with null terminator and double null terminator at the end
                    // Also ensure paths use backslashes
                    var normalizedPaths = pathList.Select(p => p.Replace('/', '\\')).ToList();
                    
                    string pFromString = string.Join("\0", normalizedPaths) + "\0\0";
                    IntPtr pFrom = IntPtr.Zero;

                    try
                    {
                        pFrom = Marshal.StringToHGlobalUni(pFromString);
                        var shf = new Shell32Interop.SHFILEOPSTRUCT
                        {
                            wFunc = Shell32Interop.FO_DELETE,
                            pFrom = pFrom,
                            pTo = IntPtr.Zero,
                            fFlags = Shell32Interop.FOF_ALLOWUNDO | Shell32Interop.FOF_NOCONFIRMATION | Shell32Interop.FOF_NOERRORUI | Shell32Interop.FOF_SILENT,
                            fAnyOperationsAborted = 0
                        };

                        int result = Shell32Interop.SHFileOperation(ref shf);
                        
                        if (result != 0)
                        {
                            tcs.SetException(new Exception($"SHFileOperation failed with error code: {result}"));
                        }
                        else
                        {
                            tcs.SetResult(true);
                            // Remove Bookmarks for deleted items (Recycle Bin)
                            // Note: We might want to restore them if Undo is called, but for now just remove them to avoid broken links.
                            // Ideally, we should store the bookmark state in the Undo snapshot.
                            foreach(var p in normalizedPaths)
                            {
                                RemoveBookmarks(p);
                                RemoveFromYankHistory(p);
                            }
                        }
                    }
                    finally
                    {
                        if (pFrom != IntPtr.Zero)
                        {
                            Marshal.FreeHGlobal(pFrom);
                        }
                    }
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            await tcs.Task;

            // Record snapshot for Undo
            string? commonDir = Path.GetDirectoryName(pathList.First());
            if (commonDir != null)
            {
                _lastDeleteSnapshot = new DeleteOperationSnapshot
                {
                    OriginalDirectory = commonDir,
                    DeletedFileNames = pathList.Select(p => Path.GetFileName(p)).ToList(),
                    Timestamp = DateTime.Now
                };
            }
        }
        else
        {
            ClearUndoHistory();
            await Task.Run(() =>
            {
                foreach (var path in paths)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    else if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                    
                    // Remove Bookmarks
                    RemoveBookmarks(path);
                    RemoveFromYankHistory(path);
                }
            }, cancellationToken);
        }
    }

    public async Task RenameAsync(string path, string newName)
    {
        ClearUndoHistory();
        await Task.Run(() =>
        {
            string dir = Path.GetDirectoryName(path) ?? "";
            string newPath = Path.Combine(dir, newName);
            
            if (File.Exists(path))
            {
                File.Move(path, newPath);
            }
            else if (Directory.Exists(path))
            {
                Directory.Move(path, newPath);
            }

            // Update Bookmarks
            UpdateBookmarks(path, newPath);
            UpdateYankHistory(path, newPath);
        });
    }

    private void UpdateBookmarks(string oldPath, string newPath)
    {
        bool changed = false;
        var bookmarks = _configService.Current.Bookmarks;
        
        // Normalize paths for comparison
        string normOld = oldPath.Replace('/', '\\').TrimEnd('\\');
        string normNew = newPath.Replace('/', '\\').TrimEnd('\\');
        
        // Create a copy of keys to iterate safely
        var keys = bookmarks.Keys.ToList();
        
        foreach (var key in keys)
        {
            string bookmarkPath = bookmarks[key];
            string normBookmark = bookmarkPath.Replace('/', '\\').TrimEnd('\\');
            
            // Exact match (file or folder rename)
            if (string.Equals(normBookmark, normOld, StringComparison.OrdinalIgnoreCase))
            {
                bookmarks[key] = normNew;
                changed = true;
            }
            // Parent folder rename (bookmark is inside the renamed folder)
            else if (normBookmark.StartsWith(normOld + "\\", StringComparison.OrdinalIgnoreCase))
            {
                // Replace the start of the path
                string relativePath = normBookmark.Substring(normOld.Length);
                bookmarks[key] = normNew + relativePath;
                changed = true;
            }
        }

        if (changed)
        {
            _configService.Save();
        }
    }

    private void UpdateYankHistory(string oldPath, string newPath)
    {
        bool changed = false;
        var history = _configService.Current.YankHistory;
        if (history == null) return;

        string normOld = oldPath.Replace('/', '\\').TrimEnd('\\');
        string normNew = newPath.Replace('/', '\\').TrimEnd('\\');

        foreach (var item in history)
        {
            for (int i = 0; i < item.Count; i++)
            {
                string path = item[i];
                string normPath = path.Replace('/', '\\').TrimEnd('\\');

                if (string.Equals(normPath, normOld, StringComparison.OrdinalIgnoreCase))
                {
                    item[i] = newPath;
                    changed = true;
                }
                else if (normPath.StartsWith(normOld + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = normPath.Substring(normOld.Length);
                    item[i] = normNew + relativePath;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            _configService.Save();
        }
    }

    private void RemoveFromYankHistory(string path)
    {
        bool changed = false;
        var history = _configService.Current.YankHistory;
        if (history == null) return;

        string normPath = path.Replace('/', '\\').TrimEnd('\\');

        for (int i = history.Count - 1; i >= 0; i--)
        {
            var item = history[i];
            for (int j = item.Count - 1; j >= 0; j--)
            {
                string p = item[j];
                string normP = p.Replace('/', '\\').TrimEnd('\\');

                if (string.Equals(normP, normPath, StringComparison.OrdinalIgnoreCase) ||
                    normP.StartsWith(normPath + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    item.RemoveAt(j);
                    changed = true;
                }
            }

            if (item.Count == 0)
            {
                history.RemoveAt(i);
                changed = true;
            }
        }

        if (changed)
        {
            _configService.Save();
        }
    }

    private void RemoveBookmarks(string path)
    {
        bool changed = false;
        var bookmarks = _configService.Current.Bookmarks;
        var keys = bookmarks.Keys.ToList();
        
        string normPath = path.Replace('/', '\\').TrimEnd('\\');

        foreach (var key in keys)
        {
            string bookmarkPath = bookmarks[key];
            string normBookmark = bookmarkPath.Replace('/', '\\').TrimEnd('\\');
            
            // Exact match or inside deleted folder
            if (string.Equals(normBookmark, normPath, StringComparison.OrdinalIgnoreCase) ||
                normBookmark.StartsWith(normPath + "\\", StringComparison.OrdinalIgnoreCase))
            {
                bookmarks.Remove(key);
                changed = true;
            }
        }

        if (changed)
        {
            _configService.Save();
        }
    }

    public async Task CreateDirectoryAsync(string path)
    {
        ClearUndoHistory();
        await Task.Run(() => Directory.CreateDirectory(path));
    }

    public async Task CreateFileAsync(string path)
    {
        ClearUndoHistory();
        
        // 1. Try Windows ShellNew (Registry) method first - Fastest and most native
        bool shellNewSuccess = await Task.Run(() => TryCreateFromShellNew(path));
        if (shellNewSuccess) 
        {
            return;
        }

        // 2. Fallback to COM/Empty file if ShellNew fails
        string ext = Path.GetExtension(path).ToLower();
        
        if (ext == ".xlsx" || ext == ".xls")
        {
            await CreateExcelFileAsync(path);
        }
        else if (ext == ".docx" || ext == ".doc")
        {
            await CreateWordFileAsync(path);
        }
        else if (ext == ".pptx" || ext == ".ppt")
        {
            await CreatePowerPointFileAsync(path);
        }
        else
        {
            await Task.Run(() => File.Create(path).Dispose());
        }
    }

    private bool TryCreateFromShellNew(string path)
    {
        try
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return false;

            // Try direct extension key first
            if (TryCreateFromRegistryKey(path, ext)) return true;

            // Try ProgID key
            using var extKey = Registry.ClassesRoot.OpenSubKey(ext);
            if (extKey != null)
            {
                string? progId = extKey.GetValue("") as string;
                if (!string.IsNullOrEmpty(progId))
                {
                    // 2. Try ProgID key: HKCR\ProgID\ShellNew
                    if (TryCreateFromRegistryKey(path, progId)) return true;

                    // 3. Try Nested ProgID key: HKCR\.ext\ProgID\ShellNew (Common for Office Click-to-Run)
                    if (TryCreateFromRegistryKey(path, $"{ext}\\{progId}")) return true;
                }
            }
        }
        catch
        {
            // ShellNew creation failed
        }
        return false;
    }

    private bool TryCreateFromRegistryKey(string path, string keyName)
    {
        using var key = Registry.ClassesRoot.OpenSubKey($"{keyName}\\ShellNew");
        if (key == null) return false;

        // 1. NullFile (Create empty file)
        if (key.GetValueNames().Contains("NullFile"))
        {
            File.Create(path).Dispose();
            return true;
        }

        // 2. FileName (Copy template)
        string? fileName = key.GetValue("FileName") as string;
        if (!string.IsNullOrEmpty(fileName))
        {
            string? templatePath = FindTemplatePath(fileName);
            if (templatePath != null)
            {
                File.Copy(templatePath, path, true);
                return true;
            }
        }
        
        // 3. Data (Write binary data)
        var data = key.GetValue("Data") as byte[];
        if (data != null)
        {
            File.WriteAllBytes(path, data);
            return true;
        }

        return false;
    }

    private string? FindTemplatePath(string fileName)
    {
        // Expand environment variables (e.g. %ProgramFiles%)
        string expandedName = Environment.ExpandEnvironmentVariables(fileName);

        // If it's already a full path and exists
        if (File.Exists(expandedName)) return expandedName;

        // If the full path doesn't exist, try to extract just the filename and search in common locations
        string nameOnly = Path.GetFileName(expandedName);

        // Common template locations
        var searchDirs = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Templates)),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonTemplates)),
            @"C:\Windows\ShellNew",
            // Office Click-to-Run locations (Default)
            @"C:\Program Files\Microsoft Office\root\vfs\Windows\ShellNew",
            @"C:\Program Files (x86)\Microsoft Office\root\vfs\Windows\ShellNew",
            // Office MSI locations (Older versions)
            @"C:\Program Files\Microsoft Office\Office16\ShellNew",
            @"C:\Program Files (x86)\Microsoft Office\Office16\ShellNew",
            @"C:\Program Files\Microsoft Office\Office15\ShellNew",
            @"C:\Program Files (x86)\Microsoft Office\Office15\ShellNew"
        };

        // Try to find the file in common directories
        foreach (var dir in searchDirs)
        {
            if (Directory.Exists(dir))
            {
                string fullPath = Path.Combine(dir, nameOnly);
                if (File.Exists(fullPath)) return fullPath;
            }
        }
        
        return null;
    }

    private async Task CreateExcelFileAsync(string path)
    {
        await Task.Run(() =>
        {
            try
            {
                var app = OfficeInteropService.Instance.GetExcelApp();
                var wb = app.Workbooks.Add();
                bool oldAlerts = app.DisplayAlerts;
                app.DisplayAlerts = false;
                try 
                {
                    wb.SaveAs(path);
                }
                finally
                {
                    wb.Close();
                    app.DisplayAlerts = oldAlerts;
                }
            }
            catch
            {
                File.Create(path).Dispose();
            }
        });
    }

    private async Task CreateWordFileAsync(string path)
    {
        await Task.Run(() =>
        {
            try
            {
                var app = OfficeInteropService.Instance.GetWordApp();
                var doc = app.Documents.Add();
                try 
                {
                    doc.SaveAs2(path);
                }
                finally
                {
                    doc.Close();
                }
            }
            catch
            {
                File.Create(path).Dispose();
            }
        });
    }

    private async Task CreatePowerPointFileAsync(string path)
    {
        await Task.Run(() =>
        {
            try
            {
                var app = OfficeInteropService.Instance.GetPptApp();
                // PowerPoint requires a window to be present for SaveAs to work reliably
                var pres = app.Presentations.Add(NetOffice.OfficeApi.Enums.MsoTriState.msoTrue);
                try 
                {
                    // Ensure at least one slide exists, otherwise the file might be considered corrupt/empty
                    if (pres.Slides.Count == 0)
                    {
                        pres.Slides.Add(1, NetOffice.PowerPointApi.Enums.PpSlideLayout.ppLayoutBlank);
                    }

                    // Reset print options to avoid potential issues when opening
                    try 
                    {
                        pres.PrintOptions.RangeType = NetOffice.PowerPointApi.Enums.PpPrintRangeType.ppPrintAll;
                        pres.PrintOptions.NumberOfCopies = 1;
                    } 
                    catch { }

                    string ext = Path.GetExtension(path).ToLower();
                    var format = ext == ".pptx" 
                        ? NetOffice.PowerPointApi.Enums.PpSaveAsFileType.ppSaveAsOpenXMLPresentation 
                        : NetOffice.PowerPointApi.Enums.PpSaveAsFileType.ppSaveAsPresentation;

                    pres.SaveAs(path, format, NetOffice.OfficeApi.Enums.MsoTriState.msoFalse);
                }
                finally
                {
                    pres.Close();
                }
            }
            catch
            {
                // Do NOT create a 0-byte file for Office documents as it is invalid.
            }
        });
    }

    public async Task CreateShortcutAsync(IEnumerable<string> sourcePaths, string destinationDir, CancellationToken cancellationToken = default)
    {
        foreach (var sourcePath in sourcePaths)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string name = Path.GetFileNameWithoutExtension(sourcePath);
            string destPath = Path.Combine(destinationDir, name + ".lnk");
            destPath = EnsureUniquePath(destPath);

            await Task.Run(() =>
            {
                try
                {
                    IShellLink link = (IShellLink)new ShellLink();
                    link.SetPath(sourcePath);
                    link.SetWorkingDirectory(Path.GetDirectoryName(sourcePath) ?? string.Empty);
                    link.SetDescription("Shortcut created by lf-windows");
                    
                    IPersistFile file = (IPersistFile)link;
                    file.Save(destPath, false);
                }
                catch
                {
                    // Error creating shortcut
                }
            }, cancellationToken);
        }
    }

    private string EnsureUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;

        string dir = Path.GetDirectoryName(path) ?? "";
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        int count = 1;

        while (true)
        {
            string newPath = Path.Combine(dir, $"{name}-{count}{ext}");
            if (!File.Exists(newPath) && !Directory.Exists(newPath))
            {
                return newPath;
            }
            count++;
        }
    }
}

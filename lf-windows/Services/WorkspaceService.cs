using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LfWindows.Models;
using LfWindows.Interop;

namespace LfWindows.Services;

public interface IWorkspaceService
{
    string GetWorkspaceRootPath();
    Task<List<WorkspaceModel>> GetWorkspacesAsync();
    Task<WorkspaceModel> CreateWorkspaceAsync(string name);
    Task RenameWorkspaceAsync(string oldName, string newName);
    Task DeleteWorkspaceAsync(string name);
    Task CreateLinkInWorkspaceAsync(string workspaceName, string sourcePath);
    Task DeleteLinkAsync(string linkPath);
    Task<string?> GetLinkTargetAsync(string linkPath);
    Task AddShortcutAsync(string workspaceName, string sourcePath);
    Task RemoveShortcutAsync(string workspaceName, string itemName);
    event EventHandler? WorkspaceChanged;
}

public class WorkspaceService : IWorkspaceService, IDisposable
{
    private readonly IConfigService _configService;
    private readonly IIconProvider _iconProvider;
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounceTimer;

    public event EventHandler? WorkspaceChanged;

    public WorkspaceService(IConfigService configService, IIconProvider iconProvider)
    {
        _configService = configService;
        _iconProvider = iconProvider;
        SetupWatcher();
    }

    private void SetupWatcher()
    {
        try
        {
            var root = GetWorkspaceRootPath();
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }

            _watcher = new FileSystemWatcher(root);
            _watcher.IncludeSubdirectories = true;
            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;
            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemChanged;
            _watcher.Changed += OnFileSystemChanged;
            _watcher.EnableRaisingEvents = true;

            _debounceTimer = new System.Timers.Timer(200);
            _debounceTimer.AutoReset = false;
            _debounceTimer.Elapsed += (s, e) => WorkspaceChanged?.Invoke(this, EventArgs.Empty);
        }
        catch { }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
    }

    public string GetWorkspaceRootPath()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string wsName = _configService.Current.WorkspaceDirectoryName;
        if (string.IsNullOrWhiteSpace(wsName)) wsName = "Workspace";
        return Path.Combine(userProfile, wsName);
    }

    public async Task<List<WorkspaceModel>> GetWorkspacesAsync()
    {
        return await Task.Run(async () =>
        {
            var root = GetWorkspaceRootPath();
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }

            var dirs = Directory.GetDirectories(root);
            var list = new List<WorkspaceModel>();
            
            // Sort by name to ensure stable A-Z mapping
            Array.Sort(dirs);

            int index = 0;
            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                var model = new WorkspaceModel
                {
                    Name = name,
                    Path = dir,
                    ShortcutKey = index < 26 ? ((char)('A' + index)).ToString() : ""
                };
                
                // Load items
                await LoadItemsAsync(model);
                
                list.Add(model);
                index++;
            }

            return list;
        });
    }

    private async Task LoadItemsAsync(WorkspaceModel model)
    {
        var files = Directory.GetFiles(model.Path, "*.lnk");
        int index = 1;
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var target = GetLinkTarget(file);
            
            var item = new WorkspaceItem
            {
                Name = name,
                LinkPath = file,
                TargetPath = target ?? "",
                IsDirectory = Directory.Exists(target),
                ShortcutKey = index <= 9 ? index.ToString() : ""
            };

            // Simple icon loading, exactly like FileListViewModel
            // Use LinkPath (the path to the .lnk file) to get the icon
            try 
            {
                var icon = await _iconProvider.GetFileIconAsync(item.LinkPath, IconSize.Small);
                if (icon != null)
                {
                    item.Icon = icon;
                }
            }
            catch (Exception)
            {
                // Ignore errors
            }

            model.Items.Add(item);
            index++;
        }
    }

    // Removed GetIconFallback as it is no longer needed

    public async Task<WorkspaceModel> CreateWorkspaceAsync(string name)
    {
        return await Task.Run(() =>
        {
            var root = GetWorkspaceRootPath();
            var path = Path.Combine(root, name);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            
            return new WorkspaceModel
            {
                Name = name,
                Path = path
            };
        });
    }

    public async Task RenameWorkspaceAsync(string oldName, string newName)
    {
        await Task.Run(() =>
        {
            var root = GetWorkspaceRootPath();
            var oldPath = Path.Combine(root, oldName);
            var newPath = Path.Combine(root, newName);

            if (Directory.Exists(oldPath) && !Directory.Exists(newPath))
            {
                Directory.Move(oldPath, newPath);
            }
        });
    }

    public async Task DeleteWorkspaceAsync(string name)
    {
        await Task.Run(() =>
        {
            var root = GetWorkspaceRootPath();
            var path = Path.Combine(root, name);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        });
    }

    public async Task CreateLinkInWorkspaceAsync(string workspaceName, string sourcePath)
    {
        await Task.Run(() =>
        {
            var root = GetWorkspaceRootPath();
            var wsPath = Path.Combine(root, workspaceName);
            if (!Directory.Exists(wsPath))
            {
                Directory.CreateDirectory(wsPath);
            }

            string name = Path.GetFileNameWithoutExtension(sourcePath);
            string destPath = Path.Combine(wsPath, name + ".lnk");
            
            // Ensure unique
            int count = 1;
            while (File.Exists(destPath))
            {
                destPath = Path.Combine(wsPath, $"{name}-{count}.lnk");
                count++;
            }

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
                // Error creating workspace link
            }
        });
    }

    public async Task DeleteLinkAsync(string linkPath)
    {
        await Task.Run(() =>
        {
            if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }
        });
    }

    public async Task<string?> GetLinkTargetAsync(string linkPath)
    {
        return await Task.Run(() => GetLinkTarget(linkPath));
    }

    public async Task AddShortcutAsync(string workspaceName, string sourcePath)
    {
        await CreateLinkInWorkspaceAsync(workspaceName, sourcePath);
    }

    public async Task RemoveShortcutAsync(string workspaceName, string itemName)
    {
        var root = GetWorkspaceRootPath();
        var wsPath = Path.Combine(root, workspaceName);
        var linkPath = Path.Combine(wsPath, itemName + ".lnk");
        await DeleteLinkAsync(linkPath);
    }

    private string? GetLinkTarget(string linkPath)
    {
        try
        {
            if (!File.Exists(linkPath)) return null;

            IShellLink link = (IShellLink)new ShellLink();
            IPersistFile file = (IPersistFile)link;
            file.Load(linkPath, 0);

            var sb = new System.Text.StringBuilder(260);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return sb.ToString();
        }
        catch
        {
            return null;
        }
    }
}

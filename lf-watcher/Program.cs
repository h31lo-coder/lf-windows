using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LfWatcher.Interop;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LfWatcher;

class Program
{
    // --- Workspace Sync Fields ---
    private static readonly ConcurrentDictionary<string, List<LinkInfo>> _watchedDirectories = new();
    private static readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private static FileSystemWatcher? _workspaceWatcher;
    private static string _workspaceRoot = "";

    private class LinkInfo
    {
        public string SourceFileName { get; set; } = "";
        public string LinkPath { get; set; } = "";
    }

    // --- Bookmark Sync Fields ---
    private static string _configPath = "";
    private static string _trackingDir = "";
    private static Dictionary<string, string> _bookmarks = new(); // Key -> Path
    // Map: Parent Directory -> List of (Folder Name, Bookmark Key)
    private static readonly ConcurrentDictionary<string, List<BookmarkInfo>> _watchedBookmarks = new();
    private static readonly ConcurrentDictionary<string, FileSystemWatcher> _bookmarkWatchers = new();
    private static FileSystemWatcher? _configWatcher;

    // --- Yank History Sync Fields ---
    private static List<List<string>> _yankHistory = new();
    private static readonly ConcurrentDictionary<string, List<YankInfo>> _watchedYankItems = new();
    private static readonly ConcurrentDictionary<string, FileSystemWatcher> _yankWatchers = new();

    private class BookmarkInfo
    {
        public string FolderName { get; set; } = "";
        public string Key { get; set; } = "";
    }

    private class YankInfo
    {
        public string FolderName { get; set; } = "";
        public int ListIndex { get; set; }
        public int FileIndex { get; set; }
        public string FullPath { get; set; } = "";
    }

    // Minimal Config Class for Deserialization (Read-Only use)
    public class AppConfig
    {
        public Dictionary<string, string> Bookmarks { get; set; } = new();
        public List<List<string>> YankHistory { get; set; } = new();
        public string WorkspaceDirectoryName { get; set; } = "Workspace";
    }

    static async Task Main(string[] args)
    {
        try 
        {
            // 1. Setup Config Path
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _configPath = Path.Combine(appData, "lf-windows", "config.yaml");
            _trackingDir = Path.Combine(appData, "lf-windows", "tracking");
            if (!Directory.Exists(_trackingDir)) Directory.CreateDirectory(_trackingDir);

            // Log startup
            // File.AppendAllText(Path.Combine(appData, "lf-windows", "watcher_debug.log"), $"[{DateTime.Now}] Watcher Started\n");

            // 2. Load Config & Bookmarks
            LoadConfig();


        // 3. Setup Workspace Root (from config or default)
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // If _workspaceRoot was not set by LoadConfig (e.g. config missing), set default
        if (string.IsNullOrEmpty(_workspaceRoot))
        {
             _workspaceRoot = Path.Combine(userProfile, "Workspace");
        }
        else
        {
             // If it was just a name, combine with profile
             if (!Path.IsPathRooted(_workspaceRoot))
             {
                 _workspaceRoot = Path.Combine(userProfile, _workspaceRoot);
             }
        }

        if (!Directory.Exists(_workspaceRoot))
        {
            Directory.CreateDirectory(_workspaceRoot);
        }

        // 4. Initial Workspace Scan
        ScanWorkspace();

        // 5. Watch Workspace for new/deleted links
        _workspaceWatcher = new FileSystemWatcher(_workspaceRoot);
        _workspaceWatcher.IncludeSubdirectories = true;
        _workspaceWatcher.Filter = "*.lnk";
        _workspaceWatcher.Created += OnLinkCreated;
        _workspaceWatcher.Deleted += OnLinkDeleted;
        _workspaceWatcher.Changed += OnLinkChanged;
        _workspaceWatcher.Renamed += OnLinkRenamed;
        _workspaceWatcher.EnableRaisingEvents = true;

        // 6. Watch Config File for changes (to reload bookmarks if changed by main app)
        string? configDir = Path.GetDirectoryName(_configPath);
        if (configDir != null && Directory.Exists(configDir))
        {
            _configWatcher = new FileSystemWatcher(configDir, "config.yaml");
            _configWatcher.Changed += (s, e) => {
                // Debounce
                Thread.Sleep(100);
                LoadConfig();
            };
            _configWatcher.EnableRaisingEvents = true;
        }

        // Keep running
        await Task.Delay(-1);
        }
        catch (Exception ex)
        {
            // Log fatal error
            try 
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                File.AppendAllText(Path.Combine(appData, "lf-windows", "watcher_crash.log"), $"[{DateTime.Now}] Fatal: {ex}\n");
            }
            catch {}
        }
    }

    private static void LoadConfig()
    {
        try
        {
            if (!File.Exists(_configPath)) return;

            string yaml = "";
            // Retry loop for reading config
            for(int i=0; i<3; i++)
            {
                try { yaml = File.ReadAllText(_configPath); break; }
                catch { Thread.Sleep(50); }
            }

            if (string.IsNullOrEmpty(yaml)) return;

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            
            var config = deserializer.Deserialize<AppConfig>(yaml);
            
            if (config != null)
            {
                if (!string.IsNullOrEmpty(config.WorkspaceDirectoryName))
                {
                    _workspaceRoot = config.WorkspaceDirectoryName;
                }

                // Update Bookmarks
                _bookmarks = config.Bookmarks ?? new Dictionary<string, string>();
                _yankHistory = config.YankHistory ?? new List<List<string>>();
                RefreshBookmarkWatchers();
                RefreshYankWatchers();
            }
        }
        catch (Exception ex)
        {
            Log($"Error loading config: {ex.Message}");
        }
    }

    private static void RefreshBookmarkWatchers()
    {
        // Clear old watchers? No, that's expensive.
        // Just clear the map and rebuild it.
        _watchedBookmarks.Clear();

        foreach (var kvp in _bookmarks)
        {
            string key = kvp.Key;
            string path = kvp.Value;
            
            // Normalize path
            path = path.Replace('/', '\\').TrimEnd('\\');

            string? parent = Path.GetDirectoryName(path);
            string? name = Path.GetFileName(path);

            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) continue;

            // Add to map
            _watchedBookmarks.AddOrUpdate(parent, 
                new List<BookmarkInfo> { new BookmarkInfo { FolderName = name, Key = key } },
                (k, list) => {
                    lock (list)
                    {
                        if (!list.Any(x => x.Key == key))
                        {
                            list.Add(new BookmarkInfo { FolderName = name, Key = key });
                        }
                    }
                    return list;
                });

            // Ensure watcher exists
            if (!_bookmarkWatchers.ContainsKey(parent))
            {
                try
                {
                    if (Directory.Exists(parent))
                    {
                        var watcher = new FileSystemWatcher(parent);
                        watcher.NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName; // Watch for folder renames/deletes
                        watcher.Renamed += OnBookmarkSourceRenamed;
                        watcher.Deleted += OnBookmarkSourceDeleted;
                        watcher.EnableRaisingEvents = true;
                        _bookmarkWatchers.TryAdd(parent, watcher);
                    }
                }
                catch { }
            }
        }

        EnsureTrackingShortcuts();
    }

    private static void RefreshYankWatchers()
    {
        _watchedYankItems.Clear();

        for (int i = 0; i < _yankHistory.Count; i++)
        {
            var list = _yankHistory[i];
            for (int j = 0; j < list.Count; j++)
            {
                string path = list[j];
                path = path.Replace('/', '\\').TrimEnd('\\');

                string? parent = Path.GetDirectoryName(path);
                string? name = Path.GetFileName(path);

                if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) continue;

                _watchedYankItems.AddOrUpdate(parent,
                    new List<YankInfo> { new YankInfo { FolderName = name, ListIndex = i, FileIndex = j, FullPath = path } },
                    (k, l) => {
                        lock (l)
                        {
                            if (!l.Any(x => x.ListIndex == i && x.FileIndex == j))
                            {
                                l.Add(new YankInfo { FolderName = name, ListIndex = i, FileIndex = j, FullPath = path });
                            }
                        }
                        return l;
                    });

                if (!_yankWatchers.ContainsKey(parent))
                {
                    try
                    {
                        if (Directory.Exists(parent))
                        {
                            var watcher = new FileSystemWatcher(parent);
                            watcher.NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName;
                            watcher.Renamed += OnYankSourceRenamed;
                            watcher.Deleted += OnYankSourceDeleted;
                            watcher.EnableRaisingEvents = true;
                            _yankWatchers.TryAdd(parent, watcher);
                        }
                    }
                    catch { }
                }
            }
        }
        EnsureYankTrackingShortcuts();
    }

    private static void EnsureTrackingShortcuts()
    {
        var updates = new List<KeyValuePair<string, string>>();

        foreach (var kvp in _bookmarks)
        {
            string key = kvp.Key;
            string path = kvp.Value;
            string linkPath = Path.Combine(_trackingDir, $"{key}.lnk");

            // Check for offline move
            if (File.Exists(linkPath))
            {
                string? resolved = ResolveTrackingLink(linkPath);
                if (!string.IsNullOrEmpty(resolved) && 
                    !string.Equals(resolved, path, StringComparison.OrdinalIgnoreCase) &&
                    (File.Exists(resolved) || Directory.Exists(resolved)))
                {
                    updates.Add(new KeyValuePair<string, string>(key, resolved));
                    path = resolved; 
                }
            }

            CreateOrUpdateTrackingLink(linkPath, path);
        }

        foreach (var update in updates)
        {
            UpdateBookmarkConfig(new List<BookmarkInfo> { new BookmarkInfo { Key = update.Key } }, update.Value, false);
        }
    }

    private static void EnsureYankTrackingShortcuts()
    {
        var updates = new List<Tuple<string, string>>();

        foreach (var list in _yankHistory)
        {
            foreach (var path in list)
            {
                string hash = GetPathHash(path);
                string linkPath = Path.Combine(_trackingDir, $"yank_{hash}.lnk");

                // Check for offline move
                if (File.Exists(linkPath))
                {
                    string? resolved = ResolveTrackingLink(linkPath);
                    if (!string.IsNullOrEmpty(resolved) && 
                        !string.Equals(resolved, path, StringComparison.OrdinalIgnoreCase) &&
                        (File.Exists(resolved) || Directory.Exists(resolved)))
                    {
                        updates.Add(Tuple.Create(path, resolved));
                        CreateOrUpdateTrackingLink(linkPath, resolved);
                        continue;
                    }
                }

                CreateOrUpdateTrackingLink(linkPath, path);
            }
        }

        foreach (var update in updates)
        {
            UpdateYankConfig(new List<YankInfo> { new YankInfo { FullPath = update.Item1 } }, update.Item2, false);
        }
    }

    private static string GetPathHash(string path)
    {
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant());
            byte[] hashBytes = md5.ComputeHash(inputBytes);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }

    private static void CreateOrUpdateTrackingLink(string linkPath, string targetPath)
    {
        try
        {
            if (!File.Exists(linkPath))
            {
                CreateTrackingLink(linkPath, targetPath);
            }
            else
            {
                string? target = GetLinkTarget(linkPath);
                if (target != targetPath)
                {
                    CreateTrackingLink(linkPath, targetPath);
                }
            }
        }
        catch { }
    }

    private static void CreateTrackingLink(string linkPath, string targetPath)
    {
        IShellLink? link = null;
        IPersistFile? file = null;
        try
        {
            link = (IShellLink)new ShellLink();
            link.SetPath(targetPath);
            file = (IPersistFile)link;
            file.Save(linkPath, true);
        }
        finally
        {
            if (file != null) Marshal.ReleaseComObject(file);
            if (link != null) Marshal.ReleaseComObject(link);
        }
    }

    private static void OnYankSourceRenamed(object sender, RenamedEventArgs e)
    {
        string dir = Path.GetDirectoryName(e.OldFullPath) ?? "";
        string oldName = e.OldName ?? "";
        string newPath = e.FullPath;

        if (_watchedYankItems.TryGetValue(dir, out var list))
        {
            List<YankInfo> targets;
            lock (list)
            {
                targets = list.Where(x => x.FolderName.Equals(oldName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (targets.Any())
            {
                UpdateYankConfig(targets, newPath, isDelete: false);
                lock (list)
                {
                    foreach(var t in targets) 
                    {
                        t.FolderName = Path.GetFileName(newPath);
                        t.FullPath = newPath;
                    }
                }
            }
        }
    }

    private static void OnYankSourceDeleted(object sender, FileSystemEventArgs e)
    {
        Log($"OnYankSourceDeleted: {e.FullPath}");
        string dir = Path.GetDirectoryName(e.FullPath) ?? "";
        string name = e.Name ?? "";

        if (_watchedYankItems.TryGetValue(dir, out var list))
        {
            List<YankInfo> targets;
            lock (list)
            {
                targets = list.Where(x => x.FolderName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            Log($"Found {targets.Count} targets for deletion");

            if (targets.Any())
            {
                Task.Run(async () => 
                {
                    await Task.Delay(1000);
                    foreach (var target in targets)
                    {
                        string hash = GetPathHash(target.FullPath);
                        string linkPath = Path.Combine(_trackingDir, $"yank_{hash}.lnk");
                        
                        if (File.Exists(linkPath))
                        {
                            string? newPath = ResolveTrackingLink(linkPath);
                            Log($"ResolveTrackingLink for {target.FullPath} -> {newPath}");
                            if (!string.IsNullOrEmpty(newPath) && (File.Exists(newPath) || Directory.Exists(newPath)) && newPath != e.FullPath)
                            {
                                // Moved
                                Log("Detected Move");
                                UpdateYankConfig(new List<YankInfo> { target }, newPath, isDelete: false);
                                CreateTrackingLink(linkPath, newPath);
                                continue;
                            }
                        }

                        // Deleted
                        Log("Detected Delete");
                        UpdateYankConfig(new List<YankInfo> { target }, null, isDelete: true);
                    }

                    lock (list)
                    {
                        list.RemoveAll(x => targets.Contains(x));
                    }
                });
            }
        }
    }

    private static void UpdateYankConfig(List<YankInfo> targets, string? newPath, bool isDelete)
    {
        try
        {
            Thread.Sleep(100);
            string yaml = "";
            for(int i=0; i<3; i++)
            {
                try { yaml = File.ReadAllText(_configPath); break; }
                catch { Thread.Sleep(50); }
            }
            
            if (string.IsNullOrEmpty(yaml)) return;

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            
            var root = deserializer.Deserialize<Dictionary<string, object>>(yaml);
            
            if (root.ContainsKey("yankHistory"))
            {
                var yankObj = root["yankHistory"];
                List<List<string>> yankList = new List<List<string>>();

                // Manual deserialization because YamlDotNet might return List<object>
                if (yankObj is List<object> listObj)
                {
                    foreach(var item in listObj)
                    {
                        if (item is List<object> innerList)
                        {
                            yankList.Add(innerList.Select(x => x.ToString()!).ToList());
                        }
                    }
                }

                bool changed = false;
                foreach(var target in targets)
                {
                    string oldPath = target.FullPath;
                    
                    // Find and update/remove
                    foreach(var list in yankList)
                    {
                        for(int i=0; i<list.Count; i++)
                        {
                            string currentPath = list[i].Replace('/', '\\').TrimEnd('\\');
                            if (currentPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase))
                            {
                                if (isDelete)
                                {
                                    list.RemoveAt(i);
                                    i--; // Adjust index
                                    changed = true;
                                }
                                else if (newPath != null)
                                {
                                    list[i] = newPath;
                                    changed = true;
                                }
                            }
                        }
                    }
                }
                
                // Clean up empty lists
                if (isDelete)
                {
                    int removed = yankList.RemoveAll(x => x.Count == 0);
                    if (removed > 0) changed = true;
                }

                if (changed)
                {
                    root["yankHistory"] = yankList;
                    
                    var serializer = new SerializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();
                    
                    string newYaml = serializer.Serialize(root);
                    
                    for(int i=0; i<3; i++)
                    {
                        try { File.WriteAllText(_configPath, newYaml); break; }
                        catch { Thread.Sleep(100); }
                    }
                    
                    _yankHistory = yankList;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Error updating yank config: {ex.Message}");
        }
    }

    private static void OnBookmarkSourceRenamed(object sender, RenamedEventArgs e)
    {
        string dir = Path.GetDirectoryName(e.OldFullPath) ?? "";
        string oldName = e.OldName ?? "";
        string newPath = e.FullPath;

        if (_watchedBookmarks.TryGetValue(dir, out var list))
        {
            List<BookmarkInfo> targets;
            lock (list)
            {
                targets = list.Where(x => x.FolderName.Equals(oldName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (targets.Any())
            {
                // Update Config
                UpdateBookmarkConfig(targets, newPath, isDelete: false);
                
                // Update internal state
                lock (list)
                {
                    foreach(var t in targets) t.FolderName = Path.GetFileName(newPath);
                }
            }
        }
    }

    private static void OnBookmarkSourceDeleted(object sender, FileSystemEventArgs e)
    {
        string dir = Path.GetDirectoryName(e.FullPath) ?? "";
        string name = e.Name ?? "";

        if (_watchedBookmarks.TryGetValue(dir, out var list))
        {
            List<BookmarkInfo> targets;
            lock (list)
            {
                targets = list.Where(x => x.FolderName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (targets.Any())
            {
                // Check if it was moved (using tracking link)
                Task.Run(async () => 
                {
                    await Task.Delay(1000);
                    foreach (var target in targets)
                    {
                        string linkPath = Path.Combine(_trackingDir, $"{target.Key}.lnk");
                        if (File.Exists(linkPath))
                        {
                            string? newPath = ResolveTrackingLink(linkPath);
                            if (!string.IsNullOrEmpty(newPath) && Directory.Exists(newPath) && newPath != e.FullPath)
                            {
                                // It was moved!
                                UpdateBookmarkConfig(new List<BookmarkInfo> { target }, newPath, isDelete: false);
                                
                                // Update tracking link to new location
                                CreateTrackingLink(linkPath, newPath);
                                continue;
                            }
                        }

                        // If we reach here, it's a real delete or move failed to track
                        UpdateBookmarkConfig(new List<BookmarkInfo> { target }, null, isDelete: true);
                    }

                    // Update internal state
                    lock (list)
                    {
                        list.RemoveAll(x => targets.Contains(x));
                    }
                });
            }
        }
    }

    private static string? ResolveTrackingLink(string linkPath)
    {
        IShellLink? link = null;
        IPersistFile? file = null;
        try
        {
            link = (IShellLink)new ShellLink();
            file = (IPersistFile)link;
            file.Load(linkPath, 0);
            
            // Resolve with NO_UI (1)
            link.Resolve(IntPtr.Zero, 1);

            var sb = new StringBuilder(260);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return sb.ToString();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (file != null) Marshal.ReleaseComObject(file);
            if (link != null) Marshal.ReleaseComObject(link);
        }
    }

    private static void UpdateBookmarkConfig(List<BookmarkInfo> targets, string? newPath, bool isDelete)
    {
        try
        {
            // Wait a bit to ensure file is not locked
            Thread.Sleep(100);

            string yaml = "";
            for(int i=0; i<3; i++)
            {
                try { yaml = File.ReadAllText(_configPath); break; }
                catch { Thread.Sleep(50); }
            }
            
            if (string.IsNullOrEmpty(yaml)) return;

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            
            var root = deserializer.Deserialize<Dictionary<string, object>>(yaml);
            
            if (root.ContainsKey("bookmarks"))
            {
                var bookmarksObj = root["bookmarks"];
                Dictionary<string, string> bookmarksDict = new Dictionary<string, string>();

                if (bookmarksObj is Dictionary<object, object> dictObj)
                {
                    foreach(var kvp in dictObj)
                    {
                        bookmarksDict[kvp.Key.ToString()!] = kvp.Value.ToString()!;
                    }
                }
                else if (bookmarksObj is Dictionary<string, string> dictStr)
                {
                    bookmarksDict = dictStr;
                }

                bool changed = false;
                foreach(var target in targets)
                {
                    if (isDelete)
                    {
                        if (bookmarksDict.ContainsKey(target.Key))
                        {
                            bookmarksDict.Remove(target.Key);
                            changed = true;
                        }
                    }
                    else if (newPath != null)
                    {
                        if (bookmarksDict.ContainsKey(target.Key))
                        {
                            bookmarksDict[target.Key] = newPath;
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    root["bookmarks"] = bookmarksDict;
                    
                    var serializer = new SerializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();
                    
                    string newYaml = serializer.Serialize(root);
                    
                    // Retry loop for writing
                    for(int i=0; i<3; i++)
                    {
                        try { File.WriteAllText(_configPath, newYaml); break; }
                        catch { Thread.Sleep(100); }
                    }
                    
                    // Update local cache
                    _bookmarks = bookmarksDict;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Error updating config: {ex.Message}");
        }
    }

    // --- Workspace Methods ---

    private static void ScanWorkspace()
    {
        try
        {
            var links = Directory.GetFiles(_workspaceRoot, "*.lnk", SearchOption.AllDirectories);
            foreach (var link in links)
            {
                RegisterLink(link);
            }
        }
        catch (Exception)
        {
            // Log error
        }
    }

    private static void RegisterLink(string linkPath)
    {
        try
        {
            string? target = GetLinkTarget(linkPath);
            if (string.IsNullOrEmpty(target) || (!File.Exists(target) && !Directory.Exists(target))) return;

            string? dir = Path.GetDirectoryName(target);
            string? name = Path.GetFileName(target);

            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return;

            // Add to map
            _watchedDirectories.AddOrUpdate(dir, 
                new List<LinkInfo> { new LinkInfo { SourceFileName = name, LinkPath = linkPath } },
                (k, list) => {
                    lock (list)
                    {
                        if (!list.Any(x => x.LinkPath == linkPath))
                        {
                            list.Add(new LinkInfo { SourceFileName = name, LinkPath = linkPath });
                        }
                    }
                    return list;
                });

            // Ensure watcher exists
            if (!_watchers.ContainsKey(dir))
            {
                try
                {
                    var watcher = new FileSystemWatcher(dir);
                    watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName;
                    watcher.Renamed += OnSourceRenamed;
                    watcher.Deleted += OnSourceDeleted;
                    watcher.EnableRaisingEvents = true;
                    _watchers.TryAdd(dir, watcher);
                }
                catch
                {
                    // Access denied or invalid path
                }
            }
        }
        catch { }
    }

    private static void UnregisterLink(string linkPath)
    {
        foreach (var kvp in _watchedDirectories)
        {
            lock (kvp.Value)
            {
                kvp.Value.RemoveAll(x => x.LinkPath == linkPath);
            }
        }
    }

    private static void OnLinkCreated(object sender, FileSystemEventArgs e) => RegisterLink(e.FullPath);
    private static void OnLinkDeleted(object sender, FileSystemEventArgs e) => UnregisterLink(e.FullPath);
    private static void OnLinkChanged(object sender, FileSystemEventArgs e) => RegisterLink(e.FullPath);
    private static void OnLinkRenamed(object sender, RenamedEventArgs e)
    {
        UnregisterLink(e.OldFullPath);
        RegisterLink(e.FullPath);
    }

    private static void Log(string message)
    {
        // Logging disabled
    }

    private static void OnSourceDeleted(object sender, FileSystemEventArgs e)
    {
        string dir = Path.GetDirectoryName(e.FullPath) ?? "";
        string name = e.Name ?? "";

        if (_watchedDirectories.TryGetValue(dir, out var list))
        {
            List<LinkInfo> targets;
            lock (list)
            {
                targets = list.Where(x => x.SourceFileName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (targets.Any())
            {
                Task.Run(() => 
                {
                    // Give Windows some time to update tracking
                    Thread.Sleep(500);

                    foreach (var target in targets)
                    {
                        // Try to resolve to see if it was moved
                        string? newPath = ResolveTrackingLink(target.LinkPath);
                        
                        if (!string.IsNullOrEmpty(newPath) && (File.Exists(newPath) || Directory.Exists(newPath)) && newPath != e.FullPath)
                        {
                            // Moved
                            Log($"Source moved: {e.FullPath} -> {newPath}");
                            UpdateLinkTarget(target.LinkPath, newPath);
                            
                            // Re-register to update watchers for new location
                            RegisterLink(target.LinkPath);
                        }
                        else
                        {
                            // Deleted
                            Log($"Source deleted: {e.FullPath}. Deleting link.");
                            try 
                            {
                                if (File.Exists(target.LinkPath)) File.Delete(target.LinkPath);
                            }
                            catch {}
                        }
                    }

                    // Remove from old list
                    lock (list)
                    {
                        list.RemoveAll(x => targets.Contains(x));
                    }
                });
            }
        }
    }

    private static void OnSourceRenamed(object sender, RenamedEventArgs e)
    {
        string dir = Path.GetDirectoryName(e.OldFullPath) ?? "";
        string oldName = e.OldName ?? "";
        string newPath = e.FullPath;

        Log($"Source renamed: {e.OldFullPath} -> {newPath}");

        if (_watchedDirectories.TryGetValue(dir, out var list))
        {
            List<LinkInfo> targets;
            lock (list)
            {
                targets = list.Where(x => x.SourceFileName.Equals(oldName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            Log($"Found {targets.Count} matching links.");

            foreach (var target in targets)
            {
                UpdateLinkTarget(target.LinkPath, newPath);

                try
                {
                    string linkDir = Path.GetDirectoryName(target.LinkPath) ?? "";
                    string newLinkName = Path.GetFileNameWithoutExtension(newPath) + ".lnk";
                    string newLinkPath = Path.Combine(linkDir, newLinkName);

                    if (!string.Equals(target.LinkPath, newLinkPath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(newLinkPath))
                        {
                            int count = 1;
                            while (File.Exists(Path.Combine(linkDir, $"{Path.GetFileNameWithoutExtension(newLinkName)}-{count}.lnk")))
                            {
                                count++;
                            }
                            newLinkPath = Path.Combine(linkDir, $"{Path.GetFileNameWithoutExtension(newLinkName)}-{count}.lnk");
                        }

                        Log($"Attempting to rename link: {target.LinkPath} -> {newLinkPath}");
                        
                        for (int i = 0; i < 5; i++)
                        {
                            try 
                            {
                                File.Move(target.LinkPath, newLinkPath);
                                Log("Rename successful.");
                                target.LinkPath = newLinkPath;
                                break;
                            }
                            catch (Exception ex)
                            {
                                Log($"Rename attempt {i+1} failed: {ex.Message}");
                                Thread.Sleep(200);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Rename logic error: {ex}");
                }
                
                lock (list)
                {
                    target.SourceFileName = Path.GetFileName(newPath);
                }
            }
        }
    }

    private static void UpdateLinkTarget(string linkPath, string newTarget)
    {
        for (int i = 0; i < 3; i++)
        {
            IShellLink? link = null;
            IPersistFile? file = null;
            try
            {
                link = (IShellLink)new ShellLink();
                file = (IPersistFile)link;
                
                file.Load(linkPath, 0);
                link.SetPath(newTarget);
                link.SetWorkingDirectory(Path.GetDirectoryName(newTarget) ?? "");
                file.Save(linkPath, true);
                Log($"Updated link target for {linkPath}");
                break;
            }
            catch (Exception ex)
            {
                Log($"Update target failed: {ex.Message}");
                Thread.Sleep(100);
            }
            finally
            {
                if (file != null) Marshal.ReleaseComObject(file);
                if (link != null) Marshal.ReleaseComObject(link);
            }
        }
    }

    private static string? GetLinkTarget(string linkPath)
    {
        IShellLink? link = null;
        IPersistFile? file = null;
        try
        {
            link = (IShellLink)new ShellLink();
            file = (IPersistFile)link;
            file.Load(linkPath, 0);

            var sb = new StringBuilder(260);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return sb.ToString();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (file != null) Marshal.ReleaseComObject(file);
            if (link != null) Marshal.ReleaseComObject(link);
        }
    }
}

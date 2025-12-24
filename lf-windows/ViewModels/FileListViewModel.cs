using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LfWindows.Models;
using LfWindows.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Threading;
using Avalonia.Collections;

using System.Text.RegularExpressions;

namespace LfWindows.ViewModels;

public partial class FileListViewModel : ViewModelBase
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IIconProvider _iconProvider;
    private readonly AppConfig _config;
    private readonly IPinyinService _pinyinService;
    private List<FileSystemItem> _allItems = new();
    private FileSystemWatcher? _watcher;
    private string _currentFilter = string.Empty;
    private System.Threading.CancellationTokenSource? _debounceCts;
    private System.Threading.CancellationTokenSource? _loadIconsCts;

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private FileSystemItem? _selectedItem;

    public AvaloniaList<FileSystemItem> Items { get; } = new();

    public FileListViewModel(IFileSystemService fileSystemService, IIconProvider iconProvider, AppConfig config, IPinyinService? pinyinService = null)
    {
        _fileSystemService = fileSystemService;
        _iconProvider = iconProvider;
        _config = config;
        _pinyinService = pinyinService ?? new PinyinService();
    }

    public void SortItems()
    {
        if (_allItems.Count == 0) return;

        IEnumerable<FileSystemItem> sorted = _allItems;

        switch (_config.SortBy)
        {
            case SortType.Natural:
            case SortType.Name:
                sorted = _config.SortReverse 
                    ? sorted.OrderByDescending(i => i.Name, new NaturalStringComparer()) 
                    : sorted.OrderBy(i => i.Name, new NaturalStringComparer());
                break;
            case SortType.Size:
                sorted = _config.SortReverse 
                    ? sorted.OrderByDescending(i => i.Size) 
                    : sorted.OrderBy(i => i.Size);
                break;
            case SortType.Time: // Modified
                sorted = _config.SortReverse 
                    ? sorted.OrderByDescending(i => i.Modified) 
                    : sorted.OrderBy(i => i.Modified);
                break;
            case SortType.Atime: // Access
                sorted = _config.SortReverse 
                    ? sorted.OrderByDescending(i => i.LastAccessTime) 
                    : sorted.OrderBy(i => i.LastAccessTime);
                break;
            case SortType.Btime: // Creation
                sorted = _config.SortReverse 
                    ? sorted.OrderByDescending(i => i.CreationTime) 
                    : sorted.OrderBy(i => i.CreationTime);
                break;
            case SortType.Ctime: // Change (Metadata) - Fallback to Modified on Windows/NetStandard if not available
                sorted = _config.SortReverse 
                    ? sorted.OrderByDescending(i => i.Modified) 
                    : sorted.OrderBy(i => i.Modified);
                break;
            case SortType.Ext:
                sorted = _config.SortReverse 
                    ? sorted.OrderByDescending(i => i.Extension) 
                    : sorted.OrderBy(i => i.Extension);
                break;
            default:
                sorted = _config.SortReverse 
                    ? sorted.OrderByDescending(i => i.Name) 
                    : sorted.OrderBy(i => i.Name);
                break;
        }

        if (_config.DirFirst)
        {
            sorted = sorted.OrderByDescending(i => i.Type == Models.FileType.Directory);
        }
        
        _allItems = sorted.ToList();
        
        ApplyFilter(_currentFilter);
    }


    public async Task LoadDirectoryAsync(string path, string? targetPath = null)
    {
        // Determine which path we want to select after loading
        string? pathRestoration = targetPath ?? SelectedItem?.Path;

        // Check if we are reloading the same directory
        bool isSameDirectory = string.Equals(path, CurrentPath, System.StringComparison.OrdinalIgnoreCase);

        CurrentPath = path;
        
        // Setup watcher if path changed or watcher is null
        if (_watcher == null || _watcher.Path != path)
        {
            SetupWatcher(path);
        }

        _loadIconsCts?.Cancel();
        _loadIconsCts = new System.Threading.CancellationTokenSource();
        var token = _loadIconsCts.Token;

        var items = await _fileSystemService.ListDirectoryAsync(path);
        
        Items.Clear();
        _allItems.Clear();
        _allItems.AddRange(items);
        
        SortItems();

        // If reloading same directory, keep the filter. Otherwise clear it.
        string filterToApply = isSameDirectory ? _currentFilter : string.Empty;
        ApplyFilter(filterToApply, null, pathRestoration);

        _ = LoadIconsAsync(items, token);
    }

    private async Task LoadIconsAsync(IEnumerable<FileSystemItem> items, System.Threading.CancellationToken token)
    {
        foreach (var item in items)
        {
            if (token.IsCancellationRequested) return;

            // Check if we are still on the same page/directory? 
            // Actually items are objects, so it's fine.
            if (item.Icon == null)
            {
                // Use Small (16x16) or Large (32x32) for list view. 
                // ExtraLarge (48x48) might be too big and cause downscaling artifacts.
                // Let's try Large (32x32) which is usually high quality and scales down nicely to 16x16.
                item.Icon = await _iconProvider.GetFileIconAsync(item.Path, IconSize.Large);
            }
        }
    }

    public void SuspendWatcher()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
        }
    }

    public void ResumeWatcher()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = true;
        }
    }

    private void SetupWatcher(string path)
    {
        try
        {
            _watcher?.Dispose();
            _watcher = null;

            // Don't watch if it's an archive path or doesn't exist
            if (ArchiveFileSystemHelper.IsArchivePath(path, out _, out _) || !Directory.Exists(path))
            {
                return;
            }

            _watcher = new FileSystemWatcher(path);
            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            // Ignore watcher errors (e.g. permissions)
            _watcher = null;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce to prevent spamming reloads on bulk operations
        _debounceCts?.Cancel();
        _debounceCts = new System.Threading.CancellationTokenSource();
        var token = _debounceCts.Token;

        Task.Run(async () => 
        {
            try
            {
                await Task.Delay(500, token);
                if (token.IsCancellationRequested) return;

                await Dispatcher.UIThread.InvokeAsync(async () => 
                {
                    if (token.IsCancellationRequested) return;
                    await LoadDirectoryAsync(CurrentPath);
                });
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
        });
    }

    public void RefreshFilter()
    {
        ApplyFilter(_currentFilter);
    }

    public void ApplyFilter(string filterText, FilterMethod? overrideMode = null, string? keepSelectionPath = null)
    {
        _currentFilter = filterText;

        // If no specific path to keep is provided, try to keep the current one
        string? targetPath = keepSelectionPath ?? SelectedItem?.Path;

        Items.Clear();
        IEnumerable<FileSystemItem> filtered = _allItems;

        // Filter logic based on user requirements:
        // 1. IsAttrHidden: Has Hidden or System attribute.
        // 2. IsDotHidden: Starts with "." AND is NOT IsAttrHidden.
        // 3. Normal: Neither.
        
        filtered = filtered.Where(i => 
        {
            bool isAttrHidden = i.IsHidden || i.IsSystem;
            bool isDotHidden = i.Name.StartsWith(".") && !isAttrHidden;

            if (isAttrHidden)
            {
                // Controlled by ShowSystemHidden (AttrFiles)
                return _config.ShowSystemHidden;
            }
            else if (isDotHidden)
            {
                // Controlled by ShowHidden (DotFiles)
                return _config.ShowHidden;
            }
            else
            {
                return true; // Normal file
            }
        });

        if (!string.IsNullOrWhiteSpace(filterText))
        {
            var mode = overrideMode ?? _config.FilterMethod;
            filtered = filtered.Where(item => IsMatch(item.Name, filterText, mode));
        }

        Items.AddRange(filtered);

        if (Items.Count > 0)
        {
            // Try to restore selection
            FileSystemItem? match = null;
            if (targetPath != null)
            {
                match = Items.FirstOrDefault(i => i.Path == targetPath);
            }

            if (match != null)
            {
                SelectedItem = match;
            }
            else
            {
                SelectedItem = Items[0];
            }
        }
        else
        {
            SelectedItem = null;
        }
    }

    private bool IsMatch(string name, string filterText, FilterMethod mode)
    {
        if (string.IsNullOrWhiteSpace(filterText)) return true;

        var patterns = filterText.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in patterns)
        {
            bool isExclusion = p.StartsWith("!");
            string pattern = isExclusion ? p.Substring(1) : p;
            
            if (string.IsNullOrEmpty(pattern)) continue;

            bool matched = false;
            
            // For Fuzzy, we use the existing logic which handles pinyin internally char-by-char
            if (mode == FilterMethod.Fuzzy)
            {
                matched = IsFuzzyMatch(name, pattern);
            }
            else
            {
                // For other modes, we match against Name OR Pinyin Initials
                string pinyinName = _pinyinService.GetPinyinInitials(name);

                switch (mode)
                {
                    case FilterMethod.Text:
                        matched = name.Contains(pattern, System.StringComparison.OrdinalIgnoreCase) ||
                                  pinyinName.Contains(pattern, System.StringComparison.OrdinalIgnoreCase);
                        break;
                    case FilterMethod.Glob:
                        try 
                        {
                            // Convert glob to regex
                            string regexPattern = "^" + Regex.Escape(pattern)
                                .Replace("\\*", ".*")
                                .Replace("\\?", ".") + "$";
                            matched = Regex.IsMatch(name, regexPattern, RegexOptions.IgnoreCase) ||
                                      Regex.IsMatch(pinyinName, regexPattern, RegexOptions.IgnoreCase);
                        }
                        catch
                        {
                            matched = name.Contains(pattern, System.StringComparison.OrdinalIgnoreCase);
                        }
                        break;
                    case FilterMethod.Regex:
                        try
                        {
                            matched = Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase) ||
                                      Regex.IsMatch(pinyinName, pattern, RegexOptions.IgnoreCase);
                        }
                        catch
                        {
                            matched = false;
                        }
                        break;
                }
            }

            if (isExclusion && matched) return false;
            if (!isExclusion && !matched) return false;
        }
        return true;
    }

    private bool IsFuzzyMatch(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return true;
        if (string.IsNullOrEmpty(text)) return false;

        int patternIdx = 0;
        int textIdx = 0;

        while (patternIdx < pattern.Length && textIdx < text.Length)
        {
            char pChar = char.ToLowerInvariant(pattern[patternIdx]);
            char tChar = char.ToLowerInvariant(text[textIdx]);

            // Match if:
            // 1. Characters match directly (case-insensitive)
            // 2. OR Text char's Pinyin initial matches Pattern char
            if (pChar == tChar || _pinyinService.GetFirstPinyinChar(text[textIdx]) == pChar)
            {
                patternIdx++;
            }
            textIdx++;
        }

        return patternIdx == pattern.Length;
    }

    public IEnumerable<string> GetCompletionMatches(string prefix)
    {
        return _allItems
            .Select(i => i.Name)
            .Where(n => n.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase));
    }

    private string _lastSearchPattern = string.Empty;
    public string LastSearchPattern => _lastSearchPattern;
    private FilterMethod _lastSearchMethod = FilterMethod.Fuzzy;
    private bool _lastSearchBackward = false;

    public void Search(string pattern, bool backward = false, bool includeCurrent = false, FilterMethod method = FilterMethod.Fuzzy)
    {
        if (string.IsNullOrEmpty(pattern) || Items.Count == 0) return;

        _lastSearchPattern = pattern;
        _lastSearchBackward = backward;
        _lastSearchMethod = method;

        PerformSearch(pattern, backward, includeCurrent, method);
    }

    private bool PerformSearch(string pattern, bool backward, bool includeCurrent = false, FilterMethod method = FilterMethod.Fuzzy)
    {
        if (string.IsNullOrEmpty(pattern) || Items.Count == 0) return false;

        // Start from current selection
        int startIndex = Items.IndexOf(SelectedItem ?? Items[0]);
        
        if (!includeCurrent)
        {
            if (backward)
            {
                startIndex--;
                if (startIndex < 0) startIndex = Items.Count - 1;
            }
            else
            {
                startIndex++;
                if (startIndex >= Items.Count) startIndex = 0;
            }
        }

        for (int i = 0; i < Items.Count; i++)
        {
            int index;
            if (backward)
                index = (startIndex - i + Items.Count) % Items.Count;
            else
                index = (startIndex + i) % Items.Count;

            if (IsMatch(Items[index].Name, pattern, method))
            {
                SelectedItem = Items[index];
                return true;
            }
        }
        return false;
    }

    public bool HasSearchPattern => !string.IsNullOrEmpty(_lastSearchPattern);

    public bool SearchNext()
    {
        if (!string.IsNullOrEmpty(_lastSearchPattern))
        {
            return PerformSearch(_lastSearchPattern, _lastSearchBackward, false, _lastSearchMethod);
        }
        return false;
    }

    public bool SearchPrev()
    {
        if (!string.IsNullOrEmpty(_lastSearchPattern))
        {
            return PerformSearch(_lastSearchPattern, !_lastSearchBackward, false, _lastSearchMethod);
        }
        return false;
    }

    public bool JumpToNext(string key)
    {
        if (Items.Count == 0) return false;
        
        int startIndex = Items.IndexOf(SelectedItem ?? Items[0]) + 1;
        if (startIndex >= Items.Count) startIndex = 0;

        for (int i = 0; i < Items.Count; i++)
        {
            int index = (startIndex + i) % Items.Count;
            if (_pinyinService.Matches(Items[index].Name, key, _config.AnchorFind))
            {
                SelectedItem = Items[index];
                return true;
            }
        }
        return false;
    }

    public bool JumpToPrev(string key)
    {
        if (Items.Count == 0) return false;
        
        int startIndex = Items.IndexOf(SelectedItem ?? Items[0]) - 1;
        if (startIndex < 0) startIndex = Items.Count - 1;

        for (int i = 0; i < Items.Count; i++)
        {
            int index = (startIndex - i + Items.Count) % Items.Count;
            if (_pinyinService.Matches(Items[index].Name, key, _config.AnchorFind))
            {
                SelectedItem = Items[index];
                return true;
            }
        }
        return false;
    }

    public void MoveUp()
    {
        if (Items.Count == 0 || SelectedItem == null) return;
        var index = Items.IndexOf(SelectedItem);
        if (index > 0)
        {
            SelectedItem = Items[index - 1];
        }
    }

    public void MoveDown()
    {
        if (Items.Count == 0 || SelectedItem == null) return;
        var index = Items.IndexOf(SelectedItem);
        if (index < Items.Count - 1)
        {
            SelectedItem = Items[index + 1];
        }
    }

    public void ToggleSelection()
    {
        if (SelectedItem != null)
        {
            SelectedItem.IsSelected = !SelectedItem.IsSelected;
        }
    }

    public void InvertSelection()
    {
        foreach (var item in Items)
        {
            item.IsSelected = !item.IsSelected;
        }
    }

    public void UnselectAll()
    {
        foreach (var item in Items)
        {
            item.IsSelected = false;
        }
    }

    public IEnumerable<FileSystemItem> GetSelectedItems()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count > 0)
        {
            return selected;
        }
        
        if (SelectedItem != null)
        {
            return new List<FileSystemItem> { SelectedItem };
        }

        return Enumerable.Empty<FileSystemItem>();
    }
}

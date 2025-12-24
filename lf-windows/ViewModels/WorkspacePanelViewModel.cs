using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LfWindows.Models;
using LfWindows.Services;
using Avalonia.Threading;

namespace LfWindows.ViewModels;

public partial class WorkspacePanelViewModel : ViewModelBase
{
    private readonly IWorkspaceService _workspaceService;
    public IWorkspaceService WorkspaceService => _workspaceService;
    private readonly IFileOperationsService _fileOperationsService;
    private Action<string>? _navigateCallback;
    private Action<string>? _openFileCallback;
    private Action? _closeCallback;
    private Action<string, string, Action>? _confirmCallback;
    // public event Action? RequestLinkMode; // Removed as wl is no longer supported in panel

    [ObservableProperty]
    private ObservableCollection<WorkspaceModel> _workspaces = new();

    [ObservableProperty]
    private WorkspaceModel? _selectedWorkspace;

    [ObservableProperty]
    private WorkspaceItem? _selectedItem;

    [ObservableProperty]
    private bool _isWorkspaceListFocused = true;

    [ObservableProperty]
    private bool _isLinkMode = false;

    [ObservableProperty]
    private string _linkModeTitle = "";

    private List<string> _linkSourceItems = new();

    public WorkspacePanelViewModel(
        IWorkspaceService workspaceService, 
        IFileOperationsService fileOperationsService)
    {
        _workspaceService = workspaceService;
        _fileOperationsService = fileOperationsService;
        _workspaceService.WorkspaceChanged += OnWorkspaceChanged;
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(LoadWorkspacesAsync);
    }

    public void SetNavigateCallback(Action<string> callback)
    {
        _navigateCallback = callback;
    }

    public void SetOpenFileCallback(Action<string> callback)
    {
        _openFileCallback = callback;
    }

    public void SetCloseCallback(Action callback)
    {
        _closeCallback = callback;
    }

    public void SetConfirmCallback(Action<string, string, Action> callback)
    {
        _confirmCallback = callback;
    }

    public async Task LoadWorkspacesAsync()
    {
        var list = await _workspaceService.GetWorkspacesAsync();
        Workspaces = new ObservableCollection<WorkspaceModel>(list);
        
        // Re-select if possible
        if (SelectedWorkspace != null)
        {
            var match = Workspaces.FirstOrDefault(w => w.Name == SelectedWorkspace.Name) 
                     ?? Workspaces.FirstOrDefault(w => w.Path == SelectedWorkspace.Path);
            
            if (match != null)
            {
                SelectedWorkspace = match;
            }
            else if (Workspaces.Any())
            {
                SelectedWorkspace = Workspaces.First();
            }
        }
        else if (Workspaces.Any())
        {
            SelectedWorkspace = Workspaces.First();
        }
        
        RefreshShortcutKeys();
    }

    private void RefreshShortcutKeys()
    {
        for (int i = 0; i < Workspaces.Count; i++)
        {
            Workspaces[i].ShortcutKey = (i < 9) ? (i + 1).ToString() : "";
        }
    }

    partial void OnSelectedWorkspaceChanged(WorkspaceModel? value)
    {
        if (value != null)
        {
            // Defer selection to ensure View has updated ItemsSource
            Dispatcher.UIThread.Post(() => 
            {
                if (value.Items.Any())
                {
                    SelectedItem = value.Items.First();
                }
                else
                {
                    SelectedItem = null;
                }
            }, DispatcherPriority.Input);
        }
    }

    public void StartLinkMode(List<string> items)
    {
        IsLinkMode = true;
        _linkSourceItems = items;
        LinkModeTitle = $"Select workspace to link {items.Count} items...";
        IsWorkspaceListFocused = true;
    }

    public void StartCreation()
    {
        IsWorkspaceListFocused = true;
        var newWs = new WorkspaceModel 
        { 
            Name = "", 
            IsEditing = true, 
            EditName = "" 
        };
        Workspaces.Add(newWs);
        SelectedWorkspace = newWs;
        // View should auto-focus the textbox due to IsEditing=true
    }

    public void StartRename()
    {
        if (SelectedWorkspace != null)
        {
            IsWorkspaceListFocused = true;
            SelectedWorkspace.EditName = SelectedWorkspace.Name;
            SelectedWorkspace.IsEditing = true;
        }
    }

    public async Task ConfirmEdit(WorkspaceModel ws)
    {
        if (string.IsNullOrWhiteSpace(ws.EditName))
        {
            // If name is empty, cancel creation or rename
            CancelEdit(ws);
            return;
        }

        if (string.IsNullOrEmpty(ws.Name)) // New workspace
        {
            try 
            {
                await _workspaceService.CreateWorkspaceAsync(ws.EditName);
                ws.IsEditing = false;
                await LoadWorkspacesAsync();
                
                // Select the newly created workspace
                var created = Workspaces.FirstOrDefault(w => w.Name == ws.EditName);
                if (created != null) SelectedWorkspace = created;
            }
            catch
            {
                // Handle error (e.g. name exists)
                CancelEdit(ws);
            }
        }
        else // Rename
        {
            try
            {
                if (ws.Name != ws.EditName)
                {
                    await _workspaceService.RenameWorkspaceAsync(ws.Name, ws.EditName);
                    ws.IsEditing = false;
                    await LoadWorkspacesAsync();

                    // Select the renamed workspace
                    var renamed = Workspaces.FirstOrDefault(w => w.Name == ws.EditName);
                    if (renamed != null) SelectedWorkspace = renamed;
                }
                else
                {
                    ws.IsEditing = false;
                }
            }
            catch
            {
                CancelEdit(ws);
            }
        }
    }

    public void CancelEdit(WorkspaceModel ws)
    {
        ws.IsEditing = false;
        if (string.IsNullOrEmpty(ws.Name)) // Was creating new
        {
            Workspaces.Remove(ws);
            if (Workspaces.Any()) SelectedWorkspace = Workspaces.First();
        }
    }

    public async Task DeleteCurrentWorkspace()
    {
        if (SelectedWorkspace != null)
        {
            if (_confirmCallback != null)
            {
                _confirmCallback("Delete Workspace", $"Are you sure you want to delete workspace '{SelectedWorkspace.Name}'?", async () => 
                {
                    await _workspaceService.DeleteWorkspaceAsync(SelectedWorkspace.Name);
                    await LoadWorkspacesAsync();
                });
            }
            else
            {
                // Fallback
                await _workspaceService.DeleteWorkspaceAsync(SelectedWorkspace.Name);
                await LoadWorkspacesAsync();
            }
        }
    }

    public async Task DeleteCurrentShortcut()
    {
        if (SelectedWorkspace != null && SelectedItem != null)
        {
            if (_confirmCallback != null)
            {
                _confirmCallback("Delete Shortcut", $"Are you sure you want to delete shortcut '{SelectedItem.Name}'?", async () => 
                {
                    await _workspaceService.RemoveShortcutAsync(SelectedWorkspace.Name, SelectedItem.Name);
                    await LoadWorkspacesAsync();
                });
            }
            else
            {
                // Fallback
                await _workspaceService.RemoveShortcutAsync(SelectedWorkspace.Name, SelectedItem.Name);
                await LoadWorkspacesAsync();
            }
        }
    }

    public async Task ConfirmLink()
    {
        // Ensure we have a workspace selected
        if (SelectedWorkspace == null && Workspaces.Any())
        {
            SelectedWorkspace = Workspaces.First();
        }

        if (IsLinkMode && SelectedWorkspace != null && _linkSourceItems.Any())
        {
            var targetName = SelectedWorkspace.Name;
            // Use case-insensitive comparison for paths
            var targetPaths = new HashSet<string>(_linkSourceItems, StringComparer.OrdinalIgnoreCase);

            foreach (var path in _linkSourceItems)
            {
                await _workspaceService.AddShortcutAsync(targetName, path);
            }
            IsLinkMode = false;
            LinkModeTitle = "";
            await LoadWorkspacesAsync();

            // Explicitly restore selection to the target workspace
            var targetWorkspace = Workspaces.FirstOrDefault(w => w.Name == targetName);
            if (targetWorkspace != null)
            {
                SelectedWorkspace = targetWorkspace;
                
                // Focus right column
                IsWorkspaceListFocused = false;

                // Defer selection to ensure View has updated ItemsSource
                Dispatcher.UIThread.Post(() => 
                {
                    // Find the item to select (one of the linked ones, or last one)
                    // Try to match by TargetPath first
                    var itemToSelect = SelectedWorkspace.Items.FirstOrDefault(i => targetPaths.Contains(i.TargetPath));
                    
                    // If not found, try to match by filename (in case TargetPath resolution differs)
                    if (itemToSelect == null)
                    {
                         itemToSelect = SelectedWorkspace.Items.FirstOrDefault(i => targetPaths.Any(tp => Path.GetFileName(tp).Equals(Path.GetFileName(i.TargetPath), StringComparison.OrdinalIgnoreCase)));
                    }
    
                    // Fallback to last item
                    if (itemToSelect == null)
                    {
                        itemToSelect = SelectedWorkspace.Items.LastOrDefault();
                    }
                    
                    if (itemToSelect != null)
                    {
                        SelectedItem = itemToSelect;
                    }
                }, DispatcherPriority.Input);
            }
        }
    }

    [RelayCommand]
    public void ExecuteItem(WorkspaceItem item)
    {
        if (item != null && !string.IsNullOrEmpty(item.TargetPath))
        {
            _navigateCallback?.Invoke(item.TargetPath);
        }
    }

    [RelayCommand]
    public void OpenItem(WorkspaceItem item)
    {
        if (item != null && !string.IsNullOrEmpty(item.TargetPath))
        {
            _openFileCallback?.Invoke(item.TargetPath);
        }
    }

    private string _inputBuffer = "";

    public async Task HandleInputAsync(string key)
    {
        // If any workspace is editing, ignore global keys except Enter/Esc which are handled by View events usually
        // But if focus is lost from TextBox, we need to handle them here
        var editingWs = Workspaces.FirstOrDefault(w => w.IsEditing);
        if (editingWs != null)
        {
            if (key == "Escape")
            {
                CancelEdit(editingWs);
            }
            else if (key == "Enter" || key == "Return")
            {
                await ConfirmEdit(editingWs);
            }
            return;
        }

        if (key == "Escape")
        {
            _inputBuffer = "";
            if (IsLinkMode)
            {
                IsLinkMode = false;
                LinkModeTitle = "";
            }
            else
            {
                _closeCallback?.Invoke();
            }
            return;
        }

        if (key == "Tab")
        {
            _inputBuffer = "";
            IsWorkspaceListFocused = !IsWorkspaceListFocused;
            return;
        }

        // Handle buffering for 'w' commands
        if (_inputBuffer == "w")
        {
            _inputBuffer = ""; // Reset buffer
            if (key == "s") { StartCreation(); return; }
            if (key == "d") { await DeleteCurrentWorkspace(); return; }
            if (key == "r") { StartRename(); return; }
            // If key is not s, d, r, fall through to handle as normal key (e.g. 'wj' -> handle 'j')
        }

        if (key == "w" && IsWorkspaceListFocused)
        {
            _inputBuffer = "w";
            return;
        }

        if (IsWorkspaceListFocused)
        {
            // Navigation
            if (key == "j" || key == "Down")
            {
                if (SelectedWorkspace != null)
                {
                    int index = Workspaces.IndexOf(SelectedWorkspace);
                    if (index < Workspaces.Count - 1) SelectedWorkspace = Workspaces[index + 1];
                }
            }
            else if (key == "k" || key == "Up")
            {
                if (SelectedWorkspace != null)
                {
                    int index = Workspaces.IndexOf(SelectedWorkspace);
                    if (index > 0) SelectedWorkspace = Workspaces[index - 1];
                }
            }
            else if (key == "Enter" || key == "Return")
            {
                if (IsLinkMode) await ConfirmLink();
            }
            else if (key == "l")
            {
                IsWorkspaceListFocused = false;
                if (SelectedItem == null && SelectedWorkspace != null && SelectedWorkspace.Items.Any())
                {
                    SelectedItem = SelectedWorkspace.Items.First();
                }
            }
            // 1-9 Selection
            else if (int.TryParse(key, out int num) && num >= 1 && num <= 9)
            {
                if (num <= Workspaces.Count) SelectedWorkspace = Workspaces[num - 1];
            }
        }
        else // Right Column Focused
        {
            // Navigation
            if (key == "j" || key == "Down")
            {
                if (SelectedWorkspace != null && SelectedItem != null)
                {
                    int index = SelectedWorkspace.Items.IndexOf(SelectedItem);
                    if (index < SelectedWorkspace.Items.Count - 1) SelectedItem = SelectedWorkspace.Items[index + 1];
                }
            }
            else if (key == "k" || key == "Up")
            {
                if (SelectedWorkspace != null && SelectedItem != null)
                {
                    int index = SelectedWorkspace.Items.IndexOf(SelectedItem);
                    if (index > 0) SelectedItem = SelectedWorkspace.Items[index - 1];
                }
            }
            else if (key == "h")
            {
                IsWorkspaceListFocused = true;
            }
            // Commands
            else if (key == "c") await DeleteCurrentShortcut();
            else if (key == "Enter" || key == "Return")
            {
                if (SelectedItem != null) OpenItem(SelectedItem);
            }
            else if (key == "o")
            {
                if (SelectedItem != null) ExecuteItem(SelectedItem);
            }
            // 1-9 Execution
            else if (int.TryParse(key, out int num) && num >= 1 && num <= 9)
            {
                if (SelectedWorkspace != null && num <= SelectedWorkspace.Items.Count)
                {
                    OpenItem(SelectedWorkspace.Items[num - 1]);
                }
            }
        }
    }
}

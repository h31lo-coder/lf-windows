using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LfWindows.Models;

public partial class WorkspaceModel : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string ShortcutKey { get; set; } = string.Empty; // A-Z
    
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = string.Empty;

    public ObservableCollection<WorkspaceItem> Items { get; set; } = new();
}

public partial class WorkspaceItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string LinkPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public string ShortcutKey { get; set; } = string.Empty; // 1-9
    
    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _icon;

    [ObservableProperty]
    private bool _isSelected;
}

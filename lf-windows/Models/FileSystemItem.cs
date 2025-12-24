using CommunityToolkit.Mvvm.ComponentModel;
using System;
using Avalonia.Media.Imaging;

namespace LfWindows.Models;

public enum FileType
{
    File,
    Directory,
    Symlink
}

public partial class FileSystemItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public FileType Type { get; set; }
    
    [ObservableProperty]
    private long _size = -1;
    
    public DateTime Modified { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime LastAccessTime { get; set; }
    public string Extension { get; set; } = string.Empty;
    public bool IsHidden { get; set; }
    public bool IsSystem { get; set; }
    public string Permissions { get; set; } = "------";
    public string Owner { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _icon;

    public string SizeString => Type == FileType.Directory ? "" : FormatSize(Size);

    public string ModifiedString => Modified.ToString("MMM dd HH:mm");

    private string FormatSize(long bytes)
    {
        if (bytes < 0) return "";
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
        {
            number = number / 1024;
            counter++;
        }
        return string.Format("{0:n1}{1}", number, suffixes[counter]);
    }
}

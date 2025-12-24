using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LfWindows.Models;

public partial class YankHistoryItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    [NotifyPropertyChangedFor(nameof(ContentText))]
    private int _index;

    public List<string> Files { get; set; } = new();

    public string ContentText
    {
        get
        {
            if (Files == null || Files.Count == 0) return "Empty";
            var names = Files.Select(System.IO.Path.GetFileName);
            var preview = string.Join(", ", names.Take(5)); // Increased preview slightly
            if (Files.Count > 5) preview += "...";
            return $"{Files.Count} files [{preview}]";
        }
    }

    public string ToolTipText
    {
        get
        {
            if (Files == null || Files.Count == 0) return "Empty";
            return string.Join("\n", Files);
        }
    }

    public string DisplayText 
    {
        get 
        {
            return $"{Index}: {ContentText}";
        }
    }
}

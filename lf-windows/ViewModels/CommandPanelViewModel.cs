using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace LfWindows.ViewModels;

public partial class CommandPanelViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<CommandItem> _commands = new();

    public void AddCommand(string name, string shortcut, ICommand command)
    {
        var item = new CommandItem { Name = name, Shortcut = shortcut, Command = command };
        item.ProcessNameForShortcut();
        Commands.Add(item);
    }
    
    public void Clear()
    {
        Commands.Clear();
    }
}

public class CommandItem
{
    public string Name { get; set; } = "";
    public string Shortcut { get; set; } = "";
    public ICommand? Command { get; set; }

    public string PreShortcutText { get; set; } = "";
    public string ShortcutText { get; set; } = "";
    public string PostShortcutText { get; set; } = "";

    public void ProcessNameForShortcut()
    {
        if (string.IsNullOrEmpty(Name) || string.IsNullOrEmpty(Shortcut))
        {
            PreShortcutText = Name;
            return;
        }

        // Find the first occurrence of the shortcut character in the name (case-insensitive)
        int index = Name.IndexOf(Shortcut, System.StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            PreShortcutText = Name.Substring(0, index);
            ShortcutText = Name.Substring(index, Shortcut.Length);
            PostShortcutText = Name.Substring(index + Shortcut.Length);
        }
        else
        {
            // Shortcut not found in name (fallback)
            PreShortcutText = Name;
        }
    }
}

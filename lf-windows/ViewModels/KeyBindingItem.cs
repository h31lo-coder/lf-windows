using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace LfWindows.ViewModels;

public partial class KeyBindingItem : ObservableObject
{
    [ObservableProperty]
    private string _commandName;

    [ObservableProperty]
    private string _keySequence;

    [ObservableProperty]
    private string _description;

    public KeyBindingItem(string commandName, string keySequence, string description = "")
    {
        _commandName = commandName;
        _keySequence = keySequence;
        _description = description;
    }
}

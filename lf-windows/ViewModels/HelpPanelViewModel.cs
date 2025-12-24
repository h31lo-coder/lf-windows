using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace LfWindows.ViewModels;

public partial class HelpItem : ObservableObject
{
    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;
}

public partial class HelpPanelViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Help";

    public ObservableCollection<HelpItem> Items { get; } = new();

    [ObservableProperty]
    private HelpItem? _selectedItem;

    [RelayCommand]
    public void ScrollUp()
    {
        if (Items.Count == 0) return;
        if (SelectedItem == null) 
        {
            SelectedItem = Items[0];
            return;
        }
        int index = Items.IndexOf(SelectedItem);
        if (index > 0)
        {
            SelectedItem = Items[index - 1];
        }
    }

    [RelayCommand]
    public void ScrollDown()
    {
        if (Items.Count == 0) return;
        if (SelectedItem == null) 
        {
            SelectedItem = Items[0];
            return;
        }
        int index = Items.IndexOf(SelectedItem);
        if (index < Items.Count - 1)
        {
            SelectedItem = Items[index + 1];
        }
    }
}

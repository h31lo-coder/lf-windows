using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace LfWindows.ViewModels;

public partial class ConfirmationDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Confirm";

    [ObservableProperty]
    private string _message = "";

    public Action? OnConfirm { get; set; }
    public Action? OnCancel { get; set; }

    [RelayCommand]
    private void Confirm()
    {
        OnConfirm?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        OnCancel?.Invoke();
    }
}

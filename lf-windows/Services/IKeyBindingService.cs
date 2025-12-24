using Avalonia.Input;
using System.Threading.Tasks;
using System.Windows.Input;
using System;

namespace LfWindows.Services;

public interface IKeyBindingService
{
    event Action<InputMode>? ModeChanged;
    InputMode CurrentMode { get; }
    void SetMode(InputMode mode);
    void RegisterBinding(InputMode mode, string key, ICommand command);
    void RegisterDefaultHandler(InputMode mode, Func<string, Task<bool>> handler);
    Task<bool> HandleKeyAsync(KeyEventArgs e);
}

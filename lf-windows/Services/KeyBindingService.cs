using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;

namespace LfWindows.Services;

public enum InputMode
{
    Normal,
    Visual,
    Command,
    Filter,
    Find,
    Bookmark,
    Jump,
    Dialog,
    YankHistory,
    Workspace,
    CommandPanel,
    Help
}

public class KeyBindingService : IKeyBindingService
{
    private readonly Dictionary<InputMode, Dictionary<string, ICommand>> _bindings = new();
    private readonly Dictionary<InputMode, Func<string, Task<bool>>> _defaultHandlers = new();
    private InputMode _currentMode = InputMode.Normal;

    public event Action<InputMode>? ModeChanged;

    public InputMode CurrentMode => _currentMode;

    public KeyBindingService()
    {
        foreach (InputMode mode in Enum.GetValues(typeof(InputMode)))
        {
            _bindings[mode] = new Dictionary<string, ICommand>();
        }
    }

    public void SetMode(InputMode mode)
    {
        if (_currentMode != mode)
        {
            _currentMode = mode;
            ModeChanged?.Invoke(mode);
        }
    }

    public IReadOnlyDictionary<string, ICommand> GetBindings(InputMode mode)
    {
        return _bindings[mode];
    }

    public void RegisterBinding(InputMode mode, string key, ICommand command)
    {
        _bindings[mode][key] = command;
    }

    public void RegisterDefaultHandler(InputMode mode, Func<string, Task<bool>> handler)
    {
        _defaultHandlers[mode] = handler;
    }

    private string _keySequence = "";
    private int _count = 0;

    public async Task<bool> HandleKeyAsync(KeyEventArgs e)
    {
        // Ignore modifier keys by themselves
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
            e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
            e.Key == Key.LeftShift || e.Key == Key.RightShift ||
            e.Key == Key.LWin || e.Key == Key.RWin)
        {
            return false;
        }

        var key = ConvertToKeyString(e);
        
        // Only handle sequences/counts in Normal/Visual mode
        if (_currentMode == InputMode.Normal || _currentMode == InputMode.Visual)
        {
            // Handle Count (1-9 starts count, 0 extends count if count > 0)
            // We need to check if the key is a pure digit without modifiers (except Shift for symbols, but digits usually don't have shift)
            if (key.Length == 1 && char.IsDigit(key[0]))
            {
                int digit = key[0] - '0';
                // If count is 0, '0' is a command (Start of line), not a digit
                if (_count > 0 || digit > 0) 
                {
                    _count = _count * 10 + digit;
                    return true;
                }
            }
            
            _keySequence += key;
            
            // Check for exact match
            if (_bindings[_currentMode].TryGetValue(_keySequence, out var command))
            {
                int repeat = Math.Max(1, _count);
                // Execute command 'repeat' times
                // Note: Some commands might handle the count themselves if we passed it, 
                // but for now we just repeat the execution.
                // Ideally, we should pass the count to the command context.
                for(int i=0; i<repeat; i++)
                {
                    if (command.CanExecute(null)) command.Execute(null);
                }
                _keySequence = "";
                _count = 0;
                return true;
            }
            
            // Check for prefix match
            bool isPrefix = false;
            foreach(var k in _bindings[_currentMode].Keys)
            {
                if (k.StartsWith(_keySequence))
                {
                    isPrefix = true;
                    break;
                }
            }
            
            if (isPrefix) return true; // Wait for next key
            
            // No match, reset sequence and try to handle as single key if sequence was length 1?
            // Or just reset and fail.
            // If I typed 'z' and it's not a prefix, and not a command, it's a no-op.
            _keySequence = "";
            _count = 0;
        }
        else
        {
            // Other modes (Command, Filter, etc.) usually handle keys directly or via default handler
            if (_bindings[_currentMode].TryGetValue(key, out var command))
            {
                if (command.CanExecute(null))
                {
                    command.Execute(null);
                    return true;
                }
            }
        }

        if (_defaultHandlers.TryGetValue(_currentMode, out var handler))
        {
            return await handler(key);
        }

        return false;
    }

    private string ConvertToKeyString(KeyEventArgs e)
    {
        // Special handling for Vim-style keys
        // If no modifiers (except Shift), try to map to char
        
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (!ctrl && !alt && !meta)
        {
            // Letters
            if (e.Key >= Key.A && e.Key <= Key.Z)
            {
                return shift ? e.Key.ToString() : e.Key.ToString().ToLower();
            }
            // Digits
            if (e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                // Shift+Digit usually maps to symbols, but KeyEventArgs gives us the key code.
                // We might need to handle symbols manually if we want full Vim compat (e.g. $ for Shift+4)
                // For now, let's just return the digit or Shift+Digit
                if (!shift) return (e.Key - Key.D0).ToString();
            }
            // NumPad
            if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            {
                return (e.Key - Key.NumPad0).ToString();
            }

            // Special characters
            if (!shift)
            {
                if (e.Key == Key.OemPeriod || e.Key == Key.Decimal) return ".";
                if (e.Key == Key.OemComma) return ",";
                if (e.Key == Key.OemSemicolon) return ";";
                if (e.Key == Key.OemQuotes) return "'";
                if (e.Key == Key.OemQuestion || e.Key == Key.Divide) return "/"; // Without shift it's /, with shift it's ?
                if (e.Key == Key.OemOpenBrackets) return "[";
                if (e.Key == Key.OemCloseBrackets) return "]";
                if (e.Key == Key.OemBackslash) return "\\";
                if (e.Key == Key.OemMinus) return "-";
                if (e.Key == Key.OemPlus) return "=";
            }
        }

        var parts = new List<string>();
        
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift"); // We include Shift if it wasn't handled above (e.g. Ctrl+Shift+A)
        if (meta) parts.Add("Win");
        
        parts.Add(e.Key.ToString());
        
        return string.Join("+", parts);
    }
}

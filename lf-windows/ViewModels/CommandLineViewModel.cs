using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace LfWindows.ViewModels;

public partial class CommandLineViewModel : ViewModelBase
{
    private List<string> _history = new();
    private int _historyIndex = -1;
    private string _tempCommandText = string.Empty;

    [ObservableProperty]
    private string _commandText = string.Empty;

    [ObservableProperty]
    private string _prefix = string.Empty;

    [ObservableProperty]
    private bool _isVisible = false;

    [ObservableProperty]
    private int _selectionStart = 0;

    [ObservableProperty]
    private int _selectionEnd = 0;

    [ObservableProperty]
    private bool _isLocked = false;

    [ObservableProperty]
    private bool _isInputHidden = false;

    public event EventHandler<string>? CommandExecuted;
    public event EventHandler? Cancelled;
    public event EventHandler? TabPressed;

    public void TriggerTab()
    {
        TabPressed?.Invoke(this, EventArgs.Empty);
    }

    public void Activate()
    {
        Prefix = ": ";
        CommandText = "";
        SelectionStart = 0;
        SelectionEnd = 0;
        IsVisible = true;
        IsLocked = false;
        IsInputHidden = false;
    }

    public void ActivateWithText(string text)
    {
        if (text.StartsWith("/") || text.StartsWith("?") || text.StartsWith("$") || 
            text.StartsWith("%") || text.StartsWith("&") || text.StartsWith("!"))
        {
            Prefix = text.Substring(0, 1) + ": ";
            CommandText = text.Substring(1);
        }
        else if (text.StartsWith(":"))
        {
            Prefix = ": ";
            CommandText = text.Substring(1);
        }
        else
        {
            Prefix = ": ";
            CommandText = text;
        }
        SelectionStart = 0;
        SelectionEnd = 0;
        IsVisible = true;
        IsLocked = false;
        IsInputHidden = false;
    }

    public void ActivateWithTextAndSelection(string text, int start, int end)
    {
        if (text.StartsWith(":"))
        {
            Prefix = ": ";
            CommandText = text.Substring(1);
            SelectionStart = Math.Max(0, start - 1);
            SelectionEnd = Math.Max(0, end - 1);
        }
        else if (text.StartsWith("/") || text.StartsWith("?") || text.StartsWith("$") || 
                    text.StartsWith("%") || text.StartsWith("&") || text.StartsWith("!"))
        {
            Prefix = text.Substring(0, 1) + ": ";
            CommandText = text.Substring(1);
            SelectionStart = Math.Max(0, start - 1);
            SelectionEnd = Math.Max(0, end - 1);
        }
        else
        {
            Prefix = "";
            CommandText = text;
            SelectionStart = start;
            SelectionEnd = end;
        }
        IsVisible = true;
        IsLocked = false;
        IsInputHidden = false;
    }

    public void SetSelection(int start, int end)
    {
        SelectionStart = start;
        SelectionEnd = end;
    }

    public void Deactivate()
    {
        IsVisible = false;
        IsLocked = false;
        Prefix = string.Empty;
        CommandText = string.Empty;
        SelectionStart = 0;
        SelectionEnd = 0;
    }

    public void Lock()
    {
        IsLocked = true;
    }

    public void Unlock()
    {
        IsLocked = false;
    }

    public void Execute()
    {
        string fullCommand = Prefix + CommandText;
        if (!string.IsNullOrWhiteSpace(fullCommand))
        {
            // Add to history if not empty and not same as last
            if (_history.Count == 0 || _history[_history.Count - 1] != fullCommand)
            {
                _history.Add(fullCommand);
            }
            _historyIndex = _history.Count; // Reset index to end

            CommandExecuted?.Invoke(this, fullCommand);
        }
        else
        {
            Deactivate();
        }
    }

    public void HistoryUp()
    {
        if (_history.Count == 0) return;

        if (_historyIndex == _history.Count)
        {
            _tempCommandText = CommandText;
        }

        if (_historyIndex > 0)
        {
            _historyIndex--;
            SetCommandFromHistory(_history[_historyIndex]);
        }
    }

    public void HistoryDown()
    {
        if (_history.Count == 0) return;

        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            SetCommandFromHistory(_history[_historyIndex]);
        }
        else if (_historyIndex == _history.Count - 1)
        {
            _historyIndex++;
            CommandText = _tempCommandText;
            SelectionStart = CommandText.Length;
            SelectionEnd = CommandText.Length;
        }
    }

    private void SetCommandFromHistory(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Check for standard prefixes: "/: ", "?: ", etc.
        if (text.Length >= 3 && text[1] == ':' && text[2] == ' ')
        {
            char c = text[0];
            if (c == '/' || c == '?' || c == '$' || c == '%' || c == '&' || c == '!')
            {
                Prefix = text.Substring(0, 3);
                CommandText = text.Substring(3);
                SelectionStart = CommandText.Length;
                SelectionEnd = CommandText.Length;
                return;
            }
        }

        // Check for command prefix ": "
        if (text.StartsWith(": "))
        {
            Prefix = ": ";
            CommandText = text.Substring(2);
            SelectionStart = CommandText.Length;
            SelectionEnd = CommandText.Length;
            return;
        }
        
        // Fallback to ActivateWithText logic
        if (text.StartsWith("/") || text.StartsWith("?") || text.StartsWith("$") || 
            text.StartsWith("%") || text.StartsWith("&") || text.StartsWith("!"))
        {
            Prefix = text.Substring(0, 1) + ": ";
            CommandText = text.Substring(1);
        }
        else if (text.StartsWith(":"))
        {
            Prefix = ": ";
            CommandText = text.Substring(1);
        }
        else
        {
            Prefix = ": ";
            CommandText = text;
        }

        if (Prefix.EndsWith(" ") && CommandText.StartsWith(" "))
        {
            CommandText = CommandText.Substring(1);
        }

        SelectionStart = CommandText.Length;
        SelectionEnd = CommandText.Length;
    }

    public bool TryPopCommand()
    {
        if (string.IsNullOrEmpty(CommandText))
        {
            // If prefix is just the prompt, close the CLI
            if (Prefix.EndsWith(": "))
            {
                Cancel();
                return true;
            }
        }
        return false;
    }

    public void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
        // Deactivate() should be called by the subscriber if needed
    }
}

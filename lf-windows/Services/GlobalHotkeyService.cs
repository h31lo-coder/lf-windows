using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;

namespace LfWindows.Services;

public class GlobalHotkeyService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int HOTKEY_ID = 9000;
    private IntPtr _handle;

    // Modifiers
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    public event Action? HotKeyPressed;

    public void Register(IntPtr handle, Key key, KeyModifiers modifiers)
    {
        _handle = handle;
        Unregister(); // Unregister previous if any

        uint fsModifiers = 0;
        if (modifiers.HasFlag(KeyModifiers.Alt)) fsModifiers |= MOD_ALT;
        if (modifiers.HasFlag(KeyModifiers.Control)) fsModifiers |= MOD_CONTROL;
        if (modifiers.HasFlag(KeyModifiers.Shift)) fsModifiers |= MOD_SHIFT;
        if (modifiers.HasFlag(KeyModifiers.Meta)) fsModifiers |= MOD_WIN;

        // Map Avalonia Key to Virtual Key
        uint vk = (uint)KeyToVirtualKey(key);

        bool success = RegisterHotKey(_handle, HOTKEY_ID, fsModifiers, vk);
        if (!success)
        {
            int errorCode = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"Failed to register hotkey. Error code: {errorCode}");
        }
    }

    private int KeyToVirtualKey(Key key)
    {
        // F1 - F24
        if (key >= Key.F1 && key <= Key.F24)
        {
            return 0x70 + (key - Key.F1);
        }
        
        // A - Z
        if (key >= Key.A && key <= Key.Z)
        {
            return 0x41 + (key - Key.A);
        }
        
        // 0 - 9
        if (key >= Key.D0 && key <= Key.D9)
        {
            return 0x30 + (key - Key.D0);
        }

        // Numpad 0 - 9
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return 0x60 + (key - Key.NumPad0);
        }

        switch (key)
        {
            case Key.Cancel: return 0x03;
            case Key.Back: return 0x08;
            case Key.Tab: return 0x09;
            case Key.Clear: return 0x0C;
            case Key.Return: return 0x0D;
            case Key.Pause: return 0x13;
            case Key.CapsLock: return 0x14;
            case Key.Escape: return 0x1B;
            case Key.Space: return 0x20;
            case Key.PageUp: return 0x21;
            case Key.PageDown: return 0x22;
            case Key.End: return 0x23;
            case Key.Home: return 0x24;
            case Key.Left: return 0x25;
            case Key.Up: return 0x26;
            case Key.Right: return 0x27;
            case Key.Down: return 0x28;
            case Key.Select: return 0x29;
            case Key.Print: return 0x2A;
            case Key.Execute: return 0x2B;
            case Key.PrintScreen: return 0x2C;
            case Key.Insert: return 0x2D;
            case Key.Delete: return 0x2E;
            case Key.Help: return 0x2F;
            case Key.LWin: return 0x5B;
            case Key.RWin: return 0x5C;
            case Key.Apps: return 0x5D;
            case Key.Sleep: return 0x5F;
            case Key.Multiply: return 0x6A;
            case Key.Add: return 0x6B;
            case Key.Separator: return 0x6C;
            case Key.Subtract: return 0x6D;
            case Key.Decimal: return 0x6E;
            case Key.Divide: return 0x6F;
            case Key.NumLock: return 0x90;
            case Key.Scroll: return 0x91;
            case Key.LeftShift: return 0xA0;
            case Key.RightShift: return 0xA1;
            case Key.LeftCtrl: return 0xA2;
            case Key.RightCtrl: return 0xA3;
            case Key.LeftAlt: return 0xA4;
            case Key.RightAlt: return 0xA5;
            case Key.OemSemicolon: return 0xBA;
            case Key.OemPlus: return 0xBB;
            case Key.OemComma: return 0xBC;
            case Key.OemMinus: return 0xBD;
            case Key.OemPeriod: return 0xBE;
            case Key.OemQuestion: return 0xBF;
            case Key.OemTilde: return 0xC0;
            case Key.OemOpenBrackets: return 0xDB;
            case Key.OemPipe: return 0xDC;
            case Key.OemCloseBrackets: return 0xDD;
            case Key.OemQuotes: return 0xDE;
            default: return 0;
        }
    }

    public void Unregister()
    {
        if (_handle != IntPtr.Zero)
        {
            UnregisterHotKey(_handle, HOTKEY_ID);
        }
    }

    public void ProcessMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotKeyPressed?.Invoke();
            handled = true;
        }
    }
}

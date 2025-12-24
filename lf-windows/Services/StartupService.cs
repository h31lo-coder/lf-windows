using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

namespace LfWindows.Services;

[SupportedOSPlatform("windows")]
public class StartupService
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "lf-windows";
    private const string WatcherName = "lf-watcher";

    public bool IsAppStartupEnabled()
    {
        return IsStartupEnabled(AppName);
    }

    public void SetAppStartup(bool enable)
    {
        string? appPath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(appPath)) return;
        
        // If running as dotnet dll, we might need the exe wrapper or dotnet command
        // Assuming published exe or standard debug exe
        if (appPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
             appPath = appPath.Substring(0, appPath.Length - 4) + ".exe";
        }

        if (!File.Exists(appPath)) return;

        SetStartup(AppName, appPath, enable);
    }

    public bool IsWatcherStartupEnabled()
    {
        return IsStartupEnabled(WatcherName);
    }

    public void SetWatcherStartup(bool enable)
    {
        string? appDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
        if (string.IsNullOrEmpty(appDir)) appDir = AppDomain.CurrentDomain.BaseDirectory;

        string watcherPath = Path.Combine(appDir, "lf-watcher.exe");
        
        if (!File.Exists(watcherPath)) 
        {
            // Try to find it in the same directory as the dll if running in dev
            watcherPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lf-watcher.exe");
            if (!File.Exists(watcherPath)) return;
        }

        // lf-watcher should run in background, maybe add arguments if needed?
        // Usually it's a console app, so we might want to run it hidden if started via registry?
        // The watcher itself should handle being hidden or the registry entry doesn't control that easily without a wrapper.
        // But lf-watcher seems to be designed to run headless or hidden?
        // In App.axaml.cs it is started with CreateNoWindow = true.
        // When started via Registry Run key, it will show a console window if it's a Console App.
        // We should check if lf-watcher is a Windows App (Output type) or Console App.
        
        SetStartup(WatcherName, watcherPath, enable);
    }

    private bool IsStartupEnabled(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(name) != null;
    }

    private void SetStartup(string name, string path, bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;

        if (enable)
        {
            key.SetValue(name, $"\"{path}\"");
        }
        else
        {
            key.DeleteValue(name, false);
        }
    }
}

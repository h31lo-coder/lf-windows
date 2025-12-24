using System;
using System.Diagnostics;
using System.IO;

namespace LfWindows.Services;

internal static class DebugLogger
{
    private static readonly object _lock = new();
    private static readonly string _logPath = Path.Combine(AppContext.BaseDirectory, "debug_log.txt");

    // Toggle logging globally if needed
#if DEBUG
    public static bool Enabled { get; set; } = true;
#else
    public static bool Enabled { get; set; } = false;
#endif

    // Optional: enable console echo if required
    public static bool EchoConsole { get; set; } = false;

    public static void Log(string message)
    {
#if DEBUG
        if (!Enabled) return;
        var proc = Process.GetCurrentProcess();
        double wsMb = proc.WorkingSet64 / 1024d / 1024d;
        double privMb = proc.PrivateMemorySize64 / 1024d / 1024d;
        double gcMb = GC.GetTotalMemory(false) / 1024d / 1024d;
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} wsMB={wsMb:F1} privMB={privMb:F1} gcMB={gcMb:F1} {message}";

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch { }
        }

        Debug.WriteLine(line);
        if (EchoConsole)
        {
            Console.WriteLine(line);
        }
#endif
    }
}
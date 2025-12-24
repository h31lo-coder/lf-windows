using Avalonia;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO.Pipes;

namespace LfWindows;

sealed class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int MessageBox(IntPtr hWnd, String text, String caption, int options);

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        const string mutexName = "Global\\LfWindows_SingleInstance_Mutex_8A9B";
        bool createdNew;

        using (var mutex = new Mutex(true, mutexName, out createdNew))
        {
            if (!createdNew)
            {
                // App is already running, signal it to show
                try 
                {
                    using var client = new NamedPipeClientStream(".", "LfWindows_Signal_Pipe", PipeDirection.Out);
                    client.Connect(1000); // Wait 1 sec max
                }
                catch { /* Ignore errors if we can't signal */ }
                return;
            }

            try
            {
                BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_error.log");
                System.IO.File.WriteAllText(logPath, ex.ToString());
                MessageBox(IntPtr.Zero, "Startup Error: " + ex.Message + "\nSee startup_error.log for details.", "lf-windows Error", 0);
            }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

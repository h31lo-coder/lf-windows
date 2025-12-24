using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using LfWindows.ViewModels;
using LfWindows.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using LfWindows.Services;
using LibVLCSharp.Shared;
using System.IO.Pipes;
using System.Threading;

namespace LfWindows;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }
    private CancellationTokenSource? _pipeCts;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        // Initialize LibVLC
        Core.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection();
        ConfigureServices(collection);
        Services = collection.BuildServiceProvider();

        // Load configuration
        var configService = Services.GetRequiredService<IConfigService>();
        configService.Load();

        // Start Background Watcher
        StartWatcherService();

        // Preload Office Interop for faster preview
        if (configService.Current.Performance.EnableOfficePreload)
        {
            OfficeInteropService.Instance.Preload();
        }
        
        // Start Single Instance Pipe Server
        StartSingleInstanceServer();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            var vm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };
            
            // Prevent app from closing when MainWindow is hidden (e.g. via 'q')
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            
            desktop.MainWindow.Show();

            InitializeTrayIcon(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartWatcherService()
    {
        try
        {
            string? appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName);
            if (string.IsNullOrEmpty(appDir)) appDir = AppDomain.CurrentDomain.BaseDirectory;

            string watcherPath = System.IO.Path.Combine(appDir, "lf-watcher.exe");
            
            if (!System.IO.File.Exists(watcherPath))
            {
                 watcherPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lf-watcher.exe");
            }

            if (System.IO.File.Exists(watcherPath))
            {
                // Check if already running
                var existing = System.Diagnostics.Process.GetProcessesByName("lf-watcher");
                if (existing.Length == 0)
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = watcherPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = System.IO.Path.GetDirectoryName(watcherPath)
                    };
                    System.Diagnostics.Process.Start(psi);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start watcher: {ex}");
        }
    }

    private void InitializeTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var trayIcon = new TrayIcon
            {
                ToolTipText = "lf-windows",
                Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://lf-windows/Assets/avalonia-logo.ico")))
            };

            var menu = new NativeMenu();
            var showItem = new NativeMenuItem("Show");
            showItem.Click += Show_Click;
            menu.Add(showItem);

            var quitItem = new NativeMenuItem("Quit");
            quitItem.Click += Quit_Click;
            menu.Add(quitItem);

            trayIcon.Menu = menu;

            var trayIcons = TrayIcon.GetIcons(this);
            if (trayIcons != null)
            {
                trayIcons.Add(trayIcon);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize TrayIcon: {ex}");
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IFileOperationsService, FileOperationsService>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<KeyBindingService>();
        services.AddSingleton<IIconProvider, WindowsIconProvider>();
        services.AddSingleton<PreviewEngine>(sp => new PreviewEngine(
            sp.GetRequiredService<IIconProvider>(),
            sp.GetRequiredService<IConfigService>()
        ));
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<DirectorySizeCacheService>();
        services.AddSingleton<StartupService>();

        services.AddTransient<WorkspacePanelViewModel>();
        services.AddSingleton<MainWindowViewModel>();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private void Show_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
             var window = desktop.MainWindow;
             if (window != null)
             {
                 window.Show();
                 window.WindowState = Avalonia.Controls.WindowState.Normal;
                 window.Activate();
                 window.Focus();
             }
        }
    }

    private void Quit_Click(object? sender, EventArgs e)
    {
        _pipeCts?.Cancel();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
             desktop.Shutdown();
        }
    }

    private void StartSingleInstanceServer()
    {
        _pipeCts = new CancellationTokenSource();
        Task.Run(async () => 
        {
            while (!_pipeCts.Token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream("LfWindows_Signal_Pipe", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_pipeCts.Token);
                    
                    // Signal received
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                    {
                        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            var window = desktop.MainWindow;
                            if (window != null)
                            {
                                window.Show();
                                if (window.WindowState == WindowState.Minimized)
                                {
                                    window.WindowState = WindowState.Normal;
                                }
                                window.Activate();
                                window.Focus();
                            }
                        }
                    });
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        });
    }
}

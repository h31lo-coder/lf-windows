using System;
using System.IO;
using System.Threading;
using LfWindows.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LfWindows.Services;

public class ConfigService : IConfigService
{
    private readonly string _configPath;
    private AppConfig _currentConfig;
    private FileSystemWatcher? _watcher;

    public event EventHandler? ConfigChanged;

    public AppConfig Current => _currentConfig;

    public ConfigService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string configDir = Path.Combine(appData, "lf-windows");
        Directory.CreateDirectory(configDir);
        _configPath = Path.Combine(configDir, "config.yaml");

        // Migration: Check for old config if new one doesn't exist
        if (!File.Exists(_configPath))
        {
            // 1. Try to load from default config in App Directory (Installer provided)
            // AppDomain.CurrentDomain.BaseDirectory is reliable for finding files deployed with the app
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string defaultConfigPath = Path.Combine(appDir, "config.default.yaml");
            
            if (File.Exists(defaultConfigPath))
            {
                try
                {
                    File.Copy(defaultConfigPath, _configPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to copy default config: {ex.Message}");
                }
            }
            // 2. If still not exists (or copy failed), check for old LfDesktop config
            else
            {
                string oldConfigPath = Path.Combine(appData, "LfDesktop", "config.yaml");
                if (File.Exists(oldConfigPath))
                {
                    try
                    {
                        File.Copy(oldConfigPath, _configPath);
                    }
                    catch
                    {
                        // Ignore copy errors
                    }
                }
            }
        }

        _currentConfig = new AppConfig();
        SetupWatcher();
    }

    private void SetupWatcher()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_configPath);
            if (dir != null)
            {
                _watcher = new FileSystemWatcher(dir, "config.yaml");
                _watcher.NotifyFilter = NotifyFilters.LastWrite;
                _watcher.Changed += OnConfigChangedOnDisk;
                _watcher.EnableRaisingEvents = true;
            }
        }
        catch { }
    }

    private void OnConfigChangedOnDisk(object sender, FileSystemEventArgs e)
    {
        // Debounce
        Thread.Sleep(100);
        Load();
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Load()
    {
        if (!File.Exists(_configPath))
        {
            _currentConfig = new AppConfig();
            Save();
            return;
        }

        try
        {
            string yaml = "";
            for (int i = 0; i < 3; i++)
            {
                try { yaml = File.ReadAllText(_configPath); break; }
                catch { Thread.Sleep(50); }
            }

            if (string.IsNullOrEmpty(yaml))
            {
                if (_currentConfig == null) _currentConfig = new AppConfig();
                return;
            }

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            _currentConfig = deserializer.Deserialize<AppConfig>(yaml) ?? new AppConfig();
            EnsureDefaults();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load config: {ex.Message}");
            if (_currentConfig == null) _currentConfig = new AppConfig();
        }
    }

    private void EnsureDefaults()
    {
        // Migration: Check if LightTheme has Dark defaults (due to missing config) and fix them
        var light = _currentConfig.Appearance.LightTheme;
        
        // Check a key property. If it matches the class default (Dark), reset it.
        if (light.CodeBackgroundColor == "#1e1e1e")
        {
            light.CodeBackgroundColor = "#ffffff";
            light.CodeTextColor = "#000000";
            light.CodeSelectionColor = "#add6ff";
            light.CodeLineNumberColor = "#237893";

            light.CodeCommentColor = "#008000";
            light.CodeStringColor = "#a31515";
            light.CodeKeywordColor = "#0000ff";
            light.CodeNumberColor = "#098658";
            light.CodeMethodColor = "#795e26";
            light.CodeClassColor = "#267f99";
            light.CodeVariableColor = "#001080";
            light.CodeOperatorColor = "#000000";
            light.CodeHtmlTagColor = "#800000";
            light.CodeCssPropertyColor = "#ff0000";
            light.CodeCssValueColor = "#0451a5";
        }

        if (light.MarkdownHeadingColor == "#61AFEF") // Default Dark Blue
        {
            light.MarkdownHeadingColor = "#005CC5";
            light.MarkdownCodeColor = "#24292E";
            light.MarkdownBlockQuoteColor = "#22863A";
            light.MarkdownLinkColor = "#0366D6";
        }
    }

    public void Save()
    {
        try
        {
            if (_watcher != null) _watcher.EnableRaisingEvents = false;

            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            var yaml = serializer.Serialize(_currentConfig);
            
            for (int i = 0; i < 3; i++)
            {
                try 
                { 
                    File.WriteAllText(_configPath, yaml); 
                    break; 
                }
                catch 
                { 
                    Thread.Sleep(100); 
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
        }
        finally
        {
            if (_watcher != null) _watcher.EnableRaisingEvents = true;
        }
        
        // Notify listeners that config has been updated (even if just saved)
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }
}

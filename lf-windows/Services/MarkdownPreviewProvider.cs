using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LfWindows.Models;

namespace LfWindows.Services;

public class MarkdownPreviewProvider : IPreviewProvider
{
    private readonly IConfigService _configService;

    public MarkdownPreviewProvider(IConfigService configService)
    {
        _configService = configService;
    }

    public bool CanPreview(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLower();
        return ext == ".md" || ext == ".markdown";
    }

    public async Task<object> GeneratePreviewAsync(string filePath, System.Threading.CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return "Cancelled";

        string text;
        if (ArchiveFileSystemHelper.IsArchivePath(filePath, out _, out string internalPath) && !string.IsNullOrEmpty(internalPath))
        {
            using var stream = ArchiveFileSystemHelper.OpenStream(filePath);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                text = await reader.ReadToEndAsync();
            }
            else
            {
                text = "Could not read file from archive.";
            }
        }
        else
        {
            text = await File.ReadAllTextAsync(filePath);
        }
        
        return await GenerateSourcePreviewAsync(text);
    }

    private async Task<object> GenerateSourcePreviewAsync(string text)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            AvaloniaEdit.Highlighting.IHighlightingDefinition? definition = null;
            try 
            {
                definition = AvaloniaEdit.Highlighting.HighlightingManager.Instance.GetDefinition("MarkDown");
                
                // Apply theme adaptation
                bool isDark = _configService.Current.Appearance.Theme == "Dark" || 
                            (_configService.Current.Appearance.Theme == "System" && Avalonia.Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark);
                
                var profile = isDark ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;

                if (definition != null)
                {
                    MarkdownThemeHelper.ApplyTheme(definition, profile);
                }
            }
            catch {}

            return new MarkdownPreviewModel(text, null, null, definition);
        });
    }
}



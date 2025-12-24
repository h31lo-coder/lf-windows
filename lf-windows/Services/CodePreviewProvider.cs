using System.IO;
using System.Threading.Tasks;
using AvaloniaEdit.Highlighting;
using LfWindows.Models;

namespace LfWindows.Services;

public class CodePreviewProvider : IPreviewProvider
{
    private readonly IConfigService _configService;

    public CodePreviewProvider(IConfigService configService)
    {
        _configService = configService;
    }

    public bool CanPreview(string filePath)
    {
        // Basic check for text-based files or code extensions
        // This list can be expanded
        string ext = Path.GetExtension(filePath).ToLower();
        return ext == ".cs" || ext == ".xml" || ext == ".json" || 
               ext == ".xaml" || ext == ".axaml" || ext == ".js" || ext == ".ts" || ext == ".tsx" ||
               ext == ".html" || ext == ".css" || ext == ".py" || ext == ".c" || 
               ext == ".cpp" || ext == ".h" || ext == ".java" || ext == ".sh" || 
               ext == ".ps1" || ext == ".bat" || ext == ".ini" || 
               ext == ".yaml" || ext == ".yml" || ext == ".csproj" || ext == ".sln" ||
               ext == ".php" || ext == ".vb" || ext == ".vbs" || ext == ".sql" || ext == ".patch" || ext == ".diff";
    }

    public async Task<object> GeneratePreviewAsync(string filePath, System.Threading.CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return "Cancelled";

        string text;
        
        if (ArchiveFileSystemHelper.IsArchivePath(filePath, out _, out string internalPath) && !string.IsNullOrEmpty(internalPath))
        {
            using var stream = ArchiveFileSystemHelper.OpenStream(filePath);
            if (stream == null) return "Could not read file from archive.";
            
            if (stream.Length > 1024 * 1024 * 5) return "File too large for code preview";
            
            using var reader = new StreamReader(stream);
            text = await reader.ReadToEndAsync();
        }
        else
        {
            if (new FileInfo(filePath).Length > 1024 * 1024 * 2) // Reduced to 2MB limit for code preview
            {
                return "File too large for code preview (Max 2MB)";
            }
            text = await File.ReadAllTextAsync(filePath);
        }
        
        var extension = Path.GetExtension(filePath);
        var highlighting = HighlightingManager.Instance.GetDefinitionByExtension(extension);

        // Fallback mappings for unsupported extensions
        if (highlighting == null)
        {
            if (extension.Equals(".ts", System.StringComparison.OrdinalIgnoreCase) || 
                extension.Equals(".tsx", System.StringComparison.OrdinalIgnoreCase))
            {
                highlighting = HighlightingManager.Instance.GetDefinition("JavaScript");
            }
            else if (extension.Equals(".axaml", System.StringComparison.OrdinalIgnoreCase) || 
                     extension.Equals(".xaml", System.StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".csproj", System.StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".sln", System.StringComparison.OrdinalIgnoreCase))
            {
                highlighting = HighlightingManager.Instance.GetDefinition("XML");
            }
            else if (extension.Equals(".razor", System.StringComparison.OrdinalIgnoreCase))
            {
                highlighting = HighlightingManager.Instance.GetDefinition("HTML");
            }
            else if (extension.Equals(".ini", System.StringComparison.OrdinalIgnoreCase) || 
                     extension.Equals(".conf", System.StringComparison.OrdinalIgnoreCase))
            {
                // INI usually doesn't have a highlighter, but we can try to find one or leave it null
            }
        }
        
        // Apply theme adaptation
        bool isDark = _configService.Current.Appearance.Theme == "Dark" || 
                    (_configService.Current.Appearance.Theme == "System" && Avalonia.Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark);
        
        var profile = isDark ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;

        if (highlighting != null)
        {
            CodeThemeHelper.ApplyTheme(highlighting, profile);
        }

        return new CodePreviewModel(text, highlighting);
    }
}

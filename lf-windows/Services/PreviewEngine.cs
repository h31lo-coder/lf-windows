using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LfWindows.Services;

public class PreviewEngine
{
    private readonly List<IPreviewProvider> _providers = new();

    public PreviewEngine(IIconProvider iconProvider, IConfigService configService)
    {
        var cacheService = new PreviewCacheService();

        _providers.Add(new FolderPreviewProvider(iconProvider));
        _providers.Add(new ArchivePreviewProvider(iconProvider));
        _providers.Add(new ImagePreviewProvider(cacheService));
        _providers.Add(new MarkdownPreviewProvider(configService));
        _providers.Add(new PdfPreviewProvider());
        _providers.Add(new VideoPreviewProvider(cacheService));
        _providers.Add(new OfficePreviewProvider(cacheService));
        _providers.Add(new FontPreviewProvider());
        _providers.Add(new CodePreviewProvider(configService));
        _providers.Add(new TextPreviewProvider());
    }

    public async Task<object> GeneratePreviewAsync(string filePath, PreviewMode mode = PreviewMode.Default, CancellationToken token = default)
    {
        // Resolve shortcut target if it's a .lnk file
        if (filePath.EndsWith(".lnk", System.StringComparison.OrdinalIgnoreCase))
        {
            var target = ShortcutResolver.Resolve(filePath);
            // Only redirect if target exists, otherwise we might want to show "Broken Link" or default preview
            if (!string.IsNullOrEmpty(target) && (File.Exists(target) || Directory.Exists(target)))
            {
                filePath = target;
            }
        }

        var provider = _providers.FirstOrDefault(p => p.CanPreview(filePath));
        if (provider != null)
        {
            return await provider.GeneratePreviewAsync(filePath, mode, token);
        }
        return "No preview available";
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using LfWindows.Models;

namespace LfWindows.Services;

public class TextPreviewProvider : IPreviewProvider
{
    public bool CanPreview(string filePath)
    {
        // Simple check for now, can be improved
        var ext = Path.GetExtension(filePath).ToLower();
        return ext == ".txt" || ext == ".log" || ext == ".conf" || ext == ".config";
    }

    public async Task<object> GeneratePreviewAsync(string filePath, System.Threading.CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return "Cancelled";

        try
        {
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
            
            return new CodePreviewModel(text);
        }
        catch (Exception ex)
        {
            return new CodePreviewModel($"Error reading file: {ex.Message}");
        }
    }
}

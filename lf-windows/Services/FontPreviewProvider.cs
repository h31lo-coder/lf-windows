using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using LfWindows.Models;
using SkiaSharp;

namespace LfWindows.Services;

public class FontPreviewProvider : IPreviewProvider
{
    public bool CanPreview(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLower();
        return ext == ".ttf" || ext == ".otf";
    }

    public Task<object> GeneratePreviewAsync(string filePath, System.Threading.CancellationToken token = default)
    {
        return GeneratePreviewAsync(filePath, PreviewMode.Default, token);
    }

    public async Task<object> GeneratePreviewAsync(string filePath, PreviewMode mode = PreviewMode.Default, System.Threading.CancellationToken token = default)
    {
        return await Task.Run<object>(() =>
        {
            try
            {
                using var typeface = SKTypeface.FromFile(filePath);
                if (typeface == null) return "Invalid font file";
                
                string familyName = typeface.FamilyName;

                // Render preview to bitmap using SkiaSharp
                // This avoids Avalonia font loading issues and crashes
                var bitmap = RenderFontPreview(typeface);
                
                return new FontPreviewModel(familyName, bitmap);
            }
            catch (Exception ex)
            {
                return $"Error loading font: {ex.Message}";
            }
        });
    }

    private Bitmap RenderFontPreview(SKTypeface typeface)
    {
        // Estimate height needed
        int width = 1000;
        int height = 800;
        
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        
        // Use a neutral background color (e.g., slightly off-white)
        canvas.Clear(SKColors.White);
        
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        
        using var font = new SKFont(typeface);

        float x = 20;
        float y = 20;

        string sample = "The quick brown fox jumps over the lazy dog.";
        string numbers = "1234567890";
        string chinese = "天地玄黄 宇宙洪荒 日月盈昃 辰宿列张";

        int[] sizes = { 12, 18, 24, 36, 48, 60 };
        
        foreach (var size in sizes)
        {
            font.Size = size;
            // Move down by line height (approx size * 1.2)
            y += size * 1.2f;
            canvas.DrawText($"{sample}", x, y, font, paint);
        }
        
        y += 40;
        font.Size = 36;
        canvas.DrawText(numbers, x, y + 36, font, paint);
        
        y += 80;
        font.Size = 36;
        canvas.DrawText(chinese, x, y + 36, font, paint);

        // Convert to Avalonia Bitmap
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        
        return new Bitmap(stream);
    }
}

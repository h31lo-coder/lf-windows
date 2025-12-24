using System;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using LfWindows.Models;

namespace LfWindows.Services;

public static class MarkdownThemeHelper
{
    public static void ApplyTheme(IHighlightingDefinition definition, ThemeProfile profile)
    {
        if (definition == null || profile == null) return;

        foreach (var color in definition.NamedHighlightingColors)
        {
            UpdateColor(color, profile);
        }
    }

    private static void UpdateColor(HighlightingColor color, ThemeProfile profile)
    {
        if (color.Name == null) return;

        switch (color.Name)
        {
            case "Heading":
                SetColor(color, profile.MarkdownHeadingColor);
                break;
            case "Emphasis":
                SetColor(color, profile.MarkdownEmphasisColor);
                break;
            case "StrongEmphasis":
                SetColor(color, profile.MarkdownStrongEmphasisColor);
                break;
            case "Code":
                SetColor(color, profile.MarkdownCodeColor); 
                break;
            case "BlockQuote":
                SetColor(color, profile.MarkdownBlockQuoteColor);
                break;
            case "Link":
                SetColor(color, profile.MarkdownLinkColor);
                break;
            case "Image":
                SetColor(color, profile.MarkdownImageColor);
                break;
            case "LineBreak":
                SetColor(color, "#808080");
                break;
            // Fallback for potential body text definitions
            case "Body":
            case "Text":
                SetColor(color, profile.RightColumnFileTextColor);
                break;
        }
    }

    private static void SetColor(HighlightingColor color, string hexColor)
    {
        if (Color.TryParse(hexColor, out var c))
        {
            color.Foreground = new SimpleHighlightingBrush(c);
        }
    }
}

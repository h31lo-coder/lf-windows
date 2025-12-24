using System;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using LfWindows.Models;

namespace LfWindows.Services;

public static class CodeThemeHelper
{
    public static void ApplyTheme(IHighlightingDefinition definition, ThemeProfile profile)
    {
        if (definition == null || profile == null) return;

        foreach (var color in definition.NamedHighlightingColors)
        {
            UpdateColor(color, profile);
        }
    }

    public static void ApplyThemeToAll(ThemeProfile profile)
    {
        if (profile == null) return;
        
        foreach (var definition in HighlightingManager.Instance.HighlightingDefinitions)
        {
            try
            {
                ApplyTheme(definition, profile);
            }
            catch (Exception)
            {
                // Ignore definitions that fail to load (e.g. missing main RuleSet)
                // This prevents the app from crashing on startup due to a single bad definition
            }
        }
    }

    private static void UpdateColor(HighlightingColor color, ThemeProfile profile)
    {
        if (color.Name == null) return;

        // Map standard AvaloniaEdit highlighting names to our theme profile
        // Note: Different languages use different names, so we try to cover common ones.
        
        string name = color.Name;

        bool Contains(string text) => name.Contains(text, StringComparison.OrdinalIgnoreCase);

        if (Contains("Comment") || Contains("DocComment"))
        {
            SetColor(color, profile.CodeCommentColor);
        }
        else if (Contains("String") || Contains("Char") || Contains("Literal"))
        {
            SetColor(color, profile.CodeStringColor);
        }
        else if (Contains("Keyword") || Contains("Type") || Contains("ControlFlow") || 
                 Contains("Modifier") || Contains("Visibility") || Contains("Namespace") || 
                 Contains("Package") || Contains("Import") || Contains("Reserved") || 
                 Contains("Preprocessor") || Contains("Directive") || Contains("Boolean") || 
                 Contains("Null") || Contains("Void") || Contains("Return") || Contains("Exception"))
        {
            SetColor(color, profile.CodeKeywordColor);
        }
        else if (Contains("Number") || Contains("Digit"))
        {
            SetColor(color, profile.CodeNumberColor);
        }
        else if (Contains("Method") || Contains("Function"))
        {
            SetColor(color, profile.CodeMethodColor);
        }
        else if (Contains("Class") || Contains("Struct") || Contains("Interface") || Contains("Enum") || Contains("Delegate"))
        {
            SetColor(color, profile.CodeClassColor);
        }
        else if (Contains("Variable") || Contains("Parameter") || Contains("Field") || Contains("Identifier") || Contains("Local"))
        {
            SetColor(color, profile.CodeVariableColor);
        }
        else if (Contains("Operator") || Contains("Punctuation"))
        {
            SetColor(color, profile.CodeOperatorColor);
        }
        else if (Contains("HtmlTag") || Contains("Tag"))
        {
            SetColor(color, profile.CodeHtmlTagColor);
        }
        else if (Contains("CssProperty") || Contains("Property"))
        {
            SetColor(color, profile.CodeCssPropertyColor);
        }
        else if (Contains("CssValue") || Contains("Value"))
        {
            SetColor(color, profile.CodeCssValueColor);
        }
        // Default text fallback? Usually handled by editor foreground, but some definitions might have "Text" or "Body"
        else if (name.Equals("Text", StringComparison.OrdinalIgnoreCase) || name.Equals("Body", StringComparison.OrdinalIgnoreCase))
        {
            SetColor(color, profile.CodeTextColor);
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

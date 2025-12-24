using Avalonia.Controls;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;

namespace LfWindows.Models;

public class MarkdownPreviewModel
{
    public TextDocument SourceDocument { get; }
    public Control? RenderedControl { get; }
    public string? ErrorMessage { get; }
    public IHighlightingDefinition? HighlightingDefinition { get; }

    public MarkdownPreviewModel(string sourceText, Control? renderedControl, string? errorMessage = null, IHighlightingDefinition? highlightingDefinition = null)
    {
        SourceDocument = new TextDocument(sourceText);
        RenderedControl = renderedControl;
        ErrorMessage = errorMessage;
        HighlightingDefinition = highlightingDefinition;
    }
}

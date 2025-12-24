using CommunityToolkit.Mvvm.ComponentModel;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;

namespace LfWindows.Models;

public partial class CodePreviewModel : ObservableObject
{
    public TextDocument Document { get; set; }
    
    [ObservableProperty]
    private IHighlightingDefinition? _syntaxHighlighting;

    public CodePreviewModel(string text, IHighlightingDefinition? highlighting = null)
    {
        Document = new TextDocument(text);
        SyntaxHighlighting = highlighting;
    }
}

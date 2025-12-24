using System;
using System.Linq;
using AvaloniaEdit.Highlighting;

public class ColorInspector
{
    public static void Inspect()
    {
        var def = HighlightingManager.Instance.GetDefinition("MarkDown");
        if (def == null) 
        {
            return;
        }

        foreach (var color in def.NamedHighlightingColors)
        {
            // Console.WriteLine($"Color: {color.Name}, Foreground: {color.Foreground}, Background: {color.Background}");
        }
    }
}

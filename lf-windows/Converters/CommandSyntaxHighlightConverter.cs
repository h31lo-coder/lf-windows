using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Controls.Documents;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LfWindows.Converters
{
    public class CommandSyntaxHighlightConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 2 || values[0] is not string text || values[1] is not IEnumerable<string> commands)
            {
                // Return simple text if binding fails
                var simpleCollection = new InlineCollection();
                if (values.Count > 0 && values[0] is string t)
                {
                    simpleCollection.Add(new Run { Text = t });
                }
                return simpleCollection;
            }

            IEnumerable<string>? noArgCommands = null;
            if (values.Count >= 3 && values[2] is IEnumerable<string> noArgs)
            {
                noArgCommands = noArgs;
            }

            var collection = new InlineCollection();
            if (string.IsNullOrEmpty(text)) return collection;

            var parts = text.Split(' ', 2);
            var command = parts[0];
            var args = parts.Length > 1 ? parts[1] : "";

            // Check if command is in the list (case insensitive)
            bool isCommand = commands.Any(c => c.Equals(command, StringComparison.OrdinalIgnoreCase));
            bool isNoArg = noArgCommands != null && noArgCommands.Any(c => c.Equals(command, StringComparison.OrdinalIgnoreCase));
            bool hasArgs = !string.IsNullOrWhiteSpace(args);

            // Special handling for help command
            if (command.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                string trimmedArgs = args.Trim();
                if (trimmedArgs.Equals("key", StringComparison.OrdinalIgnoreCase) || 
                    trimmedArgs.Equals("command", StringComparison.OrdinalIgnoreCase))
                {
                    collection.Add(new Run { Text = command, Foreground = Brush.Parse("#dd5000"), FontWeight = FontWeight.Bold });
                    collection.Add(new Run { Text = " " + args, Foreground = Brush.Parse("#dd5000"), FontWeight = FontWeight.Bold });
                    return collection;
                }
            }

            // Special handling for filter command
            if (command.Equals(":filter", StringComparison.OrdinalIgnoreCase) || command.Equals("filter", StringComparison.OrdinalIgnoreCase))
            {
                // Check first argument
                var argParts = args.Split(' ', 2);
                var mode = argParts[0];
                var rest = argParts.Length > 1 ? argParts[1] : "";

                if (new[] { "fuzzy", "text", "glob", "regex" }.Contains(mode.ToLower()))
                {
                    // Color command + mode
                    collection.Add(new Run { Text = command, Foreground = Brush.Parse("#dd5000"), FontWeight = FontWeight.Bold });
                    collection.Add(new Run { Text = " " + mode, Foreground = Brush.Parse("#dd5000"), FontWeight = FontWeight.Bold });
                    
                    if (argParts.Length > 1)
                    {
                        collection.Add(new Run { Text = " " + rest });
                    }
                    return collection;
                }
            }

            // Special handling for search modes (fuzzy, text, glob, regex)
            // We assume these are used in search context if they appear as the first word
            if (new[] { "fuzzy", "text", "glob", "regex" }.Contains(command.ToLower()))
            {
                collection.Add(new Run { Text = command, Foreground = Brush.Parse("#dd5000"), FontWeight = FontWeight.Bold });
            }
            else if (isCommand)
            {
                // If it's a no-arg command but has arguments, treat as invalid (normal color)
                if (isNoArg && hasArgs)
                {
                    collection.Add(new Run { Text = command });
                }
                else
                {
                    collection.Add(new Run { Text = command, Foreground = Brush.Parse("#dd5000"), FontWeight = FontWeight.Bold });
                }
            }
            else
            {
                collection.Add(new Run { Text = command });
            }

            if (parts.Length > 1) // If there was a space
            {
                collection.Add(new Run { Text = " " + args });
            }

            return collection;
        }
    }
}

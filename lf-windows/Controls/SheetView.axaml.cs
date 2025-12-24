using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using Avalonia.Data;
using Avalonia.Media;
using System.Data;
using System.Collections.Generic;

namespace LfWindows.Controls;

public partial class SheetView : UserControl
{
    private DataGrid? _dataGrid;

    public SheetView()
    {
        InitializeComponent();
        _dataGrid = this.FindControl<DataGrid>("GridData");
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is DataTable table && _dataGrid != null)
        {
            LoadData(table);
        }
    }

    private void LoadData(DataTable table)
    {
        if (_dataGrid == null) return;

        _dataGrid.Columns.Clear();
        
        string lastHeader = "";

        // Create columns
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            int columnIndex = i; // Capture the loop variable for the closure
            
            string headerText = col.ColumnName;
            
            // Heuristic: If header is "ColumnX" (auto-generated), use previous header to simulate merged cell header
            if (headerText.StartsWith("Column") && int.TryParse(headerText.Substring(6), out _) && !string.IsNullOrEmpty(lastHeader))
            {
                headerText = lastHeader;
            }
            else
            {
                lastHeader = headerText;
            }

            // Use TemplateColumn to support text wrapping and auto-sizing
            var templateColumn = new DataGridTemplateColumn
            {
                Header = headerText,
                IsReadOnly = true,
                Width = DataGridLength.Auto
            };

            // Create the cell template programmatically
            // We use string[] because the ItemsSource is List<string[]>
            templateColumn.CellTemplate = new FuncDataTemplate<string[]>((row, namescope) =>
            {
                var textBlock = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 300, // Approx 20 chars * 12px + buffer
                    Margin = new Thickness(5, 2),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    FontSize = 12,
                    FontWeight = Avalonia.Media.FontWeight.Normal
                };
                
                // Bind to the array index
                textBlock.Bind(TextBlock.TextProperty, new Binding($"[{columnIndex}]"));
                
                return textBlock;
            });

            _dataGrid.Columns.Add(templateColumn);
        }

        // Convert DataTable to List<string[]> to ensure reliable binding
        var rows = new List<string[]>();
        foreach (DataRow row in table.Rows)
        {
            var item = new string[table.Columns.Count];
            for (int i = 0; i < table.Columns.Count; i++)
            {
                item[i] = row[i]?.ToString() ?? "";
            }
            rows.Add(item);
        }
        
        _dataGrid.ItemsSource = rows;
    }
}

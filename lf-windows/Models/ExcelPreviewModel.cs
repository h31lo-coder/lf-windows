using System.Data;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LfWindows.Models;

public partial class ExcelPreviewModel : ObservableObject
{
    public List<DataTable> Sheets { get; }

    [ObservableProperty]
    private DataTable? _selectedSheet;

    public ExcelPreviewModel(List<DataTable> sheets)
    {
        Sheets = sheets;
        if (Sheets.Count > 0)
        {
            SelectedSheet = Sheets[0];
        }
    }
}

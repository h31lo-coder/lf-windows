using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LfWindows.Models;

public class AppConfig
{
    public AppearanceConfig Appearance { get; set; } = new();
    public PreviewConfig Preview { get; set; } = new();
    public PerformanceConfig Performance { get; set; } = new();
    public string HomeDirectory { get; set; } = string.Empty;
    public string WorkspaceDirectoryName { get; set; } = "Workspace";
    public Dictionary<string, string> Bookmarks { get; set; } = new();
    public Dictionary<string, string> KeyBindings { get; set; } = new();
    public List<List<string>> YankHistory { get; set; } = new();

    public SortType SortBy { get; set; } = SortType.Ctime;
    public bool SortReverse { get; set; } = false;
    public bool DirFirst { get; set; } = true;
    public bool ShowHidden { get; set; } = false;
    public bool ShowSystemHidden { get; set; } = false;
    public ObservableCollection<InfoType> Info { get; set; } = new();

    public string InfoTimeFormatNew { get; set; } = "MMM dd HH:mm";
    public string InfoTimeFormatOld { get; set; } = "MMM dd  yyyy";

    public FilterMethod FilterMethod { get; set; } = FilterMethod.Fuzzy;
    public FilterMethod SearchMethod { get; set; } = FilterMethod.Fuzzy;
    public bool AnchorFind { get; set; } = true;

    public List<string> CommandPanelActions { get; set; } = new();
}

public enum FilterMethod
{
    Fuzzy,
    Text,
    Glob,
    Regex
}

public enum SortType
{
    Natural,
    Name,
    Size,
    Time,
    Atime,
    Btime,
    Ctime,
    Ext
}

public enum InfoType
{
    Size,
    Time,
    Atime,
    Btime,
    Ctime,
    Perm,
    User,
    Group
}

public class AppearanceConfig
{
    public string Theme { get; set; } = "System"; // System, Light, Dark
    public double FontSize { get; set; } = 14.0;
    public double MainFontSize { get; set; } = 14.0;
    public double InfoFontSize { get; set; } = 12.0;
    public string FontFamily { get; set; } = "Segoe UI";
    public string PreviewTextFontFamily { get; set; } = "Cascadia Code, Consolas, Monospace";
    
    public double TopBarFontSize { get; set; } = 14.0;
    public string TopBarFontWeight { get; set; } = "Bold"; // Normal, Bold
    public string TopBarFontFamily { get; set; } = "Segoe UI";
    
    public double StatusBarFontSize { get; set; } = 14.0;
    public string StatusBarFontWeight { get; set; } = "Normal";
    public string StatusBarFontFamily { get; set; } = "Segoe UI";

    public double SeparatorWidth { get; set; } = 2.0;
    public double CommandLineFontSize { get; set; } = 14.0;
    public string CommandLineFontFamily { get; set; } = "Consolas";
    public string CommandLineFontWeight { get; set; } = "Normal";

    public string GlobalShowHotkey { get; set; } = "Ctrl+F12";

    // Layout
    public string ColumnRatio { get; set; } = "1,2,3";
    public bool ShowIcons { get; set; } = true;
    public bool IsCompactMode { get; set; } = false;
    public bool IsFloatMode { get; set; } = false;
    public bool ShowLineNumbers { get; set; } = true;

    // Window Size Presets
    public double SmallWindowWidth { get; set; } = 800;
    public double SmallWindowHeight { get; set; } = 600;
    public double LargeWindowWidth { get; set; } = 1200;
    public double LargeWindowHeight { get; set; } = 800;

    public ThemeProfile DarkTheme { get; set; } = new ThemeProfile
    {
        LeftColumnColor = "#222222",
        MiddleColumnColor = "#333333",
        RightColumnColor = "#111111",
        TopBarBackgroundColor = "#222222",
        TopBarTextColor = "#AAAAAA",
        StatusBarBackgroundColor = "#444444",
        StatusBarTextColor = "#FFFFFF",
        SeparatorColor = "#555555",
        CommandLineBackgroundColor = "#333333",
        CommandLineTextColor = "#FFFFFF",
        FloatingShadowColor = "#99000000",
        
        LeftColumnFileTextColor = "#CCCCCC",
        LeftColumnDirectoryTextColor = "#EEEEEE",
        MiddleColumnFileTextColor = "#FFFFFF",
        MiddleColumnDirectoryTextColor = "#87CEFA",
        MiddleColumnSelectedTextColor = "#FFFF00",
        MiddleColumnSelectedFontWeight = "Bold",
        SelectedBackgroundColor = "#555555",
        RightColumnFileTextColor = "#CCCCCC",
        RightColumnDirectoryTextColor = "#EEEEEE",

        // Markdown Colors (Soft/Dark Mode)
        MarkdownHeadingColor = "#61AFEF",
        MarkdownCodeColor = "#D4D4D4",
        MarkdownBlockQuoteColor = "#6A9955",
        MarkdownLinkColor = "#4EC9B0",

        // Code Preview Colors (VS Code Dark+ inspired)
        CodeBackgroundColor = "#1e1e1e",
        CodeTextColor = "#d4d4d4",
        CodeSelectionColor = "#264f78",
        CodeLineNumberColor = "#858585",
        
        CodeCommentColor = "#6a9955",
        CodeStringColor = "#ce9178",
        CodeKeywordColor = "#569cd6",
        CodeNumberColor = "#b5cea8",
        CodeMethodColor = "#dcdcaa",
        CodeClassColor = "#4ec9b0",
        CodeVariableColor = "#9cdcfe",
        CodeOperatorColor = "#d4d4d4",
        CodeHtmlTagColor = "#569cd6",
        CodeCssPropertyColor = "#9cdcfe",
        CodeCssValueColor = "#ce9178",

        // Dialog/Popup Colors
        DialogBackgroundColor = "#333333",
        DialogTextColor = "#FFFFFF",
        DialogBorderColor = "#555555",
        DialogButtonBackgroundColor = "#444444",
        DialogButtonTextColor = "#FFFFFF",
        DialogIndexColor = "#FFFF00"
    };

    public ThemeProfile LightTheme { get; set; } = new ThemeProfile
    {
        LeftColumnColor = "#F0F0F0",
        MiddleColumnColor = "#FFFFFF",
        RightColumnColor = "#FAFAFA",
        TopBarBackgroundColor = "#F0F0F0",
        TopBarTextColor = "#333333",
        StatusBarBackgroundColor = "#E0E0E0",
        StatusBarTextColor = "#000000",
        SeparatorColor = "#CCCCCC",
        CommandLineBackgroundColor = "#FFFFFF",
        CommandLineTextColor = "#000000",
        FloatingShadowColor = "#20000000",

        LeftColumnFileTextColor = "#666666",
        LeftColumnDirectoryTextColor = "#333333",
        MiddleColumnFileTextColor = "#000000",
        MiddleColumnDirectoryTextColor = "#00008B", // DarkBlue
        MiddleColumnSelectedTextColor = "#000000",
        MiddleColumnSelectedFontWeight = "Bold",
        SelectedBackgroundColor = "#ADD8E6", // LightBlue
        RightColumnFileTextColor = "#666666",
        RightColumnDirectoryTextColor = "#333333",

        // Markdown Colors (Strong/Light Mode)
        MarkdownHeadingColor = "#005CC5",
        MarkdownCodeColor = "#24292E",
        MarkdownBlockQuoteColor = "#22863A",
        MarkdownLinkColor = "#0366D6",

        // Code Preview Colors (VS Code Light inspired)
        CodeBackgroundColor = "#ffffff",
        CodeTextColor = "#000000",
        CodeSelectionColor = "#add6ff",
        CodeLineNumberColor = "#237893",

        CodeCommentColor = "#008000",
        CodeStringColor = "#a31515",
        CodeKeywordColor = "#0000ff",
        CodeNumberColor = "#098658",
        CodeMethodColor = "#795e26",
        CodeClassColor = "#267f99",
        CodeVariableColor = "#001080",
        CodeOperatorColor = "#000000",
        CodeHtmlTagColor = "#800000",
        CodeCssPropertyColor = "#ff0000",
        CodeCssValueColor = "#0451a5",

        // Dialog/Popup Colors
        DialogBackgroundColor = "#FFFFFF",
        DialogTextColor = "#000000",
        DialogBorderColor = "#AFAEAE",
        DialogButtonBackgroundColor = "#E0E0E0",
        DialogButtonTextColor = "#000000",
        DialogIndexColor = "#005CC5"
    };
}

public class ThemeProfile
{
    public string LeftColumnColor { get; set; } = "#222222";
    public string MiddleColumnColor { get; set; } = "#333333";
    public string RightColumnColor { get; set; } = "#111111";
    
    public string TopBarBackgroundColor { get; set; } = "#222222";
    public string TopBarTextColor { get; set; } = "#AAAAAA";
    public string StatusBarBackgroundColor { get; set; } = "#444444";
    public string StatusBarTextColor { get; set; } = "#FFFFFF";

    public string SeparatorColor { get; set; } = "#555555";

    public string CommandLineBackgroundColor { get; set; } = "#333333";
    public string CommandLineTextColor { get; set; } = "#FFFFFF";
    public string CommandLineSelectionBackgroundColor { get; set; } = "#0078D7";
    public string FloatingShadowColor { get; set; } = "#99000000";

    // New Properties
    public string LeftColumnFileTextColor { get; set; } = "#888888";
    public string LeftColumnDirectoryTextColor { get; set; } = "#AAAAAA";
    
    public string MiddleColumnFileTextColor { get; set; } = "#FFFFFF";
    public string MiddleColumnDirectoryTextColor { get; set; } = "#87CEFA"; // LightSkyBlue
    public string MiddleColumnSelectedTextColor { get; set; } = "#FFFF00"; // Yellow
    public string MiddleColumnSelectedFontWeight { get; set; } = "Bold";
    public string SelectedBackgroundColor { get; set; } = "#444444";

    public string RightColumnFileTextColor { get; set; } = "#888888";
    public string RightColumnDirectoryTextColor { get; set; } = "#AAAAAA";

    // Markdown Colors
    public string MarkdownHeadingColor { get; set; } = "#61AFEF";
    public string MarkdownCodeColor { get; set; } = "#D4D4D4";
    public string MarkdownBlockQuoteColor { get; set; } = "#6A9955";
    public string MarkdownLinkColor { get; set; } = "#4EC9B0";
    public string MarkdownEmphasisColor { get; set; } = "#C678DD"; // Purple
    public string MarkdownStrongEmphasisColor { get; set; } = "#E06C75"; // Red
    public string MarkdownImageColor { get; set; } = "#98C379"; // Green

    // Code Preview Editor Colors
    public string CodeBackgroundColor { get; set; } = "#1e1e1e";
    public string CodeTextColor { get; set; } = "#d4d4d4";
    public string CodeSelectionColor { get; set; } = "#264f78";
    public string CodeLineNumberColor { get; set; } = "#858585";

    // Code Preview Syntax Colors
    public string CodeCommentColor { get; set; } = "#6A9955";
    public string CodeStringColor { get; set; } = "#CE9178";
    public string CodeKeywordColor { get; set; } = "#569CD6";
    public string CodeNumberColor { get; set; } = "#B5CEA8";
    public string CodeMethodColor { get; set; } = "#DCDCAA";
    public string CodeClassColor { get; set; } = "#4EC9B0";
    public string CodeVariableColor { get; set; } = "#9CDCFE";
    public string CodeOperatorColor { get; set; } = "#D4D4D4";
    public string CodeHtmlTagColor { get; set; } = "#569CD6";
    public string CodeCssPropertyColor { get; set; } = "#9CDCFE";
    public string CodeCssValueColor { get; set; } = "#CE9178";

    // Dialog/Popup Colors
    public string DialogBackgroundColor { get; set; } = string.Empty;
    public string DialogTextColor { get; set; } = string.Empty;
    public string DialogBorderColor { get; set; } = string.Empty;
    public string DialogButtonBackgroundColor { get; set; } = string.Empty;
    public string DialogButtonTextColor { get; set; } = string.Empty;
    public string DialogIndexColor { get; set; } = string.Empty;
}

public class PreviewConfig
{
    public bool Enabled { get; set; } = true;
    public bool ShowHidden { get; set; } = false;
    public bool WordWrap { get; set; } = false;
}

public class PerformanceConfig
{
    public int MaxCacheSizeMb { get; set; } = 200;
    public int MaxYankHistory { get; set; } = 10;
    public bool EnableOfficePreload { get; set; } = true;
}

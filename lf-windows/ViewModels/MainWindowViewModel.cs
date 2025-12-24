using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LfWindows.Services;
using LfWindows.Messages;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LfWindows.Models;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Media;
using System.Diagnostics;

using LfWindows.Views;
using Avalonia.Controls.ApplicationLifetimes;
using System.Windows.Input;

namespace LfWindows.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly KeyBindingService _keyBindingService;
    private readonly PreviewEngine _previewEngine;
    private readonly IConfigService _configService;
    public AppConfig Config => _configService.Current;
    private readonly GlobalHotkeyService _globalHotkeyService;
    private readonly DirectorySizeCacheService _directorySizeCacheService;
    private readonly StartupService _startupService;
    private readonly Dictionary<string, string> _directorySelectionHistory = new();

    // JumpList fields
    private readonly List<JumpListEntry> _jumpList = new();
    private int _jumpListIndex = -1;
    private bool _isJumping = false;

    [ObservableProperty]
    private FileListViewModel _parentList;

    [ObservableProperty]
    private FileListViewModel _currentList;

    [ObservableProperty]
    private FileListViewModel _childList;

    private static readonly bool DebugPreviewLog = true;

    private void LogPreview(string message)
    {
        if (!DebugPreviewLog) return;
        DebugLogger.Log(message);
    }

    private static string Describe(object? obj)
    {
        if (obj == null) return "null";
        var type = obj.GetType().Name;
        if (obj is Models.PdfPreviewModel pdf) return $"PdfPreviewModel(pages={pdf.PageCount})";
        if (obj is Models.OfficePreviewModel off) return $"OfficePreviewModel(ext={off.Extension})";
        if (obj is string s) return $"string:{s}";
        return type;
    }

    private object _previewContent = "Preview";

    public object PreviewContent
    {
        get => _previewContent;
        set
        {
            if (_previewContent != value)
            {
                // Dispose old content if it implements IDisposable
                if (_previewContent is IDisposable disposable)
                {
                    // Offload disposal to background thread to prevent UI blocking
                    // especially for heavy resources like Video/VLC or Office Interop
                    Task.Run(() => 
                    {
                        try
                        {
                            disposable.Dispose();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error disposing preview content: {ex.Message}");
                        }
                    });
                }
                LogPreview($"[Preview] Switch {Describe(_previewContent)} -> {Describe(value)}");
                SetProperty(ref _previewContent, value);
            }
        }
    }

    [ObservableProperty]
    private CommandLineViewModel _commandLine;

    [ObservableProperty]
    private string _statusText = "Normal";

    [ObservableProperty]
    private bool _isFloatEnabled = false;

    partial void OnIsFloatEnabledChanged(bool value)
    {
        _configService.Current.Appearance.IsFloatMode = value;
        _configService.Save();
        OnPropertyChanged(nameof(MiddleColumnBoxShadow));
    }

    [ObservableProperty]
    private string _userNamePath = string.Empty;

    [ObservableProperty]
    private bool _isRootDirectory;

    [ObservableProperty]
    private string _userDisplayName = string.Empty;

    [ObservableProperty]
    private string _userAccountName = string.Empty;

    [ObservableProperty]
    private string _userInitials = string.Empty;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _userAvatar;

    [ObservableProperty]
    private bool _isIconsVisible = true;

    partial void OnIsIconsVisibleChanged(bool value)
    {
        _configService.Current.Appearance.ShowIcons = value;
        _configService.Save();
    }

    [ObservableProperty]
    private bool _isBookmarkListVisible = false;

    [ObservableProperty]
    private bool _showLineNumbers = true;

    partial void OnShowLineNumbersChanged(bool value)
    {
        _configService.Current.Appearance.ShowLineNumbers = value;
        _configService.Save();

        // Margin is always 15,0,10,0 for the panel
        PreviewEditorMargin = new Avalonia.Thickness(15, 0, 10, 0);
        // Padding is 3px left when line numbers are hidden
        PreviewEditorPadding = value ? new Avalonia.Thickness(0) : new Avalonia.Thickness(3, 0, 0, 0);
    }

    [ObservableProperty]
    private Avalonia.Thickness _previewEditorMargin = new(15, 0, 10, 0);

    [ObservableProperty]
    private Avalonia.Thickness _previewEditorPadding = new(0);

    [ObservableProperty]
    private bool _isPreviewVisible = true;

    [ObservableProperty]
    private bool _isFilePreviewEnabled = true;

    partial void OnIsFilePreviewEnabledChanged(bool value)
    {
        _configService.Current.Preview.Enabled = value;
        _configService.Save();
    }

    partial void OnIsPreviewVisibleChanged(bool value)
    {
        if (value)
        {
            // Restore default ratio (1:2:3) or use configured ratio if available
            // For now, hardcoding 3* as per default initialization
            RightColumnWidth = new Avalonia.Controls.GridLength(3, Avalonia.Controls.GridUnitType.Star);
        }
        else
        {
            RightColumnWidth = new Avalonia.Controls.GridLength(0);
        }
    }

    [ObservableProperty]
    private CommandPanelViewModel _commandPanel = new();

    [ObservableProperty]
    private bool _isCommandPanelVisible = false;

    [ObservableProperty]
    private bool _isDialogVisible = false;

    [ObservableProperty]
    private ConfirmationDialogViewModel? _activeDialog;

    [ObservableProperty]
    private double _previewFontSize = 14.0;

    [ObservableProperty]
    private Avalonia.Media.FontFamily _previewTextFontFamily = new("Cascadia Code, Consolas, Monospace");

    [ObservableProperty]
    private string _selectedTheme = "System";

    [ObservableProperty]
    private bool _enableOfficePreload = true;

    partial void OnEnableOfficePreloadChanged(bool value)
    {
        _configService.Current.Performance.EnableOfficePreload = value;
        _configService.Save();
    }

    [ObservableProperty]
    private string _leftColumnColor = "#222222";

    [ObservableProperty]
    private string _middleColumnColor = "#333333";

    [ObservableProperty]
    private string _rightColumnColor = "#111111";

    [ObservableProperty]
    private string _topBarBackgroundColor = "#222222";

    [ObservableProperty]
    private string _topBarTextColor = "#AAAAAA";

    [ObservableProperty]
    private string _statusBarBackgroundColor = "#444444";

    [ObservableProperty]
    private string _statusBarTextColor = "#FFFFFF";

    [ObservableProperty]
    private Avalonia.Media.FontWeight _topBarFontWeight = Avalonia.Media.FontWeight.Bold;

    [ObservableProperty]
    private Avalonia.Media.FontWeight _statusBarFontWeight = Avalonia.Media.FontWeight.Normal;

    [ObservableProperty]
    private string _separatorColor = "#555555";

    [ObservableProperty]
    private double _separatorWidth = 2.0;

    [ObservableProperty]
    private string _commandLineBackgroundColor = "#333333";

    [ObservableProperty]
    private string _commandLineTextColor = "#FFFFFF";

    [ObservableProperty]
    private string _commandLineSelectionBackgroundColor = "#0078D7";

    [ObservableProperty]
    private string _dialogBackgroundColor = "#333333";

    [ObservableProperty]
    private string _dialogTextColor = "#FFFFFF";

    [ObservableProperty]
    private string _dialogBorderColor = "#000000";

    [ObservableProperty]
    private string _dialogButtonBackgroundColor = "#444444";

    [ObservableProperty]
    private string _dialogButtonTextColor = "#FFFFFF";

    [ObservableProperty]
    private string _dialogIndexColor = "#FFFF00";

    [ObservableProperty]
    private string _inputBackgroundColor = "#333333";

    [ObservableProperty]
    private string _inputTextColor = "#FFFFFF";

    [ObservableProperty]
    private string _inputCaretColor = "#FFFFFF";

    [ObservableProperty]
    private string _inputBorderColor = "#555555";

    [ObservableProperty]
    private double _commandLineFontSize = 14.0;

    [ObservableProperty]
    private Avalonia.Media.FontFamily _commandLineFontFamily = new("Consolas");

    [ObservableProperty]
    private string _commandLineFontWeight = "Normal";

    partial void OnCommandLineFontWeightChanged(string value)
    {
        _configService.Current.Appearance.CommandLineFontWeight = value;
        _configService.Save();
    }

    [ObservableProperty]
    private Avalonia.Media.FontFamily _topBarFontFamily = new("Segoe UI");

    partial void OnTopBarFontFamilyChanged(Avalonia.Media.FontFamily value)
    {
        _configService.Current.Appearance.TopBarFontFamily = value.Name;
        _configService.Save();
    }

    [ObservableProperty]
    private Avalonia.Media.FontFamily _statusBarFontFamily = new("Segoe UI");

    partial void OnStatusBarFontFamilyChanged(Avalonia.Media.FontFamily value)
    {
        _configService.Current.Appearance.StatusBarFontFamily = value.Name;
        _configService.Save();
    }

    [ObservableProperty]
    private Avalonia.Media.FontFamily _mainFontFamily = new("Segoe UI");

    [ObservableProperty]
    private double _mainFontSize = 14.0;

    [ObservableProperty]
    private double _infoFontSize = 12.0;

    partial void OnInfoFontSizeChanged(double value)
    {
        _configService.Current.Appearance.InfoFontSize = value;
        _configService.Save();
    }

    [ObservableProperty]
    private double _topBarFontSize = 14.0;

    partial void OnTopBarFontSizeChanged(double value)
    {
        _configService.Current.Appearance.TopBarFontSize = value;
        _configService.Save();
    }

    [ObservableProperty]
    private double _statusBarFontSize = 14.0;

    partial void OnStatusBarFontSizeChanged(double value)
    {
        _configService.Current.Appearance.StatusBarFontSize = value;
        _configService.Save();
    }

    [ObservableProperty]
    private string _globalShowHotkey = "F12";

    [ObservableProperty]
    private bool _isCompactMode = false;

    partial void OnIsCompactModeChanged(bool value)
    {
        _configService.Current.Appearance.IsCompactMode = value;
        _configService.Save();
        ListItemPadding = value ? new Avalonia.Thickness(10, 2) : new Avalonia.Thickness(10, 6);
    }

    [ObservableProperty]
    private Avalonia.Thickness _listItemPadding = new(10, 6);

    [ObservableProperty]
    private double _windowWidth = 1000;

    [ObservableProperty]
    private double _windowHeight = 700;

    [ObservableProperty]
    private PixelPoint _windowPosition = new(10, 10);

    [ObservableProperty]
    private double _smallWindowWidth = 800;

    [ObservableProperty]
    private double _smallWindowHeight = 600;

    [ObservableProperty]
    private double _largeWindowWidth = 1200;

    [ObservableProperty]
    private double _largeWindowHeight = 800;

    // New Observable Properties for Colors
    [ObservableProperty]
    private string _leftColumnFileTextColor = "#888888";
    [ObservableProperty]
    private string _leftColumnDirectoryTextColor = "#AAAAAA";

    [ObservableProperty]
    private string _middleColumnFileTextColor = "#FFFFFF";
    [ObservableProperty]
    private string _middleColumnDirectoryTextColor = "#87CEFA";
    [ObservableProperty]
    private string _middleColumnSelectedTextColor = "#FFFF00";
    [ObservableProperty]
    private Avalonia.Media.FontWeight _middleColumnSelectedFontWeight = Avalonia.Media.FontWeight.Bold;
    [ObservableProperty]
    private string _selectedBackgroundColor = "#555555";

    [ObservableProperty]
    private string _rightColumnFileTextColor = "#888888";
    [ObservableProperty]
    private string _rightColumnDirectoryTextColor = "#AAAAAA";

    // Code Preview Editor Colors
    [ObservableProperty]
    private string _codeBackgroundColor = "#1e1e1e";
    [ObservableProperty]
    private string _codeTextColor = "#d4d4d4";
    [ObservableProperty]
    private string _codeSelectionColor = "#264f78";
    [ObservableProperty]
    private string _codeLineNumberColor = "#858585";

    [ObservableProperty]
    private string _floatingShadowColor = "#99000000";

    [ObservableProperty]
    private bool _isPreviewWordWrapEnabled;

    partial void OnIsPreviewWordWrapEnabledChanged(bool value)
    {
        _configService.Current.Preview.WordWrap = value;
        _configService.Save();
    }

    public Avalonia.Media.BoxShadows MiddleColumnBoxShadow
    {
        get
        {
            if (IsFloatEnabled)
            {
                try
                {
                    return Avalonia.Media.BoxShadows.Parse($"0 0 25 5 {FloatingShadowColor}");
                }
                catch
                {
                    return Avalonia.Media.BoxShadows.Parse("0 0 25 5 #99000000");
                }
            }
            return new Avalonia.Media.BoxShadows();
        }
    }

    public List<Avalonia.Media.FontFamily> AvailableFonts { get; } = Avalonia.Media.FontManager.Current.SystemFonts.ToList();

    [ObservableProperty]
    private string _editingTheme = "Dark";

    // Edit Properties for Settings UI
    public string EditLeftColumnColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.LeftColumnColor : _configService.Current.Appearance.LightTheme.LeftColumnColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.LeftColumnColor != value)
            {
                profile.LeftColumnColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditMiddleColumnColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.MiddleColumnColor : _configService.Current.Appearance.LightTheme.MiddleColumnColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.MiddleColumnColor != value)
            {
                profile.MiddleColumnColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditRightColumnColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.RightColumnColor : _configService.Current.Appearance.LightTheme.RightColumnColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.RightColumnColor != value)
            {
                profile.RightColumnColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditTopBarBackgroundColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.TopBarBackgroundColor : _configService.Current.Appearance.LightTheme.TopBarBackgroundColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.TopBarBackgroundColor != value)
            {
                profile.TopBarBackgroundColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditTopBarTextColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.TopBarTextColor : _configService.Current.Appearance.LightTheme.TopBarTextColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.TopBarTextColor != value)
            {
                profile.TopBarTextColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditStatusBarBackgroundColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.StatusBarBackgroundColor : _configService.Current.Appearance.LightTheme.StatusBarBackgroundColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.StatusBarBackgroundColor != value)
            {
                profile.StatusBarBackgroundColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditStatusBarTextColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.StatusBarTextColor : _configService.Current.Appearance.LightTheme.StatusBarTextColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.StatusBarTextColor != value)
            {
                profile.StatusBarTextColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditSeparatorColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.SeparatorColor : _configService.Current.Appearance.LightTheme.SeparatorColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.SeparatorColor != value)
            {
                profile.SeparatorColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCommandLineBackgroundColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CommandLineBackgroundColor : _configService.Current.Appearance.LightTheme.CommandLineBackgroundColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CommandLineBackgroundColor != value)
            {
                profile.CommandLineBackgroundColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCommandLineTextColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CommandLineTextColor : _configService.Current.Appearance.LightTheme.CommandLineTextColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CommandLineTextColor != value)
            {
                profile.CommandLineTextColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCommandLineSelectionBackgroundColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CommandLineSelectionBackgroundColor : _configService.Current.Appearance.LightTheme.CommandLineSelectionBackgroundColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CommandLineSelectionBackgroundColor != value)
            {
                profile.CommandLineSelectionBackgroundColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    // New Edit Properties
    public string EditLeftColumnFileTextColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.LeftColumnFileTextColor : _configService.Current.Appearance.LightTheme.LeftColumnFileTextColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.LeftColumnFileTextColor != value)
            {
                profile.LeftColumnFileTextColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditLeftColumnDirectoryTextColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.LeftColumnDirectoryTextColor : _configService.Current.Appearance.LightTheme.LeftColumnDirectoryTextColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.LeftColumnDirectoryTextColor != value)
            {
                profile.LeftColumnDirectoryTextColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditMiddleColumnFileTextColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.MiddleColumnFileTextColor : _configService.Current.Appearance.LightTheme.MiddleColumnFileTextColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.MiddleColumnFileTextColor != value)
            {
                profile.MiddleColumnFileTextColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditMiddleColumnDirectoryTextColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.MiddleColumnDirectoryTextColor : _configService.Current.Appearance.LightTheme.MiddleColumnDirectoryTextColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.MiddleColumnDirectoryTextColor != value)
            {
                profile.MiddleColumnDirectoryTextColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditMiddleColumnSelectedTextColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.MiddleColumnSelectedTextColor : _configService.Current.Appearance.LightTheme.MiddleColumnSelectedTextColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.MiddleColumnSelectedTextColor != value)
            {
                profile.MiddleColumnSelectedTextColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditMiddleColumnSelectedFontWeightString
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.MiddleColumnSelectedFontWeight : _configService.Current.Appearance.LightTheme.MiddleColumnSelectedFontWeight;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.MiddleColumnSelectedFontWeight != value)
            {
                profile.MiddleColumnSelectedFontWeight = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditSelectedBackgroundColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.SelectedBackgroundColor : _configService.Current.Appearance.LightTheme.SelectedBackgroundColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.SelectedBackgroundColor != value)
            {
                profile.SelectedBackgroundColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditRightColumnFileTextColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.RightColumnFileTextColor : _configService.Current.Appearance.LightTheme.RightColumnFileTextColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.RightColumnFileTextColor != value)
            {
                profile.RightColumnFileTextColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditRightColumnDirectoryTextColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.RightColumnDirectoryTextColor : _configService.Current.Appearance.LightTheme.RightColumnDirectoryTextColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.RightColumnDirectoryTextColor != value)
            {
                profile.RightColumnDirectoryTextColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    // Markdown Colors
    public string EditMarkdownHeadingColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.MarkdownHeadingColor : _configService.Current.Appearance.LightTheme.MarkdownHeadingColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.MarkdownHeadingColor != value)
            {
                profile.MarkdownHeadingColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditMarkdownCodeColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.MarkdownCodeColor : _configService.Current.Appearance.LightTheme.MarkdownCodeColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.MarkdownCodeColor != value)
            {
                profile.MarkdownCodeColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditMarkdownBlockQuoteColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.MarkdownBlockQuoteColor : _configService.Current.Appearance.LightTheme.MarkdownBlockQuoteColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.MarkdownBlockQuoteColor != value)
            {
                profile.MarkdownBlockQuoteColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditMarkdownLinkColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.MarkdownLinkColor : _configService.Current.Appearance.LightTheme.MarkdownLinkColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.MarkdownLinkColor != value)
            {
                profile.MarkdownLinkColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    // New Markdown Items
    public string EditMarkdownEmphasisColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.MarkdownEmphasisColor : _configService.Current.Appearance.LightTheme.MarkdownEmphasisColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.MarkdownEmphasisColor != value)
            {
                profile.MarkdownEmphasisColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditMarkdownStrongEmphasisColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.MarkdownStrongEmphasisColor : _configService.Current.Appearance.LightTheme.MarkdownStrongEmphasisColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.MarkdownStrongEmphasisColor != value)
            {
                profile.MarkdownStrongEmphasisColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditMarkdownImageColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.MarkdownImageColor : _configService.Current.Appearance.LightTheme.MarkdownImageColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.MarkdownImageColor != value)
            {
                profile.MarkdownImageColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    // Code Preview Colors
    public string EditCodeBackgroundColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeBackgroundColor : _configService.Current.Appearance.LightTheme.CodeBackgroundColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeBackgroundColor != value)
            {
                profile.CodeBackgroundColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCodeTextColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeTextColor : _configService.Current.Appearance.LightTheme.CodeTextColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeTextColor != value)
            {
                profile.CodeTextColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCodeSelectionColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeSelectionColor : _configService.Current.Appearance.LightTheme.CodeSelectionColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeSelectionColor != value)
            {
                profile.CodeSelectionColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCodeLineNumberColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeLineNumberColor : _configService.Current.Appearance.LightTheme.CodeLineNumberColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeLineNumberColor != value)
            {
                profile.CodeLineNumberColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCodeCommentColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeCommentColor : _configService.Current.Appearance.LightTheme.CodeCommentColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeCommentColor != value)
            {
                profile.CodeCommentColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCodeStringColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeStringColor : _configService.Current.Appearance.LightTheme.CodeStringColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeStringColor != value)
            {
                profile.CodeStringColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCodeKeywordColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeKeywordColor : _configService.Current.Appearance.LightTheme.CodeKeywordColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeKeywordColor != value)
            {
                profile.CodeKeywordColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCodeNumberColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeNumberColor : _configService.Current.Appearance.LightTheme.CodeNumberColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeNumberColor != value)
            {
                profile.CodeNumberColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCodeMethodColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeMethodColor : _configService.Current.Appearance.LightTheme.CodeMethodColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeMethodColor != value)
            {
                profile.CodeMethodColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCodeClassColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeClassColor : _configService.Current.Appearance.LightTheme.CodeClassColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeClassColor != value)
            {
                profile.CodeClassColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCodeVariableColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeVariableColor : _configService.Current.Appearance.LightTheme.CodeVariableColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeVariableColor != value)
            {
                profile.CodeVariableColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCodeOperatorColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeOperatorColor : _configService.Current.Appearance.LightTheme.CodeOperatorColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeOperatorColor != value)
            {
                profile.CodeOperatorColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCodeHtmlTagColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeHtmlTagColor : _configService.Current.Appearance.LightTheme.CodeHtmlTagColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeHtmlTagColor != value)
            {
                profile.CodeHtmlTagColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCodeCssPropertyColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeCssPropertyColor : _configService.Current.Appearance.LightTheme.CodeCssPropertyColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeCssPropertyColor != value)
            {
                profile.CodeCssPropertyColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    public string EditCodeCssValueColor
    {
        get => EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme.CodeCssValueColor : _configService.Current.Appearance.LightTheme.CodeCssValueColor;
        set
        {
            var profile = EditingTheme == "Dark" ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;
            if (profile.CodeCssValueColor != value)
            {
                profile.CodeCssValueColor = value;
                _configService.Save();
                OnPropertyChanged();
                RefreshActiveTheme();
            }
        }
    }

    partial void OnEditingThemeChanged(string value)
    {
        OnPropertyChanged(nameof(EditLeftColumnColor));
        OnPropertyChanged(nameof(EditMiddleColumnColor));
        OnPropertyChanged(nameof(EditRightColumnColor));
        OnPropertyChanged(nameof(EditTopBarBackgroundColor));
        OnPropertyChanged(nameof(EditTopBarTextColor));
        OnPropertyChanged(nameof(EditStatusBarBackgroundColor));
        OnPropertyChanged(nameof(EditStatusBarTextColor));
        OnPropertyChanged(nameof(EditSeparatorColor));
        OnPropertyChanged(nameof(EditCommandLineBackgroundColor));
        OnPropertyChanged(nameof(EditCommandLineTextColor));
        
        OnPropertyChanged(nameof(EditLeftColumnFileTextColor));
        OnPropertyChanged(nameof(EditLeftColumnDirectoryTextColor));
        OnPropertyChanged(nameof(EditMiddleColumnFileTextColor));
        OnPropertyChanged(nameof(EditMiddleColumnDirectoryTextColor));
        OnPropertyChanged(nameof(EditMiddleColumnSelectedTextColor));
        OnPropertyChanged(nameof(EditMiddleColumnSelectedFontWeightString));
        OnPropertyChanged(nameof(EditSelectedBackgroundColor));
        OnPropertyChanged(nameof(EditRightColumnFileTextColor));
        OnPropertyChanged(nameof(EditRightColumnDirectoryTextColor));

        // Markdown
        OnPropertyChanged(nameof(EditMarkdownHeadingColor));
        OnPropertyChanged(nameof(EditMarkdownCodeColor));
        OnPropertyChanged(nameof(EditMarkdownImageColor));
        OnPropertyChanged(nameof(EditMarkdownBlockQuoteColor));
        OnPropertyChanged(nameof(EditMarkdownEmphasisColor));
        OnPropertyChanged(nameof(EditMarkdownLinkColor));
        OnPropertyChanged(nameof(EditMarkdownStrongEmphasisColor));

        // Code
        OnPropertyChanged(nameof(EditCodeBackgroundColor));
        OnPropertyChanged(nameof(EditCodeTextColor));
        OnPropertyChanged(nameof(EditCodeSelectionColor));
        OnPropertyChanged(nameof(EditCodeLineNumberColor));
        OnPropertyChanged(nameof(EditCodeCommentColor));
        OnPropertyChanged(nameof(EditCodeStringColor));
        OnPropertyChanged(nameof(EditCodeKeywordColor));
        OnPropertyChanged(nameof(EditCodeNumberColor));
        OnPropertyChanged(nameof(EditCodeMethodColor));
        OnPropertyChanged(nameof(EditCodeClassColor));
        OnPropertyChanged(nameof(EditCodeVariableColor));
        OnPropertyChanged(nameof(EditCodeOperatorColor));
        OnPropertyChanged(nameof(EditCodeHtmlTagColor));
        OnPropertyChanged(nameof(EditCodeCssPropertyColor));
        OnPropertyChanged(nameof(EditCodeCssValueColor));
    }

    private void RefreshActiveTheme()
    {
        ApplyTheme(SelectedTheme);
    }

    // Helper for ComboBox binding
    public string TopBarFontWeightString
    {
        get => TopBarFontWeight.ToString();
        set
        {
            if (Enum.TryParse<Avalonia.Media.FontWeight>(value, out var weight))
            {
                TopBarFontWeight = weight;
            }
        }
    }

    public string StatusBarFontWeightString
    {
        get => StatusBarFontWeight.ToString();
        set
        {
            if (Enum.TryParse<Avalonia.Media.FontWeight>(value, out var weight))
            {
                StatusBarFontWeight = weight;
            }
        }
    }

    [ObservableProperty]
    private Avalonia.Controls.GridLength _leftColumnWidth = new(1, Avalonia.Controls.GridUnitType.Star);

    [ObservableProperty]
    private Avalonia.Controls.GridLength _middleColumnWidth = new(2, Avalonia.Controls.GridUnitType.Star);

    [ObservableProperty]
    private Avalonia.Controls.GridLength _rightColumnWidth = new(3, Avalonia.Controls.GridUnitType.Star);

    [ObservableProperty]
    private bool _isSettingsVisible = false;

    public System.Collections.ObjectModel.ObservableCollection<BookmarkItem> BookmarkItems { get; } = new();

    [ObservableProperty]
    private BookmarkItem? _selectedBookmarkItem;
    public System.Collections.ObjectModel.ObservableCollection<YankHistoryItem> YankHistory { get; } = new();

    [ObservableProperty]
    private bool _isYankHistoryVisible;

    [ObservableProperty]
    private HelpPanelViewModel _helpPanel = new();

    [ObservableProperty]
    private bool _isHelpPanelVisible;

    [ObservableProperty]
    private YankHistoryItem? _selectedYankHistoryItem;

    private List<string> _clipboardFiles = new();
    private List<string> _clipboardBackup = new();
    private bool _isCutOperation = false;
    private bool _isLinkOperation = false;
    private string _lastLocation = string.Empty;

    // Design-time constructor
    // Command Panel Configuration
    public ObservableCollection<string> AvailablePanelCommands { get; } = new();
    public ObservableCollection<string> SelectedPanelCommands { get; } = new();

    private void InitializePanelCommands()
    {
        // Populate AvailablePanelCommands
        var allCommands = new List<string>
        {
            // File Operations
            "copy", "cut", "paste", "delete", "rename", "create-file", "create-dir", "create-link",
            "copy-path", "clear-yank", "clear-clipboard", "yank-history",
            
            // Navigation
            "up", "down", "updir", "open", "go-home", "set-home",
            "top", "bottom", "high", "middle", "low",
            "page-up", "page-down", "half-up", "half-down",
            "jump-next", "jump-prev", "mark-load",
            
            // Selection
            "select", "unselect", "invert", "visual",
            
            // Search & Filter
            "search", "filter", "find", 
            "search-next", "search-prev", "find-next", "find-prev",
            
            // Sorting & View Options
            "sort-name", "sort-date", "sort-size", "sort-ext", "sort-natural",
            "set-reverse!", "set-dirfirst!", "set-hidden!", "set-dotfilesonly!", "set-anchorfind",
            "refresh", "toggle-hidden",
            
            // Display & Layout
            "preview", "wrap", "line", "compact", "float", "icon",
            "small", "large", "settings",
            "dark", "light", "system",
            "scroll-preview-up", "scroll-preview-down", "scroll-preview-left", "scroll-preview-right",
            "popup",
            
            // System / External
            "open-terminal", "explorer", "execute", "quit", "help",
            
            // Workspace
            "workspace-open", "workspace-create", "workspace-link"
        };
        
        AvailablePanelCommands.Clear();
        foreach (var cmd in allCommands)
        {
            AvailablePanelCommands.Add(cmd);
        }

        // Populate SelectedPanelCommands from Config
        if (_configService.Current.CommandPanelActions.Count == 0)
        {
            // Default commands if empty
            var defaults = new List<string> 
            { 
                "copy", "cut", "paste", "delete", "rename",
                "create-file", "create-dir", "filter", "search", "go-home",
                "sort-date", "toggle-hidden", "refresh", "open-terminal", "copy-path"
            };
            _configService.Current.CommandPanelActions.AddRange(defaults);
            _configService.Save();
        }

        SelectedPanelCommands.Clear();
        foreach (var cmd in _configService.Current.CommandPanelActions)
        {
            SelectedPanelCommands.Add(cmd);
        }
    }

    [RelayCommand]
    public void AddPanelCommand(string command)
    {
        if (!string.IsNullOrEmpty(command) && !SelectedPanelCommands.Contains(command))
        {
            if (SelectedPanelCommands.Count >= 15)
            {
                StatusText = "Maximum 15 commands allowed in the panel.";
                return;
            }
            SelectedPanelCommands.Add(command);
            _configService.Current.CommandPanelActions.Add(command);
            _configService.Save();
        }
    }

    [RelayCommand]
    public void RemovePanelCommand(string command)
    {
        if (SelectedPanelCommands.Contains(command))
        {
            SelectedPanelCommands.Remove(command);
            _configService.Current.CommandPanelActions.Remove(command);
            _configService.Save();
        }
    }

    [RelayCommand]
    public void MovePanelCommandUp(string command)
    {
        int index = SelectedPanelCommands.IndexOf(command);
        if (index > 0)
        {
            SelectedPanelCommands.Move(index, index - 1);
            
            // Update config
            var configList = _configService.Current.CommandPanelActions;
            int configIndex = configList.IndexOf(command);
            if (configIndex > 0)
            {
                var item = configList[configIndex];
                configList.RemoveAt(configIndex);
                configList.Insert(configIndex - 1, item);
                _configService.Save();
            }
        }
    }

    [RelayCommand]
    public void MovePanelCommandDown(string command)
    {
        int index = SelectedPanelCommands.IndexOf(command);
        if (index >= 0 && index < SelectedPanelCommands.Count - 1)
        {
            SelectedPanelCommands.Move(index, index + 1);

            // Update config
            var configList = _configService.Current.CommandPanelActions;
            int configIndex = configList.IndexOf(command);
            if (configIndex >= 0 && configIndex < configList.Count - 1)
            {
                var item = configList[configIndex];
                configList.RemoveAt(configIndex);
                configList.Insert(configIndex + 1, item);
                _configService.Save();
            }
        }
    }

    private System.Windows.Input.ICommand GetCommandAction(string commandName)
    {
        return commandName switch
        {
            // Interactive / Special Handling
            "create-file" => new RelayCommand(InitiateNewFile),
            "create-dir" => new RelayCommand(InitiateMakeDirectory),
            "rename" => new RelayCommand(InitiateRename),
            "search" => new RelayCommand(() => EnterSearchMode(FilterMethod.Fuzzy)),
            "filter" => new RelayCommand(() => EnterFilterMode(FilterMethod.Fuzzy)),
            "find" => new RelayCommand(EnterFindMode),
            "copy-path" => new RelayCommand(CopyPathToClipboard),
            "help" => new RelayCommand(ShowKeyBindingsHelp),
            "copy" => new RelayCommand(YankSelection), // Map copy to YankSelection directly
            
            // Aliases / Presets
            "sort-name" => new AsyncRelayCommand(async () => await ExecuteCommandImpl("set-sortby name")),
            "sort-date" => new AsyncRelayCommand(async () => await ExecuteCommandImpl("set-sortby time")),
            "sort-size" => new AsyncRelayCommand(async () => await ExecuteCommandImpl("set-sortby size")),
            "sort-ext" => new AsyncRelayCommand(async () => await ExecuteCommandImpl("set-sortby ext")),
            "sort-natural" => new AsyncRelayCommand(async () => await ExecuteCommandImpl("set-sortby natural")),
            "toggle-hidden" => new AsyncRelayCommand(async () => await ExecuteCommandImpl("set-hidden!")),
            
            // Default: Execute by name
            _ => new AsyncRelayCommand(async () => await ExecuteCommandImpl(commandName))
        };
    }

    private string GetCommandDisplayName(string commandName)
    {
        return commandName switch
        {
            "copy-path" => "Copy Path",
            "open-terminal" => "Terminal",
            "create-file" => "New File",
            "create-dir" => "New Dir",
            "create-link" => "Link",
            "delete" => "Delete",
            "rename" => "Rename",
            "search" => "Search",
            "filter" => "Filter",
            "find" => "Find",
            "sort-name" => "Sort Name",
            "sort-date" => "Sort Date",
            "sort-size" => "Sort Size",
            "sort-ext" => "Sort Ext",
            "sort-natural" => "Sort Natural",
            "toggle-hidden" => "Hidden",
            "set-hidden!" => "Hidden",
            "set-reverse!" => "Reverse",
            "set-dirfirst!" => "Dir First",
            "set-dotfilesonly!" => "Dot Only",
            "set-anchorfind" => "Anchor Find",
            "refresh" => "Reload",
            "quit" => "Quit",
            "help" => "Help",
            "cut" => "Cut",
            "copy" => "Copy",
            "paste" => "Paste",
            "go-home" => "Home",
            "set-home" => "Set Home",
            "yank-history" => "Yank Hist",
            "clear-yank" => "Clear Yank",
            "clear-clipboard" => "Clear Clip",
            "workspace-open" => "Workspaces",
            "workspace-create" => "New WS",
            "workspace-link" => "Link WS",
            "scroll-preview-up" => "Prev Up",
            "scroll-preview-down" => "Prev Down",
            "scroll-preview-left" => "Prev Left",
            "scroll-preview-right" => "Prev Right",
            "mark-load" => "Bookmarks",
            "explorer" => "Explorer",
            "execute" => "Execute",
            "popup" => "Popup",
            _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(commandName.Replace("-", " "))
        };
    }

    public MainWindowViewModel() 
    {
        _fileSystemService = new FileSystemService();
        _configService = new ConfigService(); // Fallback
        _fileOperationsService = new FileOperationsService(_configService);
        _keyBindingService = new KeyBindingService();
        var iconProvider = new WindowsIconProvider();
        _previewEngine = new PreviewEngine(iconProvider, _configService);
        _globalHotkeyService = new GlobalHotkeyService();
        _startupService = new StartupService();
        _directorySizeCacheService = new DirectorySizeCacheService();
        _workspacePanel = new WorkspacePanelViewModel(new WorkspaceService(_configService, iconProvider), _fileOperationsService);
        var pinyinService = new PinyinService();
        _parentList = new FileListViewModel(_fileSystemService, iconProvider, _configService.Current, pinyinService);
        _currentList = new FileListViewModel(_fileSystemService, iconProvider, _configService.Current, pinyinService);
        _childList = new FileListViewModel(_fileSystemService, iconProvider, _configService.Current, pinyinService);
        _commandLine = new CommandLineViewModel();
    }

    [ObservableProperty]
    private WorkspacePanelViewModel _workspacePanel;

    [ObservableProperty]
    private bool _isWorkspacePanelVisible;

    [ObservableProperty]
    private bool _isFocusInCommandLine;

    [ObservableProperty]
    private bool _isAppStartupEnabled;

    public string WorkspaceDirectory
    {
        get => _configService.Current.WorkspaceDirectoryName;
        set
        {
            if (_configService.Current.WorkspaceDirectoryName != value)
            {
                _configService.Current.WorkspaceDirectoryName = value;
                _configService.Save();
                OnPropertyChanged();
            }
        }
    }

    partial void OnIsAppStartupEnabledChanged(bool value)
    {
        try
        {
            _startupService.SetAppStartup(value);
        }
        catch (Exception ex)
        {
            StatusText = $"Error setting startup: {ex.Message}";
        }
    }

    [ObservableProperty]
    private bool _isWatcherStartupEnabled;

    partial void OnIsWatcherStartupEnabledChanged(bool value)
    {
        try
        {
            _startupService.SetWatcherStartup(value);
        }
        catch (Exception ex)
        {
            StatusText = $"Error setting watcher startup: {ex.Message}";
        }
    }

    public MainWindowViewModel(
        IFileSystemService fileSystemService, 
        IFileOperationsService fileOperationsService,
        KeyBindingService keyBindingService, 
        PreviewEngine previewEngine, 
        IIconProvider iconProvider,
        IConfigService configService,
        GlobalHotkeyService globalHotkeyService,
        DirectorySizeCacheService directorySizeCacheService,
        StartupService startupService,
        WorkspacePanelViewModel workspacePanel)
    {
        _fileSystemService = fileSystemService;
        _fileOperationsService = fileOperationsService;
        _keyBindingService = keyBindingService;
        _previewEngine = previewEngine;
        _configService = configService;
        _configService.ConfigChanged += OnConfigChanged;
        _globalHotkeyService = globalHotkeyService;
        _directorySizeCacheService = directorySizeCacheService;
        _startupService = startupService;
        _workspacePanel = workspacePanel;

        // Initialize Startup States
        _isAppStartupEnabled = _startupService.IsAppStartupEnabled();
        _isWatcherStartupEnabled = _startupService.IsWatcherStartupEnabled();

        // Load YankHistory
        if (_configService.Current.YankHistory != null)
        {
            foreach (var files in _configService.Current.YankHistory)
            {
                YankHistory.Add(new YankHistoryItem { Files = new List<string>(files) });
            }
            ReindexYankHistory();
        }

        _workspacePanel.SetNavigateCallback(path => 
        {
            IsWorkspacePanelVisible = false;
            _keyBindingService.SetMode(InputMode.Normal);
            WeakReferenceMessenger.Default.Send(new FocusRequestMessage(FocusTarget.CommandLine));
            _ = NavigateToAsync(path);
        });

        _workspacePanel.SetOpenFileCallback(path => 
        {
            IsWorkspacePanelVisible = false;
            _keyBindingService.SetMode(InputMode.Normal);
            WeakReferenceMessenger.Default.Send(new FocusRequestMessage(FocusTarget.CommandLine));
            
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusText = $"Error opening item: {ex.Message}";
            }
        });

        _workspacePanel.SetCloseCallback(() => 
        {
            IsWorkspacePanelVisible = false;
            _keyBindingService.SetMode(InputMode.Normal);
            // Return focus to main window/list
            WeakReferenceMessenger.Default.Send(new FocusRequestMessage(FocusTarget.CommandLine)); // Or list?
            // Actually, if we close panel, we should probably focus the file list.
            // But FocusRequestMessage only supports CommandLine and WorkspacePanel currently?
            // Let's check FocusTarget enum.
        });

        _workspacePanel.SetConfirmCallback((title, message, onConfirm) => 
        {
            _previousMode = _keyBindingService.CurrentMode;
            _keyBindingService.SetMode(InputMode.Dialog);
            IsDialogVisible = true;
            ActiveDialog = new ConfirmationDialogViewModel
            {
                Title = title,
                Message = message,
                OnConfirm = () => 
                {
                    CloseDialog();
                    onConfirm();
                },
                OnCancel = () => 
                {
                    CloseDialog();
                }
            };
        });

        /* _workspacePanel.RequestLinkMode += () => 
        {
            var selectedItems = GetSelectedItems();
            if (!selectedItems.Any())
            {
                StatusText = "No items selected to link.";
                return;
            }
            _workspacePanel.StartLinkMode(selectedItems.Select(i => i.Path).ToList());
        }; */

        // We need to listen to selection changes in WorkspacePanel to update Command Line text
        _workspacePanel.PropertyChanged += (s, e) => 
        {
            // No longer needed for command line update
        };

        InitializePanelCommands();

        var pinyinService = new PinyinService();
        _parentList = new FileListViewModel(fileSystemService, iconProvider, configService.Current, pinyinService);
        _currentList = new FileListViewModel(fileSystemService, iconProvider, configService.Current, pinyinService);
        _childList = new FileListViewModel(fileSystemService, iconProvider, configService.Current, pinyinService);
        _commandLine = new CommandLineViewModel();
        
        _commandLine.CommandExecuted += OnCommandExecuted;
        _commandLine.Cancelled += OnCommandCancelled;
        _commandLine.TabPressed += OnTabPressed;
        _commandLine.PropertyChanged += CommandLine_PropertyChanged;

        _currentList.PropertyChanged += CurrentList_PropertyChanged;
        _currentList.Items.CollectionChanged += CurrentList_Items_CollectionChanged;

        RegisterKeyBindings();

        // Initialize from config
        PreviewFontSize = _configService.Current.Appearance.FontSize;
        PreviewTextFontFamily = new Avalonia.Media.FontFamily(_configService.Current.Appearance.PreviewTextFontFamily);
        SelectedTheme = _configService.Current.Appearance.Theme;
        EnableOfficePreload = _configService.Current.Performance.EnableOfficePreload;
        GlobalShowHotkey = _configService.Current.Appearance.GlobalShowHotkey;

        // Initialize Global Hotkey
        _globalHotkeyService.HotKeyPressed += OnGlobalHotKeyPressed;
        ApplyGlobalHotkey();

        // Global settings
        SeparatorWidth = _configService.Current.Appearance.SeparatorWidth;
        CommandLineFontSize = _configService.Current.Appearance.CommandLineFontSize;
        CommandLineFontFamily = new Avalonia.Media.FontFamily(_configService.Current.Appearance.CommandLineFontFamily);
        CommandLineFontWeight = _configService.Current.Appearance.CommandLineFontWeight;
        TopBarFontFamily = new Avalonia.Media.FontFamily(_configService.Current.Appearance.TopBarFontFamily);
        StatusBarFontFamily = new Avalonia.Media.FontFamily(_configService.Current.Appearance.StatusBarFontFamily);
        MainFontFamily = new Avalonia.Media.FontFamily(_configService.Current.Appearance.FontFamily);
        MainFontSize = _configService.Current.Appearance.MainFontSize;
        InfoFontSize = _configService.Current.Appearance.InfoFontSize;
        TopBarFontSize = _configService.Current.Appearance.TopBarFontSize;
        StatusBarFontSize = _configService.Current.Appearance.StatusBarFontSize;
        
        IsCompactMode = _configService.Current.Appearance.IsCompactMode;
        IsFloatEnabled = _configService.Current.Appearance.IsFloatMode;
        ShowLineNumbers = _configService.Current.Appearance.ShowLineNumbers;
        IsFilePreviewEnabled = _configService.Current.Preview.Enabled;
        ListItemPadding = IsCompactMode ? new Avalonia.Thickness(10, 2) : new Avalonia.Thickness(10, 6);

        SmallWindowWidth = _configService.Current.Appearance.SmallWindowWidth;
        SmallWindowHeight = _configService.Current.Appearance.SmallWindowHeight;
        LargeWindowWidth = _configService.Current.Appearance.LargeWindowWidth;
        LargeWindowHeight = _configService.Current.Appearance.LargeWindowHeight;

        if (Enum.TryParse<Avalonia.Media.FontWeight>(_configService.Current.Appearance.TopBarFontWeight, out var tbWeight))
            TopBarFontWeight = tbWeight;
        if (Enum.TryParse<Avalonia.Media.FontWeight>(_configService.Current.Appearance.StatusBarFontWeight, out var sbWeight))
            StatusBarFontWeight = sbWeight;

        ApplyColumnRatio(_configService.Current.Appearance.ColumnRatio);
        IsIconsVisible = _configService.Current.Appearance.ShowIcons;
        IsPreviewWordWrapEnabled = _configService.Current.Preview.WordWrap;
        
        _ = LoadUserInfoAsync();

        // Apply initial theme
        ApplyTheme(SelectedTheme);

        // Listen for system theme changes
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += (s, e) =>
            {
                if (SelectedTheme == "System")
                {
                    ApplyTheme("System");
                }
            };
        }

        // Initialize with current directory or Home Directory
        var startDir = Directory.GetCurrentDirectory();
        if (!string.IsNullOrEmpty(_configService.Current.HomeDirectory) && Directory.Exists(_configService.Current.HomeDirectory))
        {
            startDir = _configService.Current.HomeDirectory;
        }
        _ = NavigateToAsync(startDir);
    }

    private void OnGlobalHotKeyPressed()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = desktop.MainWindow;
            if (window != null)
            {
                window.Show();
                window.WindowState = Avalonia.Controls.WindowState.Normal;
                window.Activate();
                window.Focus();
            }
        }
    }

    public void ApplyGlobalHotkey()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (desktop.MainWindow != null)
                {
                    var handle = desktop.MainWindow.TryGetPlatformHandle()?.Handle;
                    if (handle.HasValue)
                    {
                        // Parse hotkey string (Simple implementation for now)
                        // Supports "F12", "Ctrl+F12", etc.
                        var parts = GlobalShowHotkey.Split('+');
                        var keyStr = parts.Last();
                        if (Enum.TryParse<Avalonia.Input.Key>(keyStr, true, out var key))
                        {
                            var modifiers = Avalonia.Input.KeyModifiers.None;
                            foreach (var part in parts.Take(parts.Length - 1))
                            {
                                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers |= Avalonia.Input.KeyModifiers.Control;
                                if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= Avalonia.Input.KeyModifiers.Alt;
                                if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= Avalonia.Input.KeyModifiers.Shift;
                                if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)) modifiers |= Avalonia.Input.KeyModifiers.Meta;
                            }
                            
                            _globalHotkeyService.Register(handle.Value, key, modifiers);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to register hotkey: {ex.Message}");
        }
    }

    partial void OnGlobalShowHotkeyChanged(string value)
    {
        _configService.Current.Appearance.GlobalShowHotkey = value;
        _configService.Save();
        ApplyGlobalHotkey();
    }
        


    private List<string> _completionMatches = new();
    private int _completionIndex = -1;
    private string _completionPrefix = string.Empty;
    private bool _isCompleting = false;

    public IReadOnlyList<string> AllCommands => _allCommands;
    private readonly List<string> _allCommands = new() 
    { 
        "quit", "q", "clear", "cd", "rename", "mkdir", "new", "delete", "rm",
        "settings", "compact", "wrap", "line", "preview",
        "small", "large", "float", "icon", "dark", "light", "system",
        "set-home", "go-home", "clear-yank", "create-link", "set-yank",
        "workspace-open", "workspace-link", "workspace-create", "command-panel",
        "filter", "search", "find", "mark-save", "mark-load",
        "set-sortby", "set-dirfirst", "set-nodirfirst", "set-dirfirst!", "set-info", 
        "set-timefmt", "set-infotimefmtnew", "set-infotimefmtold", "set-info_font_size", 
        "set-hidden!", "set-dotfilesonly!", "set-filtermethod", "set-searchmethod", "set-reverse!", "set-anchorfind",
        "sort-name", "sort-date", "sort-size", "sort-ext", "sort-natural", "toggle-hidden",
        "up", "down", "updir", "open", "top", "bottom", "high", "middle", "low",
        "page-up", "page-down", "half-up", "half-down",
        "search-next", "search-prev", "find-next", "find-prev", "jump-next", "jump-prev",
        "visual", "select", "unselect", "invert", "yank", "cut", "paste", "copy", "copy-path",
        "yank-history", "clear-clipboard", "execute", "explorer", "open-terminal", "popup", "refresh", "help",
        "scroll-preview-up", "scroll-preview-down", "scroll-preview-left", "scroll-preview-right"
    };

    public IReadOnlySet<string> NoArgCommands => _noArgCommands;
    private readonly HashSet<string> _noArgCommands = new() 
    { 
        "quit", "q", "clear", "delete", "rm",
        "settings", "compact", "wrap", "line", "preview",
        "small", "large", "float", "icon", "dark", "light", "system",
        "set-home", "go-home", "clear-yank", "create-link",
        "workspace-open", "workspace-link", "workspace-create", "command-panel",
        "mark-load", "search", "find", "help",
        "set-dirfirst", "set-nodirfirst", "set-dirfirst!", 
        "set-hidden!", "set-dotfilesonly!", "set-reverse!", "set-anchorfind",
        "sort-name", "sort-date", "sort-size", "sort-ext", "sort-natural", "toggle-hidden",
        "up", "down", "updir", "open", "top", "bottom", "high", "middle", "low",
        "page-up", "page-down", "half-up", "half-down",
        "search-next", "search-prev", "find-next", "find-prev", "jump-next", "jump-prev",
        "visual", "select", "unselect", "invert", "yank", "cut", "paste", "copy", "copy-path",
        "yank-history", "clear-clipboard", "execute", "explorer", "open-terminal", "popup", "refresh",
        "scroll-preview-up", "scroll-preview-down", "scroll-preview-left", "scroll-preview-right"
    };

    private void CommandLine_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandLineViewModel.CommandText))
        {
            if (!_isCompleting)
            {
                _completionMatches.Clear();
                _completionIndex = -1;
            }

            var text = CommandLine.CommandText;
            // Handle incremental search
            if (CommandLine.Prefix == "/: ")
            {
                var parts = text.Split(' ', 2);
                if (parts.Length > 0 && Enum.TryParse<FilterMethod>(parts[0], true, out var mode))
                {
                    string pattern = parts.Length > 1 ? parts[1] : "";
                    CurrentList.Search(pattern, false, true, mode);
                    
                    // Update config to remember last used mode
                    if (_configService.Current.SearchMethod != mode)
                    {
                        _configService.Current.SearchMethod = mode;
                        _configService.Save();
                    }
                }
            }
            else if (CommandLine.Prefix == "?: ")
            {
                var parts = text.Split(' ', 2);
                if (parts.Length > 0 && Enum.TryParse<FilterMethod>(parts[0], true, out var mode))
                {
                    string pattern = parts.Length > 1 ? parts[1] : "";
                    CurrentList.Search(pattern, true, true, mode);
                    
                    // Update config to remember last used mode
                    if (_configService.Current.SearchMethod != mode)
                    {
                        _configService.Current.SearchMethod = mode;
                        _configService.Save();
                    }
                }
            }

            // Handle new filter format: filter <mode> <pattern>
            if (CommandLine.Prefix == ": " && (text.StartsWith("filter ") || text.StartsWith(":filter ")))
            {
                // Expected format: "filter <mode> <pattern>"
                // e.g. "filter fuzzy abc" -> pattern "abc"
                // e.g. "filter fuzzy " -> pattern ""
                
                var parts = text.Split(' ', 3);
                string pattern = "";
                FilterMethod? mode = null;

                if (parts.Length > 1)
                {
                    if (Enum.TryParse<FilterMethod>(parts[1], true, out var parsedMode))
                    {
                        mode = parsedMode;
                    }
                }

                if (parts.Length > 2)
                {
                    pattern = parts[2];
                }
                
                CurrentList.ApplyFilter(pattern, mode);
            }
        }
    }

    private void OnTabPressed(object? sender, EventArgs e)
    {
        // If Workspace Panel is visible, Tab switches focus
        if (IsWorkspacePanelVisible)
        {
            SwitchWorkspaceFocus();
            return;
        }

        if (_completionMatches.Count == 0)
        {
            var text = CommandLine.CommandText;
            var prefix = CommandLine.Prefix;

            // 1. Command Completion (Prefix is ": ")
            if (prefix == ": " || prefix == ":")
            {
                // Check if we are completing arguments for specific commands
                string cmd = text.TrimStart();
                int spaceIndex = cmd.IndexOf(' ');
                
                if (spaceIndex >= 0)
                {
                    // We have a command and maybe partial argument
                    string commandName = cmd.Substring(0, spaceIndex);
                    string argPrefix = cmd.Substring(spaceIndex + 1);
                    
                    if (commandName == "filter" || commandName == "set-filtermethod" || commandName == "set-searchmethod")
                    {
                        var modes = new List<string> { "fuzzy", "text", "glob", "regex" };
                        _completionMatches = modes
                            .Where(m => m.StartsWith(argPrefix, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(m => m)
                            .Select(m => m + " ")
                            .ToList();
                            
                        _completionPrefix = commandName + " ";
                    }
                    else
                    {
                        // Default file completion for other commands?
                        // Or maybe just nothing for now
                        _completionMatches = new List<string>();
                    }
                }
                else
                {
                    // Completing the command itself
                    _completionPrefix = "";
                    _completionMatches = _allCommands
                        .Where(c => c.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(c => c)
                        .ToList();
                }
            }
            // 2. Search/Filter Mode Completion (Prefix is "/: " or "?: ")
            else if (prefix == "/: " || prefix == "?: ")
            {
                string cmd = text.TrimStart();
                int spaceIndex = cmd.IndexOf(' ');

                if (spaceIndex < 0)
                {
                    // Completing the mode (fuzzy, text, glob, regex)
                    var modes = new List<string> { "fuzzy", "text", "glob", "regex" };
                    _completionMatches = modes
                        .Where(m => m.StartsWith(cmd, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(m => m)
                        .Select(m => m + " ")
                        .ToList();
                    
                    _completionPrefix = "";
                }
                else
                {
                    // Completing the pattern - fall back to file completion logic below?
                    // Or handle it here.
                    // Let's reuse the file completion logic in the 'else' block by NOT matching here if we want file completion.
                    // But we are in an 'else if'.
                    
                    // Copying file completion logic here for pattern
                    string matchPrefix = text.Substring(spaceIndex + 1);
                    string commandPrefix = text.Substring(0, spaceIndex + 1);
                    
                    _completionPrefix = commandPrefix;
                    _completionMatches = CurrentList.GetCompletionMatches(matchPrefix).ToList();
                }
            }
            // 3. File/Argument Completion
            else
            {
                string matchPrefix = text;
                string commandPrefix = "";
                
                int lastSpace = text.LastIndexOf(' ');
                if (lastSpace >= 0)
                {
                    commandPrefix = text.Substring(0, lastSpace + 1);
                    matchPrefix = text.Substring(lastSpace + 1);
                }

                _completionPrefix = commandPrefix;
                
                // Find matches in current directory
                _completionMatches = CurrentList.GetCompletionMatches(matchPrefix).ToList();
            }
            
            if (_completionMatches.Count > 0)
            {
                _completionIndex = 0;
                
                _isCompleting = true;
                CommandLine.CommandText = _completionPrefix + _completionMatches[0];
                CommandLine.SelectionStart = CommandLine.CommandText.Length;
                CommandLine.SelectionEnd = CommandLine.CommandText.Length;
                _isCompleting = false;
            }
        }
        else
        {
            // Cycle
            _completionIndex = (_completionIndex + 1) % _completionMatches.Count;
            
            _isCompleting = true;
            CommandLine.CommandText = _completionPrefix + _completionMatches[_completionIndex];
            CommandLine.SelectionStart = CommandLine.CommandText.Length;
            CommandLine.SelectionEnd = CommandLine.CommandText.Length;
            _isCompleting = false;
        }
    }

    private void CurrentList_Items_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        UpdateStatusText();
    }

    private void CurrentList_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileListViewModel.SelectedItem))
        {
            UpdatePreview();
            UpdateStatusText();
            UpdateUserNamePath();
        }
        else if (e.PropertyName == nameof(FileListViewModel.CurrentPath))
        {
            // Clear undo history when changing directory
            _fileOperationsService.ClearUndoHistory();
            
            UpdateUserNamePath();
            UpdateStatusText();
            // _ = CalculateAllDirectoriesSizeAsync();
        }
    }

    private CancellationTokenSource? _calculationCts;

    private async Task CalculateAllDirectoriesSizeAsync()
    {
        // Optimization: Only calculate if Size info is enabled
        if (!_configService.Current.Info.Contains(Models.InfoType.Size)) return;

        // Cancel previous calculation
        _calculationCts?.Cancel();
        _calculationCts = new CancellationTokenSource();
        var token = _calculationCts.Token;

        var currentPath = CurrentList.CurrentPath;

        // First pass: Apply cached sizes
        bool anyMissing = false;
        foreach (var item in CurrentList.Items.Where(i => i.Type == Models.FileType.Directory))
        {
            if (_directorySizeCacheService.TryGetSize(item.Path, out long cachedSize))
            {
                item.Size = cachedSize;
            }
            else
            {
                anyMissing = true;
            }
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(UpdateStatusText);

        if (!anyMissing) return;

        var items = CurrentList.Items.Where(i => i.Type == Models.FileType.Directory && i.Size == -1).ToList();
        
        // Prioritize current item if it's in the list
        var currentItem = CurrentList.SelectedItem;
        if (currentItem != null && items.Contains(currentItem))
        {
            items.Remove(currentItem);
            items.Insert(0, currentItem);
        }

        foreach (var item in items)
        {
            if (token.IsCancellationRequested) return;
            if (CurrentList.CurrentPath != currentPath) return;

            await CalculateDirectorySizeAsync(item);
        }

        if (token.IsCancellationRequested) return;
        if (CurrentList.CurrentPath != currentPath) return;

        // After calculating all children, we can sum them up to get the total size of the current folder
        // and cache it for future use (e.g. when navigating up)
        long totalSize = CurrentList.Items.Sum(i => i.Size == -1 ? 0 : i.Size);
        _directorySizeCacheService.UpdateSize(currentPath, totalSize);

        Avalonia.Threading.Dispatcher.UIThread.Post(UpdateStatusText);
    }

    private void UpdateUserNamePath()
    {
        var user = Environment.UserName;
        // Show full path of selected item if available, otherwise show current directory path
        var path = CurrentList.SelectedItem?.Path ?? CurrentList.CurrentPath;
        UserNamePath = $"{user}: {path}";
    }

    private void UpdateStatusText()
    {
        var total = CurrentList.Items.Count;
        var isVisual = _keyBindingService.CurrentMode == InputMode.Visual;

        var selectedItems = CurrentList.Items.Where(i => i.IsSelected).ToList();
        var selectedCount = selectedItems.Count;

        if (selectedCount > 0 || isVisual)
        {
            long selectedTotalSize = selectedItems.Sum(i => i.Size == -1 ? 0 : i.Size);
            string selectedSizeStr = FormatSize(selectedTotalSize);
            
            StatusText = $"{selectedCount}/{total} selected   Size: {selectedSizeStr}";
        }
        else
        {
            var item = CurrentList.SelectedItem;
            int index = item != null ? CurrentList.Items.IndexOf(item) + 1 : 0;
            
            string itemInfo = "";
            if (item != null)
            {
                string sizeStr;
                if (item.Type == FileType.Directory)
                {
                    sizeStr = "folder";
                }
                else
                {
                    long displaySize = item.Size == -1 ? 0 : item.Size;
                    sizeStr = FormatSize(displaySize);
                }
                itemInfo = $"{sizeStr}   {item.CreationTime:yyyy/MM/dd HH:mm}";
            }

            StatusText = $"{index}/{total}   {itemInfo}";
        }

        // Append Clipboard Status
        int historyLimit = _configService.Current.Performance.MaxYankHistory;
        if (_clipboardFiles.Count > 0)
        {
            int fileCount = 0;
            int folderCount = 0;
            int lnkCount = 0;

            foreach (var path in _clipboardFiles)
            {
                // Check if it is an item inside an archive
                bool isArchiveItem = ArchiveFileSystemHelper.IsArchivePath(path, out _, out string internalPath) && !string.IsNullOrEmpty(internalPath);

                if (File.Exists(path) || isArchiveItem)
                {
                    if (!isArchiveItem && Path.GetExtension(path).ToLower() == ".lnk")
                    {
                        lnkCount++;
                    }
                    else
                    {
                        fileCount++;
                    }
                }
                else if (Directory.Exists(path))
                {
                    folderCount++;
                }
            }

            var parts = new List<string>();
            if (fileCount > 0) parts.Add($"{fileCount} file{(fileCount > 1 ? "s" : "")}");
            if (folderCount > 0) parts.Add($"{folderCount} folder{(folderCount > 1 ? "s" : "")}");
            if (lnkCount > 0) parts.Add($"{lnkCount} lnk{(lnkCount > 1 ? "s" : "")}");

            string counts = string.Join(" ", parts);
            string action = _isLinkOperation ? "lnk" : (_isCutOperation ? "cut" : "yanked");
            
            string historyInfo = YankHistory.Count > 0 ? $" (History: {YankHistory.Count}/{historyLimit})" : "";
            StatusText += $"   [{counts} {action}{historyInfo}]";
        }
        else if (YankHistory.Count > 0)
        {
             StatusText += $"   [History: {YankHistory.Count}/{historyLimit}]";
        }
    }

    private bool IsOfficeFile(string path)
    {
        var ext = Path.GetExtension(path).ToLower();
        return ext == ".docx" || ext == ".xlsx" || ext == ".pptx";
    }

    private async Task CalculateDirectorySizeAsync(FileSystemItem item)
    {
        if (item.Type == Models.FileType.Directory)
        {
            long size = await _fileSystemService.GetDirectorySizeAsync(item.Path);
            
            // Update cache
            _directorySizeCacheService.UpdateSize(item.Path, size);

            // Only update if size changed to avoid loops if it stays 0
            if (item.Size != size)
            {
                item.Size = size;
                Avalonia.Threading.Dispatcher.UIThread.Post(UpdateStatusText);
            }
        }
    }

    private string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private CancellationTokenSource? _previewCts;
    private const int PreviewDebounceMs = 100;

    private async void UpdatePreview()
    {
        try
        {
            _previewCts?.Cancel();
            _previewCts = new CancellationTokenSource();
            var token = _previewCts.Token;

            var selected = CurrentList.SelectedItem;
            if (selected == null)
            {
                PreviewContent = "No selection";
                return;
            }

            if (selected.Type == Models.FileType.File && IsFilePreviewEnabled)
            {
                // Don't show loading immediately to avoid flicker on cached items
                // PreviewContent = "Loading preview...";
            }

            try
            {
                // Debounce to prevent lag during rapid navigation
                await Task.Delay(PreviewDebounceMs, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested) return;

            if (selected.Type == Models.FileType.File && IsFilePreviewEnabled)
            {
                // Only show loading if we passed the debounce check
                PreviewContent = "Loading preview...";
            }

            if (selected.Type == Models.FileType.File)
            {
                if (!IsFilePreviewEnabled)
                {
                    try
                    {
                        var info = new System.IO.FileInfo(selected.Path);
                        PreviewContent = new FileInfoPreviewModel
                        {
                            FileName = selected.Name,
                            FilePath = selected.Path,
                            FileSize = FormatSize(selected.Size),
                            Created = info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                            Modified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                            Accessed = info.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss"),
                            Attributes = info.Attributes.ToString(),
                            Extension = selected.Extension
                        };
                    }
                    catch (Exception ex)
                    {
                        PreviewContent = $"Error getting file info: {ex.Message}";
                    }
                    return;
                }

                try 
                {
                    // 1. Get preview
                    // Note: OfficePreviewProvider now returns OfficePreviewModel directly even for Default mode
                    // to ensure smooth transition within the same control.
                    var preview = await _previewEngine.GeneratePreviewAsync(selected.Path, PreviewMode.Default, token);
                    
                    if (token.IsCancellationRequested)
                    {
                        // If cancelled, dispose the result if it's disposable (e.g. we loaded a bitmap just before cancel)
                        if (preview is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                        return;
                    }

                    PreviewContent = preview;

                    // 2. If it's an ImagePreviewModel (e.g. non-Office image), we are done.
                    // If it's OfficePreviewModel, the control handles the loading.
                    // We only need to check if we got a "fast" preview that needs upgrading to "full"
                    // BUT, since we changed OfficePreviewProvider to return OfficePreviewModel immediately,
                    // we don't need the two-step loading here anymore for Office files.
                    // The OfficePreviewControl handles the thumbnail -> activeX transition.
                }
                catch (Exception ex)
                {
                    PreviewContent = $"Error generating preview: {ex.Message}";
                }
            }
            else if (selected.Type == Models.FileType.Directory)
            {
                await ChildList.LoadDirectoryAsync(selected.Path);
                
                // Restore selection from history for the preview column
                if (_directorySelectionHistory.TryGetValue(selected.Path, out var lastSelectedPath))
                {
                    var item = ChildList.Items.FirstOrDefault(i => i.Path == lastSelectedPath);
                    if (item != null)
                    {
                        ChildList.SelectedItem = item;
                    }
                }

                PreviewContent = ChildList;
            }
            else
            {
                PreviewContent = "Directory preview not implemented";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled error in UpdatePreview: {ex}");
            PreviewContent = $"Error: {ex.Message}";
        }
    }

    private void OpenInExplorer()
    {
        try
        {
            var selected = CurrentList.SelectedItem;
            if (selected != null)
            {
                // Open parent folder and select the file
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{selected.Path}\"");
            }
            else
            {
                // Open current folder
                System.Diagnostics.Process.Start("explorer.exe", $"\"{CurrentList.CurrentPath}\"");
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error opening explorer: {ex.Message}";
        }
    }

    private void OpenTerminal()
    {
        try
        {
            string path = CurrentList.CurrentPath;
            // Try Windows Terminal first
            try 
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "wt",
                    Arguments = $"-d \"{path}\"",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Fallback to PowerShell
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoExit -Command \"Set-Location '{path}'\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error opening terminal: {ex.Message}";
        }
    }

    private async void ToggleWorkspacePanel()
    {
        IsWorkspacePanelVisible = !IsWorkspacePanelVisible;
        if (IsWorkspacePanelVisible)
        {
            _keyBindingService.SetMode(InputMode.Workspace);
            WorkspacePanel.IsLinkMode = false;
            WorkspacePanel.IsWorkspaceListFocused = true;
            await WorkspacePanel.LoadWorkspacesAsync();
            WeakReferenceMessenger.Default.Send(new FocusRequestMessage(FocusTarget.WorkspacePanel));
        }
        else
        {
            _keyBindingService.SetMode(InputMode.Normal);
            CommandLine.Cancel(); // Ensure command line is closed if it was open
        }
    }

    private async void InitiateWorkspaceLink()
    {
        var selectedItems = GetSelectedItems();
        if (!selectedItems.Any())
        {
            StatusText = "No items selected to link.";
            return;
        }

        IsWorkspacePanelVisible = true;
        _keyBindingService.SetMode(InputMode.Workspace);
        await WorkspacePanel.LoadWorkspacesAsync();
        
        WorkspacePanel.StartLinkMode(selectedItems.Select(i => i.Path).ToList());
        
        // Ensure focus is on the panel, not command line
        IsFocusInCommandLine = false;
        WeakReferenceMessenger.Default.Send(new FocusRequestMessage(FocusTarget.WorkspacePanel));
    }

    private async void InitiateWorkspaceCreation()
    {
        IsWorkspacePanelVisible = true;
        _keyBindingService.SetMode(InputMode.Workspace);
        await WorkspacePanel.LoadWorkspacesAsync();
        
        WorkspacePanel.StartCreation();

        // Ensure focus is on the panel, not command line
        IsFocusInCommandLine = false;
        WeakReferenceMessenger.Default.Send(new FocusRequestMessage(FocusTarget.WorkspacePanel));
    }

    private List<Models.FileSystemItem> GetSelectedItems()
    {
        var selectedItems = CurrentList.GetSelectedItems().ToList();
        if (selectedItems.Count == 0 && CurrentList.SelectedItem != null)
        {
            selectedItems.Add(CurrentList.SelectedItem);
        }
        return selectedItems;
    }

    private async void OpenWorkspacePanel()
    {
        IsWorkspacePanelVisible = true;
        _keyBindingService.SetMode(InputMode.Workspace);
        await WorkspacePanel.LoadWorkspacesAsync();
        
        // Ensure focus is on the panel
        IsFocusInCommandLine = false;
        WeakReferenceMessenger.Default.Send(new FocusRequestMessage(FocusTarget.WorkspacePanel));
    }

    public void SwitchWorkspaceFocus()
    {
        if (IsFocusInCommandLine)
        {
            IsFocusInCommandLine = false;
            // Tab cycle is only between CommandLine and Workspace list
            WorkspacePanel.IsWorkspaceListFocused = true;
            WeakReferenceMessenger.Default.Send(new FocusRequestMessage(FocusTarget.WorkspacePanel));
        }
        else
        {
            IsFocusInCommandLine = true;
            WeakReferenceMessenger.Default.Send(new FocusRequestMessage(FocusTarget.CommandLine));
        }
    }

    public ObservableCollection<KeyBindingItem> KeyBindingItems { get; } = new();
    private Dictionary<string, ICommand> _commandRegistry = new();

    private void InitializeCommandRegistry()
    {
        _commandRegistry.Clear();

        // Normal Mode Commands
        _commandRegistry["Normal.ScrollPreviewUp"] = new RelayCommand(ScrollPreviewUp);
        _commandRegistry["Normal.ScrollPreviewDown"] = new RelayCommand(ScrollPreviewDown);
        _commandRegistry["Normal.ScrollPreviewLeft"] = new RelayCommand(ScrollPreviewLeft);
        _commandRegistry["Normal.ScrollPreviewRight"] = new RelayCommand(ScrollPreviewRight);
        _commandRegistry["Normal.MoveUp"] = new RelayCommand(MoveUp);
        _commandRegistry["Normal.MoveDown"] = new RelayCommand(MoveDown);
        _commandRegistry["Normal.MoveUpDir"] = new RelayCommand(MoveUpDir);
        _commandRegistry["Normal.Open"] = new RelayCommand(Open);
        _commandRegistry["Normal.MoveToTop"] = new RelayCommand(MoveToTop);
        _commandRegistry["Normal.MoveToBottom"] = new RelayCommand(MoveToBottom);
        _commandRegistry["Normal.MoveToScreenTop"] = new RelayCommand(MoveToScreenTop);
        _commandRegistry["Normal.MoveToScreenMiddle"] = new RelayCommand(MoveToScreenMiddle);
        _commandRegistry["Normal.MoveToScreenBottom"] = new RelayCommand(MoveToScreenBottom);
        _commandRegistry["Normal.ScrollHalfPageUp"] = new RelayCommand(ScrollHalfPageUp);
        _commandRegistry["Normal.ScrollHalfPageDown"] = new RelayCommand(ScrollHalfPageDown);
        _commandRegistry["Normal.ScrollPageUp"] = new RelayCommand(ScrollPageUp);
        _commandRegistry["Normal.ScrollPageDown"] = new RelayCommand(ScrollPageDown);
        _commandRegistry["Normal.EnterJumpMode"] = new RelayCommand(EnterJumpMode);
        _commandRegistry["Normal.EnterCommandMode"] = new RelayCommand(EnterCommandMode);
        _commandRegistry["Normal.FindPrev"] = new RelayCommand(FindPrev);
        _commandRegistry["Normal.FindNext"] = new RelayCommand(FindNext);
        _commandRegistry["Normal.EnterSearchMode"] = new RelayCommand(EnterSearchMode);
        _commandRegistry["Normal.EnterSearchBackMode"] = new RelayCommand(EnterSearchBackMode);
        _commandRegistry["Normal.SearchNext"] = new RelayCommand(SearchNext);
        _commandRegistry["Normal.SearchPrev"] = new RelayCommand(SearchPrev);
        _commandRegistry["Normal.EnterFindMode"] = new RelayCommand(EnterFindMode);
        _commandRegistry["Normal.EnterFindBackMode"] = new RelayCommand(EnterFindBackMode);
        _commandRegistry["Normal.EnterBookmarkMode"] = new RelayCommand(EnterBookmarkMode);
        _commandRegistry["Normal.JumpPrev"] = new RelayCommand(JumpPrev);
        _commandRegistry["Normal.JumpNext"] = new RelayCommand(JumpNext);
        _commandRegistry["Normal.ToggleVisualMode"] = new RelayCommand(ToggleVisualMode);
        _commandRegistry["Normal.ToggleSelection"] = new RelayCommand(ToggleSelection);
        _commandRegistry["Normal.UnselectAll"] = new RelayCommand(UnselectAll);
        _commandRegistry["Normal.InvertSelection"] = new RelayCommand(InvertSelection);
        _commandRegistry["Normal.OpenPopupPreview"] = new AsyncRelayCommand(OpenPopupPreview);
        _commandRegistry["Normal.InitiateWorkspaceLink"] = new RelayCommand(InitiateWorkspaceLink);
        _commandRegistry["Normal.InitiateWorkspaceCreation"] = new RelayCommand(InitiateWorkspaceCreation);
        _commandRegistry["Normal.ToggleWorkspacePanel"] = new RelayCommand(ToggleWorkspacePanel);
        _commandRegistry["Normal.OpenInExplorer"] = new RelayCommand(OpenInExplorer);
        _commandRegistry["Normal.ExecuteCurrentItem"] = new RelayCommand(ExecuteCurrentItem);
        _commandRegistry["Normal.HideApplication"] = new RelayCommand(HideApplication);
        _commandRegistry["Normal.HandleEscape"] = new RelayCommand(HandleEscape);
        
        _commandRegistry["Normal.SetHidden"] = new AsyncRelayCommand(() => ExecuteCommandImpl("set-hidden!"));
        _commandRegistry["Normal.EnterFilterMode"] = new RelayCommand(EnterFilterMode);
        _commandRegistry["Normal.SetDotfilesOnly"] = new AsyncRelayCommand(() => ExecuteCommandImpl("set-dotfilesonly!"));
        _commandRegistry["Normal.SetReverse"] = new AsyncRelayCommand(() => ExecuteCommandImpl("set-reverse!"));
        _commandRegistry["Normal.SetDirFirst"] = new AsyncRelayCommand(() => ExecuteCommandImpl("set-dirfirst!"));
        _commandRegistry["Normal.SetInfoPerm"] = new AsyncRelayCommand(() => ExecuteCommandImpl("set-info perm"));
        _commandRegistry["Normal.SetInfo"] = new AsyncRelayCommand(() => ExecuteCommandImpl("set-info"));
        _commandRegistry["Normal.SetInfoSize"] = new AsyncRelayCommand(() => ExecuteCommandImpl("set-info size"));
        _commandRegistry["Normal.SetInfoTime"] = new AsyncRelayCommand(() => ExecuteCommandImpl("set-info time"));
        _commandRegistry["Normal.SetInfoSizeTime"] = new AsyncRelayCommand(() => ExecuteCommandImpl("set-info size:time"));
        
        _commandRegistry["Normal.SortNatural"] = new AsyncRelayCommand(async () => { await ExecuteCommandImpl("set-sortby natural"); await ExecuteCommandImpl("set-info"); });
        _commandRegistry["Normal.SortSize"] = new AsyncRelayCommand(async () => { await ExecuteCommandImpl("set-sortby size"); await ExecuteCommandImpl("set-info size"); });
        _commandRegistry["Normal.SortTime"] = new AsyncRelayCommand(async () => { await ExecuteCommandImpl("set-sortby time"); await ExecuteCommandImpl("set-info time"); });
        _commandRegistry["Normal.SortAtime"] = new AsyncRelayCommand(async () => { await ExecuteCommandImpl("set-sortby atime"); await ExecuteCommandImpl("set-info atime"); });
        _commandRegistry["Normal.SortBtime"] = new AsyncRelayCommand(async () => { await ExecuteCommandImpl("set-sortby btime"); await ExecuteCommandImpl("set-info btime"); });
        _commandRegistry["Normal.SortCtime"] = new AsyncRelayCommand(async () => { await ExecuteCommandImpl("set-sortby ctime"); await ExecuteCommandImpl("set-info ctime"); });
        _commandRegistry["Normal.SortExt"] = new AsyncRelayCommand(async () => { await ExecuteCommandImpl("set-sortby ext"); await ExecuteCommandImpl("set-info"); });

        _commandRegistry["Normal.YankSelection"] = new RelayCommand(YankSelection);
        _commandRegistry["Normal.CutSelection"] = new RelayCommand(CutSelection);
        _commandRegistry["Normal.PasteSelection"] = new RelayCommand(PasteSelection);
        _commandRegistry["Normal.CreateLinkSelection"] = new RelayCommand(CreateLinkSelection);
        _commandRegistry["Normal.ShowYankHistory"] = new RelayCommand(ShowYankHistory);
        _commandRegistry["Normal.RequestDelete"] = new RelayCommand(RequestDelete);
        _commandRegistry["Normal.InitiateRename"] = new RelayCommand(InitiateRename);
        _commandRegistry["Normal.RefreshAsync"] = new AsyncRelayCommand(RefreshAsync);
        _commandRegistry["Normal.ClearActiveClipboard"] = new RelayCommand(ClearActiveClipboard);
        _commandRegistry["Normal.InitiateNewFile"] = new RelayCommand(InitiateNewFile);
        _commandRegistry["Normal.InitiateMakeDirectory"] = new RelayCommand(InitiateMakeDirectory);
        _commandRegistry["Normal.OpenCommandPanel"] = new RelayCommand(OpenCommandPanel);

        // Visual Mode Commands
        _commandRegistry["Visual.MoveUp"] = new RelayCommand(MoveUp);
        _commandRegistry["Visual.MoveDown"] = new RelayCommand(MoveDown);
        _commandRegistry["Visual.ToggleVisualMode"] = new RelayCommand(ToggleVisualMode);
        _commandRegistry["Visual.ExitVisualMode"] = new RelayCommand(ExitVisualMode);
        _commandRegistry["Visual.ToggleSelection"] = new RelayCommand(ToggleSelection);
        _commandRegistry["Visual.UnselectAll"] = new RelayCommand(UnselectAll);
        _commandRegistry["Visual.YankSelection"] = new RelayCommand(YankSelection);
        _commandRegistry["Visual.CutSelection"] = new RelayCommand(CutSelection);
        _commandRegistry["Visual.RequestDelete"] = new RelayCommand(RequestDelete);
        _commandRegistry["Visual.VisualChange"] = new RelayCommand(VisualChange);

        // Workspace Mode Commands
        _commandRegistry["Workspace.SwitchWorkspaceFocus"] = new RelayCommand(SwitchWorkspaceFocus);

        // Jump Mode Commands
        _commandRegistry["Jump.SelectPrevBookmark"] = new RelayCommand(SelectPrevBookmark);
        _commandRegistry["Jump.SelectNextBookmark"] = new RelayCommand(SelectNextBookmark);
        _commandRegistry["Jump.JumpToSelectedBookmark"] = new RelayCommand(JumpToSelectedBookmark);
        _commandRegistry["Jump.DeleteSelectedBookmark"] = new RelayCommand(DeleteSelectedBookmark);
        _commandRegistry["Jump.ExitJumpMode"] = new RelayCommand(ExitJumpMode);

        // Yank History Mode Commands
        _commandRegistry["YankHistory.HideYankHistory"] = new RelayCommand(HideYankHistory);
        _commandRegistry["YankHistory.DeleteSelectedYankHistoryItem"] = new RelayCommand(DeleteSelectedYankHistoryItem);
        _commandRegistry["YankHistory.ConfirmYankHistorySelection"] = new RelayCommand(ConfirmYankHistorySelection);
        _commandRegistry["YankHistory.SelectNextYankHistory"] = new RelayCommand(SelectNextYankHistory);
        _commandRegistry["YankHistory.SelectPrevYankHistory"] = new RelayCommand(SelectPrevYankHistory);

        // Dialog Mode Commands
        _commandRegistry["Dialog.ConfirmDialog"] = new RelayCommand(ConfirmDialog);
        _commandRegistry["Dialog.CancelDialog"] = new RelayCommand(CancelDialog);
        _commandRegistry["Dialog.CancelDialogKeepSelection"] = new RelayCommand(CancelDialogKeepSelection);

        // Help Mode Commands
        _commandRegistry["Help.ScrollDown"] = new RelayCommand(() => HelpPanel.ScrollDown());
        _commandRegistry["Help.ScrollUp"] = new RelayCommand(() => HelpPanel.ScrollUp());
        _commandRegistry["Help.CloseHelpPanel"] = new RelayCommand(CloseHelpPanel);

        // Window & Misc Commands
        _commandRegistry["Normal.SetSmallWindow"] = new RelayCommand(SetSmallWindow);
        _commandRegistry["Normal.SetLargeWindow"] = new RelayCommand(SetLargeWindow);
        _commandRegistry["Normal.ToggleFullscreen"] = new RelayCommand(ToggleFullscreen);
        _commandRegistry["Normal.GoHomeDirectory"] = new RelayCommand(GoHomeDirectory);

        // Command Panel Commands
        _commandRegistry["CommandPanel.CloseCommandPanel"] = new RelayCommand(CloseCommandPanel);
    }

    private void EnsureDefaultKeyBindings()
    {
        var defaults = new Dictionary<string, string>
        {
            // Normal Mode
            ["Normal.ScrollPreviewUp"] = "Up",
            ["Normal.ScrollPreviewDown"] = "Down",
            ["Normal.ScrollPreviewLeft"] = "Left",
            ["Normal.ScrollPreviewRight"] = "Right",
            ["Normal.MoveUp"] = "k",
            ["Normal.MoveDown"] = "j",
            ["Normal.MoveUpDir"] = "h",
            ["Normal.Open"] = "l",
            ["Normal.MoveToTop"] = "gg",
            ["Normal.MoveToBottom"] = "G",
            ["Normal.MoveToScreenTop"] = "H",
            ["Normal.MoveToScreenMiddle"] = "M",
            ["Normal.MoveToScreenBottom"] = "L",
            ["Normal.ScrollHalfPageUp"] = "Ctrl+U",
            ["Normal.ScrollHalfPageDown"] = "Ctrl+D",
            ["Normal.ScrollPageUp"] = "Ctrl+B",
            ["Normal.ScrollPageDown"] = "Ctrl+F",
            ["Normal.EnterJumpMode"] = ";",
            ["Normal.EnterCommandMode"] = "Shift+OemSemicolon",
            ["Normal.FindPrev"] = ",",
            ["Normal.FindNext"] = ".",
            ["Normal.EnterSearchMode"] = "/",
            ["Normal.EnterSearchBackMode"] = "Shift+OemQuestion",
            ["Normal.SearchNext"] = "=",
            ["Normal.SearchPrev"] = "-",
            ["Normal.EnterFindMode"] = "f",
            ["Normal.EnterFindBackMode"] = "F",
            ["Normal.EnterBookmarkMode"] = "m",
            ["Normal.JumpPrev"] = "[",
            ["Normal.JumpNext"] = "]",
            ["Normal.ToggleVisualMode"] = "V",
            ["Normal.ToggleSelection"] = "Space",
            ["Normal.UnselectAll"] = "u",
            ["Normal.InvertSelection"] = "v",
            ["Normal.OpenPopupPreview"] = "t",
            ["Normal.InitiateWorkspaceLink"] = "wl",
            ["Normal.InitiateWorkspaceCreation"] = "ws",
            ["Normal.ToggleWorkspacePanel"] = "wo",
            ["Normal.OpenInExplorer"] = "e",
            ["Normal.ExecuteCurrentItem"] = "o",
            ["Normal.HideApplication"] = "q",
            ["Normal.HandleEscape"] = "Escape",
            ["Normal.SetHidden"] = "zh",
            ["Normal.EnterFilterMode"] = "i",
            ["Normal.SetDotfilesOnly"] = "z.",
            ["Normal.SetReverse"] = "zr",
            ["Normal.SetDirFirst"] = "zd",
            ["Normal.SetInfoPerm"] = "zp",
            ["Normal.SetInfo"] = "zn",
            ["Normal.SetInfoSize"] = "zs",
            ["Normal.SetInfoTime"] = "zt",
            ["Normal.SetInfoSizeTime"] = "za",
            ["Normal.SortNatural"] = "sn",
            ["Normal.SortSize"] = "ss",
            ["Normal.SortTime"] = "st",
            ["Normal.SortAtime"] = "sa",
            ["Normal.SortBtime"] = "sb",
            ["Normal.SortCtime"] = "sc",
            ["Normal.SortExt"] = "se",
            ["Normal.YankSelection"] = "y",
            ["Normal.CutSelection"] = "x",
            ["Normal.PasteSelection"] = "p",
            ["Normal.CreateLinkSelection"] = "Ctrl+L",
            ["Normal.ShowYankHistory"] = "P",
            ["Normal.RequestDelete"] = "Delete,D",
            ["Normal.InitiateRename"] = "r",
            ["Normal.RefreshAsync"] = "F5",
            ["Normal.ClearActiveClipboard"] = "c",
            ["Normal.InitiateNewFile"] = "n",
            ["Normal.InitiateMakeDirectory"] = "N",
            ["Normal.OpenCommandPanel"] = "zz",

            // Visual Mode
            ["Visual.MoveUp"] = "Up,k",
            ["Visual.MoveDown"] = "Down,j",
            ["Visual.ToggleVisualMode"] = "V",
            ["Visual.ExitVisualMode"] = "Escape",
            ["Visual.ToggleSelection"] = "Space",
            ["Visual.UnselectAll"] = "u",
            ["Visual.YankSelection"] = "y",
            ["Visual.CutSelection"] = "x",
            ["Visual.RequestDelete"] = "Delete,D",
            ["Visual.VisualChange"] = "o",

            // Workspace Mode
            ["Workspace.SwitchWorkspaceFocus"] = "Tab",

            // Jump Mode
            ["Jump.SelectPrevBookmark"] = "Up,k",
            ["Jump.SelectNextBookmark"] = "Down,j",
            ["Jump.JumpToSelectedBookmark"] = "Enter,Return",
            ["Jump.DeleteSelectedBookmark"] = "Delete,c",
            ["Jump.ExitJumpMode"] = "Escape",

            // Yank History Mode
            ["YankHistory.HideYankHistory"] = "Escape",
            ["YankHistory.DeleteSelectedYankHistoryItem"] = "c",
            ["YankHistory.ConfirmYankHistorySelection"] = "Enter,Return,Space",
            ["YankHistory.SelectNextYankHistory"] = "j,Down",
            ["YankHistory.SelectPrevYankHistory"] = "k,Up",

            // Dialog Mode
            ["Dialog.ConfirmDialog"] = "y",
            ["Dialog.CancelDialog"] = "n",
            ["Dialog.CancelDialogKeepSelection"] = "Escape",

            // Help Mode
            ["Help.ScrollDown"] = "j,Down",
            ["Help.ScrollUp"] = "k,Up",
            ["Help.CloseHelpPanel"] = "q,Escape",

            // Window & Misc
            ["Normal.SetSmallWindow"] = "Alt+D1,Alt+NumPad1",
            ["Normal.SetLargeWindow"] = "Alt+D2,Alt+NumPad2",
            ["Normal.ToggleFullscreen"] = "F11",
            ["Normal.GoHomeDirectory"] = "gh",

            // Command Panel Mode
            ["CommandPanel.CloseCommandPanel"] = "Escape"
        };

        foreach (var kvp in defaults)
        {
            if (!_configService.Current.KeyBindings.ContainsKey(kvp.Key))
            {
                _configService.Current.KeyBindings[kvp.Key] = kvp.Value;
            }
        }
        _configService.Save();
    }

    private void RegisterKeyBindings()
    {
        InitializeCommandRegistry();
        EnsureDefaultKeyBindings();
        KeyBindingItems.Clear();

        foreach (var kvp in _configService.Current.KeyBindings)
        {
            var actionKey = kvp.Key;
            var keySequenceStr = kvp.Value;
            
            var parts = actionKey.Split('.', 2);
            if (parts.Length != 2) continue;
            
            if (!Enum.TryParse<InputMode>(parts[0], out var mode)) continue;
            var commandName = parts[1];

            if (_commandRegistry.TryGetValue(actionKey, out var command))
            {
                // Support multiple keys separated by comma
                var keys = keySequenceStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var key in keys)
                {
                    _keyBindingService.RegisterBinding(mode, key.Trim(), command);
                }

                // Add to UI list
                KeyBindingItems.Add(new KeyBindingItem(commandName, keySequenceStr, mode.ToString()));
            }
        }

        // Register Default Handlers (these are not configurable via simple key-action map yet)
        _keyBindingService.RegisterDefaultHandler(InputMode.Workspace, (key) => 
        {
            _ = Task.Run(async () => 
            {
                try
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => 
                    {
                        await WorkspacePanel.HandleInputAsync(key);
                    });
                }
                catch { }
            });
            return Task.FromResult(true);
        });

        _keyBindingService.RegisterDefaultHandler(InputMode.Find, HandleFindKey);
        _keyBindingService.RegisterDefaultHandler(InputMode.Bookmark, HandleBookmarkKey);
        _keyBindingService.RegisterDefaultHandler(InputMode.Jump, HandleJumpKey);
        _keyBindingService.RegisterDefaultHandler(InputMode.CommandPanel, HandleCommandPanelKey);
        _keyBindingService.RegisterDefaultHandler(InputMode.YankHistory, HandleYankHistoryKey);
    }

    private void OpenCommandPanel()
    {
        CommandPanel.Clear();
        
        // Ensure commands are initialized
        if (SelectedPanelCommands.Count == 0)
        {
            InitializePanelCommands();
        }

        var usedShortcuts = new System.Collections.Generic.HashSet<string>();
        
        foreach (var cmdName in SelectedPanelCommands)
        {
            string displayName = GetCommandDisplayName(cmdName);
            string shortcut = "";

            // Try to find a shortcut from the name
            foreach (char c in displayName)
            {
                string s = c.ToString().ToLower();
                if (char.IsLetterOrDigit(c) && !usedShortcuts.Contains(s))
                {
                    shortcut = s;
                    usedShortcuts.Add(s);
                    break;
                }
            }

            // Fallback if all letters in name are used
            if (string.IsNullOrEmpty(shortcut))
            {
                string allChars = "abcdefghijklmnopqrstuvwxyz0123456789";
                foreach (char c in allChars)
                {
                    string s = c.ToString();
                    if (!usedShortcuts.Contains(s))
                    {
                        shortcut = s;
                        usedShortcuts.Add(s);
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(shortcut))
            {
                CommandPanel.AddCommand(
                    displayName, 
                    shortcut, 
                    GetCommandAction(cmdName)
                );
            }
        }

        IsCommandPanelVisible = true;
        _previousMode = _keyBindingService.CurrentMode;
        _keyBindingService.SetMode(InputMode.CommandPanel);
    }

    private void CloseCommandPanel()
    {
        IsCommandPanelVisible = false;
        _keyBindingService.SetMode(_previousMode);
    }

    private Task<bool> HandleCommandPanelKey(string key)
    {
        ExecuteCommandPanelAction(key);
        return Task.FromResult(true);
    }

    private Task<bool> HandleYankHistoryKey(string key)
    {
        if (int.TryParse(key, out int index) && index >= 1 && index <= 9)
        {
            SelectYankHistoryByIndex(index);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    private void ExecuteCommandPanelAction(string key)
    {
        var cmd = CommandPanel.Commands.FirstOrDefault(c => c.Shortcut == key);
        if (cmd != null && cmd.Command != null && cmd.Command.CanExecute(null))
        {
            CloseCommandPanel();
            cmd.Command.Execute(null);
        }
    }

    [RelayCommand]
    public void SetSmallWindow()
    {
        WindowWidth = SmallWindowWidth;
        WindowHeight = SmallWindowHeight;
        WindowPosition = new PixelPoint(10, 10);
    }

    [RelayCommand]
    public void SetLargeWindow()
    {
        WindowWidth = LargeWindowWidth;
        WindowHeight = LargeWindowHeight;
        WindowPosition = new PixelPoint(10, 10);
    }

    [RelayCommand]
    public void ToggleCompactMode()
    {
        IsCompactMode = !IsCompactMode;
    }

    [RelayCommand]
    public void ToggleFullscreen()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = desktop.MainWindow;
            if (window != null)
            {
                if (window.WindowState == Avalonia.Controls.WindowState.FullScreen)
                {
                    window.WindowState = Avalonia.Controls.WindowState.Normal;
                }
                else
                {
                    window.WindowState = Avalonia.Controls.WindowState.FullScreen;
                }
            }
        }
    }

    public async Task RefreshAsync()
    {
        // 1. Clear caches
        _directorySizeCacheService.Invalidate(CurrentList.CurrentPath);
        
        // 2. Suspend watcher temporarily to avoid conflict
        CurrentList.SuspendWatcher();
        
        // 3. Reload directory
        await CurrentList.LoadDirectoryAsync(CurrentList.CurrentPath);
        
        // 4. Resume watcher (recreates it)
        CurrentList.ResumeWatcher();
        
        // 5. Update status
        UpdateStatusText();
        // _ = CalculateAllDirectoriesSizeAsync();
    }

    [RelayCommand]
    public void TogglePreviewWrap()
    {
        IsPreviewWordWrapEnabled = !IsPreviewWordWrapEnabled;
    }

    [RelayCommand]
    public void QuitApplication()
    {
        Environment.Exit(0);
    }

    [RelayCommand]
    public void HideApplication()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow?.Hide();
        }
    }

    [RelayCommand]
    public void ShowApplication()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = desktop.MainWindow;
            if (window != null)
            {
                window.Show();
                window.WindowState = Avalonia.Controls.WindowState.Normal;
                window.Activate();
                window.Focus();
            }
        }
    }

    public void ProcessWin32Message(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        _globalHotkeyService.ProcessMessage(hwnd, msg, wParam, lParam, ref handled);
    }

    private int _visualAnchorIndex = -1;

    private void ToggleVisualMode()
    {
        if (_keyBindingService.CurrentMode == InputMode.Visual)
        {
            _keyBindingService.SetMode(InputMode.Normal);
            _visualAnchorIndex = -1;
            UpdateStatusText();
        }
        else
        {
            _keyBindingService.SetMode(InputMode.Visual);
            var selected = CurrentList.SelectedItem;
            if (selected != null)
            {
                _visualAnchorIndex = CurrentList.Items.IndexOf(selected);
            }
            CurrentList.ToggleSelection();
            UpdateStatusText();
        }
    }

    private void VisualChange()
    {
        if (_keyBindingService.CurrentMode != InputMode.Visual) return;
        if (_visualAnchorIndex == -1) return;
        
        var selected = CurrentList.SelectedItem;
        if (selected == null) return;

        var currentIndex = CurrentList.Items.IndexOf(selected);
        if (currentIndex == -1) return;

        // Swap anchor and current
        var temp = _visualAnchorIndex;
        _visualAnchorIndex = currentIndex;
        
        // Move cursor to old anchor
        if (temp >= 0 && temp < CurrentList.Items.Count)
        {
            CurrentList.SelectedItem = CurrentList.Items[temp];
        }
    }

    private void SetHomeDirectory()
    {
        ActiveDialog = new ConfirmationDialogViewModel
        {
            Title = "Set Home Directory",
            Message = $"Set current directory as home?\n{CurrentList.CurrentPath}",
            OnConfirm = () =>
            {
                _configService.Current.HomeDirectory = CurrentList.CurrentPath;
                _configService.Save();
                StatusText = "Home directory set to: " + CurrentList.CurrentPath;
                CloseDialog();
            },
            OnCancel = CloseDialog
        };
        
        IsDialogVisible = true;
        _previousMode = _keyBindingService.CurrentMode;
        _keyBindingService.SetMode(InputMode.Dialog);
    }

    private async void GoHomeDirectory()
    {
        string home = _configService.Current.HomeDirectory;
        if (string.IsNullOrEmpty(home) || !Directory.Exists(home))
        {
            // Default to user profile if not set or invalid
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        
        await NavigateToAsync(home);
    }

    private void ExitVisualMode()
    {
        _keyBindingService.SetMode(InputMode.Normal);
        CurrentList.UnselectAll();
        UpdateStatusText();
    }

    private void ToggleSelection()
    {
        CurrentList.ToggleSelection();
        UpdateStatusText();
        if (_keyBindingService.CurrentMode == InputMode.Visual)
        {
            MoveDown();
        }
    }

    private async void UnselectAll()
    {
        bool hasSelection = CurrentList.Items.Any(i => i.IsSelected);

        if (hasSelection)
        {
            CurrentList.UnselectAll();
            if (_keyBindingService.CurrentMode == InputMode.Visual)
            {
                _keyBindingService.SetMode(InputMode.Normal);
            }
            UpdateStatusText();
        }
        else if (_fileOperationsService.CanUndoLastDelete(CurrentList.CurrentPath))
        {
            await _fileOperationsService.UndoLastDeleteAsync();
            
            // Give Windows Shell some time to actually restore the file
            await Task.Delay(500);
            
            await CurrentList.LoadDirectoryAsync(CurrentList.CurrentPath);
        }
    }

    private void InvertSelection()
    {
        CurrentList.InvertSelection();
        UpdateStatusText();
    }

    private void InitiateRename()
    {
        if (!IsFilterLocked) ExitFilterMode(); // Requirement 3.2.b

        var selected = CurrentList.SelectedItem;
        if (selected != null)
        {
            string name = selected.Name;
            string cmd = $":rename {name}";
            
            // Calculate selection
            int start = 8; // ":rename "
            int length = name.Length;
            string ext = Path.GetExtension(name);
            if (!string.IsNullOrEmpty(ext) && name.Length > ext.Length)
            {
                length -= ext.Length;
            }
            
            CommandLine.ActivateWithTextAndSelection(cmd, start, start + length);
        }
    }

    private void InitiateNewFile()
    {
        if (!IsFilterLocked) ExitFilterMode(); // Requirement 3.2.b
        CommandLine.ActivateWithText(":new ");
    }

    private void InitiateMakeDirectory()
    {
        if (!IsFilterLocked) ExitFilterMode(); // Requirement 3.2.b
        CommandLine.ActivateWithText(":mkdir ");
    }

    private async void CopyPathToClipboard()
    {
        var selectedItems = CurrentList.GetSelectedItems().ToList();
        if (selectedItems.Any())
        {
            var text = string.Join(Environment.NewLine, selectedItems.Select(i => i.Path));
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var clipboard = desktop.MainWindow?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(text);
                    StatusText = $"Copied {selectedItems.Count} path(s) to clipboard";
                }
            }
        }
    }

    private void YankSelection()
    {
        var selectedItems = CurrentList.GetSelectedItems().ToList();
        if (selectedItems.Any())
        {
            _clipboardBackup.Clear(); // Clear backup on new yank
            _clipboardFiles.Clear();
            foreach (var item in selectedItems)
            {
                _clipboardFiles.Add(item.Path);
            }
            _isCutOperation = false;
            
            AddToYankHistory(new List<string>(_clipboardFiles));

            if (_keyBindingService.CurrentMode == InputMode.Visual)
            {
                ExitVisualMode();
            }
            UpdateStatusText();
        }
    }

    private void AddToYankHistory(List<string> files)
    {
        // Check for existing duplicate anywhere in the list and remove it
        YankHistoryItem? existingItem = null;
        foreach (var item in YankHistory)
        {
            if (AreFileListsEqual(item.Files, files))
            {
                existingItem = item;
                break;
            }
        }

        if (existingItem != null)
        {
            YankHistory.Remove(existingItem);
        }

        var newItem = new YankHistoryItem { Files = files };
        YankHistory.Insert(0, newItem);

        int limit = _configService.Current.Performance.MaxYankHistory;
        while (YankHistory.Count > limit)
        {
            YankHistory.RemoveAt(YankHistory.Count - 1);
        }

        ReindexYankHistory();

        // Sync to Config
        _configService.Current.YankHistory = YankHistory.Select(x => x.Files).ToList();
        _configService.Save();
    }

    private bool AreFileListsEqual(List<string> list1, List<string> list2)
    {
        if (list1.Count != list2.Count) return false;
        for (int i = 0; i < list1.Count; i++)
        {
            if (list1[i] != list2[i]) return false;
        }
        return true;
    }

    private void ReindexYankHistory()
    {
        for (int i = 0; i < YankHistory.Count; i++)
        {
            YankHistory[i].Index = i + 1;
        }
    }

    private void ShowYankHistory()
    {
        if (YankHistory.Count == 0) return;
        IsYankHistoryVisible = true;
        if (YankHistory.Count > 0)
        {
            SelectedYankHistoryItem = YankHistory[0];
        }
        _keyBindingService.SetMode(InputMode.YankHistory);
    }

    private void HideYankHistory()
    {
        IsYankHistoryVisible = false;
        _keyBindingService.SetMode(InputMode.Normal);
    }

    private void HandleEscape()
    {
        // If we are in Filter Lock Mode
        if (IsFilterLocked)
        {
            // Unlock and Exit Filter Mode completely
            IsFilterLocked = false;
            FilterModeStatus = string.Empty;
            FilterPatternStatus = string.Empty;
            
            CurrentList.ApplyFilter("");
            return;
        }

        if (IsYankHistoryVisible)
        {
            HideYankHistory();
            return;
        }
        
        if (CurrentList.Items.Any(i => i.IsSelected))
        {
            UnselectAll();
            return;
        }
        
        // If nothing else, maybe clear search or filter?
        // For now, do nothing else as per user request to avoid accidental clear
    }

    private void SelectNextYankHistory()
    {
        if (YankHistory.Count == 0) return;
        if (SelectedYankHistoryItem == null)
        {
            SelectedYankHistoryItem = YankHistory[0];
            return;
        }
        int index = YankHistory.IndexOf(SelectedYankHistoryItem);
        if (index < YankHistory.Count - 1)
        {
            SelectedYankHistoryItem = YankHistory[index + 1];
        }
    }

    private void SelectPrevYankHistory()
    {
        if (YankHistory.Count == 0) return;
        if (SelectedYankHistoryItem == null)
        {
            SelectedYankHistoryItem = YankHistory[0];
            return;
        }
        int index = YankHistory.IndexOf(SelectedYankHistoryItem);
        if (index > 0)
        {
            SelectedYankHistoryItem = YankHistory[index - 1];
        }
    }

    private void SelectYankHistoryByIndex(int index)
    {
        // index is 1-based
        if (index > 0 && index <= YankHistory.Count)
        {
            SelectedYankHistoryItem = YankHistory[index - 1];
            ConfirmYankHistorySelection();
        }
    }

    private void ConfirmYankHistorySelection()
    {
        if (SelectedYankHistoryItem != null)
        {
            // Move to top
            var item = SelectedYankHistoryItem;
            int oldIndex = YankHistory.IndexOf(item);
            if (oldIndex > 0)
            {
                YankHistory.Move(oldIndex, 0);
                ReindexYankHistory();
            }

            _clipboardFiles.Clear();
            _clipboardFiles.AddRange(item.Files);
            _isCutOperation = false;
            PasteSelection();
        }
        HideYankHistory();
    }

    private void ClearActiveClipboard()
    {
        _clipboardFiles.Clear();
        _clipboardBackup.Clear();
        _isCutOperation = false;
        UpdateStatusText();
    }

    private void DeleteSelectedYankHistoryItem()
    {
        if (SelectedYankHistoryItem != null)
        {
            int index = YankHistory.IndexOf(SelectedYankHistoryItem);
            YankHistory.Remove(SelectedYankHistoryItem);
            ReindexYankHistory();
            
            // Sync to Config
            _configService.Current.YankHistory = YankHistory.Select(x => x.Files).ToList();
            _configService.Save();
            
            if (YankHistory.Count > 0)
            {
                // Select next item or last item if we deleted the last one
                if (index >= YankHistory.Count) index = YankHistory.Count - 1;
                SelectedYankHistoryItem = YankHistory[index];
            }
            UpdateStatusText();
        }
    }

    private void ClearAllYank()
    {
        YankHistory.Clear();
        // Sync to Config
        _configService.Current.YankHistory.Clear();
        _configService.Save();

        _clipboardFiles.Clear();
        _clipboardBackup.Clear();
        UpdateStatusText();
    }

    private void SetYankLimit(string arg)
    {
        if (int.TryParse(arg, out int limit) && limit > 0)
        {
            _configService.Current.Performance.MaxYankHistory = limit;
            _configService.Save();
            
            // Trim history
            while (YankHistory.Count > limit)
            {
                YankHistory.RemoveAt(YankHistory.Count - 1);
            }
        }
    }


    private void CutSelection()
    {
        var selectedItems = CurrentList.GetSelectedItems().ToList();
        if (selectedItems.Any())
        {
            // Backup current clipboard if it's a yank operation
            if (!_isCutOperation && !_isLinkOperation && _clipboardFiles.Count > 0)
            {
                _clipboardBackup.Clear();
                _clipboardBackup.AddRange(_clipboardFiles);
            }

            _clipboardFiles.Clear();
            foreach (var item in selectedItems)
            {
                _clipboardFiles.Add(item.Path);
            }
            _isCutOperation = true;
            _isLinkOperation = false;

            if (_keyBindingService.CurrentMode == InputMode.Visual)
            {
                ExitVisualMode();
            }
            UpdateStatusText();
        }
    }

    private void CreateLinkSelection()
    {
        var selectedItems = CurrentList.GetSelectedItems().ToList();
        if (selectedItems.Any())
        {
            // Backup current clipboard if it's a yank operation
            if (!_isCutOperation && !_isLinkOperation && _clipboardFiles.Count > 0)
            {
                _clipboardBackup.Clear();
                _clipboardBackup.AddRange(_clipboardFiles);
            }

            _clipboardFiles.Clear();
            foreach (var item in selectedItems)
            {
                _clipboardFiles.Add(item.Path);
            }
            _isCutOperation = false;
            _isLinkOperation = true;

            if (_keyBindingService.CurrentMode == InputMode.Visual)
            {
                ExitVisualMode();
            }
            UpdateStatusText();
        }
    }

    private async void PasteSelection()
    {
        ExitFilterMode(); // Requirement 3.2.b

        if (_clipboardFiles.Count == 0) return;

        var destDir = CurrentList.CurrentPath;
        
        try 
        {
            if (_isLinkOperation)
            {
                await _fileOperationsService.CreateShortcutAsync(_clipboardFiles, destDir);
                _clipboardFiles.Clear();
                _isLinkOperation = false;
                
                // Restore backup if available
                if (_clipboardBackup.Count > 0)
                {
                    _clipboardFiles.AddRange(_clipboardBackup);
                    _clipboardBackup.Clear();
                    _isCutOperation = false; // Restored items are yanked
                }
            }
            else if (_isCutOperation)
            {
                await _fileOperationsService.MoveAsync(_clipboardFiles, destDir);
                _clipboardFiles.Clear();
                
                // Restore backup if available
                if (_clipboardBackup.Count > 0)
                {
                    _clipboardFiles.AddRange(_clipboardBackup);
                    _clipboardBackup.Clear();
                    _isCutOperation = false; // Restored items are yanked
                }
                else
                {
                    _isCutOperation = false;
                }
            }
            else
            {
                await _fileOperationsService.CopyAsync(_clipboardFiles, destDir);
            }
            
            await CurrentList.LoadDirectoryAsync(CurrentList.CurrentPath);
            UpdateStatusText();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error pasting: {ex.Message}");
        }
    }

    private void ClearClipboard()
    {
        if (_clipboardFiles.Count > 0)
        {
            _clipboardFiles.Clear();
            _clipboardBackup.Clear();
            _isCutOperation = false;
            _isLinkOperation = false;
            UpdateStatusText();
        }
        else
        {
            // If clipboard is empty, maybe clear selection?
            CurrentList.UnselectAll();
            UpdateStatusText();
        }
    }

    private InputMode _previousMode;

    private void RequestDelete()
    {
        var selectedItems = CurrentList.GetSelectedItems().ToList();
        int count = selectedItems.Count;
        string message;
        
        if (count == 0)
        {
            // No selection, delete current item
            if (CurrentList.SelectedItem == null) return;
            selectedItems.Add(CurrentList.SelectedItem);
            count = 1;
        }

        // Construct detailed message
        var folders = selectedItems.Where(i => i.Type == FileType.Directory).Select(i => i.Name).ToList();
        var files = selectedItems.Where(i => i.Type != FileType.Directory).Select(i => i.Name).ToList();

        var sb = new StringBuilder();
        sb.Append($"Are you sure you want to move {count} items to Recycle Bin?");
        
        var details = new List<string>();
        if (folders.Count > 0)
        {
            string label = folders.Count == 1 ? "folder" : "folders";
            details.Add($"{folders.Count} {label}: {string.Join(", ", folders)}");
        }
        if (files.Count > 0)
        {
            string label = files.Count == 1 ? "file" : "files";
            details.Add($"{files.Count} {label}: {string.Join(", ", files)}");
        }

        if (details.Count > 0)
        {
            sb.AppendLine();
            sb.Append(string.Join("\n", details));
        }
        
        message = sb.ToString();

        ActiveDialog = new ConfirmationDialogViewModel
        {
            Title = "Delete Confirmation",
            Message = message,
            OnConfirm = async () => 
            {
                CloseDialog();
                try
                {
                    // Clear preview to release file locks
                    PreviewContent = "Preview";
                    // Give a small delay for handles to be released (especially for LibVLC)
                    await Task.Delay(100);

                    // Capture index before deletion to restore focus to the next item
                    int selectedIndex = -1;
                    if (CurrentList.SelectedItem != null)
                    {
                        selectedIndex = CurrentList.Items.IndexOf(CurrentList.SelectedItem);
                    }

                    var paths = selectedItems.Select(i => i.Path).ToList();
                    await _fileOperationsService.DeleteAsync(paths); // Defaults to recycle bin
                    await CurrentList.LoadDirectoryAsync(CurrentList.CurrentPath);
                    
                    // Restore selection logic:
                    // If we deleted the item at index N, the item that was at N+1 is now at N.
                    // So keeping the same index effectively selects the next item.
                    // We only need to clamp if we deleted the last item(s).
                    if (CurrentList.Items.Count > 0)
                    {
                        if (selectedIndex >= CurrentList.Items.Count)
                        {
                            selectedIndex = CurrentList.Items.Count - 1;
                        }
                        
                        if (selectedIndex >= 0)
                        {
                            CurrentList.SelectedItem = CurrentList.Items[selectedIndex];
                        }
                        else
                        {
                            // Fallback to first item if something weird happened
                            CurrentList.SelectedItem = CurrentList.Items[0];
                        }
                    }

                    if (_keyBindingService.CurrentMode == InputMode.Visual)
                    {
                        ExitVisualMode();
                    }
                    StatusText = $"Moved {count} items to Recycle Bin";
                }
                catch (Exception ex)
                {
                    StatusText = $"Error: {ex.Message}";
                }
            },
            OnCancel = () => 
            {
                CloseDialog();
                if (_keyBindingService.CurrentMode == InputMode.Visual)
                {
                    UnselectAll();
                }
                else
                {
                    CurrentList.UnselectAll();
                }
                StatusText = "Delete cancelled";
            }
        };
        
        IsDialogVisible = true;
        _previousMode = _keyBindingService.CurrentMode;
        _keyBindingService.SetMode(InputMode.Dialog);
    }

    private void ConfirmDialog()
    {
        ActiveDialog?.OnConfirm?.Invoke();
    }

    private void CancelDialog()
    {
        ActiveDialog?.OnCancel?.Invoke();
    }

    private void CancelDialogKeepSelection()
    {
        CloseDialog();
        StatusText = "Delete cancelled";
    }

    private void CloseDialog()
    {
        IsDialogVisible = false;
        ActiveDialog = null;
        _keyBindingService.SetMode(_previousMode);
    }

    private void EnterCommandMode()
    {
        CommandLine.Activate();
        // We need to switch input mode in KeyBindingService, but for now we handle it via UI focus
    }

    private string _lastFilterString = "";

    public string FilterPrompt => $"{_configService.Current.FilterMethod.ToString().ToLower()} filter ";

    private void EnterFilterMode()
    {
        EnterFilterMode(_configService.Current.FilterMethod);
    }

    private void EnterFilterMode(FilterMethod method)
    {
        // Fix: Ensure locked state is cleared when entering edit mode
        if (IsFilterLocked)
        {
            IsFilterLocked = false;
            FilterModeStatus = string.Empty;
            FilterPatternStatus = string.Empty;
        }

        _configService.Current.FilterMethod = method;
        _configService.Save();
        OnPropertyChanged(nameof(FilterPrompt));
        
        string prefix = method switch
        {
            FilterMethod.Fuzzy => ":filter fuzzy ",
            FilterMethod.Text => ":filter text ",
            FilterMethod.Glob => ":filter glob ",
            FilterMethod.Regex => ":filter regex ",
            _ => ":filter fuzzy "
        };

        string fullText = prefix + _lastFilterString;
        int selectionStart = prefix.Length;
        int selectionEnd = fullText.Length;

        CommandLine.ActivateWithTextAndSelection(fullText, selectionStart, selectionEnd);
        
        // If we already have a filter applied, re-apply it with new method immediately
        if (!string.IsNullOrEmpty(_lastFilterString))
        {
             CurrentList.ApplyFilter(_lastFilterString);
        }
    }

    private void ExitFilterMode()
    {
        // Handle Editing Mode
        if (CommandLine.IsVisible && CommandLine.Prefix == ": " && CommandLine.CommandText.StartsWith("filter "))
        {
            CommandLine.Deactivate();
            CurrentList.ApplyFilter("");
            _keyBindingService.SetMode(InputMode.Normal);
        }

        // Handle Locked Mode
        if (IsFilterLocked)
        {
            IsFilterLocked = false;
            FilterModeStatus = string.Empty;
            FilterPatternStatus = string.Empty;
            // Note: CurrentList.ApplyFilter("") will be called by LoadDirectoryAsync anyway if navigating,
            // but calling it here ensures consistency if ExitFilterMode is called elsewhere.
            CurrentList.ApplyFilter("");
        }
    }

    private void EnterSearchMode()
    {
        EnterSearchMode(_configService.Current.SearchMethod);
    }

    private void EnterSearchMode(FilterMethod method)
    {
        _configService.Current.SearchMethod = method;
        _configService.Save();

        string modeStr = method.ToString().ToLower();
        string lastPattern = CurrentList.LastSearchPattern;
        
        // Prefix is "/: ", CommandText starts with mode + last pattern
        CommandLine.Prefix = "/: ";
        CommandLine.CommandText = $"{modeStr} {lastPattern}";
        
        // Select the pattern part so user can easily overwrite it
        int selectionStart = modeStr.Length + 1;
        int selectionEnd = CommandLine.CommandText.Length;
        
        CommandLine.ActivateWithTextAndSelection(CommandLine.CommandText, selectionStart, selectionEnd);
        // We need to manually set Prefix because ActivateWithTextAndSelection might override it based on text
        CommandLine.Prefix = "/: ";
    }

    private void EnterSearchBackMode()
    {
        EnterSearchBackMode(_configService.Current.SearchMethod);
    }

    private void EnterSearchBackMode(FilterMethod method)
    {
        _configService.Current.SearchMethod = method;
        _configService.Save();

        string modeStr = method.ToString().ToLower();
        string lastPattern = CurrentList.LastSearchPattern;

        // Prefix is "?: ", CommandText starts with mode + last pattern
        CommandLine.Prefix = "?: ";
        CommandLine.CommandText = $"{modeStr} {lastPattern}";
        
        // Select the pattern part so user can easily overwrite it
        int selectionStart = modeStr.Length + 1;
        int selectionEnd = CommandLine.CommandText.Length;

        CommandLine.ActivateWithTextAndSelection(CommandLine.CommandText, selectionStart, selectionEnd);
        // We need to manually set Prefix because ActivateWithTextAndSelection might override it based on text
        CommandLine.Prefix = "?: ";
    }

    private void SearchNext()
    {
        if (!CurrentList.HasSearchPattern)
        {
            StatusText = "No search pattern";
            return;
        }
        
        if (!CurrentList.SearchNext())
        {
            StatusText = "Pattern not found";
        }
    }

    private void SearchPrev()
    {
        if (!CurrentList.HasSearchPattern)
        {
            StatusText = "No search pattern";
            return;
        }

        if (!CurrentList.SearchPrev())
        {
            StatusText = "Pattern not found";
        }
    }

    private string _lastFindChar = string.Empty;
    private bool _lastFindReverse = false;
    private bool _isFindBack = false;

    private void EnterFindBackMode()
    {
        _isFindBack = true;
        _keyBindingService.SetMode(InputMode.Find);
    }

    private void FindNext()
    {
        if (string.IsNullOrEmpty(_lastFindChar))
        {
            StatusText = "No find character";
            return;
        }

        bool found;
        if (_lastFindReverse)
            found = CurrentList.JumpToPrev(_lastFindChar);
        else
            found = CurrentList.JumpToNext(_lastFindChar);
            
        if (!found)
        {
            StatusText = $"Character '{_lastFindChar}' not found";
        }
    }

    private void FindPrev()
    {
        if (string.IsNullOrEmpty(_lastFindChar))
        {
            StatusText = "No find character";
            return;
        }

        bool found;
        if (_lastFindReverse)
            found = CurrentList.JumpToNext(_lastFindChar);
        else
            found = CurrentList.JumpToPrev(_lastFindChar);
            
        if (!found)
        {
            StatusText = $"Character '{_lastFindChar}' not found";
        }
    }

    private void EnterFindMode()
    {
        _isFindBack = false;
        _keyBindingService.SetMode(InputMode.Find);
        // TODO: Show some indicator that we are in Find mode?
    }

    private void EnterBookmarkMode()
    {
        CommandLine.ActivateWithText("mark-save ");
    }

    private void OnConfigChanged(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Reload Bookmarks
            if (IsBookmarkListVisible)
            {
                BookmarkItems.Clear();
                var bookmarks = _configService.Current.Bookmarks;
                foreach (var kvp in bookmarks)
                {
                    BookmarkItems.Add(new Models.BookmarkItem { Key = kvp.Key, Path = kvp.Value });
                }
                if (BookmarkItems.Count > 0)
                {
                    SelectedBookmarkItem = BookmarkItems[0];
                }
            }

            // Reload YankHistory
            YankHistory.Clear();
            if (_configService.Current.YankHistory != null)
            {
                foreach (var files in _configService.Current.YankHistory)
                {
                    YankHistory.Add(new YankHistoryItem { Files = new List<string>(files) });
                }
                ReindexYankHistory();
            }
        });
    }

    private void EnterJumpMode()
    {
        _keyBindingService.SetMode(InputMode.Jump);
        
        // Populate bookmark items for UI
        BookmarkItems.Clear();
        var bookmarks = _configService.Current.Bookmarks;
        foreach (var kvp in bookmarks)
        {
            BookmarkItems.Add(new Models.BookmarkItem { Key = kvp.Key, Path = kvp.Value });
        }
        if (BookmarkItems.Count > 0)
        {
            SelectedBookmarkItem = BookmarkItems[0];
        }
        IsBookmarkListVisible = true;
    }

    private void SelectPrevBookmark()
    {
        if (BookmarkItems.Count == 0) return;
        if (SelectedBookmarkItem == null)
        {
            SelectedBookmarkItem = BookmarkItems[BookmarkItems.Count - 1];
            return;
        }
        int index = BookmarkItems.IndexOf(SelectedBookmarkItem);
        if (index > 0)
        {
            SelectedBookmarkItem = BookmarkItems[index - 1];
        }
    }

    private void SelectNextBookmark()
    {
        if (BookmarkItems.Count == 0) return;
        if (SelectedBookmarkItem == null)
        {
            SelectedBookmarkItem = BookmarkItems[0];
            return;
        }
        int index = BookmarkItems.IndexOf(SelectedBookmarkItem);
        if (index < BookmarkItems.Count - 1)
        {
            SelectedBookmarkItem = BookmarkItems[index + 1];
        }
    }

    private async void JumpToSelectedBookmark()
    {
        if (SelectedBookmarkItem != null)
        {
            IsBookmarkListVisible = false;
            _keyBindingService.SetMode(InputMode.Normal);
            await NavigateToAsync(SelectedBookmarkItem.Path);
        }
    }

    private void DeleteSelectedBookmark()
    {
        if (SelectedBookmarkItem != null)
        {
            string key = SelectedBookmarkItem.Key;
            _configService.Current.Bookmarks.Remove(key);
            _configService.Save();
            
            BookmarkItems.Remove(SelectedBookmarkItem);
            if (BookmarkItems.Count > 0)
            {
                SelectedBookmarkItem = BookmarkItems[0];
            }
            StatusText = $"Bookmark '{key}' deleted";
        }
    }

    private void ExitJumpMode()
    {
        IsBookmarkListVisible = false;
        _keyBindingService.SetMode(InputMode.Normal);
    }

    private Task<bool> HandleFindKey(string key)
    {
        // key is like "A", "B", "D1" (for 1), etc.
        // We need to map Key enum string to char if possible, or just use the first char if it's a letter.
        
        string searchChar = key;
        if (key.Length == 1)
        {
            searchChar = key;
        }
        else if (key.StartsWith("D") && key.Length == 2 && char.IsDigit(key[1]))
        {
            searchChar = key.Substring(1);
        }
        // Handle other keys if necessary
        
        _lastFindChar = searchChar;
        _lastFindReverse = _isFindBack;

        bool found;
        if (_isFindBack)
            found = CurrentList.JumpToPrev(searchChar);
        else
            found = CurrentList.JumpToNext(searchChar);

        if (!found)
        {
            StatusText = $"Character '{searchChar}' not found";
        }

        _keyBindingService.SetMode(InputMode.Normal);
        return Task.FromResult(true);
    }

    private Task<bool> HandleBookmarkKey(string key)
    {
        if (key.Length == 1)
        {
            string k = key.ToLower();
            _configService.Current.Bookmarks[k] = CurrentList.CurrentPath;
            _configService.Save();
            StatusText = $"Bookmark '{k}' saved to {CurrentList.CurrentPath}";
        }
        _keyBindingService.SetMode(InputMode.Normal);
        return Task.FromResult(true);
    }

    private async Task<bool> HandleJumpKey(string key)
    {
        // Handle special jump to last location (;)
        if (key == "OemSemicolon" || key == ";")
        {
             if (!string.IsNullOrEmpty(_lastLocation))
             {
                 IsBookmarkListVisible = false;
                 _keyBindingService.SetMode(InputMode.Normal);
                 await NavigateToAsync(_lastLocation);
                 return true;
             }
             return true; // Handled but no last location
        }

        if (key.Length == 1)
        {
            string k = key.ToLower();
            if (_configService.Current.Bookmarks.TryGetValue(k, out var path))
            {
                IsBookmarkListVisible = false;
                _keyBindingService.SetMode(InputMode.Normal);
                await NavigateToAsync(path);
                return true;
            }
        }
        
        return false;
    }


    [ObservableProperty]
    private bool _isFilterActive = false;

    [ObservableProperty]
    private bool _isFilterLocked = false;

    [ObservableProperty]
    private string _filterModeStatus = string.Empty;

    [ObservableProperty]
    private string _filterPatternStatus = string.Empty;

    private async void OnCommandExecuted(object? sender, string fullCommand)
    {
        if (string.IsNullOrEmpty(fullCommand)) return;

        // Special handling for Filter Lock Mode
        if (fullCommand.StartsWith("filter ") || fullCommand.StartsWith(":filter "))
        {
            // Parse command to get mode and pattern
            string cmd = fullCommand.StartsWith(":") ? fullCommand.Substring(1) : fullCommand;
            var parts = cmd.Split(' ', 2);
            
            if (parts.Length >= 1)
            {
                // Lock the filter
                IsFilterLocked = true;
                
                // Construct status string
                string mode = parts[0].Replace("filter-", ""); // filter-text -> text
                if (mode == "filter") mode = _configService.Current.FilterMethod.ToString().ToLower();
                
                string pattern = parts.Length > 1 ? parts[1] : "";
                FilterModeStatus = $"filter {mode}: ";
                FilterPatternStatus = pattern;
                
                // Hide command line
                CommandLine.Deactivate();
                
                // Switch to Normal mode
                _keyBindingService.SetMode(InputMode.Normal);
                
                // Ensure filter is applied
                _lastFilterString = pattern;
                CurrentList.ApplyFilter(pattern);
                return;
            }
        }


        var commands = fullCommand.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var cmd in commands)
        {
            await ExecuteCommandImpl(cmd.Trim());
        }
        
        if (!CommandLine.IsLocked)
        {
            CommandLine.Deactivate();
        }
    }

    private async Task ExecuteCommandImpl(string fullCommand)
    {
        if (string.IsNullOrEmpty(fullCommand)) return;

        string commandContent = fullCommand;

        // Handle Prefixes
        if (fullCommand.StartsWith("/: "))
        {
            var content = fullCommand.Substring(3);
            var parts = content.Split(' ', 2);
            if (parts.Length > 0 && Enum.TryParse<FilterMethod>(parts[0], true, out var mode))
            {
                string pattern = parts.Length > 1 ? parts[1] : "";
                CurrentList.Search(pattern, false, true, mode);
            }
            return;
        }
        if (fullCommand.StartsWith("?: "))
        {
            var content = fullCommand.Substring(3);
            var parts = content.Split(' ', 2);
            if (parts.Length > 0 && Enum.TryParse<FilterMethod>(parts[0], true, out var mode))
            {
                string pattern = parts.Length > 1 ? parts[1] : "";
                CurrentList.Search(pattern, true, true, mode);
            }
            return;
        }
        if (fullCommand.StartsWith("$: "))
        {
            // Shell command
            // TODO: Implement shell command execution
            StatusText = "Shell command not implemented yet: " + fullCommand.Substring(3);
            return;
        }
        if (fullCommand.StartsWith("!: "))
        {
            // Shell wait command
            StatusText = "Shell wait command not implemented yet: " + fullCommand.Substring(3);
            return;
        }
        if (fullCommand.StartsWith("&: "))
        {
            // Shell async command
            StatusText = "Shell async command not implemented yet: " + fullCommand.Substring(3);
            return;
        }
        if (fullCommand.StartsWith("%: "))
        {
            // Shell pipe command
            StatusText = "Shell pipe command not implemented yet: " + fullCommand.Substring(3);
            return;
        }

        if (fullCommand.StartsWith(": "))
        {
            commandContent = fullCommand.Substring(2);
        }
        else if (fullCommand.StartsWith(":"))
        {
             commandContent = fullCommand.Substring(1);
        }

        if (commandContent.StartsWith("filter "))
        {
            // Filter confirmed - Lock Mode
            // Format: "filter <mode> <pattern>"
            
            var parts = commandContent.Split(' ', 3);
            string modeStr = "fuzzy";
            string pattern = "";
            FilterMethod? filterMode = null;

            if (parts.Length > 1)
            {
                modeStr = parts[1];
                if (Enum.TryParse<FilterMethod>(modeStr, true, out var parsedMode))
                {
                    filterMode = parsedMode;
                    _configService.Current.FilterMethod = parsedMode;
                    _configService.Save();
                }
            }

            if (parts.Length > 2)
            {
                pattern = parts[2];
            }

            // Lock Filter
            CommandLine.Deactivate(); // Close command line
            _keyBindingService.SetMode(InputMode.Normal);
            
            // Ensure filter is applied
            _lastFilterString = pattern;
            CurrentList.ApplyFilter(pattern, filterMode);
            
            if (!string.IsNullOrEmpty(pattern))
            {
                IsFilterActive = true;
                IsFilterLocked = true;
                FilterModeStatus = $"filter {modeStr}: ";
                FilterPatternStatus = pattern;
            }
            else
            {
                IsFilterActive = false;
                IsFilterLocked = false; // Don't lock if empty? Or lock empty? User said "lock on Enter".
                // If empty pattern, maybe we shouldn't lock or show status?
                // But user might want to lock "show all".
                // Let's assume lock.
                IsFilterLocked = true;
                FilterModeStatus = $"filter {modeStr}: ";
                FilterPatternStatus = "";
            }

            if (CurrentList.SelectedItem == null && CurrentList.Items.Count > 0)
            {
                CurrentList.SelectedItem = CurrentList.Items[0];
            }
            return;
        }

        var command = commandContent;
        
        if (command.StartsWith("mark-save "))
        {
            string key = command.Substring("mark-save ".Length).Trim();
            if (!string.IsNullOrEmpty(key))
            {
                string k = key.ToLower();
                string path = CurrentList.CurrentPath;

                // Remove existing bookmarks pointing to the same path to ensure uniqueness
                var keysToRemove = _configService.Current.Bookmarks
                    .Where(x => string.Equals(x.Value, path, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Key)
                    .ToList();

                foreach (var oldKey in keysToRemove)
                {
                    _configService.Current.Bookmarks.Remove(oldKey);
                }

                _configService.Current.Bookmarks[k] = path;
                _configService.Save();
                StatusText = $"Bookmark '{k}' saved to {path}";
            }
            return;
        }
        
        // Handle commands that don't take arguments but might have trailing spaces
        // This fixes the issue where ":settings " (with space) fails to execute
        var commandTrimmed = command.Trim();

        if (commandTrimmed == "quit" || commandTrimmed == "q")
        {
            Environment.Exit(0);
        }
        else if (commandTrimmed == "mark-load")
        {
            EnterJumpMode();
            return;
        }
        else if (command.StartsWith("set-sortby "))
        {
            var arg = command.Substring(11).Trim();
            if (Enum.TryParse<SortType>(arg, true, out var sortType))
            {
                _configService.Current.SortBy = sortType;
                _configService.Save();
                OnPropertyChanged(nameof(Config));
                ParentList.SortItems();
                CurrentList.SortItems();
                ChildList.SortItems();
            }
        }
        else if (commandTrimmed == "set-dirfirst")
        {
            _configService.Current.DirFirst = true;
            _configService.Save();
            OnPropertyChanged(nameof(Config));
            ParentList.SortItems();
            CurrentList.SortItems();
            ChildList.SortItems();
        }
        else if (commandTrimmed == "set-nodirfirst")
        {
            _configService.Current.DirFirst = false;
            _configService.Save();
            OnPropertyChanged(nameof(Config));
            ParentList.SortItems();
            CurrentList.SortItems();
            ChildList.SortItems();
        }
        else if (commandTrimmed == "set-dirfirst!")
        {
            _configService.Current.DirFirst = !_configService.Current.DirFirst;
            _configService.Save();
            OnPropertyChanged(nameof(Config));
            ParentList.SortItems();
            CurrentList.SortItems();
            ChildList.SortItems();
        }
        else if (command.StartsWith("set-info "))
        {
            var arg = command.Substring(9).Trim();
            var newInfo = new System.Collections.ObjectModel.ObservableCollection<InfoType>();
            if (!string.IsNullOrEmpty(arg))
            {
                var parts = arg.Split(':');
                foreach (var part in parts)
                {
                    if (Enum.TryParse<InfoType>(part, true, out var infoType))
                    {
                        newInfo.Add(infoType);
                    }
                }
            }
            _configService.Current.Info = newInfo;
            _configService.Save();
            OnPropertyChanged(nameof(Config));
            ParentList.SortItems();
            CurrentList.SortItems();
            ChildList.SortItems();
        }
        else if (commandTrimmed == "set-info")
        {
             _configService.Current.Info = new System.Collections.ObjectModel.ObservableCollection<InfoType>();
             _configService.Save();
             OnPropertyChanged(nameof(Config));
             ParentList.SortItems();
             CurrentList.SortItems();
             ChildList.SortItems();
        }
        else if (command.StartsWith("set-timefmt "))
        {
            // lf uses timefmt for status line, but we can use it for info column too or separate
            // lf has infotimefmtnew and infotimefmtold
            // Let's support setting both or just one generic
            // For now, let's assume user wants to set the 'new' format which is most visible
            var arg = command.Substring(12).Trim();
            _configService.Current.InfoTimeFormatNew = arg;
            _configService.Save();
            OnPropertyChanged(nameof(Config));
        }
        else if (command.StartsWith("set-infotimefmtnew "))
        {
            var arg = command.Substring(19).Trim();
            _configService.Current.InfoTimeFormatNew = arg;
            _configService.Save();
            OnPropertyChanged(nameof(Config));
        }
        else if (command.StartsWith("set-infotimefmtold "))
        {
            var arg = command.Substring(19).Trim();
            _configService.Current.InfoTimeFormatOld = arg;
            _configService.Save();
            OnPropertyChanged(nameof(Config));
        }
        else if (command.StartsWith("set-info_font_size "))
        {
            if (double.TryParse(command.Substring(19).Trim(), out double size))
            {
                InfoFontSize = size;
            }
        }
        else if (commandTrimmed == "set-hidden!")
        {
            // zh logic:
            // If currently showing ALL (Dot=True AND Attr=True), then toggle to NONE (Dot=False, Attr=False).
            // Otherwise (if showing None, or showing only Dot), toggle to ALL (Dot=True, Attr=True).
            
            if (_configService.Current.ShowHidden && _configService.Current.ShowSystemHidden)
            {
                // Currently ALL -> Go to NONE
                _configService.Current.ShowHidden = false;
                _configService.Current.ShowSystemHidden = false;
            }
            else
            {
                // Currently NONE or DOT-ONLY -> Go to ALL
                _configService.Current.ShowHidden = true;
                _configService.Current.ShowSystemHidden = true;
            }
            
            _configService.Save();
            ParentList.RefreshFilter();
            CurrentList.RefreshFilter();
            ChildList.RefreshFilter();
        }
        else if (commandTrimmed == "set-anchorfind")
        {
            _configService.Current.AnchorFind = !_configService.Current.AnchorFind;
            _configService.Save();
            StatusText = $"anchorfind: {_configService.Current.AnchorFind}";
        }
        else if (commandTrimmed == "set-dotfilesonly!")
        {
            // z. logic:
            // If currently showing DOT-ONLY (Dot=True AND Attr=False), then toggle to NONE (Dot=False, Attr=False).
            // Otherwise (if showing None, or showing ALL), toggle to DOT-ONLY (Dot=True, Attr=False).

            if (_configService.Current.ShowHidden && !_configService.Current.ShowSystemHidden)
            {
                // Currently DOT-ONLY -> Go to NONE
                _configService.Current.ShowHidden = false;
                _configService.Current.ShowSystemHidden = false;
            }
            else
            {
                // Currently NONE or ALL -> Go to DOT-ONLY
                _configService.Current.ShowHidden = true;
                _configService.Current.ShowSystemHidden = false;
            }

            _configService.Save();
            ParentList.RefreshFilter();
            CurrentList.RefreshFilter();
            ChildList.RefreshFilter();
        }
        else if (command.StartsWith("set-filtermethod "))
        {
            var arg = command.Substring(17).Trim();

            if (Enum.TryParse<FilterMethod>(arg, true, out var method))
            {
                _configService.Current.FilterMethod = method;
                _configService.Save();
                OnPropertyChanged(nameof(FilterPrompt)); // Update UI
                
                // Re-apply filter with new method
                ParentList.RefreshFilter();
                CurrentList.RefreshFilter();
                ChildList.RefreshFilter();
                
                StatusText = $"Filter method set to: {method}";

                // If we have an active filter, re-lock the command line so the UI shows the filter status
                if (!string.IsNullOrEmpty(_lastFilterString))
                {
                    CommandLine.ActivateWithText(FilterPrompt + _lastFilterString);
                    CommandLine.Lock();
                    // Ensure focus remains on the list, not the command line
                    _keyBindingService.SetMode(InputMode.Normal);
                }
            }
            else
            {
                StatusText = $"Invalid filter method: {arg}";
            }
        }
        else if (command.StartsWith("set-searchmethod "))
        {
            var arg = command.Substring(17).Trim();

            if (Enum.TryParse<FilterMethod>(arg, true, out var method))
            {
                _configService.Current.SearchMethod = method;
                _configService.Save();
                StatusText = $"Search method set to: {method}";
            }
            else
            {
                StatusText = $"Invalid search method: {arg}";
            }
        }
        else if (commandTrimmed == "set-reverse!")
        {
            _configService.Current.SortReverse = !_configService.Current.SortReverse;
            _configService.Save();
            OnPropertyChanged(nameof(Config));
            ParentList.SortItems();
            CurrentList.SortItems();
            ChildList.SortItems();
        }
        else if (command.StartsWith("help "))
        {
            var arg = command.Substring(5).Trim();
            if (arg == "key")
            {
                ShowKeyBindingsHelp();
            }
            else if (arg == "command")
            {
                ShowCommandsHelp();
            }
            else 
            {
                ShowKeyBindingsHelp();
            }
        }
        else if (commandTrimmed == "help")
        {
             ShowKeyBindingsHelp();
        }
        else if (commandTrimmed == "clear")
        {
            ClearClipboard();
        }
        else if (commandTrimmed == "settings")
        {
            ToggleSettings();
        }
        else if (commandTrimmed == "compact")
        {
            ToggleCompactMode();
        }
        else if (commandTrimmed == "wrap")
        {
            TogglePreviewWrap();
        }
        else if (commandTrimmed == "line")
        {
            ShowLineNumbers = !ShowLineNumbers;
        }
        else if (commandTrimmed == "preview")
        {
            IsFilePreviewEnabled = !IsFilePreviewEnabled;
            UpdatePreview();
        }
        else if (commandTrimmed == "set-home")
        {
            SetHomeDirectory();
        }
        else if (commandTrimmed == "go-home")
        {
            GoHomeDirectory();
        }
        else if (commandTrimmed == "clear-yank")
        {
            ClearAllYank();
        }
        else if (commandTrimmed == "create-link")
        {
            CreateLinkSelection();
        }
        else if (command.StartsWith("set-yank "))
        {
            var arg = command.Substring(9).Trim();
            SetYankLimit(arg);
        }
        else if (commandTrimmed == "small")
        {
            SetSmallWindow();
        }
        else if (commandTrimmed == "large")
        {
            SetLargeWindow();
        }
        else if (command.StartsWith("link to space: "))
        {
            // Legacy command handling removed or redirected
            InitiateWorkspaceLink();
        }
        else if (command.StartsWith("workspace-create: ") || command.StartsWith("workspace-create "))
        {
            // Redirect to new flow
            InitiateWorkspaceCreation();
        }
        else if (commandTrimmed == "workspace-open")
        {
            OpenWorkspacePanel();
        }
        else if (commandTrimmed == "workspace-link")
        {
            InitiateWorkspaceLink();
        }
        else if (commandTrimmed == "workspace-create")
        {
            InitiateWorkspaceCreation();
        }
        else if (commandTrimmed == "command-panel")
        {
            OpenCommandPanel();
        }
        else if (command.StartsWith("cd "))
        {
            var path = command.Substring(3).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusText = "Argument required for command: cd";
                return;
            }
            if (Directory.Exists(path))
            {
                await NavigateToAsync(path);
            }
        }
        else if (command.StartsWith("rename "))
        {
            var newName = command.Substring(7).Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                StatusText = "Argument required for command: rename";
                return;
            }
            var selected = CurrentList.SelectedItem;
            if (selected != null && !string.IsNullOrWhiteSpace(newName))
            {
                try
                {
                    // Clear preview to release file locks (especially for Video/PDF)
                    PreviewContent = "Preview";
                    await Task.Delay(100); // Give time for controls to release handles

                    // Suspend watcher to prevent race condition where watcher reloads with old selection
                    CurrentList.SuspendWatcher();
                    try
                    {
                        await _fileOperationsService.RenameAsync(selected.Path, newName);
                        
                        // Calculate new path to restore selection
                        var dir = Path.GetDirectoryName(selected.Path);
                        var newPath = Path.Combine(dir ?? "", newName);

                        await CurrentList.LoadDirectoryAsync(CurrentList.CurrentPath, newPath);
                    }
                    finally
                    {
                        CurrentList.ResumeWatcher();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error renaming: {ex.Message}");
                }
            }
        }
        else if (command.StartsWith("mkdir "))
        {
            var dirName = command.Substring(6).Trim();
            if (string.IsNullOrWhiteSpace(dirName))
            {
                StatusText = "Argument required for command: mkdir";
                return;
            }
            if (!string.IsNullOrWhiteSpace(dirName))
            {
                try
                {
                    var path = Path.Combine(CurrentList.CurrentPath, dirName);
                    
                    // Suspend watcher to prevent race condition
                    CurrentList.SuspendWatcher();
                    try
                    {
                        await _fileSystemService.CreateDirectoryAsync(path);
                        await CurrentList.LoadDirectoryAsync(CurrentList.CurrentPath, path);
                    }
                    finally
                    {
                        CurrentList.ResumeWatcher();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error creating directory: {ex.Message}");
                }
            }
        }
        else if (command.StartsWith("new "))
        {
            var fileName = command.Substring(4).Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                StatusText = "Argument required for command: new";
                return;
            }
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                try
                {
                    var path = Path.Combine(CurrentList.CurrentPath, fileName);
                    
                    // Suspend watcher to prevent race condition
                    CurrentList.SuspendWatcher();
                    try
                    {
                        await _fileOperationsService.CreateFileAsync(path);
                        await CurrentList.LoadDirectoryAsync(CurrentList.CurrentPath, path);
                    }
                    finally
                    {
                        CurrentList.ResumeWatcher();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error creating file: {ex.Message}");
                }
            }
        }
        else if (commandTrimmed == "cd")
        {
            StatusText = "Argument required for command: cd";
        }
        else if (commandTrimmed == "rename")
        {
            StatusText = "Argument required for command: rename";
        }
        else if (commandTrimmed == "mkdir")
        {
            StatusText = "Argument required for command: mkdir";
        }
        else if (commandTrimmed == "new")
        {
            StatusText = "Argument required for command: new";
        }
        else if (command.StartsWith("delete") || command.StartsWith("rm"))
        {
            RequestDelete();
        }
        else if (commandTrimmed == "float")
        {
            ToggleFloat();
        }
        else if (commandTrimmed == "icon")
        {
            ToggleIcons();
        }
        else if (commandTrimmed == "dark")
        {
            SetTheme("dark");
        }
        else if (commandTrimmed == "light")
        {
            SetTheme("light");
        }
        else if (commandTrimmed == "system")
        {
            SetTheme("system");
        }
        else if (commandTrimmed == "up") MoveUp();
        else if (commandTrimmed == "down") MoveDown();
        else if (commandTrimmed == "updir") MoveUpDir();
        else if (commandTrimmed == "open") Open();
        else if (commandTrimmed == "top") MoveToTop();
        else if (commandTrimmed == "bottom") MoveToBottom();
        else if (commandTrimmed == "high") MoveToScreenTop();
        else if (commandTrimmed == "middle") MoveToScreenMiddle();
        else if (commandTrimmed == "low") MoveToScreenBottom();
        else if (commandTrimmed == "page-up") ScrollPageUp();
        else if (commandTrimmed == "page-down") ScrollPageDown();
        else if (commandTrimmed == "half-up") ScrollHalfPageUp();
        else if (commandTrimmed == "half-down") ScrollHalfPageDown();
        
        else if (commandTrimmed == "search-next") SearchNext();
        else if (commandTrimmed == "search-prev") SearchPrev();
        else if (commandTrimmed == "find-next") FindNext();
        else if (commandTrimmed == "find-prev") FindPrev();
        else if (commandTrimmed == "jump-next") JumpNext();
        else if (commandTrimmed == "jump-prev") JumpPrev();
        
        else if (commandTrimmed == "visual") ToggleVisualMode();
        else if (commandTrimmed == "select") ToggleSelection();
        else if (commandTrimmed == "unselect") UnselectAll();
        else if (commandTrimmed == "invert") InvertSelection();
        else if (commandTrimmed == "yank") YankSelection();
        else if (commandTrimmed == "cut") CutSelection();
        else if (commandTrimmed == "paste") PasteSelection();
        else if (commandTrimmed == "yank-history") ShowYankHistory();
        else if (commandTrimmed == "clear-clipboard") ClearActiveClipboard();
        else if (commandTrimmed == "execute") ExecuteCurrentItem();
        else if (commandTrimmed == "explorer") OpenInExplorer();
        else if (commandTrimmed == "open-terminal") OpenTerminal();
        else if (commandTrimmed == "popup") await OpenPopupPreview();
        else if (commandTrimmed == "refresh") await RefreshAsync();
        
        else if (commandTrimmed == "scroll-preview-up") ScrollPreviewUp();
        else if (commandTrimmed == "scroll-preview-down") ScrollPreviewDown();
        else if (commandTrimmed == "scroll-preview-left") ScrollPreviewLeft();
        else if (commandTrimmed == "scroll-preview-right") ScrollPreviewRight();
        else
        {
            StatusText = $"Unknown command: {command}";
        }
    }

    [RelayCommand]
    public void SetTheme(string themeName)
    {
        if (themeName.Equals("dark", StringComparison.OrdinalIgnoreCase))
        {
            SelectedTheme = "Dark";
        }
        else if (themeName.Equals("light", StringComparison.OrdinalIgnoreCase))
        {
            SelectedTheme = "Light";
        }
        else if (themeName.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            SelectedTheme = "System";
        }
    }

    [RelayCommand]
    public void ToggleFloat()
    {
        IsFloatEnabled = !IsFloatEnabled;
    }

    [RelayCommand]
    public void ToggleIcons()
    {
        IsIconsVisible = !IsIconsVisible;
    }

    [RelayCommand]
    public void ToggleSettings()
    {
        if (IsSettingsVisible)
        {
            // Closing settings - Save Key Bindings
            foreach (var item in KeyBindingItems)
            {
                var key = $"{item.Description}.{item.CommandName}";
                if (_configService.Current.KeyBindings.ContainsKey(key))
                {
                    _configService.Current.KeyBindings[key] = item.KeySequence;
                }
            }
            _configService.Save();
        }
        IsSettingsVisible = !IsSettingsVisible;
    }

    [RelayCommand]
    public async Task OpenPopupPreview()
    {
        var selected = CurrentList.SelectedItem;
        if (selected == null || selected.Type != Models.FileType.File) return;

        // Exclude archive files from popup preview
        var archiveExtensions = new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab" };
        if (archiveExtensions.Any(ext => selected.Path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        try
        {
            // Stop existing video preview in right column if playing
            if (PreviewContent is VideoPreviewModel videoPreview)
            {
                videoPreview.Stop();
            }

            // Force full preview
            var content = await _previewEngine.GeneratePreviewAsync(selected.Path, PreviewMode.Full);
            
            var window = new PreviewWindow
            {
                PreviewContent = content
            };

            if (window.DataContext is PreviewWindowViewModel vm)
            {
                vm.Title = selected.Name;
                vm.FontFamily = PreviewTextFontFamily;
                vm.FontSize = PreviewFontSize;

                if (content is not VideoPreviewModel)
                {
                    vm.CustomBackgroundColor = RightColumnColor;
                    vm.UseCustomBackground = true;

                    if (content is CodePreviewModel || content is MarkdownPreviewModel)
                    {
                        vm.BorderThickness = new Thickness(1);
                        try
                        {
                            vm.BorderBrush = Brush.Parse(SeparatorColor);
                        }
                        catch
                        {
                            vm.BorderBrush = Brushes.Gray;
                        }
                    }
                    else
                    {
                        vm.BorderThickness = new Thickness(0);
                        vm.BorderBrush = Brushes.Transparent;
                    }
                }
                else
                {
                    vm.UseCustomBackground = false;
                    vm.BorderThickness = new Thickness(0);
                    vm.BorderBrush = Brushes.Transparent;
                }
            }

            window.Show();
        }
        catch (Exception ex)
        {
            // Show error in status bar or log
            StatusText = $"Preview error: {ex.Message}";
        }
    }

    private void OnCommandCancelled(object? sender, EventArgs e)
    {
        // Handle Esc key from CommandLine
        
        // If we are in Filter Lock Mode (Visible + Locked + Prefix is filter )
        if (CommandLine.IsVisible && CommandLine.IsLocked && CommandLine.Prefix == ": " && CommandLine.CommandText.StartsWith("filter "))
        {
            // Unlock and switch back to Filter Input Mode
            CommandLine.Unlock();
            // Re-activate to force focus
            CommandLine.ActivateWithText(":" + CommandLine.CommandText);
            return;
        }

        // If we are in Filter Input Mode (Visible + Unlocked + Prefix is filter )
        if (CommandLine.IsVisible && !CommandLine.IsLocked && CommandLine.Prefix == ": " && CommandLine.CommandText.StartsWith("filter "))
        {
            // Exit Filter Mode completely
            CommandLine.Deactivate();
            CurrentList.ApplyFilter(""); // Clear filter
            _keyBindingService.SetMode(_previousMode);
            return;
        }

        // Clear filter if it was a filter command (Legacy check, maybe not needed if Prefix logic covers it)
        if (CommandLine.CommandText.StartsWith("/"))
        {
            CurrentList.ApplyFilter(string.Empty);
        }
        
        CommandLine.Deactivate();
        _keyBindingService.SetMode(_previousMode);
    }

    private bool CanScrollPreview()
    {
        // Only when right column is preview interface, not subdirectory
        return PreviewContent != null && 
               !(PreviewContent is FileListViewModel) && 
               !(PreviewContent is string);
    }

    private void ScrollPreviewUp()
    {
        if (CanScrollPreview())
        {
            WeakReferenceMessenger.Default.Send(new ScrollPreviewMessage(ScrollDirection.Up));
        }
    }

    private void ScrollPreviewDown()
    {
        if (CanScrollPreview())
        {
            WeakReferenceMessenger.Default.Send(new ScrollPreviewMessage(ScrollDirection.Down));
        }
    }

    private void ScrollPreviewLeft()
    {
        if (CanScrollPreview())
        {
            WeakReferenceMessenger.Default.Send(new ScrollPreviewMessage(ScrollDirection.Left));
        }
    }

    private void ScrollPreviewRight()
    {
        if (CanScrollPreview())
        {
            WeakReferenceMessenger.Default.Send(new ScrollPreviewMessage(ScrollDirection.Right));
        }
    }

    private void MoveUp()
    {
        CurrentList.MoveUp();
        if (_keyBindingService.CurrentMode == InputMode.Visual)
        {
            CurrentList.ToggleSelection();
            UpdateStatusText();
        }
    }

    private void MoveDown()
    {
        CurrentList.MoveDown();
        if (_keyBindingService.CurrentMode == InputMode.Visual)
        {
            CurrentList.ToggleSelection();
            UpdateStatusText();
        }
    }

    private void MoveToTop()
    {
        if (CurrentList.Items.Count > 0)
        {
            CurrentList.SelectedItem = CurrentList.Items[0];
            // TODO: Handle Visual Mode range selection
        }
    }

    private void MoveToBottom()
    {
        if (CurrentList.Items.Count > 0)
        {
            CurrentList.SelectedItem = CurrentList.Items[CurrentList.Items.Count - 1];
            // TODO: Handle Visual Mode range selection
        }
    }

    private void MoveUpDir()
    {
        var currentPath = CurrentList.CurrentPath;
        var parentPath = _fileSystemService.GetParentPath(currentPath);
        if (!string.IsNullOrEmpty(parentPath))
        {
            _ = NavigateToAsync(parentPath, currentPath);
        }
    }

    private void Open()
    {
        var selected = CurrentList.SelectedItem;
        if (selected != null)
        {
            // Check if it is a shortcut to a directory
            if (selected.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var target = ShortcutResolver.Resolve(selected.Path);
                if (!string.IsNullOrEmpty(target) && Directory.Exists(target))
                {
                    _ = NavigateToAsync(target);
                    return;
                }
            }

            // Check if it is an archive we want to enter
            bool isArchive = ArchiveFileSystemHelper.IsArchiveExtension(selected.Path) && File.Exists(selected.Path);

            if (selected.Type == Models.FileType.Directory || isArchive)
            {
                _ = NavigateToAsync(selected.Path);
            }
            else
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(selected.Path) { UseShellExecute = true });
                }
                catch
                {
                    // Error opening file
                }
            }
        }
    }

    private void ApplyColumnRatio(string ratio)
    {
        var parts = ratio.Split(',');
        if (parts.Length == 3 && 
            double.TryParse(parts[0], out double l) && 
            double.TryParse(parts[1], out double m) && 
            double.TryParse(parts[2], out double r))
        {
            LeftColumnWidth = new Avalonia.Controls.GridLength(l, Avalonia.Controls.GridUnitType.Star);
            MiddleColumnWidth = new Avalonia.Controls.GridLength(m, Avalonia.Controls.GridUnitType.Star);
            RightColumnWidth = new Avalonia.Controls.GridLength(r, Avalonia.Controls.GridUnitType.Star);
        }
    }

    private async void JumpPrev()
    {
        if (_jumpListIndex > 0)
        {
            _jumpListIndex--;
            var entry = _jumpList[_jumpListIndex];
            
            _isJumping = true;
            try
            {
                await NavigateToAsync(entry.Directory, entry.SelectedPath);
            }
            finally
            {
                _isJumping = false;
            }
        }
    }

    private async void JumpNext()
    {
        if (_jumpListIndex < _jumpList.Count - 1)
        {
            _jumpListIndex++;
            var entry = _jumpList[_jumpListIndex];
            
            _isJumping = true;
            try
            {
                await NavigateToAsync(entry.Directory, entry.SelectedPath);
            }
            finally
            {
                _isJumping = false;
            }
        }
    }

    public async Task NavigateToAsync(string path, string? selectPath = null)
    {
        // Exit Filter Mode if active (Requirement 3.2.b)
        ExitFilterMode();

        // Check if it is an archive we want to enter
        // We treat archives as directories, so we don't redirect to parent for them
        bool isArchive = ArchiveFileSystemHelper.IsArchiveExtension(path) && File.Exists(path);

        // Handle file navigation: If path is a file, navigate to parent and select the file
        // Exception: If it's an archive, we proceed to load it as a directory
        if (File.Exists(path) && !Directory.Exists(path) && !isArchive)
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                selectPath = path;
                path = parent;
            }
        }

        // --- JumpList Logic Start ---
        if (!_isJumping)
        {
            // If we have a current location, update its selection state in history
            if (_jumpListIndex >= 0 && _jumpListIndex < _jumpList.Count)
            {
                var currentEntry = _jumpList[_jumpListIndex];
                // Only update if the path matches (sanity check)
                if (currentEntry.Directory == CurrentList.CurrentPath)
                {
                    currentEntry.SelectedPath = CurrentList.SelectedItem?.Path;
                }
            }
            
            // Truncate forward history
            if (_jumpListIndex < _jumpList.Count - 1)
            {
                _jumpList.RemoveRange(_jumpListIndex + 1, _jumpList.Count - (_jumpListIndex + 1));
            }
            
            // Add new entry
            _jumpList.Add(new JumpListEntry { Directory = path, SelectedPath = selectPath });
            _jumpListIndex++;
        }
        // --- JumpList Logic End ---

        // Save history for the directory we are leaving
        if (!string.IsNullOrEmpty(CurrentList.CurrentPath))
        {
            _lastLocation = CurrentList.CurrentPath;
            if (CurrentList.SelectedItem != null)
            {
                 _directorySelectionHistory[CurrentList.CurrentPath] = CurrentList.SelectedItem.Path;
            }
        }

        await CurrentList.LoadDirectoryAsync(path);
        
        if (selectPath != null)
        {
            var item = CurrentList.Items.FirstOrDefault(i => i.Path == selectPath);
            if (item != null)
            {
                CurrentList.SelectedItem = item;
            }
        }
        else 
        {
            // Try to restore last selection from history
            bool restored = false;
            if (_directorySelectionHistory.TryGetValue(path, out var lastSelectedPath))
            {
                var item = CurrentList.Items.FirstOrDefault(i => i.Path == lastSelectedPath);
                if (item != null)
                {
                    CurrentList.SelectedItem = item;
                    restored = true;
                }
            }

            if (!restored && CurrentList.Items.Count > 0)
            {
                CurrentList.SelectedItem = CurrentList.Items[0];
            }
        }

        var parentPath = _fileSystemService.GetParentPath(path);
        if (!string.IsNullOrEmpty(parentPath))
        {
            IsRootDirectory = false;
            await ParentList.LoadDirectoryAsync(parentPath);
            var currentDirItem = ParentList.Items.FirstOrDefault(i => i.Path == path);
            if (currentDirItem != null)
            {
                ParentList.SelectedItem = currentDirItem;
            }
        }
        else
        {
            IsRootDirectory = true;
            ParentList.Items.Clear();
        }
    }

    [DllImport("secur32.dll", CharSet = CharSet.Auto)]
    public static extern int GetUserNameEx(int nameFormat, StringBuilder userName, ref uint userNameSize);

    [DllImport("shell32.dll", EntryPoint = "#261", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void GetUserTilePath([MarshalAs(UnmanagedType.LPWStr)] string? username, uint flags, StringBuilder buffer, int bufferSize);

    private async Task LoadUserInfoAsync()
    {
        try
        {
            // 1. Try to get Display Name (e.g. "Bin Luo")
            string displayName = "";
            try
            {
                // NameDisplay = 3
                StringBuilder sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (GetUserNameEx(3, sb, ref size) != 0)
                {
                    displayName = sb.ToString();
                }
            }
            catch { }

            // 2. Try to get User Principal Name (e.g. "email@outlook.com")
            string principalName = "";
            try
            {
                // NameUserPrincipal = 8
                StringBuilder sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (GetUserNameEx(8, sb, ref size) != 0)
                {
                    principalName = sb.ToString();
                }
            }
            catch { }

            // 2.1 Try Registry for Email (IdentityCRL) if P/Invoke failed
            if (string.IsNullOrEmpty(principalName))
            {
                try
                {
                    // Look for Microsoft Account email in Registry
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\IdentityCRL\UserExtendedProperties"))
                    {
                        if (key != null)
                        {
                            var subkeys = key.GetSubKeyNames();
                            foreach (var subkey in subkeys)
                            {
                                // The subkey name is often the email address or contains it
                                if (subkey.Contains("@"))
                                {
                                    principalName = subkey;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // Fallback to WinRT if P/Invoke failed or returned empty
            if (string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(principalName))
            {
                try 
                {
                    var user = await Windows.System.UserProfile.UserInformation.GetDisplayNameAsync();
                    if (!string.IsNullOrWhiteSpace(user) && string.IsNullOrEmpty(displayName))
                    {
                        displayName = user;
                    }

                    var domainName = await Windows.System.UserProfile.UserInformation.GetPrincipalNameAsync();
                    if (!string.IsNullOrWhiteSpace(domainName) && string.IsNullOrEmpty(principalName))
                    {
                        principalName = domainName;
                    }
                }
                catch { }
            }

            // Final Fallbacks
            if (string.IsNullOrEmpty(displayName)) displayName = Environment.UserName;
            if (string.IsNullOrEmpty(principalName)) principalName = System.Security.Principal.WindowsIdentity.GetCurrent().Name;

            // Set Properties
            UserDisplayName = displayName;
            UserAccountName = principalName;
            if (!string.IsNullOrEmpty(displayName))
            {
                UserInitials = displayName.Substring(0, 1).ToUpper();
            }

            // 3. Try to get Avatar
            bool avatarLoaded = false;
            
            // 3.1 Try Registry (HKLM AccountPicture) - Most reliable for local/MSA users on Win10/11
            if (!avatarLoaded)
            {
                try
                {
                    string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
                    if (!string.IsNullOrEmpty(sid))
                    {
                        string regPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\{sid}";
                        using (var key = Registry.LocalMachine.OpenSubKey(regPath))
                        {
                            if (key != null)
                            {
                                // Try to get the largest image
                                string[] imageKeys = { "Image1080", "Image448", "Image200", "Image96", "Image64", "Image32" };
                                foreach (var imgKey in imageKeys)
                                {
                                    var filePath = key.GetValue(imgKey) as string;
                                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                                    {
                                        UserAvatar = new Avalonia.Media.Imaging.Bitmap(filePath);
                                        avatarLoaded = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // 3.2 Try WinRT
            if (!avatarLoaded)
            {
                try 
                {
                    var file = Windows.System.UserProfile.UserInformation.GetAccountPicture(Windows.System.UserProfile.AccountPictureKind.LargeImage);
                    if (file != null)
                    {
                        using var stream = await file.OpenReadAsync();
                        using var netStream = stream.AsStreamForRead();
                        using var ms = new MemoryStream();
                        await netStream.CopyToAsync(ms);
                        ms.Position = 0;
                        UserAvatar = new Avalonia.Media.Imaging.Bitmap(ms);
                        avatarLoaded = true;
                    }
                }
                catch { }
            }

            // 3.3 Try GetUserTilePath (undocumented shell32 #261)
            if (!avatarLoaded)
            {
                try
                {
                    StringBuilder sb = new StringBuilder(1024);
                    GetUserTilePath(null, 0, sb, sb.Capacity);
                    string path = sb.ToString();
                    if (File.Exists(path))
                    {
                        UserAvatar = new Avalonia.Media.Imaging.Bitmap(path);
                        avatarLoaded = true;
                    }
                }
                catch { }
            }

            // 3.4 Try Public Account Pictures with SID
            if (!avatarLoaded)
            {
                try
                {
                    string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
                    if (!string.IsNullOrEmpty(sid))
                    {
                        string publicBase = Path.Combine(Environment.GetEnvironmentVariable("PUBLIC") ?? "C:\\Users\\Public", "AccountPictures");
                        string sidFolder = Path.Combine(publicBase, sid);
                        
                        if (Directory.Exists(sidFolder))
                        {
                            var files = Directory.GetFiles(sidFolder, "*.jpg");
                            // Get the largest file (likely high res)
                            var largest = files.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
                            if (largest != null)
                            {
                                 UserAvatar = new Avalonia.Media.Imaging.Bitmap(largest);
                                 avatarLoaded = true;
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch
        {
            // Absolute Fallback
            UserDisplayName = "User";
            UserAccountName = "Local Account";
        }
    }

    partial void OnSelectedThemeChanged(string value)
    {
        _configService.Current.Appearance.Theme = value;
        _configService.Save();
        ApplyTheme(value);
        
        // Force refresh preview to apply new theme colors to syntax highlighting
        if (PreviewContent is CodePreviewModel || PreviewContent is MarkdownPreviewModel)
        {
            // We need to re-generate the preview. 
            // The easiest way is to re-trigger the selection logic, but we don't have direct access to it here easily without coupling.
            // However, we can just reload the current file if we know it.
            // But PreviewContent doesn't store the file path directly.
            // Let's try to trigger a refresh via the parent list selection if possible, or just accept that the user needs to re-select.
            // BETTER: We can update the highlighting definition in place if we have access to it.
            
            if (PreviewContent is CodePreviewModel codeModel && codeModel.SyntaxHighlighting != null)
            {
                var profile = value == "Dark" || (value == "System" && Application.Current?.ActualThemeVariant == ThemeVariant.Dark) 
                    ? _configService.Current.Appearance.DarkTheme 
                    : _configService.Current.Appearance.LightTheme;
                
                CodeThemeHelper.ApplyTheme(codeModel.SyntaxHighlighting, profile);
                
                // Force a redraw of the editor by toggling something or notifying property changed?
                // AvaloniaEdit might not update colors immediately if the definition object is modified.
                // We might need to re-set the SyntaxHighlighting property.
                var currentHighlighting = codeModel.SyntaxHighlighting;
                codeModel.SyntaxHighlighting = null;
                codeModel.SyntaxHighlighting = currentHighlighting;
            }
             else if (PreviewContent is MarkdownPreviewModel mdModel && mdModel.HighlightingDefinition != null)
            {
                 var profile = value == "Dark" || (value == "System" && Application.Current?.ActualThemeVariant == ThemeVariant.Dark) 
                    ? _configService.Current.Appearance.DarkTheme 
                    : _configService.Current.Appearance.LightTheme;
                
                MarkdownThemeHelper.ApplyTheme(mdModel.HighlightingDefinition, profile);
            }
        }
    }

    private void ApplyTheme(string theme)
    {
        var app = Application.Current;
        if (app == null) return;

        ThemeVariant variant;
        if (theme == "System")
        {
            app.RequestedThemeVariant = ThemeVariant.Default;
            variant = app.ActualThemeVariant;
        }
        else if (theme == "Dark")
        {
            app.RequestedThemeVariant = ThemeVariant.Dark;
            variant = ThemeVariant.Dark;
        }
        else
        {
            app.RequestedThemeVariant = ThemeVariant.Light;
            variant = ThemeVariant.Light;
        }

        var profile = variant == ThemeVariant.Dark ? _configService.Current.Appearance.DarkTheme : _configService.Current.Appearance.LightTheme;

        LeftColumnColor = profile.LeftColumnColor;
        MiddleColumnColor = profile.MiddleColumnColor;
        RightColumnColor = profile.RightColumnColor;
        TopBarBackgroundColor = profile.TopBarBackgroundColor;
        TopBarTextColor = profile.TopBarTextColor;
        StatusBarBackgroundColor = profile.StatusBarBackgroundColor;
        StatusBarTextColor = profile.StatusBarTextColor;
        SeparatorColor = profile.SeparatorColor;
        CommandLineBackgroundColor = profile.CommandLineBackgroundColor;
        CommandLineTextColor = profile.CommandLineTextColor;

        // Migration for CommandLineSelectionBackgroundColor
        if (string.IsNullOrEmpty(profile.CommandLineSelectionBackgroundColor) || 
            (variant == ThemeVariant.Light && profile.CommandLineSelectionBackgroundColor == "#0078D7"))
        {
             profile.CommandLineSelectionBackgroundColor = variant == ThemeVariant.Light ? "#ADD8E6" : "#0078D7";
             _configService.Save();
        }
        CommandLineSelectionBackgroundColor = profile.CommandLineSelectionBackgroundColor;

        // Update Code Highlighting Definitions
        CodeThemeHelper.ApplyThemeToAll(profile);

        // Migration: Darken DialogButtonBackgroundColor
        if (variant == ThemeVariant.Dark && profile.DialogButtonBackgroundColor == "#444444")
        {
             profile.DialogButtonBackgroundColor = "#2D2D2D";
             _configService.Save();
        }
        else if (variant == ThemeVariant.Light && profile.DialogButtonBackgroundColor == "#E0E0E0")
        {
             profile.DialogButtonBackgroundColor = "#CCCCCC";
             _configService.Save();
        }

        // Fallback for missing dialog colors (migration) or incorrect dark colors in light theme
        if (string.IsNullOrEmpty(profile.DialogBackgroundColor) || 
           (variant == ThemeVariant.Light && profile.DialogBackgroundColor == "#333333"))
        {
            if (variant == ThemeVariant.Dark)
            {
                DialogBackgroundColor = "#333333";
                DialogTextColor = "#FFFFFF";
                DialogBorderColor = "#555555";
                DialogButtonBackgroundColor = "#2D2D2D";
                DialogButtonTextColor = "#FFFFFF";
                DialogIndexColor = "#FFFF00";
            }
            else
            {
                DialogBackgroundColor = "#FFFFFF";
                DialogTextColor = "#000000";
                DialogBorderColor = "#AFAEAE";
                DialogButtonBackgroundColor = "#CCCCCC";
                DialogButtonTextColor = "#000000";
                DialogIndexColor = "#005CC5";
            }
            
            // Update config with defaults
            profile.DialogBackgroundColor = DialogBackgroundColor;
            profile.DialogTextColor = DialogTextColor;
            profile.DialogBorderColor = DialogBorderColor;
            profile.DialogButtonBackgroundColor = DialogButtonBackgroundColor;
            profile.DialogButtonTextColor = DialogButtonTextColor;
            profile.DialogIndexColor = DialogIndexColor;
            _configService.Save();
        }
        else
        {
            DialogBackgroundColor = profile.DialogBackgroundColor;
            DialogTextColor = profile.DialogTextColor;
            DialogBorderColor = profile.DialogBorderColor;
            DialogButtonBackgroundColor = profile.DialogButtonBackgroundColor;
            DialogButtonTextColor = profile.DialogButtonTextColor;
            DialogIndexColor = profile.DialogIndexColor;
        }

        LeftColumnFileTextColor = profile.LeftColumnFileTextColor;
        LeftColumnDirectoryTextColor = profile.LeftColumnDirectoryTextColor;
        MiddleColumnFileTextColor = profile.MiddleColumnFileTextColor;
        MiddleColumnDirectoryTextColor = profile.MiddleColumnDirectoryTextColor;
        MiddleColumnSelectedTextColor = profile.MiddleColumnSelectedTextColor;
        SelectedBackgroundColor = profile.SelectedBackgroundColor;
        RightColumnFileTextColor = profile.RightColumnFileTextColor;
        RightColumnDirectoryTextColor = profile.RightColumnDirectoryTextColor;
        FloatingShadowColor = profile.FloatingShadowColor;

        CodeBackgroundColor = profile.CodeBackgroundColor;
        CodeTextColor = profile.CodeTextColor;
        CodeSelectionColor = profile.CodeSelectionColor;
        CodeLineNumberColor = profile.CodeLineNumberColor;

        // Input Colors (Dynamic based on theme variant)
        if (variant == ThemeVariant.Dark)
        {
            InputBackgroundColor = "#333333";
            InputTextColor = "#FFFFFF";
            InputCaretColor = "#FFFFFF";
            InputBorderColor = "#555555";
        }
        else
        {
            InputBackgroundColor = "#FFFFFF";
            InputTextColor = "#000000";
            InputCaretColor = "#000000";
            InputBorderColor = "#CCCCCC";
        }

        if (Enum.TryParse<Avalonia.Media.FontWeight>(profile.MiddleColumnSelectedFontWeight, out var weight))
        {
            MiddleColumnSelectedFontWeight = weight;
        }
        else
        {
            MiddleColumnSelectedFontWeight = Avalonia.Media.FontWeight.Bold;
        }
    }

    partial void OnPreviewFontSizeChanged(double value)
    {
        _configService.Current.Appearance.FontSize = value;
        _configService.Save();
    }

    // Old On...Changed methods removed as they are now handled by Edit... properties

    partial void OnTopBarFontWeightChanged(Avalonia.Media.FontWeight value)
    {
        _configService.Current.Appearance.TopBarFontWeight = value.ToString();
        _configService.Save();
        OnPropertyChanged(nameof(TopBarFontWeightString));
    }

    partial void OnStatusBarFontWeightChanged(Avalonia.Media.FontWeight value)
    {
        _configService.Current.Appearance.StatusBarFontWeight = value.ToString();
        _configService.Save();
        OnPropertyChanged(nameof(StatusBarFontWeightString));
    }

    public string ColumnRatioString
    {
        get => _configService.Current.Appearance.ColumnRatio;
        set
        {
            if (_configService.Current.Appearance.ColumnRatio != value)
            {
                _configService.Current.Appearance.ColumnRatio = value;
                _configService.Save();
                ApplyColumnRatio(value);
                OnPropertyChanged();
            }
        }
    }

    partial void OnSeparatorWidthChanged(double value)
    {
        _configService.Current.Appearance.SeparatorWidth = value;
        _configService.Save();
    }

    partial void OnCommandLineFontSizeChanged(double value)
    {
        _configService.Current.Appearance.CommandLineFontSize = value;
        _configService.Save();
    }

    partial void OnCommandLineFontFamilyChanged(Avalonia.Media.FontFamily value)
    {
        _configService.Current.Appearance.CommandLineFontFamily = value.Name;
        _configService.Save();
    }

    partial void OnMainFontFamilyChanged(Avalonia.Media.FontFamily value)
    {
        _configService.Current.Appearance.FontFamily = value.Name;
        _configService.Save();
    }

    partial void OnMainFontSizeChanged(double value)
    {
        _configService.Current.Appearance.MainFontSize = value;
        _configService.Save();
    }

    partial void OnPreviewTextFontFamilyChanged(Avalonia.Media.FontFamily value)
    {
        _configService.Current.Appearance.PreviewTextFontFamily = value.Name;
        _configService.Save();
    }

    partial void OnFloatingShadowColorChanged(string value)
    {
        OnPropertyChanged(nameof(MiddleColumnBoxShadow));
    }

    partial void OnSmallWindowWidthChanged(double value)
    {
        _configService.Current.Appearance.SmallWindowWidth = value;
        _configService.Save();
    }

    partial void OnSmallWindowHeightChanged(double value)
    {
        _configService.Current.Appearance.SmallWindowHeight = value;
        _configService.Save();
    }

    partial void OnLargeWindowWidthChanged(double value)
    {
        _configService.Current.Appearance.LargeWindowWidth = value;
        _configService.Save();
    }

    partial void OnLargeWindowHeightChanged(double value)
    {
        _configService.Current.Appearance.LargeWindowHeight = value;
        _configService.Save();
    }

    [RelayCommand]
    public void ResetSettings()
    {
        // Reset Shared Settings
        _configService.Current.Appearance.Theme = "System";
        _configService.Current.Appearance.FontSize = 14.0;
        _configService.Current.Appearance.TopBarFontWeight = "Bold";
        _configService.Current.Appearance.StatusBarFontWeight = "Normal";
        _configService.Current.Appearance.SeparatorWidth = 2.0;
        _configService.Current.Appearance.CommandLineFontSize = 14.0;
        _configService.Current.Appearance.CommandLineFontFamily = "Consolas";
        _configService.Current.Appearance.ColumnRatio = "1,2,3";
        _configService.Current.Appearance.ShowIcons = true;

        // Reset Dark Theme
        _configService.Current.Appearance.DarkTheme = new ThemeProfile
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
            CommandLineSelectionBackgroundColor = "#0078D7",
            LeftColumnFileTextColor = "#CCCCCC",
            LeftColumnDirectoryTextColor = "#EEEEEE",
            MiddleColumnFileTextColor = "#FFFFFF",
            MiddleColumnDirectoryTextColor = "#87CEFA",
            MiddleColumnSelectedTextColor = "#FFFF00",
            MiddleColumnSelectedFontWeight = "Bold",
            SelectedBackgroundColor = "#555555",
            RightColumnFileTextColor = "#CCCCCC",
            RightColumnDirectoryTextColor = "#EEEEEE"
        };

        // Reset Light Theme
        _configService.Current.Appearance.LightTheme = new ThemeProfile
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
            CommandLineSelectionBackgroundColor = "#ADD8E6",
            LeftColumnFileTextColor = "#666666",
            LeftColumnDirectoryTextColor = "#333333",
            MiddleColumnFileTextColor = "#000000",
            MiddleColumnDirectoryTextColor = "#00008B",
            MiddleColumnSelectedTextColor = "#000000",
            MiddleColumnSelectedFontWeight = "Bold",
            SelectedBackgroundColor = "#ADD8E6",
            RightColumnFileTextColor = "#666666",
            RightColumnDirectoryTextColor = "#333333"
        };

        _configService.Save();

        // Update Observable Properties
        SelectedTheme = "System";
        PreviewFontSize = 14.0;
        TopBarFontWeight = Avalonia.Media.FontWeight.Bold;
        StatusBarFontWeight = Avalonia.Media.FontWeight.Normal;
        SeparatorWidth = 2.0;
        CommandLineFontSize = 14.0;
        CommandLineFontFamily = new Avalonia.Media.FontFamily("Consolas");
        ColumnRatioString = "1,2,3";
        IsIconsVisible = true;

        // Force refresh of Edit properties
        OnEditingThemeChanged(EditingTheme);
        
        // Apply the reset theme
        ApplyTheme("System");
    }

    private void MoveToScreenTop() => WeakReferenceMessenger.Default.Send(new NavigationMessage(NavigationType.ScreenTop));
    private void MoveToScreenMiddle() => WeakReferenceMessenger.Default.Send(new NavigationMessage(NavigationType.ScreenMiddle));
    private void MoveToScreenBottom() => WeakReferenceMessenger.Default.Send(new NavigationMessage(NavigationType.ScreenBottom));
    private void ScrollPageUp() => WeakReferenceMessenger.Default.Send(new NavigationMessage(NavigationType.PageUp));
    private void ScrollPageDown() => WeakReferenceMessenger.Default.Send(new NavigationMessage(NavigationType.PageDown));
    private void ScrollHalfPageUp() => WeakReferenceMessenger.Default.Send(new NavigationMessage(NavigationType.HalfPageUp));
    private void ScrollHalfPageDown() => WeakReferenceMessenger.Default.Send(new NavigationMessage(NavigationType.HalfPageDown));

    private async void ExecuteCurrentItem()
    {
        var item = CurrentList.SelectedItem;
        if (item == null) return;

        if (item.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var target = ShortcutResolver.Resolve(item.Path);
            
            if (!string.IsNullOrEmpty(target) && (File.Exists(target) || Directory.Exists(target)))
            {
                await NavigateToAsync(target);
            }
        }
    }

    private void ShowKeyBindingsHelp()
    {
        HelpPanel.Title = "Key Bindings";
        HelpPanel.Items.Clear();
        
        // Define Categories
        var categories = new Dictionary<string, List<(string Key, string Desc)>>
        {
            { "Navigation", new List<(string, string)>() },
            { "File Operations", new List<(string, string)>() },
            { "Selection", new List<(string, string)>() },
            { "Search & Filter", new List<(string, string)>() },
            { "Preview & Layout", new List<(string, string)>() },
            { "Workspace", new List<(string, string)>() },
            { "Bookmarks & Jumps", new List<(string, string)>() },
            { "Yank History", new List<(string, string)>() },
            { "Command Panel", new List<(string, string)>() },
            { "Dialogs", new List<(string, string)>() },
            { "Popup Preview", new List<(string, string)>() },
            { "Help", new List<(string, string)>() },
            { "Other", new List<(string, string)>() }
        };

        // Helper to add to category
        void Add(string category, string key, string desc)
        {
            if (categories.ContainsKey(category))
            {
                categories[category].Add((key, desc));
            }
            else
            {
                categories["Other"].Add((key, desc));
            }
        }

        // 1. Normal Mode Bindings
        var normalBindings = _keyBindingService.GetBindings(InputMode.Normal);
        foreach (var kvp in normalBindings)
        {
            string key = kvp.Key;
            string desc = GetDescriptionForKey(key, InputMode.Normal);
            string category = GetCategoryForKey(key, InputMode.Normal);
            Add(category, key, desc);
        }

        // 2. Visual Mode Bindings
        var visualBindings = _keyBindingService.GetBindings(InputMode.Visual);
        foreach (var kvp in visualBindings)
        {
            string key = kvp.Key;
            string desc = GetDescriptionForKey(key, InputMode.Visual);
            Add("Selection", key + " (Visual)", desc);
        }

        // 3. Jump Mode Bindings
        var jumpBindings = _keyBindingService.GetBindings(InputMode.Jump);
        foreach (var kvp in jumpBindings)
        {
            string key = kvp.Key;
            string desc = GetDescriptionForKey(key, InputMode.Jump);
            Add("Bookmarks & Jumps", key + " (Jump Mode)", desc);
        }

        // 4. Yank History Mode Bindings
        var yankBindings = _keyBindingService.GetBindings(InputMode.YankHistory);
        foreach (var kvp in yankBindings)
        {
            string key = kvp.Key;
            string desc = GetDescriptionForKey(key, InputMode.YankHistory);
            Add("Yank History", key + " (History Mode)", desc);
        }

        // 5. Dialog Mode Bindings
        var dialogBindings = _keyBindingService.GetBindings(InputMode.Dialog);
        foreach (var kvp in dialogBindings)
        {
            string key = kvp.Key;
            string desc = GetDescriptionForKey(key, InputMode.Dialog);
            Add("Dialogs", key + " (Dialog)", desc);
        }

        // 6. Workspace Mode Bindings (Implicit & Explicit)
        // Explicit
        var wsBindings = _keyBindingService.GetBindings(InputMode.Workspace);
        foreach (var kvp in wsBindings)
        {
            string key = kvp.Key;
            string desc = GetDescriptionForKey(key, InputMode.Workspace);
            Add("Workspace", key + " (Panel)", desc);
        }
        // Implicit (Handled in WorkspacePanelViewModel)
        Add("Workspace", "j / Down", "Select Next Workspace/Item");
        Add("Workspace", "k / Up", "Select Previous Workspace/Item");
        Add("Workspace", "l", "Focus Item List");
        Add("Workspace", "h", "Focus Workspace List");
        Add("Workspace", "ws", "Create Workspace (w then s)");
        Add("Workspace", "wr", "Rename Workspace (w then r)");
        Add("Workspace", "wd", "Delete Workspace (w then d)");
        Add("Workspace", "c", "Delete Shortcut (Item List)");
        Add("Workspace", "Enter", "Open Item / Confirm");
        Add("Workspace", "o", "Execute Item");
        Add("Workspace", "1-9", "Select/Open Workspace/Item 1-9");

        // 7. Popup Preview (Implicit in PreviewWindow)
        Add("Popup Preview", "h", "Rotate Left (-90°)");
        Add("Popup Preview", "l", "Rotate Right (+90°)");
        Add("Popup Preview", "j / Down", "Scroll Down");
        Add("Popup Preview", "k / Up", "Scroll Up");
        Add("Popup Preview", "Left / Right", "Page Navigation (PDF)");

        // 8. Help Mode
        var helpBindings = _keyBindingService.GetBindings(InputMode.Help);
        foreach (var kvp in helpBindings)
        {
            string key = kvp.Key;
            string desc = GetDescriptionForKey(key, InputMode.Help);
            Add("Help", key + " (Help Panel)", desc);
        }

        // Build the list
        foreach (var category in categories.Keys)
        {
            var list = categories[category];
            if (list.Count > 0)
            {
                // Add Header
                HelpPanel.Items.Add(new HelpItem { Key = $"--- {category} ---", Description = "" });
                
                // Add Items
                foreach (var item in list.OrderBy(x => x.Key))
                {
                    HelpPanel.Items.Add(new HelpItem { Key = item.Key, Description = item.Desc });
                }
            }
        }
        
        if (HelpPanel.Items.Count > 0)
        {
            HelpPanel.SelectedItem = HelpPanel.Items[0];
        }

        IsHelpPanelVisible = true;
        _keyBindingService.SetMode(InputMode.Help);
    }

    private string GetCategoryForKey(string key, InputMode mode)
    {
        if (mode != InputMode.Normal) return "Other";

        return key switch
        {
            "j" or "k" or "h" or "l" or "gg" or "G" or "H" or "M" or "L" or 
            "Up" or "Down" or "Left" or "Right" or 
            "Ctrl+U" or "Ctrl+D" or "Ctrl+B" or "Ctrl+F" or "gh" => "Navigation",

            "y" or "x" or "p" or "d" or "c" or "r" or "Delete" or "D" or 
            "n" or "N" or "Ctrl+L" or "e" or "o" => "File Operations",

            "Space" or "v" or "V" or "u" => "Selection",

            "/" or "?" or "Shift+OemQuestion" or "=" or "-" or 
            "f" or "F" or "," or "." or 
            "i" or "zh" or "z." => "Search & Filter",

            "t" or "zr" or "zd" or "zp" or "zn" or "zs" or "zt" or "za" or 
            "sn" or "ss" or "st" or "sa" or "sb" or "sc" or "se" or 
            "F5" or "Alt+D1" or "Alt+NumPad1" or "Alt+D2" or "Alt+NumPad2" or "F11" => "Preview & Layout",

            "wl" or "ws" or "wo" => "Workspace",

            "m" or "[" or "]" or ";" => "Bookmarks & Jumps",

            "P" => "Yank History",

            ":" or "Shift+OemSemicolon" or "zz" => "Command Panel",

            "q" or "Escape" => "Other",

            _ => "Other"
        };
    }

    private string GetDescriptionForKey(string key, InputMode mode)
    {
        if (mode == InputMode.Normal)
        {
            return GetDescriptionForKey(key); // Use existing method for Normal mode
        }

        return (mode, key) switch
        {
            // Visual Mode
            (InputMode.Visual, "Up") => "Move Up",
            (InputMode.Visual, "Down") => "Move Down",
            (InputMode.Visual, "j") => "Move Down",
            (InputMode.Visual, "k") => "Move Up",
            (InputMode.Visual, "V") => "Exit Visual Mode",
            (InputMode.Visual, "Escape") => "Exit Visual Mode",
            (InputMode.Visual, "Space") => "Toggle Selection",
            (InputMode.Visual, "u") => "Unselect All",
            (InputMode.Visual, "y") => "Yank Selection",
            (InputMode.Visual, "x") => "Cut Selection",
            (InputMode.Visual, "D") => "Delete Selection",
            (InputMode.Visual, "Delete") => "Delete Selection",
            (InputMode.Visual, "o") => "Change Selection End",

            // Jump Mode
            (InputMode.Jump, "Up") => "Select Previous Bookmark",
            (InputMode.Jump, "Down") => "Select Next Bookmark",
            (InputMode.Jump, "k") => "Select Previous Bookmark",
            (InputMode.Jump, "j") => "Select Next Bookmark",
            (InputMode.Jump, "Enter") => "Jump to Selected",
            (InputMode.Jump, "Return") => "Jump to Selected",
            (InputMode.Jump, "Delete") => "Delete Bookmark",
            (InputMode.Jump, "c") => "Delete Bookmark",
            (InputMode.Jump, "Escape") => "Exit Jump Mode",

            // Yank History Mode
            (InputMode.YankHistory, "Escape") => "Close Yank History",
            (InputMode.YankHistory, "c") => "Delete Item",
            (InputMode.YankHistory, "Enter") => "Paste Selected",
            (InputMode.YankHistory, "Return") => "Paste Selected",
            (InputMode.YankHistory, "Space") => "Paste Selected",
            (InputMode.YankHistory, "j") => "Select Next",
            (InputMode.YankHistory, "k") => "Select Previous",
            (InputMode.YankHistory, "Down") => "Select Next",
            (InputMode.YankHistory, "Up") => "Select Previous",
            (InputMode.YankHistory, var k) when int.TryParse(k, out _) => $"Paste Item {k}",

            // Dialog Mode
            (InputMode.Dialog, "y") => "Confirm",
            (InputMode.Dialog, "n") => "Cancel",
            (InputMode.Dialog, "Escape") => "Cancel",

            // Workspace Mode
            (InputMode.Workspace, "Tab") => "Switch Focus",
            (InputMode.Workspace, "Escape") => "Close Workspace Panel",

            // Help Mode
            (InputMode.Help, "j") => "Scroll Down",
            (InputMode.Help, "k") => "Scroll Up",
            (InputMode.Help, "Down") => "Scroll Down",
            (InputMode.Help, "Up") => "Scroll Up",
            (InputMode.Help, "Escape") => "Close Help",
            (InputMode.Help, "q") => "Close Help",

            _ => "Command"
        };
    }

    private string GetDescriptionForKey(string key)
    {
        return key switch
        {
            // Navigation
            "k" => "Move Up",
            "j" => "Move Down",
            "h" => "Move Up Directory",
            "l" => "Open / Enter Directory",
            "gg" => "Go to Top",
            "G" => "Go to Bottom",
            "H" => "Move to Screen Top",
            "M" => "Move to Screen Middle",
            "L" => "Move to Screen Bottom",
            "Ctrl+U" => "Scroll Half Page Up",
            "Ctrl+D" => "Scroll Half Page Down",
            "Ctrl+B" => "Scroll Page Up",
            "Ctrl+F" => "Scroll Page Down",
            "[" => "Jump to Previous Selection",
            "]" => "Jump to Next Selection",
            
            // Preview
            "Up" => "Scroll Preview Up",
            "Down" => "Scroll Preview Down",
            "Left" => "Scroll Preview Left",
            "Right" => "Scroll Preview Right",
            "t" => "Open Popup Preview",

            // Selection
            "Space" => "Toggle Selection",
            "v" => "Invert Selection",
            "V" => "Toggle Visual Mode",
            "u" => "Unselect All",

            // File Operations
            "y" => "Copy (Yank)",
            "x" => "Cut",
            "p" => "Paste",
            "d" => "Cut (Alternative)",
            "c" => "Clear Active Clipboard",
            "r" => "Rename",
            "Delete" => "Delete",
            "D" => "Delete",
            "n" => "Create New File",
            "N" => "Create New Directory",
            "Ctrl+L" => "Create Symbolic Link",
            "P" => "Show Yank History",
            "e" => "Open in Explorer",
            "o" => "Execute Current Item",

            // Search & Filter
            "/" => "Search Mode",
            "?" => "Search Backwards Mode",
            "Shift+OemQuestion" => "Search Backwards Mode",
            "=" => "Search Next",
            "-" => "Search Previous",
            "f" => "Find Mode",
            "F" => "Find Backwards Mode",
            "," => "Find Previous",
            "." => "Find Next",
            "i" => "Enter Filter Mode",
            "zh" => "Toggle Hidden Files",
            "z." => "Toggle Dotfiles Only",

            // Sorting & View
            "zr" => "Toggle Reverse Sort",
            "zd" => "Toggle Directories First",
            "zp" => "Set Info: Permissions",
            "zn" => "Set Info: None",
            "zs" => "Set Info: Size",
            "zt" => "Set Info: Time",
            "za" => "Set Info: Size & Time",
            "sn" => "Sort by Natural",
            "ss" => "Sort by Size",
            "st" => "Sort by Time",
            "sa" => "Sort by Access Time",
            "sb" => "Sort by Birth Time",
            "sc" => "Sort by Change Time",
            "se" => "Sort by Extension",
            "F5" => "Refresh",
            "Alt+D1" => "Set Small Window",
            "Alt+NumPad1" => "Set Small Window",
            "Alt+D2" => "Set Large Window",
            "Alt+NumPad2" => "Set Large Window",
            "F11" => "Toggle Fullscreen",

            // Modes & Panels
            ":" => "Command Mode",
            "Shift+OemSemicolon" => "Command Mode",
            ";" => "Jump Mode",
            "m" => "Bookmark Mode",
            "zz" => "Open Command Panel",
            "wo" => "Toggle Workspace Panel",
            "wl" => "Link to Workspace",
            "ws" => "Create Workspace",
            "gh" => "Go to Home Directory",
            "q" => "Hide Application",
            "Escape" => "Cancel / Close",

            _ => "Command"
        };
    }

    private void ShowCommandsHelp()
    {
        HelpPanel.Title = "Commands";
        HelpPanel.Items.Clear();
        
        foreach (var cmd in AvailablePanelCommands.OrderBy(c => c))
        {
            HelpPanel.Items.Add(new HelpItem { Key = cmd, Description = GetDescriptionForCommand(cmd) });
        }
        
        if (HelpPanel.Items.Count > 0)
        {
            HelpPanel.SelectedItem = HelpPanel.Items[0];
        }

        IsHelpPanelVisible = true;
        _keyBindingService.SetMode(InputMode.Help);
    }

    private string GetDescriptionForCommand(string cmd)
    {
        return cmd switch
        {
            // File Operations
            "copy" => "Copy selected items to clipboard",
            "cut" => "Cut selected items to clipboard",
            "paste" => "Paste items from clipboard",
            "delete" => "Delete selected items (Move to Recycle Bin)",
            "rename" => "Rename current item",
            "create-file" => "Create a new file",
            "create-dir" => "Create a new directory",
            "create-link" => "Create symbolic link",
            "copy-path" => "Copy full path of current item to clipboard",
            "clear-yank" => "Clear yank history",
            "clear-clipboard" => "Clear current clipboard selection",
            "yank-history" => "Show yank history panel",
            
            // Navigation
            "up" => "Move cursor up",
            "down" => "Move cursor down",
            "updir" => "Go to parent directory",
            "open" => "Open selected item / Enter directory",
            "go-home" => "Navigate to home directory",
            "set-home" => "Set current directory as home",
            "top" => "Move cursor to top of list",
            "bottom" => "Move cursor to bottom of list",
            "high" => "Move cursor to top of screen",
            "middle" => "Move cursor to middle of screen",
            "low" => "Move cursor to bottom of screen",
            "page-up" => "Scroll one page up",
            "page-down" => "Scroll one page down",
            "half-up" => "Scroll half page up",
            "half-down" => "Scroll half page down",
            "jump-next" => "Jump to next selected item",
            "jump-prev" => "Jump to previous selected item",
            "mark-load" => "Load bookmark (Jump Mode)",
            
            // Selection
            "select" => "Select current item",
            "unselect" => "Unselect current item",
            "invert" => "Invert selection of current item",
            "visual" => "Toggle Visual Mode",
            
            // Search & Filter
            "search" => "Start search mode",
            "filter" => "Start filter mode",
            "find" => "Start find mode",
            "search-next" => "Go to next search result",
            "search-prev" => "Go to previous search result",
            "find-next" => "Go to next find result",
            "find-prev" => "Go to previous find result",
            
            // Sorting & View Options
            "sort-name" => "Sort by Name",
            "sort-date" => "Sort by Date (Modified Time)",
            "sort-size" => "Sort by Size",
            "sort-ext" => "Sort by Extension",
            "sort-natural" => "Sort Naturally",
            "set-reverse!" => "Toggle Reverse Sort",
            "set-dirfirst!" => "Toggle Directories First",
            "set-hidden!" => "Toggle Hidden Files",
            "set-dotfilesonly!" => "Toggle Dotfiles Only",
            "set-anchorfind" => "Toggle Anchor Find",
            "refresh" => "Refresh current directory",
            "toggle-hidden" => "Toggle Hidden Files (Alias)",
            
            // Display & Layout
            "preview" => "Toggle File Preview",
            "wrap" => "Toggle Text Wrap in Preview",
            "line" => "Toggle Line Numbers in Preview",
            "compact" => "Toggle Compact Mode",
            "float" => "Toggle Float Mode (No Title Bar)",
            "icon" => "Toggle Icons",
            "small" => "Set Small Window Size",
            "large" => "Set Large Window Size",
            "settings" => "Open Settings Panel",
            "dark" => "Set Dark Theme",
            "light" => "Set Light Theme",
            "system" => "Set System Theme",
            "scroll-preview-up" => "Scroll Preview Up",
            "scroll-preview-down" => "Scroll Preview Down",
            "scroll-preview-left" => "Scroll Preview Left",
            "scroll-preview-right" => "Scroll Preview Right",
            "popup" => "Open Popup Preview Window",
            
            // System / External
            "open-terminal" => "Open Terminal in current directory",
            "explorer" => "Open Windows Explorer in current directory",
            "execute" => "Execute current item (Open with default app)",
            "quit" => "Quit Application",
            "help" => "Show Help",
            
            // Workspace
            "workspace-open" => "Open Workspace Panel",
            "workspace-create" => "Create New Workspace",
            "workspace-link" => "Link Current Directory to Workspace",
            
            _ => "Execute " + cmd
        };
    }

    private void CloseHelpPanel()
    {
        IsHelpPanelVisible = false;
        _keyBindingService.SetMode(InputMode.Normal);
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using LfWindows.Services;
using LfWindows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Avalonia;
using Avalonia.VisualTree;
using System;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Linq;
using CommunityToolkit.Mvvm.Messaging;
using LfWindows.Messages;

namespace LfWindows.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Use Tunneling strategy to capture keys before controls (like ListBox) consume them
        this.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        this.Activated += MainWindow_Activated;
        
        // Auto-focus window on mouse enter/move
        this.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble);

        var listBox = this.FindControl<ListBox>("CurrentListBox");
        if (listBox != null)
        {
            listBox.SelectionChanged += CurrentListBox_SelectionChanged;
        }

        var cmdTextBox = this.FindControl<TextBox>("CommandTextBox");
        if (cmdTextBox != null)
        {
            cmdTextBox.GotFocus += (s, e) => 
            { 
                if (DataContext is MainWindowViewModel vm) vm.IsFocusInCommandLine = true; 
            };
        }

        var wsListBox = this.FindControl<ListBox>("WorkspaceListBox");
        if (wsListBox != null)
        {
            wsListBox.GotFocus += (s, e) => 
            { 
                if (DataContext is MainWindowViewModel vm) vm.IsFocusInCommandLine = false; 
            };
        }

        var wsItemsListBox = this.FindControl<ListBox>("WorkspaceItemsListBox");
        if (wsItemsListBox != null)
        {
            wsItemsListBox.SelectionChanged += (s, e) => 
            {
                if (e.AddedItems.Count > 0 && e.AddedItems[0] is object item)
                {
                    wsItemsListBox.ScrollIntoView(item);
                }
            };
        }

        WeakReferenceMessenger.Default.Register<ScrollPreviewMessage>(this, (r, m) =>
        {
            ((MainWindow)r).OnScrollPreview(m);
        });

        WeakReferenceMessenger.Default.Register<FocusRequestMessage>(this, (r, m) =>
        {
            ((MainWindow)r).OnFocusRequest(m);
        });

        WeakReferenceMessenger.Default.Register<NavigationMessage>(this, (r, m) =>
        {
            ((MainWindow)r).OnNavigationRequest(m);
        });

        this.DataContextChanged += OnDataContextChanged;

        // Subscribe to Input Mode changes for auto-IME switching
        if (Application.Current is App app && app.Services != null)
        {
            var keyBindingService = app.Services.GetService<IKeyBindingService>();
            if (keyBindingService != null)
            {
                keyBindingService.ModeChanged += OnInputModeChanged;
            }
        }
    }

    private void OnInputModeChanged(InputMode mode)
    {
        if (mode == InputMode.Normal)
        {
            // Switch to English IME when entering Normal mode
            var handle = this.TryGetPlatformHandle();
            if (handle != null)
            {
                LfWindows.Interop.Imm32.SwitchToEnglish(handle.Handle);
            }
        }
    }

    private DateTime _lastActivationAttempt = DateTime.MinValue;

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        // Debounce activation attempts to avoid spamming Windows API and causing taskbar flashing
        if ((DateTime.Now - _lastActivationAttempt).TotalMilliseconds < 500)
            return;

        var handle = this.TryGetPlatformHandle();
        if (handle != null)
        {
            var foreground = LfWindows.Interop.User32.GetForegroundWindow();
            if (foreground != handle.Handle)
            {
                _lastActivationAttempt = DateTime.Now;
                ForceActivateWindow(handle.Handle, foreground);
            }
        }
    }

    private void ForceActivateWindow(IntPtr hWnd, IntPtr foregroundWnd)
    {
        if (foregroundWnd == IntPtr.Zero)
        {
            Activate();
            return;
        }

        uint foregroundThreadId = LfWindows.Interop.User32.GetWindowThreadProcessId(foregroundWnd, IntPtr.Zero);
        uint currentThreadId = LfWindows.Interop.User32.GetCurrentThreadId();

        if (foregroundThreadId != currentThreadId)
        {
            LfWindows.Interop.User32.AttachThreadInput(foregroundThreadId, currentThreadId, true);
            
            LfWindows.Interop.User32.SetForegroundWindow(hWnd);
            LfWindows.Interop.User32.SetFocus(hWnd);
            
            LfWindows.Interop.User32.AttachThreadInput(foregroundThreadId, currentThreadId, false);
        }
        else
        {
            LfWindows.Interop.User32.SetForegroundWindow(hWnd);
        }
        
        // Ensure window is visible and not minimized
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        
        Activate();
    }

    private void OnNavigationRequest(NavigationMessage message)
    {
        var listBox = this.FindControl<ListBox>("CurrentListBox");
        if (listBox == null) return;
        
        var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer == null) return;

        var items = listBox.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        if (!items.Any()) return;

        int firstVisibleIndex = -1;
        int lastVisibleIndex = -1;
        int firstFullyVisibleIndex = -1;
        int lastFullyVisibleIndex = -1;

        // Find visible range by checking actual visual bounds
        foreach (var item in items)
        {
            var topLeft = item.TranslatePoint(new Point(0, 0), scrollViewer);
            if (topLeft.HasValue)
            {
                double top = topLeft.Value.Y;
                double bottom = top + item.Bounds.Height;
                double viewportHeight = scrollViewer.Viewport.Height;

                int index = listBox.IndexFromContainer(item);
                if (index < 0) continue;

                // Check intersection with viewport (0 to Viewport.Height)
                // Use a small tolerance (1px) to avoid selecting items that are barely visible or just outside
                if (bottom > 1 && top < viewportHeight - 1)
                {
                    if (firstVisibleIndex == -1 || index < firstVisibleIndex) firstVisibleIndex = index;
                    if (lastVisibleIndex == -1 || index > lastVisibleIndex) lastVisibleIndex = index;
                }

                // Check for fully visible items (strict)
                // Allow 1px tolerance for rounding errors
                if (top >= -1 && bottom <= viewportHeight + 1)
                {
                    if (firstFullyVisibleIndex == -1 || index < firstFullyVisibleIndex) firstFullyVisibleIndex = index;
                    if (lastFullyVisibleIndex == -1 || index > lastFullyVisibleIndex) lastFullyVisibleIndex = index;
                }
            }
        }

        // Prefer fully visible items for navigation to avoid scrolling
        // If no item is fully visible (e.g. item larger than viewport), fallback to partially visible
        if (firstFullyVisibleIndex != -1) firstVisibleIndex = firstFullyVisibleIndex;
        if (lastFullyVisibleIndex != -1) lastVisibleIndex = lastFullyVisibleIndex;

        if (firstVisibleIndex == -1 || lastVisibleIndex == -1)
        {
            // Fallback to estimation if visual hit test fails (should rarely happen)
            double itemHeight = 28;
            var container = items.FirstOrDefault();
            if (container != null) itemHeight = container.Bounds.Height;
            if (itemHeight <= 0) itemHeight = 28;

            firstVisibleIndex = (int)(scrollViewer.Offset.Y / itemHeight);
            int visibleCountEst = (int)(scrollViewer.Viewport.Height / itemHeight);
            if (visibleCountEst < 1) visibleCountEst = 1;
            lastVisibleIndex = firstVisibleIndex + visibleCountEst - 1;
        }

        int visibleCount = lastVisibleIndex - firstVisibleIndex + 1;
        int itemCount = listBox.ItemCount;
        int currentIndex = listBox.SelectedIndex;
        int newIndex = currentIndex;

        switch (message.Type)
        {
            case NavigationType.ScreenTop:
                newIndex = firstVisibleIndex;
                break;
            case NavigationType.ScreenMiddle:
                newIndex = firstVisibleIndex + visibleCount / 2;
                break;
            case NavigationType.ScreenBottom:
                newIndex = lastVisibleIndex;
                break;
            case NavigationType.PageUp:
                newIndex = currentIndex - visibleCount;
                break;
            case NavigationType.PageDown:
                newIndex = currentIndex + visibleCount;
                break;
            case NavigationType.HalfPageUp:
                newIndex = currentIndex - visibleCount / 2;
                break;
            case NavigationType.HalfPageDown:
                newIndex = currentIndex + visibleCount / 2;
                break;
        }

        newIndex = Math.Max(0, Math.Min(itemCount - 1, newIndex));
        
        if (newIndex != currentIndex)
        {
            listBox.SelectedIndex = newIndex;
            var item = listBox.Items[newIndex];
            if (item != null)
            {
                listBox.ScrollIntoView(item);
            }
        }
    }


    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (vm.WorkspacePanel != null)
            {
                vm.WorkspacePanel.PropertyChanged += WorkspacePanel_PropertyChanged;
            }
            if (vm.CommandLine != null)
            {
                vm.CommandLine.PropertyChanged += CommandLine_PropertyChanged;
            }
        }
    }

    private void WorkspacePanel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspacePanelViewModel.IsWorkspaceListFocused))
        {
            var vm = sender as WorkspacePanelViewModel;
            if (vm == null) return;

            Dispatcher.UIThread.Post(() =>
            {
                if (vm.IsWorkspaceListFocused)
                {
                    var wsListBox = this.FindControl<ListBox>("WorkspaceListBox");
                    wsListBox?.Focus();
                }
                else
                {
                    var wsItemsListBox = this.FindControl<ListBox>("WorkspaceItemsListBox");
                    wsItemsListBox?.Focus();
                }
            });
        }
    }

    private void OnFocusRequest(FocusRequestMessage message)
    {
        // Use a slightly lower priority to ensure UI is ready, but higher than Input
        Dispatcher.UIThread.Post(() =>
        {
            switch (message.Target)
            {
                case FocusTarget.CommandLine:
                    var textBox = this.FindControl<TextBox>("CommandTextBox");
                    textBox?.Focus();
                    break;
                case FocusTarget.WorkspacePanel:
                    var listBox = this.FindControl<ListBox>("WorkspaceListBox");
                    if (listBox != null)
                    {
                        // Ensure the ListBox itself is focusable and visible
                        if (!listBox.IsVisible) return;

                        // Force focus to the ListBox
                        listBox.Focus();

                        // If there's a selected item, try to focus its container specifically
                        // This helps with keyboard navigation (Up/Down) working immediately
                        if (listBox.ItemCount > 0)
                        {
                            if (listBox.SelectedIndex == -1)
                            {
                                listBox.SelectedIndex = 0;
                            }
                            
                            var container = listBox.ContainerFromIndex(listBox.SelectedIndex) as Control;
                            container?.Focus();
                        }
                    }
                    break;
            }
        }, DispatcherPriority.Input);
    }

    private void OnScrollPreview(ScrollPreviewMessage message)
    {
        var previewControl = this.FindControl<ContentControl>("PreviewContentControl");
        if (previewControl == null) return;

        // Find a ScrollViewer inside the preview control
        var scrollViewer = previewControl.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        
        if (scrollViewer != null)
        {
            double offset = 40; // Scroll amount
            switch (message.Direction)
            {
                case ScrollDirection.Up:
                    scrollViewer.Offset = new Vector(scrollViewer.Offset.X, Math.Max(0, scrollViewer.Offset.Y - offset));
                    break;
                case ScrollDirection.Down:
                    scrollViewer.Offset = new Vector(scrollViewer.Offset.X, Math.Min(scrollViewer.Extent.Height - scrollViewer.Viewport.Height, scrollViewer.Offset.Y + offset));
                    break;
                case ScrollDirection.Left:
                    scrollViewer.Offset = new Vector(Math.Max(0, scrollViewer.Offset.X - offset), scrollViewer.Offset.Y);
                    break;
                case ScrollDirection.Right:
                    scrollViewer.Offset = new Vector(Math.Min(scrollViewer.Extent.Width - scrollViewer.Viewport.Width, scrollViewer.Offset.X + offset), scrollViewer.Offset.Y);
                    break;
            }
        }
    }

    private void CurrentListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem != null)
        {
            // Use DispatcherPriority.Render to ensure we run after layout updates and AutoScroll
            Dispatcher.UIThread.Post(() => 
            {
                var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                if (scrollViewer != null)
                {
                    int selectedIndex = listBox.SelectedIndex;
                    // Get the container of the selected item to determine item height
                    var container = listBox.ContainerFromIndex(selectedIndex) as Control;
                    // If selected item container is not available, try the first item
                    if (container == null && listBox.ItemCount > 0)
                    {
                        container = listBox.ContainerFromIndex(0) as Control;
                    }

                    if (container != null)
                    {
                        double itemHeight = container.Bounds.Height;
                        
                        // Avoid division by zero
                        if (itemHeight > 0)
                        {
                            // Convert pixel values to item indices
                            double topIndex = scrollViewer.Offset.Y / itemHeight;
                            double viewportItems = scrollViewer.Viewport.Height / itemHeight;
                            double bottomIndex = topIndex + viewportItems;
                            
                            // Check if selected index is within the last 2 visible items
                            // We use a threshold (e.g. 1.5) to ensure we catch it even if partially visible
                            if (selectedIndex >= bottomIndex - 1.5)
                            {
                                // We want to scroll down to push the selected item up.
                                // Target: Scroll down by 15 lines.
                                double scrollDeltaItems = 15;
                                double targetItemIndex = topIndex + scrollDeltaItems;
                                double targetOffset = targetItemIndex * itemHeight;

                                // Constraint 1: Don't scroll past the end of the list
                                double maxOffset = scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
                                if (targetOffset > maxOffset) targetOffset = maxOffset;

                                // Constraint 2: Don't scroll so far that the selected item disappears off the TOP
                                // We want selectedIndex >= targetItemIndex (so it's at least the top item)
                                // Let's leave a margin of 1 item at the top if possible
                                double maxTopItemIndex = Math.Max(0, selectedIndex - 1);
                                double maxTopOffset = maxTopItemIndex * itemHeight;
                                
                                if (targetOffset > maxTopOffset)
                                {
                                    targetOffset = maxTopOffset;
                                }

                                // Only scroll if we are actually moving down
                                if (targetOffset > scrollViewer.Offset.Y)
                                {
                                    scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetOffset);
                                }
                            }
                        }
                    }
                }
            }, DispatcherPriority.Render);
        }
    }

    private bool _isUpdatingPosition = false;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CommandLine.PropertyChanged += CommandLine_PropertyChanged;
            
            // Sync Position: VM -> View
            Position = vm.WindowPosition;
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.WindowPosition))
                {
                    if (!_isUpdatingPosition)
                    {
                        _isUpdatingPosition = true;
                        Position = vm.WindowPosition;
                        _isUpdatingPosition = false;
                    }
                }
            };

            // Sync Position: View -> VM
            this.PositionChanged += (s, args) =>
            {
                if (!_isUpdatingPosition)
                {
                    _isUpdatingPosition = true;
                    vm.WindowPosition = Position;
                    _isUpdatingPosition = false;
                }
            };
        }
    }

    private void CommandLine_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Handle IsVisible change OR IsLocked change (when unlocking)
        if (e.PropertyName == nameof(CommandLineViewModel.IsVisible) || 
            e.PropertyName == nameof(CommandLineViewModel.IsLocked))
        {
            if (sender is CommandLineViewModel cmd)
            {
                if (cmd.IsVisible && !cmd.IsLocked)
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        // When Workspace Panel is open, only focus the command line if VM says focus should be there.
                        // This prevents the delayed Focus() below from stealing focus back after Tab switches to the workspace list.
                        if (DataContext is MainWindowViewModel vm && vm.IsWorkspacePanelVisible && !vm.IsFocusInCommandLine)
                        {
                            return;
                        }

                        var textBox = this.FindControl<TextBox>("CommandTextBox");
                        if (textBox != null)
                        {
                            // Capture target selection from VM *before* focusing, 
                            // in case Focus() triggers binding updates that clear it.
                            int targetStart = cmd.SelectionStart;
                            int targetEnd = cmd.SelectionEnd;

                            // Ensure text is synced if binding hasn't happened yet
                            if (textBox.Text != cmd.CommandText)
                            {
                                textBox.Text = cmd.CommandText;
                            }

                            textBox.Focus();
                            
                            // Wait for Focus() side effects (like SelectAll) to complete
                            await Task.Delay(50);

                            // Focus may have been switched (e.g., via Tab) during the delay.
                            if (DataContext is MainWindowViewModel vmAfterDelay && vmAfterDelay.IsWorkspacePanelVisible && !vmAfterDelay.IsFocusInCommandLine)
                            {
                                return;
                            }

                            int textLength = textBox.Text?.Length ?? 0;
                            
                            // Explicitly clear selection first to ensure clean state
                            textBox.SelectionStart = 0;
                            textBox.SelectionEnd = 0;

                            if (targetEnd > targetStart)
                            {
                                // Clamp values to be safe
                                int start = Math.Max(0, Math.Min(targetStart, textLength));
                                int end = Math.Max(0, Math.Min(targetEnd, textLength));
                                
                                textBox.SelectionStart = start;
                                textBox.SelectionEnd = end;
                            }
                            else
                            {
                                textBox.CaretIndex = textLength;
                            }
                        }
                    });
                }
                else if (cmd.IsVisible && cmd.IsLocked)
                {
                    // Focus ListBox when Locked to ensure navigation keys work and focus isn't lost
                    Dispatcher.UIThread.Post(() =>
                    {
                        var listBox = this.FindControl<ListBox>("CurrentListBox");
                        listBox?.Focus();
                    });
                }
            }
        }
    }

    // Replaces the previous OnKeyDown
    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // If the source is a TextBox (e.g. Workspace editing), let it handle the input
        if (e.Source is TextBox)
        {
            return;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            // Special handling for No-Arg Command Mode (Input Hidden)
            // This must be checked BEFORE isTyping check, because isTyping is true when InputHidden is true.
            if (vm.CommandLine.IsVisible && vm.CommandLine.IsInputHidden)
            {
                if (e.Key == Key.Enter)
                {
                    vm.CommandLine.Execute();
                    this.Focus();
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Escape)
                {
                    vm.CommandLine.Cancel();
                    this.Focus();
                    e.Handled = true;
                    return;
                }
                
                // Block other keys to prevent them from triggering shortcuts while in this mode
                e.Handled = true;
                return;
            }

            bool isTyping = vm.CommandLine.IsVisible && !vm.CommandLine.IsLocked;

            // If we are in Workspace mode and focus is NOT in command line, we are not "typing" in the sense of blocking keys.
            // This allows Tab key (and others) to be handled by KeyBindingService when focus is in the Workspace Panel.
            if (vm.IsWorkspacePanelVisible && !vm.IsFocusInCommandLine)
            {
                isTyping = false;
            }

            if (isTyping || vm.IsSettingsVisible)
            {
                return;
            }
        }

        var app = (App)Application.Current!;
        if (app.Services != null)
        {
            var keyBindingService = app.Services.GetRequiredService<KeyBindingService>();
            bool handled = await keyBindingService.HandleKeyAsync(e);
            if (handled)
            {
                e.Handled = true;
                return;
            }
        }

        // If KeyBindingService didn't handle it, and it's a Tab key, swallow it to prevent default focus cycling
        // unless we are in a specific mode that allows it (which we are not, for main view columns).
        if (e.Key == Key.Tab)
        {
            e.Handled = true;
        }
    }

    // Remove the old OnKeyDown if it exists or ensure it's not attached in XAML
    // Since we use AddHandler in constructor, we should remove KeyDown="OnKeyDown" from XAML or rename this method
    // The previous method was OnKeyDown. I'll keep the name OnCommandLineKeyDown for the TextBox.

    private void OnCommandLineKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (e.Key == Key.Back)
            {
                var textBox = sender as TextBox;
                if (textBox != null && string.IsNullOrEmpty(textBox.Text))
                {
                    if (vm.CommandLine.TryPopCommand())
                    {
                        e.Handled = true;
                        // Move caret to end after update
                        Dispatcher.UIThread.Post(() => 
                        {
                            if (textBox != null)
                            {
                                textBox.CaretIndex = textBox.Text?.Length ?? 0;
                            }
                        });
                        return;
                    }
                }
            }

            if (e.Key == Key.Enter)
            {
                vm.CommandLine.Execute();
                this.Focus(); // Return focus to window
            }
            else if (e.Key == Key.Escape)
            {
                vm.CommandLine.Cancel();
                this.Focus(); // Return focus to window
            }
            else if (e.Key == Key.Up)
            {
                vm.CommandLine.HistoryUp();
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                vm.CommandLine.HistoryDown();
                e.Handled = true;
            }
            else if (e.Key == Key.Tab)
            {
                if (vm.IsWorkspacePanelVisible)
                {
                    vm.SwitchWorkspaceFocus();
                    e.Handled = true;
                }
                else
                {
                    vm.CommandLine.TriggerTab();
                    e.Handled = true;
                }
            }
        }
    }

    private void OnWorkspaceEditAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            // Use a slightly lower priority or small delay to ensure the control is fully ready and layout is done
            Dispatcher.UIThread.Post(async () => 
            {
                // Small delay to allow ListBox to finish its own focus handling
                await Task.Delay(50);
                
                if (textBox.IsVisible)
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }
            }, DispatcherPriority.Input);

            // Subscribe to property changes to focus when becoming visible
            textBox.PropertyChanged += (s, args) => 
            {
                if (args.Property == Visual.IsVisibleProperty && textBox.IsVisible)
                {
                    Dispatcher.UIThread.Post(async () => 
                    {
                        await Task.Delay(50);
                        textBox.Focus();
                        textBox.SelectAll();
                    }, DispatcherPriority.Input);
                }
            };
        }
    }

    private async void OnWorkspaceEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is Models.WorkspaceModel ws && DataContext is MainWindowViewModel vm)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await vm.WorkspacePanel.ConfirmEdit(ws);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                vm.WorkspacePanel.CancelEdit(ws);
            }
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private async void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.RefreshAsync();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        HookWndProc();
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ApplyGlobalHotkey();
        }
    }

    private delegate IntPtr SubclassProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);
    private SubclassProcDelegate? _subclassProc;
    private bool _isHooked = false;

    private void HookWndProc()
    {
        if (_isHooked) return;

        if (OperatingSystem.IsWindows())
        {
            var handle = this.TryGetPlatformHandle()?.Handle;
            if (handle.HasValue)
            {
                _subclassProc = new SubclassProcDelegate(SubclassProc);
                bool result = SetWindowSubclass(handle.Value, _subclassProc, 0, IntPtr.Zero);
                if (result)
                {
                    _isHooked = true;
                }
            }
        }
    }

    private IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            bool handled = false;
            vm.ProcessWin32Message(hWnd, (int)uMsg, wParam, lParam, ref handled);
            if (handled) return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProcDelegate pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}

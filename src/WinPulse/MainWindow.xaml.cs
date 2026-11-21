using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;
using WinPulse.Core;
using WinPulse.ViewModels;
using WinPulse.Views;

namespace WinPulse;

public partial class MainWindow : Window
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x57A1;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkSpace = 0x20;

    private bool _forceClose;
    private IntPtr _windowHandle;
    private HwndSource? _hwndSource;
    private QuickSearchWindow? _quickSearchWindow;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public MainWindow()
    {
        InitializeComponent();

        // Fit inside the current work area (handles high-DPI and small screens), then start maximized
        // so the sidebar, grid and preview pane always have room.
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1440, Math.Max(MinWidth, workArea.Width - 40));
        Height = Math.Min(900, Math.Max(640, workArea.Height - 40));
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
        WindowState = WindowState.Maximized;

        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (FindResource("TrayIcon") is TaskbarIcon trayIcon)
        {
            TrayService.Initialize(trayIcon);
        }

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // On narrow windows the preview panes steal too much room; start them collapsed.
            if (ActualWidth < 1180)
            {
                viewModel.Files.IsPreviewVisible = false;
                viewModel.Screenshots.Preview.IsVisible = false;
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPaletteOpen) &&
            sender is MainViewModel { IsPaletteOpen: true })
        {
            Dispatcher.BeginInvoke(new Action(() => PaletteSearchBox.Focus()));
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceClose)
        {
            return;
        }

        if (AppHost.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            TrayService.Notify(
                "WinPulse is still running",
                "Tracking continues in the background. Right-click the tray icon to exit.");
        }
    }

    private void RestoreWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    /// <summary>Called by quick search before running an action so navigation is visible.</summary>
    public void RestoreForQuickAction() => RestoreWindow();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _windowHandle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WndProc);

        if (!RegisterHotKey(_windowHandle, HotkeyId, ModControl | ModShift, VkSpace))
        {
            Log.Warn("Global hotkey Ctrl+Shift+Space is already in use by another app.");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            OpenQuickSearch();
            handled = true;
        }

        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HotkeyId);
        }

        _hwndSource?.RemoveHook(WndProc);
        base.OnClosed(e);
    }

    private void OpenQuickSearch()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (_quickSearchWindow is { IsVisible: true })
        {
            _quickSearchWindow.Activate();
            return;
        }

        _quickSearchWindow = new QuickSearchWindow(viewModel) { Owner = this };
        _quickSearchWindow.Show();
        _quickSearchWindow.Activate();
    }

    /// <summary>Used by the --quick-search launch flag for verification and shortcuts.</summary>
    public void ShowQuickSearch() => OpenQuickSearch();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (!viewModel.IsPaletteOpen &&
            e.Key == Key.K &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            viewModel.OpenPaletteCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (!viewModel.IsPaletteOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                viewModel.ClosePaletteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down:
                viewModel.MovePaletteSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                viewModel.MovePaletteSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                viewModel.ExecutePaletteSelection();
                e.Handled = true;
                break;
        }
    }

    private void PaletteBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ClosePaletteCommand.Execute(null);
        }
    }

    private void PaletteList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ExecutePaletteSelection();
        }
    }

    // ------------------------------------------------------------------ tray

    private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e) => RestoreWindow();

    private void TrayIcon_TrayLeftMouseUp(object sender, RoutedEventArgs e) => OpenQuickSearch();

    private void TrayQuickSearch_Click(object sender, RoutedEventArgs e) => OpenQuickSearch();

    private void TrayOpen_Click(object sender, RoutedEventArgs e) => RestoreWindow();

    private void TrayScan_Click(object sender, RoutedEventArgs e)
    {
        RestoreWindow();
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ScanNowCommand.Execute(null);
        }
    }

    private void TrayFiles_Click(object sender, RoutedEventArgs e) => GoTo("File activity");

    private void TrayScreenshots_Click(object sender, RoutedEventArgs e) => GoTo("Screenshots");

    private void TrayCleanup_Click(object sender, RoutedEventArgs e) => GoTo("Cleanup");

    private void TraySettings_Click(object sender, RoutedEventArgs e) => GoTo("Settings");

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _forceClose = true;
        Application.Current.Shutdown();
    }

    private void GoTo(string page)
    {
        RestoreWindow();
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.NavigateTo(page);
        }
    }

    // -------------------------------------------------------------- title bar

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ToggleThemeCommand.Execute(null);
        }
    }

    private void OpenGitHub_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.OpenGitHubCommand.Execute(null);
        }
    }
}

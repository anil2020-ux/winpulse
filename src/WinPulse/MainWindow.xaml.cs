using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Hardcodet.Wpf.TaskbarNotification;
using WinPulse.Core;
using WinPulse.ViewModels;

namespace WinPulse;

public partial class MainWindow : Window
{
    private bool _forceClose;

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

using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using WinPulse.Core;
using WinPulse.ViewModels;

namespace WinPulse;

public partial class MainWindow : Window
{
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

        // On narrow windows the preview pane steals too much room; start it collapsed.
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel && ActualWidth < 1180)
            {
                viewModel.Files.IsPreviewVisible = false;
            }
        };
    }

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

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WinPulse.Models;
using WinPulse.ViewModels;

namespace WinPulse.Views;

/// <summary>Compact tray/global-hotkey search popup (Ctrl+Shift+Space).</summary>
public partial class QuickSearchWindow : Window
{
    private readonly MainViewModel _main;
    private readonly ObservableCollection<PaletteItem> _results = new();
    private readonly DispatcherTimer _debounce;

    public QuickSearchWindow(MainViewModel main)
    {
        InitializeComponent();
        _main = main;
        ResultsList.ItemsSource = _results;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await SearchAsync(SearchBox.Text);
        };

        SearchBox.TextChanged += (_, _) =>
        {
            _debounce.Stop();
            _debounce.Start();
        };

        Loaded += async (_, _) =>
        {
            PositionNearTray();
            SearchBox.Focus();
            await SearchAsync(string.Empty);
        };

        Deactivated += (_, _) => Close();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void PositionNearTray()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left + 24, area.Right - Width - 24);
        Top = Math.Max(area.Top + 24, area.Bottom - Height - 24);
    }

    private async Task SearchAsync(string query)
    {
        try
        {
            var items = await _main.BuildPaletteResultsAsync(query);
            _results.Clear();
            foreach (var item in items)
            {
                _results.Add(item);
            }

            if (_results.Count > 0)
            {
                ResultsList.SelectedIndex = 0;
            }
        }
        catch
        {
            // Never let quick search crash the app.
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Down:
                if (_results.Count > 0)
                {
                    ResultsList.SelectedIndex = Math.Min(_results.Count - 1, ResultsList.SelectedIndex + 1);
                }

                e.Handled = true;
                break;
            case Key.Up:
                if (_results.Count > 0)
                {
                    ResultsList.SelectedIndex = Math.Max(0, ResultsList.SelectedIndex - 1);
                }

                e.Handled = true;
                break;
            case Key.Enter:
                ExecuteSelected();
                e.Handled = true;
                break;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ExecuteSelected();

    private void ExecuteSelected()
    {
        if (ResultsList.SelectedItem is not PaletteItem item)
        {
            return;
        }

        if (Application.Current.MainWindow is MainWindow mainWindow)
        {
            mainWindow.RestoreForQuickAction();
        }

        _main.ExecutePaletteItem(item);
        Close();
    }
}

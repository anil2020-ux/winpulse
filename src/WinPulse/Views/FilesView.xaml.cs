using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinPulse.Core;
using WinPulse.ViewModels;

namespace WinPulse.Views;

public partial class FilesView : UserControl
{
    private FilesViewModel? _viewModel;

    public FilesView()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Detach();
        Loaded += (_, _) => Focus();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        if (e.NewValue is FilesViewModel viewModel)
        {
            _viewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void Detach()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    /// <summary>
    /// WebView2's Source cannot be bound directly: WPF restores it to null when the binding
    /// deactivates, which throws. Navigate only after the runtime is initialized instead.
    /// </summary>
    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FilesViewModel.PreviewPdfUri))
        {
            return;
        }

        var uri = _viewModel?.PreviewPdfUri;
        if (uri is null)
        {
            return;
        }

        try
        {
            await PdfPreview.EnsureCoreWebView2Async();
            if (_viewModel?.PreviewPdfUri is { } current)
            {
                PdfPreview.Source = current;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("PDF preview unavailable: " + ex.Message);
        }
    }

    private void FilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is FilesViewModel viewModel)
        {
            viewModel.SetSelection(FilesGrid.SelectedItems);
        }
    }

    private void FilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is FilesViewModel viewModel && viewModel.OpenCommand.CanExecute(null))
        {
            viewModel.OpenCommand.Execute(null);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not FilesViewModel viewModel)
        {
            return;
        }

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (control && e.Key == Key.R)
        {
            viewModel.RefreshCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            viewModel.DeleteCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            viewModel.RenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && viewModel.SelectedFile is not null)
        {
            viewModel.OpenCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void PlayMedia_Click(object sender, RoutedEventArgs e) => MediaPlayer.Play();

    private void PauseMedia_Click(object sender, RoutedEventArgs e) => MediaPlayer.Pause();

    private void StopMedia_Click(object sender, RoutedEventArgs e) => MediaPlayer.Stop();
}

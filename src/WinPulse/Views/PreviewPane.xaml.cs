using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WinPulse.Core;
using WinPulse.ViewModels;

namespace WinPulse.Views;

/// <summary>Shared file preview surface (header, metadata, image/text/PDF/media content).</summary>
public partial class PreviewPane : UserControl
{
    private PreviewViewModel? _viewModel;

    public PreviewPane()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Detach();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        if (e.NewValue is PreviewViewModel viewModel)
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
    /// WebView2's Source cannot be bound: WPF restores it to null when a binding deactivates and
    /// the control throws. Navigate from code after the runtime is ready instead.
    /// </summary>
    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PreviewViewModel.PreviewPdfUri))
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

    private void PlayMedia_Click(object sender, RoutedEventArgs e) => MediaPlayer.Play();

    private void PauseMedia_Click(object sender, RoutedEventArgs e) => MediaPlayer.Pause();

    private void StopMedia_Click(object sender, RoutedEventArgs e) => MediaPlayer.Stop();
}

using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WinPulse.Core;
using WinPulse.Models;
using WinPulse.Services;

namespace WinPulse.ViewModels;

/// <summary>
/// Shared preview state used by every page that shows files (File activity, Cleanup).
/// The matching view is Views/PreviewPane.xaml.
/// </summary>
public sealed partial class PreviewViewModel : ObservableObject
{
    private CancellationTokenSource? _loadCts;

    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private FileEntry? _file;
    [ObservableProperty] private string _title = "No file selected";
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private bool _showEmptyPreview = true;
    [ObservableProperty] private bool _showTextPreview;
    [ObservableProperty] private bool _showImagePreview;
    [ObservableProperty] private bool _showPdfPreview;
    [ObservableProperty] private bool _showMediaPreview;
    [ObservableProperty] private bool _showUnsupportedPreview;
    [ObservableProperty] private ImageSource? _previewImage;
    [ObservableProperty] private Uri? _previewPdfUri;
    [ObservableProperty] private Uri? _previewMedia;
    [ObservableProperty] private string? _previewText;
    [ObservableProperty] private string _typeLabel = "preview";
    [ObservableProperty] private string _meta = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _truncated;

    public void ToggleVisibility() => IsVisible = !IsVisible;

    public async Task LoadAsync(FileEntry? file)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        File = file;
        Title = file?.Name ?? "No file selected";
        ShowTextPreview = ShowImagePreview = ShowPdfPreview = ShowMediaPreview = ShowUnsupportedPreview = false;
        PreviewText = null;
        PreviewImage = null;
        PreviewPdfUri = null;
        PreviewMedia = null;
        Message = null;
        Truncated = false;
        TypeLabel = file?.CategoryLabel ?? "preview";

        if (file is null)
        {
            HasPreview = false;
            ShowEmptyPreview = true;
            Meta = string.Empty;
            return;
        }

        HasPreview = true;
        ShowEmptyPreview = false;
        Meta = $"{file.SizeText}  •  {file.ModifiedText}  •  {file.FolderName}";

        var cts = new CancellationTokenSource();
        _loadCts = cts;

        try
        {
            var info = await Task.Run(() => AppHost.Preview.Describe(file.FullPath), cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(info.TypeLabel))
            {
                TypeLabel = info.TypeLabel;
            }

            switch (info.Kind)
            {
                case PreviewKind.Text:
                    PreviewText = info.Text ?? string.Empty;
                    Truncated = info.Truncated;
                    ShowTextPreview = true;
                    break;

                case PreviewKind.Image:
                    var image = await Task.Run(() => LoadImage(file.FullPath), cts.Token).ConfigureAwait(true);
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    PreviewImage = image;
                    ShowImagePreview = image is not null;
                    if (image is null)
                    {
                        Message = "Windows could not decode this image.";
                        ShowUnsupportedPreview = true;
                    }

                    break;

                case PreviewKind.Pdf:
                    PreviewPdfUri = new Uri(file.FullPath);
                    ShowPdfPreview = true;
                    break;

                case PreviewKind.Audio:
                case PreviewKind.Video:
                    PreviewMedia = info.MediaSource;
                    ShowMediaPreview = true;
                    break;

                default:
                    Message = info.Message ?? "No preview available for this file type.";
                    ShowUnsupportedPreview = true;
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Selection moved on.
        }
        catch (Exception ex)
        {
            Log.Warn($"Preview failed for {file.FullPath}: {ex.Message}");
            Message = "Could not build a preview: " + ex.Message;
            ShowUnsupportedPreview = true;
        }
    }

    private static ImageSource? LoadImage(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.DecodePixelWidth = 1100;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}

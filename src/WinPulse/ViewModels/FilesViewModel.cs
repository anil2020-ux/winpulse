using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WinPulse.Core;
using WinPulse.Models;
using WinPulse.Services;

namespace WinPulse.ViewModels;

/// <summary>The file activity explorer: filter, preview and act on indexed files.</summary>
public sealed partial class FilesViewModel : ObservableObject, IActivatableViewModel
{
    private readonly MainViewModel _main;
    private CancellationTokenSource? _reloadCts;
    private CancellationTokenSource? _previewCts;
    private bool _initialized;

    public ObservableCollection<FileEntry> Files { get; } = new();
    public ObservableCollection<FileEntry> SelectedFiles { get; } = new();
    public ObservableCollection<RootFolder> RootOptions { get; } = new();

    public IReadOnlyList<string> Categories { get; } =
        new[] { "All categories" }.Concat(FileKinds.AllCategories).ToArray();

    public IReadOnlyList<string> DateFilters { get; } = new[]
    {
        "Any time", "Today", "Last 7 days", "Last 30 days", "Last 90 days", "This year",
    };

    public IReadOnlyList<string> SizeFilters { get; } = new[]
    {
        "Any size", "Tiny (< 1 MB)", "Small (< 10 MB)", "Large (> 10 MB)", "Huge (> 100 MB)", "Very large (> 1 GB)",
    };

    public IReadOnlyList<string> SortOptions { get; } = new[]
    {
        "Newest first", "Oldest first", "Largest first", "Smallest first", "Name A–Z", "Name Z–A", "Recently added",
    };

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedCategory = "All categories";
    [ObservableProperty] private string _selectedDate = "Any time";
    [ObservableProperty] private string _selectedSize = "Any size";
    [ObservableProperty] private string _selectedSort = "Newest first";
    [ObservableProperty] private RootFolder? _selectedRoot;
    [ObservableProperty] private FileEntry? _selectedFile;
    [ObservableProperty] private bool _screenshotsOnly;
    [ObservableProperty] private bool _installersOnly;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _resultSummary = "No files loaded yet";
    [ObservableProperty] private int _totalMatches;
    [ObservableProperty] private int _selectedCount;

    // Preview state
    [ObservableProperty] private bool _isPreviewVisible = true;
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
    [ObservableProperty] private string _previewTypeLabel = string.Empty;
    [ObservableProperty] private string _previewMeta = string.Empty;
    [ObservableProperty] private string? _previewMessage;
    [ObservableProperty] private bool _previewTruncated;
    [ObservableProperty] private string _previewPath = string.Empty;

    public FilesViewModel(MainViewModel main)
    {
        _main = main;
        LoadRootOptions();
        _initialized = true;
        _ = ReloadAsync();
    }

    public MainViewModel Main => _main;

    public void OnActivated()
    {
        if (Files.Count == 0)
        {
            _ = ReloadAsync();
        }
    }

    public void OnDeactivated()
    {
    }

    private void LoadRootOptions()
    {
        RootOptions.Clear();
        RootOptions.Add(new RootFolder { Label = "All folders", Path = string.Empty, Enabled = true });
        foreach (var root in AppHost.Database.GetRoots())
        {
            RootOptions.Add(root);
        }

        SelectedRoot = RootOptions[0];
    }

    public Task ReloadRootsAsync()
    {
        LoadRootOptions();
        return ReloadAsync();
    }

    // ---------------------------------------------------------------- queries

    private FileQuery BuildQuery()
    {
        var query = new FileQuery
        {
            Search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            RootPath = SelectedRoot is { Path.Length: > 0 } root ? root.Path : null,
            Categories = SelectedCategory == "All categories" ? null : new[] { SelectedCategory },
            ScreenshotsOnly = ScreenshotsOnly,
            InstallersOnly = InstallersOnly,
            Limit = 2000,
        };

        var now = DateTime.Now;
        switch (SelectedDate)
        {
            case "Today":
                query.From = DateTime.Today;
                break;
            case "Last 7 days":
                query.From = now.AddDays(-7);
                break;
            case "Last 30 days":
                query.From = now.AddDays(-30);
                break;
            case "Last 90 days":
                query.From = now.AddDays(-90);
                break;
            case "This year":
                query.From = new DateTime(now.Year, 1, 1);
                break;
        }

        switch (SelectedSize)
        {
            case "Tiny (< 1 MB)":
                query.MaxSize = 1024L * 1024;
                break;
            case "Small (< 10 MB)":
                query.MaxSize = 10L * 1024 * 1024;
                break;
            case "Large (> 10 MB)":
                query.MinSize = 10L * 1024 * 1024;
                break;
            case "Huge (> 100 MB)":
                query.MinSize = 100L * 1024 * 1024;
                break;
            case "Very large (> 1 GB)":
                query.MinSize = 1024L * 1024 * 1024;
                break;
        }

        query.Sort = SelectedSort switch
        {
            "Oldest first" => "modified_asc",
            "Largest first" => "size_desc",
            "Smallest first" => "size_asc",
            "Name A–Z" => "name_asc",
            "Name Z–A" => "name_desc",
            "Recently added" => "first_desc",
            _ => "modified_desc",
        };

        return query;
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        _reloadCts?.Cancel();
        _reloadCts?.Dispose();
        _reloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _reloadCts.Token;

        IsLoading = true;
        try
        {
            var query = BuildQuery();
            var selectedPath = SelectedFile?.FullPath;
            var result = await Task.Run(() =>
            {
                var list = AppHost.Database.QueryFiles(query, out var total);
                return (List: list, Total: total);
            }, token).ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            Files.Clear();
            foreach (var file in result.List)
            {
                Files.Add(file);
            }

            TotalMatches = result.Total;
            var shownBytes = result.List.Sum(file => file.Size);
            ResultSummary = result.Total == 0
                ? "No files match these filters"
                : $"Showing {result.List.Count:N0} of {result.Total:N0} files  •  {Formats.Bytes(shownBytes)}";

            SelectedFile = Files.FirstOrDefault(file => file.FullPath == selectedPath)
                           ?? Files.FirstOrDefault();
            SelectedCount = 0;
            SelectedFiles.Clear();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query.
        }
        catch (Exception ex)
        {
            Log.Error("Files reload failed", ex);
            _main.Notify("Could not load files: " + ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ---------------------------------------------------------------- filters

    private void ScheduleReload(int delayMs = 0)
    {
        if (!_initialized)
        {
            return;
        }

        _ = DebouncedReloadAsync(delayMs);
    }

    private async Task DebouncedReloadAsync(int delayMs)
    {
        try
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs).ConfigureAwait(true);
            }

            await ReloadAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Ignore.
        }
    }

    partial void OnSearchTextChanged(string value) => ScheduleReload(280);

    partial void OnSelectedCategoryChanged(string value) => ScheduleReload();

    partial void OnSelectedDateChanged(string value) => ScheduleReload();

    partial void OnSelectedSizeChanged(string value) => ScheduleReload();

    partial void OnSelectedSortChanged(string value) => ScheduleReload();

    partial void OnSelectedRootChanged(RootFolder? value) => ScheduleReload();

    partial void OnScreenshotsOnlyChanged(bool value) => ScheduleReload();

    partial void OnInstallersOnlyChanged(bool value) => ScheduleReload();

    [RelayCommand]
    private async Task Refresh() => await ReloadAsync();

    [RelayCommand]
    private async Task ScanNow()
    {
        await _main.RunScanAsync();
    }

    [RelayCommand]
    private async Task ClearFilters()
    {
        SearchText = string.Empty;
        SelectedCategory = "All categories";
        SelectedDate = "Any time";
        SelectedSize = "Any size";
        SelectedSort = "Newest first";
        ScreenshotsOnly = false;
        InstallersOnly = false;
        if (RootOptions.Count > 0)
        {
            SelectedRoot = RootOptions[0];
        }

        await ReloadAsync();
    }

    [RelayCommand]
    private void TogglePreview() => IsPreviewVisible = !IsPreviewVisible;

    [RelayCommand]
    private async Task ExportCsv()
    {
        var rows = Files.ToList();
        if (rows.Count == 0)
        {
            DialogService.Info("Nothing to export", "No rows match the current filters.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export file activity to CSV",
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"winpulse-files-{DateTime.Now:yyyyMMdd-HHmm}.csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var csv = new StringBuilder();
            csv.AppendLine("Name,Path,Category,SizeBytes,Size,Modified,Created,Source,Folder,Protected");
            foreach (var file in rows)
            {
                csv.AppendLine(string.Join(",",
                    Csv(file.Name),
                    Csv(file.FullPath),
                    Csv(file.CategoryLabel),
                    file.Size.ToString(),
                    Csv(file.SizeText),
                    file.Modified.ToString("s"),
                    file.Created.ToString("s"),
                    Csv(file.RootLabel),
                    Csv(file.Folder),
                    file.Keep ? "yes" : "no"));
            }

            await File.WriteAllTextAsync(dialog.FileName, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            _main.Notify($"Exported {rows.Count:N0} rows to {Path.GetFileName(dialog.FileName)}.");
            FileOperations.RevealInExplorer(dialog.FileName);
        }
        catch (Exception ex)
        {
            Log.Error("CSV export failed", ex);
            DialogService.Info("Export failed", ex.Message);
        }
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    // ---------------------------------------------------------------- selection

    public void SetSelection(System.Collections.IList selectedItems)
    {
        SelectedFiles.Clear();
        foreach (var item in selectedItems)
        {
            if (item is FileEntry file)
            {
                SelectedFiles.Add(file);
            }
        }

        SelectedCount = SelectedFiles.Count;
    }

    private IReadOnlyList<FileEntry> GetTargets() =>
        SelectedFiles.Count > 0
            ? SelectedFiles.ToList()
            : SelectedFile is { } single ? new[] { single } : Array.Empty<FileEntry>();

    // ------------------------------------------------------------------ actions

    [RelayCommand]
    private void Open()
    {
        if (SelectedFile is not { } file)
        {
            return;
        }

        var result = FileOperations.Open(file.FullPath);
        _main.Notify(result.Message);
    }

    [RelayCommand]
    private void Reveal()
    {
        if (SelectedFile is not { } file)
        {
            return;
        }

        var result = FileOperations.RevealInExplorer(file.FullPath);
        _main.Notify(result.Message);
    }

    [RelayCommand]
    private void OpenContainingFolder()
    {
        if (SelectedFile is not { } file)
        {
            return;
        }

        var result = FileOperations.OpenFolder(file.Folder);
        _main.Notify(result.Message);
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (SelectedFile is not { } file)
        {
            return;
        }

        try
        {
            Clipboard.SetText(file.FullPath);
            _main.Notify("Path copied to clipboard.");
        }
        catch (Exception ex)
        {
            _main.Notify("Clipboard error: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (SelectedFile is not { } file)
        {
            return;
        }

        var newName = DialogService.Prompt(
            "Rename file",
            "Enter a new name for this file:",
            file.Name,
            "Rename");

        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == file.Name)
        {
            return;
        }

        var name = newName.Trim();
        var result = FileOperations.Rename(file.FullPath, name);
        if (!result.Success)
        {
            DialogService.Info("Rename failed", result.Message);
            return;
        }

        var newPath = Path.Combine(file.Folder, name);
        var extension = Path.GetExtension(newPath).ToLowerInvariant();
        AppHost.Database.UpdateAfterRename(
            file.Id,
            newPath,
            name,
            extension,
            FileKinds.CategoryOf(extension, newPath),
            FileKinds.IsScreenshot(name, newPath, extension));

        _main.Notify(result.Message);
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task MoveAsync()
    {
        var targets = GetTargets();
        if (targets.Count == 0)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = targets.Count == 1 ? "Move file to folder" : $"Move {targets.Count} files to folder",
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var targetDirectory = dialog.FolderName;
        var result = await Task.Run(() => FileOperations.Move(targets.Select(file => file.FullPath).ToList(), targetDirectory));
        CleanupMovedRows(targets);
        _main.Notify(result.Message);
        if (!result.Success)
        {
            DialogService.Info("Move failed", result.Message);
        }

        await ReloadAsync();
        await _main.Dashboard.RefreshSlowAsync();
        _ = _main.RunScanAsync();
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        var targets = GetTargets();
        if (targets.Count == 0)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = targets.Count == 1 ? "Copy file to folder" : $"Copy {targets.Count} files to folder",
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var targetDirectory = dialog.FolderName;
        var result = await Task.Run(() => FileOperations.Copy(targets.Select(file => file.FullPath).ToList(), targetDirectory));
        _main.Notify(result.Message);
        if (!result.Success)
        {
            DialogService.Info("Copy failed", result.Message);
            return;
        }

        _ = _main.RunScanAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var targets = GetTargets();
        if (targets.Count == 0)
        {
            return;
        }

        var totalBytes = targets.Sum(file => file.Size);
        var confirmed = DialogService.Confirm(
            "Move to Recycle Bin",
            $"Move {targets.Count:N0} file(s) ({Formats.Bytes(totalBytes)}) to the Recycle Bin?\n\nYou can restore them from the Recycle Bin if you change your mind.",
            "Move to Recycle Bin");

        if (!confirmed)
        {
            return;
        }

        var result = await Task.Run(() => FileOperations.Recycle(targets.Select(file => file.FullPath).ToList()));
        if (result.Success)
        {
            foreach (var file in targets)
            {
                AppHost.Database.DeleteRow(file.Id);
            }

            _main.Notify(result.Message);
            await ReloadAsync();
            await _main.Dashboard.RefreshSlowAsync();
            _main.UpdateIndexSummary();
        }
        else
        {
            DialogService.Info("Delete failed", result.Message);
        }
    }

    [RelayCommand]
    private void ToggleProtect()
    {
        if (SelectedFile is not { } file)
        {
            return;
        }

        file.Keep = !file.Keep;
        AppHost.Database.SetProtected(file.Id, file.Keep);
        _main.Notify(file.Keep
            ? "File protected — cleanup suggestions will skip it."
            : "Protection removed.");
    }

    private static void CleanupMovedRows(IEnumerable<FileEntry> moved)
    {
        foreach (var file in moved)
        {
            if (!File.Exists(file.FullPath))
            {
                AppHost.Database.DeleteRow(file.Id);
            }
        }
    }

    // ------------------------------------------------------------------ preview

    partial void OnSelectedFileChanged(FileEntry? value) => _ = LoadPreviewAsync(value);

    private async Task LoadPreviewAsync(FileEntry? file)
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;

        ShowTextPreview = ShowImagePreview = ShowPdfPreview = ShowMediaPreview = ShowUnsupportedPreview = false;
        PreviewText = null;
        PreviewImage = null;
        PreviewPdfUri = null;
        PreviewMedia = null;
        PreviewMessage = null;
        PreviewTruncated = false;
        PreviewPath = file?.FullPath ?? string.Empty;

        if (file is null)
        {
            HasPreview = false;
            ShowEmptyPreview = true;
            return;
        }

        HasPreview = true;
        ShowEmptyPreview = false;
        PreviewTypeLabel = file.CategoryLabel;
        PreviewMeta = $"{file.SizeText}  •  {file.ModifiedText}  •  {file.FolderName}";

        var cts = new CancellationTokenSource();
        _previewCts = cts;

        try
        {
            var info = await Task.Run(() => AppHost.Preview.Describe(file.FullPath), cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(info.TypeLabel))
            {
                PreviewTypeLabel = info.TypeLabel;
            }

            switch (info.Kind)
            {
                case PreviewKind.Text:
                    PreviewText = info.Text ?? string.Empty;
                    PreviewTruncated = info.Truncated;
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
                        PreviewMessage = "Windows could not decode this image.";
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
                    PreviewMessage = info.Message ?? "No preview available for this file type.";
                    ShowUnsupportedPreview = true;
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Selected another file.
        }
        catch (Exception ex)
        {
            Log.Warn($"Preview failed for {file.FullPath}: {ex.Message}");
            PreviewMessage = "Could not build a preview: " + ex.Message;
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

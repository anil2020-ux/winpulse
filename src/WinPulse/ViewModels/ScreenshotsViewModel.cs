using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPulse.Core;
using WinPulse.Models;
using WinPulse.Services;

namespace WinPulse.ViewModels;

/// <summary>Gallery of every screenshot WinPulse has seen, with preview and cleanup actions.</summary>
public sealed partial class ScreenshotsViewModel : ObservableObject, IActivatableViewModel
{
    private const int MaxTiles = 240;
    private readonly MainViewModel _main;
    private CancellationTokenSource? _loadCts;

    public ObservableCollection<ScreenshotTile> Tiles { get; } = new();
    public PreviewViewModel Preview { get; } = new();

    public IReadOnlyList<string> Filters { get; } = new[]
    {
        "All screenshots", "Last 7 days", "Last 30 days", "Older than 30 days",
    };

    [ObservableProperty] private string _selectedFilter = "All screenshots";
    [ObservableProperty] private ScreenshotTile? _selectedTile;
    [ObservableProperty] private string _summary = "Loading screenshots…";
    [ObservableProperty] private string _countText = "0";
    [ObservableProperty] private string _sizeText = "0 B";
    [ObservableProperty] private string _oldText = "0";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoaded;

    public ScreenshotsViewModel(MainViewModel main) => _main = main;

    public void OnActivated() => _ = ReloadAsync();

    public void OnDeactivated()
    {
    }

    partial void OnSelectedFilterChanged(string value)
    {
        if (IsLoaded)
        {
            _ = ReloadAsync();
        }
    }

    partial void OnSelectedTileChanged(ScreenshotTile? value) => _ = Preview.LoadAsync(value?.File);

    public async Task ReloadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            var query = new FileQuery
            {
                ScreenshotsOnly = true,
                Sort = "modified_desc",
                Limit = MaxTiles,
            };

            var now = DateTime.Now;
            switch (SelectedFilter)
            {
                case "Last 7 days":
                    query.From = now.AddDays(-7);
                    break;
                case "Last 30 days":
                    query.From = now.AddDays(-30);
                    break;
                case "Older than 30 days":
                    query.To = now.AddDays(-30);
                    break;
            }

            var selectedPath = SelectedTile?.File.FullPath;
            var (files, total) = await Task.Run(() =>
            {
                var list = AppHost.Database.QueryFiles(query, out var count);
                return (List: list, Total: count);
            }, token).ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            Tiles.Clear();
            foreach (var file in files)
            {
                Tiles.Add(new ScreenshotTile(file));
            }

            CountText = total.ToString("N0");
            SizeText = Formats.Bytes(files.Sum(file => file.Size));
            Summary = total == 0
                ? "No screenshots found yet"
                : $"{total:N0} screenshots  •  {SizeText}  •  showing the newest {files.Count:N0}";

            var oldCutoff = DateTime.Now.AddDays(-30);
            AppHost.Database.QueryFiles(
                new FileQuery { ScreenshotsOnly = true, To = oldCutoff, Limit = 1 }, out var oldCount);
            OldText = oldCount.ToString("N0");

            SelectedTile = Tiles.FirstOrDefault(tile => tile.File.FullPath == selectedPath) ?? Tiles.FirstOrDefault();
            IsLoaded = true;

            _ = LoadThumbnailsAsync(Tiles.ToList(), token);
        }
        catch (OperationCanceledException)
        {
            // Switched filter or left the page.
        }
        catch (Exception ex)
        {
            Log.Error("Screenshots reload failed", ex);
            Summary = "Could not load screenshots: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static async Task LoadThumbnailsAsync(List<ScreenshotTile> tiles, CancellationToken token)
    {
        using var semaphore = new SemaphoreSlim(4);

        var tasks = tiles.Select(async tile =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            await semaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var image = await Task.Run(() => LoadThumbnail(tile.File.FullPath), token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                MainViewModel.Dispatch(() => tile.Thumbnail = image);
            }
            catch (OperationCanceledException)
            {
                // Reloaded.
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            // Individual failures are already ignored.
        }
    }

    private static ImageSource? LoadThumbnail(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.DecodePixelWidth = 320;
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

    // ---------------------------------------------------------------- actions

    [RelayCommand]
    private async Task Refresh() => await ReloadAsync();

    [RelayCommand]
    private void TogglePreview() => Preview.ToggleVisibility();

    [RelayCommand]
    private void Open()
    {
        if (SelectedTile is { } tile)
        {
            _main.Notify(FileOperations.Open(tile.File.FullPath).Message);
        }
    }

    [RelayCommand]
    private void Reveal()
    {
        if (SelectedTile is { } tile)
        {
            _main.Notify(FileOperations.RevealInExplorer(tile.File.FullPath).Message);
        }
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedTile is not { } tile)
        {
            return;
        }

        var confirmed = DialogService.Confirm(
            "Delete screenshot",
            $"Move “{tile.Name}” ({tile.File.SizeText}) to the Recycle Bin?",
            "Move to Recycle Bin");

        if (!confirmed)
        {
            return;
        }

        var result = FileOperations.Recycle(new[] { tile.File.FullPath });
        if (result.Success)
        {
            AppHost.Database.DeleteRow(tile.File.Id);
            Tiles.Remove(tile);
            SelectedTile = Tiles.FirstOrDefault();
            CountText = (int.TryParse(CountText.Replace(",", string.Empty), out var count) ? Math.Max(0, count - 1) : Tiles.Count).ToString("N0");
            SizeText = Formats.Bytes(Tiles.Sum(entry => entry.File.Size));
            _main.Notify(result.Message);
        }
        else
        {
            DialogService.Info("Delete failed", result.Message);
        }
    }

    [RelayCommand]
    private void ProtectSelected()
    {
        if (SelectedTile is not { } tile)
        {
            return;
        }

        AppHost.Database.SetProtected(tile.File.Id, true);
        Tiles.Remove(tile);
        SelectedTile = Tiles.FirstOrDefault();
        _main.Notify("Screenshot protected from cleanup suggestions.");
    }

    [RelayCommand]
    private async Task DeleteOldAsync()
    {
        var cutoff = DateTime.Now.AddDays(-30);
        var ancient = await Task.Run(() => AppHost.Database.QueryFiles(
            new FileQuery { ScreenshotsOnly = true, To = cutoff, Limit = 2000 }, out _)).ConfigureAwait(true);

        if (ancient.Count == 0)
        {
            _main.Notify("No screenshots older than 30 days.");
            return;
        }

        var bytes = ancient.Sum(file => file.Size);
        var confirmed = DialogService.Confirm(
            "Delete old screenshots",
            $"Move {ancient.Count:N0} screenshots older than 30 days ({Formats.Bytes(bytes)}) to the Recycle Bin?",
            "Move to Recycle Bin");

        if (!confirmed)
        {
            return;
        }

        var result = await Task.Run(() => FileOperations.Recycle(ancient.Select(file => file.FullPath).ToList()));
        if (!result.Success)
        {
            DialogService.Info("Delete failed", result.Message);
            return;
        }

        foreach (var file in ancient)
        {
            AppHost.Database.DeleteRow(file.Id);
        }

        _main.Notify(result.Message);
        await ReloadAsync();
        await _main.Dashboard.RefreshSlowAsync();
        _main.UpdateIndexSummary();
    }
}

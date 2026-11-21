using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPulse.Core;
using WinPulse.Models;
using WinPulse.Services;

namespace WinPulse.ViewModels;

public sealed class NavItem
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Glyph { get; init; }
    public required object ViewModel { get; init; }
}

/// <summary>Root view model: owns navigation, live performance sampling, scanning and updates.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _performanceTimer;
    private readonly DispatcherTimer _autoScanTimer;
    private bool _samplingPerformance;
    private int _secondsSinceSlowRefresh;
    private string? _downloadedUpdatePath;
    private CancellationTokenSource? _paletteCts;
    private bool _lowDiskNotified;

    public DashboardViewModel Dashboard { get; }
    public FilesViewModel Files { get; }
    public ScreenshotsViewModel Screenshots { get; }
    public ProcessesViewModel Processes { get; }
    public AppsViewModel Apps { get; }
    public CleanupViewModel Cleanup { get; }
    public SettingsViewModel Settings { get; }
    public ObservableCollection<NavItem> Navigation { get; }

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private string _statusMessage = "Welcome to WinPulse";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private double _scanProgress;
    [ObservableProperty] private string _scanDetail = string.Empty;
    [ObservableProperty] private string _versionText = "v" + AppInfo.Version;
    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private PerformanceSnapshot? _performance;
    [ObservableProperty] private int _indexedFileCount;
    [ObservableProperty] private string _indexedSummary = "Index not built yet";
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private UpdateInfo? _latestUpdate;
    [ObservableProperty] private bool _updatePanelOpen;
    [ObservableProperty] private bool _isCheckingUpdates;
    [ObservableProperty] private bool _isDownloadingUpdate;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private bool _isUpdateReady;
    [ObservableProperty] private string _updateStatus = string.Empty;

    // Command palette
    public ObservableCollection<PaletteItem> PaletteResults { get; } = new();
    [ObservableProperty] private bool _isPaletteOpen;
    [ObservableProperty] private string _paletteQuery = string.Empty;
    [ObservableProperty] private int _paletteSelectedIndex;

    public MainViewModel()
    {
        Dashboard = new DashboardViewModel(this);
        Files = new FilesViewModel(this);
        Screenshots = new ScreenshotsViewModel(this);
        Processes = new ProcessesViewModel();
        Apps = new AppsViewModel();
        Cleanup = new CleanupViewModel(this);
        Settings = new SettingsViewModel(this);

        Navigation = new ObservableCollection<NavItem>
        {
            new() { Title = "Dashboard", Subtitle = "Live system overview", Glyph = "\uE80F", ViewModel = Dashboard },
            new() { Title = "File activity", Subtitle = "Downloads, saves, screenshots and more", Glyph = "\uE8B7", ViewModel = Files },
            new() { Title = "Screenshots", Subtitle = "Everything you captured, in one place", Glyph = "\uE91B", ViewModel = Screenshots },
            new() { Title = "Processes", Subtitle = "What is running on your PC right now", Glyph = "\uE9F5", ViewModel = Processes },
            new() { Title = "Apps", Subtitle = "Installed programs and downloaded installers", Glyph = "\uE71D", ViewModel = Apps },
            new() { Title = "Cleanup", Subtitle = "Reclaim space without the guesswork", Glyph = "\uE74D", ViewModel = Cleanup },
            new() { Title = "Settings", Subtitle = "Folders, scanning and updates", Glyph = "\uE713", ViewModel = Settings },
        };

        _selectedNav = Navigation[0];
        _performanceTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnPerformanceTick, Dispatcher.CurrentDispatcher);
        _autoScanTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher);
        _autoScanTimer.Tick += async (_, _) =>
        {
            if (!IsScanning)
            {
                await RunScanAsync();
            }
        };

        RefreshSchedule();
        IsDarkTheme = ThemeManager.IsDark;
    }

    public void OnStarted()
    {
        _performanceTimer.Start();
        SelectedNav = Navigation[0];
        _ = InitializeAsync();
        _ = SamplePerformanceAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            StatusMessage = "Reading system information…";
            await Dashboard.RefreshAsync();

            var lastScan = AppHost.Settings.LastScan;
            var stale = lastScan is null ||
                        DateTime.Now - lastScan.Value > TimeSpan.FromMinutes(Math.Max(1, AppHost.Settings.ScanIntervalMinutes));

            if (AppHost.Settings.AutoScan && stale)
            {
                await RunScanAsync();
            }
            else
            {
                await Files.ReloadAsync();
                UpdateIndexSummary();
                StatusMessage = lastScan is null ? "Ready. Run a scan to build your index." : "Ready.";
            }

            AppHost.Indexer.RestartWatching();

            if (AppHost.Settings.AutoUpdateCheck)
            {
                _ = CheckUpdatesLaterAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Startup initialization failed", ex);
            Notify("Startup problem: " + ex.Message);
        }
    }

    private async Task CheckUpdatesLaterAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(6)).ConfigureAwait(true);
        await CheckUpdatesCoreAsync(silent: true);
    }

    // ------------------------------------------------------------ performance

    private void OnPerformanceTick(object? sender, EventArgs e) => _ = SamplePerformanceAsync();

    private async Task SamplePerformanceAsync()
    {
        if (_samplingPerformance)
        {
            return;
        }

        _samplingPerformance = true;
        try
        {
            var snapshot = await Task.Run(() => AppHost.Performance.Sample()).ConfigureAwait(true);
            Performance = snapshot;
            Dashboard.UpdatePerformance(snapshot);

            // Low disk warning once per crossing, with tray balloon.
            if (snapshot.DiskFreeBytes > 0 && snapshot.DiskFreeBytes < 15L * 1024 * 1024 * 1024)
            {
                if (!_lowDiskNotified)
                {
                    _lowDiskNotified = true;
                    TrayService.Notify(
                        "Low disk space",
                        $"{snapshot.DiskFreeText} left on your system drive. Cleanup can suggest safe deletions.");
                }
            }
            else if (snapshot.DiskFreeBytes > 20L * 1024 * 1024 * 1024)
            {
                _lowDiskNotified = false;
            }

            _secondsSinceSlowRefresh++;
            if (_secondsSinceSlowRefresh >= 30)
            {
                _secondsSinceSlowRefresh = 0;
                await Dashboard.RefreshSlowAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Performance sampling failed: " + ex.Message);
        }
        finally
        {
            _samplingPerformance = false;
        }
    }

    // ---------------------------------------------------------------- scanning

    [RelayCommand]
    private async Task ScanNow() => await RunScanAsync();

    public async Task RunScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        ScanProgress = 0;
        ScanDetail = "Looking for changes…";
        StatusMessage = "Scanning your watched folders…";

        try
        {
            var progress = new Progress<ScanProgress>(OnScanProgress);
            var result = await AppHost.Indexer.ScanAsync(progress);
            OnScanProgress(result);
            StatusMessage = result.Message;
            await Dashboard.RefreshAsync();
            await Files.ReloadAsync();
            await Settings.RefreshStatsAsync();
            if (Screenshots.IsLoaded)
            {
                await Screenshots.ReloadAsync();
            }

            UpdateIndexSummary();

            if (result.Phase == ScanPhase.Done && result.NewFiles > 0)
            {
                Notify($"{result.NewFiles} new file(s) indexed. Open File activity to review them.");
                await NotifyLargeNewFilesAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Scan failed", ex);
            Notify("Scan failed: " + ex.Message);
        }
        finally
        {
            IsScanning = false;
            ScanDetail = string.Empty;
            ScanProgress = 0;
        }
    }

    private async Task NotifyLargeNewFilesAsync()
    {
        try
        {
            var events = await Task.Run(() => AppHost.Database.GetRecentEvents(15)).ConfigureAwait(true);
            var large = events.FirstOrDefault(activity =>
                activity.Kind == "added" &&
                activity.Size >= 100L * 1024 * 1024 &&
                activity.At > DateTime.Now.AddMinutes(-10));

            if (large is not null)
            {
                TrayService.Notify(
                    "New large file indexed",
                    $"{large.Name}  •  {large.SizeText}",
                    Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not notify about new files: " + ex.Message);
        }
    }

    private void OnScanProgress(ScanProgress progress)
    {
        switch (progress.Phase)
        {
            case ScanPhase.Discovering:
                ScanProgress = progress.RootsTotal > 0 ? progress.RootsDone * 100.0 / progress.RootsTotal : 0;
                ScanDetail = progress.Message;
                break;
            case ScanPhase.Indexing:
                ScanProgress = progress.RootsTotal > 0
                    ? Math.Min(99, (progress.RootsDone + 0.45) * 100.0 / progress.RootsTotal)
                    : 0;
                ScanDetail = $"{progress.Root}: {progress.FilesIndexed:N0} files";
                break;
            case ScanPhase.Finalizing:
                ScanProgress = progress.RootsTotal > 0
                    ? Math.Min(99, (progress.RootsDone + 0.9) * 100.0 / progress.RootsTotal)
                    : 90;
                ScanDetail = progress.Message;
                break;
            case ScanPhase.Done:
                ScanProgress = 100;
                ScanDetail = progress.Message;
                break;
            case ScanPhase.Cancelled:
                ScanDetail = "Scan cancelled.";
                break;
            case ScanPhase.Failed:
                ScanDetail = progress.Message;
                break;
        }
    }

    public void RefreshSchedule()
    {
        _autoScanTimer.Stop();
        if (!AppHost.Settings.AutoScan)
        {
            return;
        }

        _autoScanTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, AppHost.Settings.ScanIntervalMinutes));
        _autoScanTimer.Start();
    }

    public void UpdateIndexSummary()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var stats = AppHost.Database.GetStats(AppHost.Settings.CleanupAgeDays);
                Dispatch(() =>
                {
                    IndexedFileCount = stats.TotalFiles;
                    IndexedSummary = stats.TotalFiles == 0
                        ? "Index not built yet"
                        : $"{stats.TotalFiles:N0} files  •  {stats.TotalSizeText}";
                });
            }
            catch (Exception ex)
            {
                Log.Warn("Could not update index summary: " + ex.Message);
            }
        });
    }

    // ------------------------------------------------------------------- theme

    [RelayCommand]
    private void ToggleTheme() => IsDarkTheme = !IsDarkTheme;

    partial void OnIsDarkThemeChanged(bool value)
    {
        ThemeManager.Apply(value);
        if (AppHost.IsInitialized)
        {
            AppHost.Settings.Theme = value ? "Dark" : "Light";
        }
    }

    // ---------------------------------------------------------------- updates

    [RelayCommand]
    private async Task CheckForUpdates() => await CheckUpdatesCoreAsync(silent: false);

    private async Task CheckUpdatesCoreAsync(bool silent)
    {
        if (IsCheckingUpdates)
        {
            return;
        }

        IsCheckingUpdates = true;
        if (!silent)
        {
            UpdateStatus = "Checking GitHub for a newer release…";
        }

        try
        {
            var result = await AppHost.Updates.CheckAsync().ConfigureAwait(true);
            if (!result.Success)
            {
                UpdateStatus = result.Message;
                if (!silent)
                {
                    Notify(result.Message);
                }

                return;
            }

            LatestUpdate = result.Update;
            UpdateAvailable = result.UpdateAvailable;
            UpdateStatus = result.Message;

            if (!silent)
            {
                UpdatePanelOpen = true;
                Notify(result.UpdateAvailable ? $"Update available: v{result.Update!.Version}" : "WinPulse is up to date.");
            }
            else if (result.UpdateAvailable)
            {
                Notify($"Update available: v{result.Update!.Version}. Open the update panel to install it.");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = ex.Message;
            Log.Error("Update check failed", ex);
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private void OpenUpdatePanel() => UpdatePanelOpen = true;

    [RelayCommand]
    private void CloseUpdatePanel() => UpdatePanelOpen = false;

    [RelayCommand]
    private async Task DownloadUpdate()
    {
        if (LatestUpdate is null || IsDownloadingUpdate)
        {
            return;
        }

        IsDownloadingUpdate = true;
        IsUpdateReady = false;
        DownloadProgress = 0;
        UpdateStatus = "Starting download…";

        try
        {
            var progress = new Progress<double>(percent => DownloadProgress = percent);
            var result = await AppHost.Updates.DownloadAsync(LatestUpdate, progress).ConfigureAwait(true);
            if (!result.Success)
            {
                UpdateStatus = result.Message;
                Notify(result.Message);
                return;
            }

            _downloadedUpdatePath = result.FilePath;
            IsUpdateReady = true;
            UpdateStatus = "Update downloaded. Restart WinPulse to finish installing.";
            Notify(result.Message);
        }
        catch (Exception ex)
        {
            UpdateStatus = "Download failed: " + ex.Message;
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    [RelayCommand]
    private void InstallUpdate()
    {
        if (string.IsNullOrWhiteSpace(_downloadedUpdatePath))
        {
            return;
        }

        var result = AppHost.Updates.ApplyAndRestart(_downloadedUpdatePath);
        UpdateStatus = result.Message;
        Notify(result.Message);
        if (result.Success)
        {
            Application.Current.Shutdown();
        }
    }

    [RelayCommand]
    private void OpenGitHub() => OpenUrl(AppInfo.GitHubUrl);

    [RelayCommand]
    private void OpenReleases() => OpenUrl(AppInfo.GitHubUrl + "/releases/latest");

    [RelayCommand]
    private void Navigate(string? pageTitle)
    {
        var target = Navigation.FirstOrDefault(item =>
            item.Title.Equals(pageTitle, StringComparison.OrdinalIgnoreCase));
        if (target is not null)
        {
            SelectedNav = target;
        }
    }

    public void NavigateTo(string pageTitle) => Navigate(pageTitle);

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null)
        {
            return;
        }

        foreach (var item in Navigation)
        {
            if (ReferenceEquals(item, value))
            {
                (item.ViewModel as IActivatableViewModel)?.OnActivated();
            }
            else
            {
                (item.ViewModel as IActivatableViewModel)?.OnDeactivated();
            }
        }
    }

    // ------------------------------------------------------- command palette

    private static readonly PaletteItem[] PaletteCommands =
    {
        new() { Title = "Dashboard", Subtitle = "Page", Glyph = "\uE80F", Kind = "page" },
        new() { Title = "File activity", Subtitle = "Page", Glyph = "\uE8B7", Kind = "page" },
        new() { Title = "Screenshots", Subtitle = "Page", Glyph = "\uE91B", Kind = "page" },
        new() { Title = "Processes", Subtitle = "Page", Glyph = "\uE9F5", Kind = "page" },
        new() { Title = "Apps", Subtitle = "Page", Glyph = "\uE71D", Kind = "page" },
        new() { Title = "Cleanup", Subtitle = "Page", Glyph = "\uE74D", Kind = "page" },
        new() { Title = "Settings", Subtitle = "Page", Glyph = "\uE713", Kind = "page" },
        new() { Title = "Scan now", Subtitle = "Refresh the file index", Glyph = "\uE72C", Kind = "command", CommandId = "scan" },
        new() { Title = "Check for updates", Subtitle = "GitHub release check", Glyph = "\uE895", Kind = "command", CommandId = "updates" },
        new() { Title = "Toggle theme", Subtitle = "Switch dark / light", Glyph = "\uE793", Kind = "command", CommandId = "theme" },
        new() { Title = "Open Downloads folder", Subtitle = "Explorer", Glyph = "\uE8B7", Kind = "command", CommandId = "downloads" },
        new() { Title = "Open data folder", Subtitle = "Index, logs and updates", Glyph = "\uE8DA", Kind = "command", CommandId = "data" },
        new() { Title = "Open logs", Subtitle = "Troubleshooting", Glyph = "\uE7C3", Kind = "command", CommandId = "logs" },
    };

    [RelayCommand]
    private void OpenPalette()
    {
        PaletteQuery = string.Empty;
        IsPaletteOpen = true;
        Log.Info("Command palette opened.");
        _ = UpdatePaletteAsync(string.Empty);
    }

    [RelayCommand]
    private void ClosePalette()
    {
        IsPaletteOpen = false;
        PaletteQuery = string.Empty;
    }

    partial void OnPaletteQueryChanged(string value) => _ = DebouncePaletteAsync(value);

    private async Task DebouncePaletteAsync(string query)
    {
        _paletteCts?.Cancel();
        var cts = _paletteCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(140, cts.Token).ConfigureAwait(true);
            await UpdatePaletteAsync(query).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke.
        }
    }

    private async Task UpdatePaletteAsync(string query)
    {
        var trimmed = query.Trim();
        var results = new List<PaletteItem>();

        var commands = PaletteCommands
            .Where(item => trimmed.Length == 0 ||
                           item.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                           item.Subtitle.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .Take(trimmed.Length == 0 ? 7 : 6);

        results.AddRange(commands);

        try
        {
            List<FileEntry> files;
            if (trimmed.Length == 0)
            {
                files = await Task.Run(() => AppHost.Database.QueryRecentFiles(5)).ConfigureAwait(true);
            }
            else if (trimmed.Length >= 2)
            {
                var fileQuery = new FileQuery { Search = trimmed, Limit = 8, Sort = "modified_desc" };
                files = await Task.Run(() => AppHost.Database.QueryFiles(fileQuery, out _)).ConfigureAwait(true);
            }
            else
            {
                files = new List<FileEntry>();
            }

            results.AddRange(files.Select(file => new PaletteItem
            {
                Title = file.Name,
                Subtitle = $"{file.SizeText}  •  {file.ModifiedRelative}  •  {file.RootLabel}",
                Glyph = file.Glyph,
                Kind = "file",
                File = file,
            }));
        }
        catch (Exception ex)
        {
            Log.Warn("Palette file search failed: " + ex.Message);
        }

        if (!IsPaletteOpen)
        {
            return;
        }

        PaletteResults.Clear();
        foreach (var item in results)
        {
            PaletteResults.Add(item);
        }

        PaletteSelectedIndex = PaletteResults.Count > 0 ? 0 : -1;
    }

    public void MovePaletteSelection(int delta)
    {
        if (PaletteResults.Count == 0)
        {
            return;
        }

        PaletteSelectedIndex = Math.Clamp(PaletteSelectedIndex + delta, 0, PaletteResults.Count - 1);
    }

    public void ExecutePaletteSelection()
    {
        var item = PaletteResults.ElementAtOrDefault(PaletteSelectedIndex);
        if (item is null)
        {
            return;
        }

        ClosePalette();

        if (item.Kind == "file" && item.File is not null)
        {
            Files.SearchText = item.File.Name;
            Navigate("File activity");
            return;
        }

        if (item.Kind == "page")
        {
            Navigate(item.Title);
            return;
        }

        switch (item.CommandId)
        {
            case "scan":
                _ = RunScanAsync();
                break;
            case "updates":
                _ = CheckUpdatesCoreAsync(silent: false);
                break;
            case "theme":
                ToggleTheme();
                break;
            case "downloads":
                var downloads = KnownFolders.Get("Downloads");
                if (downloads is not null)
                {
                    FileOperations.OpenFolder(downloads);
                }

                break;
            case "data":
                FileOperations.OpenFolder(AppPaths.DataDirectory);
                break;
            case "logs":
                FileOperations.RevealInExplorer(AppPaths.LogFile);
                break;
        }
    }

    // ---------------------------------------------------------------- helpers

    public void Notify(string message)
    {
        Dispatch(() => StatusMessage = message);
        Log.Info(message);
    }

    public static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {url}: {ex.Message}");
        }
    }
}

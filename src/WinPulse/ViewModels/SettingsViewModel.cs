using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WinPulse.Core;
using WinPulse.Models;
using WinPulse.Services;

namespace WinPulse.ViewModels;

/// <summary>Settings: watched folders, scanning cadence, startup, updates and data.</summary>
public sealed partial class SettingsViewModel : ObservableObject, IActivatableViewModel
{
    private readonly MainViewModel _main;

    public ObservableCollection<RootFolder> Roots { get; } = new();
    public IReadOnlyList<int> ScanIntervals { get; } = new[] { 1, 5, 10, 15, 30, 60 };

    [ObservableProperty] private int _scanIntervalMinutes;
    [ObservableProperty] private bool _autoScan;
    [ObservableProperty] private bool _watchFolders;
    [ObservableProperty] private bool _autoUpdate;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private string _githubToken = string.Empty;
    [ObservableProperty] private IndexStats _stats = new();
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusNote = "Changes are saved automatically.";

    public MainViewModel Main => _main;
    public string VersionText => "WinPulse " + AppInfo.Version;
    public string DataFolderText => AppPaths.DataDirectory;
    public string LogFileText => AppPaths.LogFile;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _scanIntervalMinutes = AppHost.Settings.ScanIntervalMinutes;
        _autoScan = AppHost.Settings.AutoScan;
        _watchFolders = AppHost.Settings.WatchFolders;
        _autoUpdate = AppHost.Settings.AutoUpdateCheck;
        _startWithWindows = AppHost.Settings.StartWithWindows;
        _githubToken = AppHost.Settings.GitHubToken;
        _ = RefreshAsync();
    }

    public void OnActivated() => _ = RefreshAsync();

    public void OnDeactivated()
    {
    }

    public async Task RefreshAsync()
    {
        try
        {
            var roots = await Task.Run(() => AppHost.Database.GetRoots()).ConfigureAwait(true);
            Roots.Clear();
            foreach (var root in roots)
            {
                Roots.Add(root);
            }

            await RefreshStatsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warn("Settings refresh failed: " + ex.Message);
        }
    }

    public async Task RefreshStatsAsync()
    {
        try
        {
            Stats = await Task.Run(() => AppHost.Database.GetStats(AppHost.Settings.CleanupAgeDays)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not refresh index stats: " + ex.Message);
        }
    }

    // ------------------------------------------------------------- persistence

    partial void OnScanIntervalMinutesChanged(int value)
    {
        AppHost.Settings.ScanIntervalMinutes = value;
        _main.RefreshSchedule();
        StatusNote = $"Scanning every {value} minutes.";
    }

    partial void OnAutoScanChanged(bool value)
    {
        AppHost.Settings.AutoScan = value;
        _main.RefreshSchedule();
        StatusNote = value ? "Automatic scanning enabled." : "Automatic scanning disabled.";
    }

    partial void OnWatchFoldersChanged(bool value)
    {
        AppHost.Settings.WatchFolders = value;
        AppHost.Indexer.RestartWatching();
        StatusNote = value ? "Watching folders for changes." : "Live folder watching disabled.";
    }

    partial void OnAutoUpdateChanged(bool value)
    {
        AppHost.Settings.AutoUpdateCheck = value;
        StatusNote = value ? "WinPulse will check for updates on launch." : "Automatic update checks disabled.";
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        AppHost.Settings.StartWithWindows = value;
        StatusNote = value ? "WinPulse will start with Windows." : "Startup entry removed.";
    }

    // ---------------------------------------------------------------- commands

    [RelayCommand]
    private async Task Refresh() => await RefreshAsync();

    [RelayCommand]
    private async Task AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Add a folder for WinPulse to track", Multiselect = false };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var path = dialog.FolderName;
        if (Roots.Any(root => root.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            StatusNote = "That folder is already tracked.";
            return;
        }

        var label = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(label))
        {
            label = path;
        }

        AppHost.Database.AddRoot(path, label);
        await RefreshAsync();
        StatusNote = $"Added “{label}” — scanning now.";
        await _main.RunScanAsync();
    }

    [RelayCommand]
    private async Task RemoveRoot(RootFolder? root)
    {
        if (root is null)
        {
            return;
        }

        var confirmed = DialogService.Confirm(
            "Stop tracking folder",
            $"Stop tracking “{root.Label}” and remove its indexed files from WinPulse?\n\nNo files on disk are touched.",
            "Stop tracking");

        if (!confirmed)
        {
            return;
        }

        AppHost.Database.RemoveRoot(root.Id);
        await RefreshAsync();
        await _main.Files.ReloadRootsAsync();
        _main.Notify($"Stopped tracking {root.Label}.");
    }

    [RelayCommand]
    private async Task ToggleRoot(RootFolder? root)
    {
        if (root is null)
        {
            return;
        }

        AppHost.Database.SetRootEnabled(root.Id, !root.Enabled);
        await RefreshAsync();
        StatusNote = root.Enabled ? $"Paused {root.Label}." : $"Resumed {root.Label}.";
    }

    [RelayCommand]
    private async Task ClearIndex()
    {
        var confirmed = DialogService.Confirm(
            "Clear the index",
            "Delete WinPulse's local index of files? Your actual files are not touched. A new scan will rebuild the index.",
            "Clear index");

        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            AppHost.Database.ClearIndex();
            await RefreshStatsAsync();
            await _main.Files.ReloadAsync();
            await _main.Dashboard.RefreshAsync();
            _main.UpdateIndexSummary();
            _main.Notify("Index cleared.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenDataFolder() => FileOperations.OpenFolder(AppPaths.DataDirectory);

    [RelayCommand]
    private void OpenLogs()
    {
        if (File.Exists(AppPaths.LogFile))
        {
            FileOperations.RevealInExplorer(AppPaths.LogFile);
        }
        else
        {
            FileOperations.OpenFolder(AppPaths.LogDirectory);
        }
    }

    [RelayCommand]
    private void SaveToken()
    {
        AppHost.Settings.GitHubToken = GithubToken;
        StatusNote = string.IsNullOrWhiteSpace(GithubToken)
            ? "GitHub token cleared."
            : "GitHub token saved locally.";
    }

    [RelayCommand]
    private void CheckUpdates() => _main.CheckForUpdatesCommand.Execute(null);

    [RelayCommand]
    private void OpenRepo() => _main.OpenGitHubCommand.Execute(null);
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WinPulse.Core;
using WinPulse.Models;
using WinPulse.Services;

namespace WinPulse.ViewModels;

/// <summary>Dashboard: hardware summary, live gauges, storage mix and recent activity.</summary>
public sealed partial class DashboardViewModel : ObservableObject, IActivatableViewModel
{
    private readonly MainViewModel _main;

    [ObservableProperty] private SystemSpecs _specs = new();
    [ObservableProperty] private IndexStats _stats = new();
    [ObservableProperty] private string _cpuPercentText = "0%";
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private string _ramPercentText = "0%";
    [ObservableProperty] private double _ramPercent;
    [ObservableProperty] private string _ramDetail = "—";
    [ObservableProperty] private string _diskFreeText = "—";
    [ObservableProperty] private string _diskTotalText = "—";
    [ObservableProperty] private string _diskUsedPercentText = "—";
    [ObservableProperty] private double _diskUsedPercent;
    [ObservableProperty] private string _uptimeText = "—";
    [ObservableProperty] private string _processCountText = "—";
    [ObservableProperty] private string _lastScanText = "Never";
    [ObservableProperty] private string _diskHistorySummary = "Collecting history…";
    [ObservableProperty] private string _diskHistoryCaption = string.Empty;
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<DiskSpec> Disks { get; } = new();
    public ObservableCollection<ActivityEvent> RecentActivity { get; } = new();
    public ObservableCollection<CategoryStat> Categories { get; } = new();
    public ObservableCollection<FolderStat> TopFolders { get; } = new();
    public ObservableCollection<double> CpuHistory { get; } = new();
    public ObservableCollection<double> RamHistory { get; } = new();
    public ObservableCollection<double> DiskHistory { get; } = new();

    public DashboardViewModel(MainViewModel main) => _main = main;

    public void OnActivated() => _ = RefreshAsync();

    public void OnDeactivated()
    {
    }

    public async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var specs = await Task.Run(() => AppHost.SystemInfo.GetSpecs()).ConfigureAwait(true);
            Specs = specs;
            Disks.Clear();
            foreach (var disk in specs.Disks)
            {
                Disks.Add(disk);
            }

            await RefreshSlowAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error("Dashboard refresh failed", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RefreshSlowAsync()
    {
        try
        {
            var stats = await Task.Run(() => AppHost.Database.GetStats(AppHost.Settings.CleanupAgeDays)).ConfigureAwait(true);
            Stats = stats;
            LastScanText = stats.LastScanText;

            var events = await Task.Run(() => AppHost.Database.GetRecentEvents(7)).ConfigureAwait(true);
            RecentActivity.Clear();
            foreach (var activity in events)
            {
                RecentActivity.Add(activity);
            }

            var categories = await Task.Run(() => AppHost.Database.GetCategoryBreakdown()).ConfigureAwait(true);
            Categories.Clear();
            foreach (var category in categories)
            {
                Categories.Add(category);
            }

            var folders = await Task.Run(() => AppHost.Database.GetTopFolders(6)).ConfigureAwait(true);
            TopFolders.Clear();
            foreach (var folder in folders)
            {
                TopFolders.Add(folder);
            }

            var history = await Task.Run(() => AppHost.Database.GetDiskHistory(DateTime.Now.AddDays(-7))).ConfigureAwait(true);
            DiskHistory.Clear();
            foreach (var sample in history)
            {
                DiskHistory.Add(sample.FreeGb);
            }

            DiskHistoryCaption = history.Count == 0
                ? "No samples yet"
                : $"Last 7 days  •  {history.Count:N0} samples";

            if (history.Count >= 2)
            {
                var first = history[0];
                var last = history[^1];
                var delta = last.FreeBytes - first.FreeBytes;
                var direction = delta >= 0 ? "+" : "−";
                DiskHistorySummary =
                    $"{last.FreeText} free now  •  {direction}{Formats.Bytes(Math.Abs(delta))} since {first.At:MMM d, HH:mm}";
            }
            else if (history.Count == 1)
            {
                DiskHistorySummary = $"{history[0].FreeText} free  •  history builds every 5 minutes while WinPulse runs";
            }
            else
            {
                DiskHistorySummary = "History builds every 5 minutes while WinPulse runs.";
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Dashboard stats refresh failed: " + ex.Message);
        }
    }

    public void UpdatePerformance(PerformanceSnapshot snapshot)
    {
        CpuPercent = snapshot.CpuPercent;
        CpuPercentText = snapshot.CpuPercentText;
        RamPercent = snapshot.RamPercent;
        RamPercentText = snapshot.RamPercentText;
        RamDetail = $"{snapshot.RamUsedText}  •  {Formats.Bytes(snapshot.RamAvailableBytes)} free";
        DiskFreeText = snapshot.DiskFreeText;
        DiskTotalText = snapshot.DiskTotalText;
        DiskUsedPercentText = $"{Math.Clamp(100 - snapshot.DiskFreePercent, 0, 100):0}% used";
        DiskUsedPercent = Math.Clamp(100 - snapshot.DiskFreePercent, 0, 100);
        UptimeText = Specs.BootTime == default
            ? "—"
            : Formats.Duration(DateTime.Now - Specs.BootTime);
        ProcessCountText = snapshot.ProcessCount.ToString("N0");
        LastScanText = AppHost.Settings.LastScan is { } last ? Formats.Relative(last) : "Never";

        Push(CpuHistory, snapshot.CpuPercent);
        Push(RamHistory, snapshot.RamPercent);
    }

    private static void Push(ObservableCollection<double> values, double value)
    {
        values.Add(value);
        while (values.Count > 60)
        {
            values.RemoveAt(0);
        }
    }
}

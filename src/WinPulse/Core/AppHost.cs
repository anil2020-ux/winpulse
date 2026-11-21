using WinPulse.Core;
using WinPulse.Services;

namespace WinPulse;

/// <summary>Composition root for app-wide services. Initialized once from <see cref="App"/>.</summary>
public static class AppHost
{
    public static Database Database { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;
    public static SystemInfoService SystemInfo { get; private set; } = null!;
    public static PerformanceService Performance { get; private set; } = null!;
    public static ShellIconService ShellIcons { get; private set; } = null!;
    public static ProcessService Processes { get; private set; } = null!;
    public static InstalledAppsService InstalledApps { get; private set; } = null!;
    public static FileIndexer Indexer { get; private set; } = null!;
    public static CleanupService Cleanup { get; private set; } = null!;
    public static UpdateService Updates { get; private set; } = null!;
    public static PreviewService Preview { get; private set; } = null!;
    public static bool IsInitialized { get; private set; }

    public static void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }

        AppPaths.EnsureCreated();
        Database = new Database(AppPaths.DatabasePath);
        Database.Initialize();

        Settings = new SettingsService(Database);
        SystemInfo = new SystemInfoService();
        Performance = new PerformanceService();
        ShellIcons = new ShellIconService();
        Processes = new ProcessService();
        InstalledApps = new InstalledAppsService(Database);
        Indexer = new FileIndexer(Database, Settings);
        Cleanup = new CleanupService(Database);
        Updates = new UpdateService(Settings);
        Preview = new PreviewService();

        ThemeManager.Apply(Settings.Theme);
        IsInitialized = true;
        Log.Info("Services initialized.");
    }

    public static void Shutdown()
    {
        try
        {
            Indexer?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Indexer shutdown: " + ex.Message);
        }

        try
        {
            Database?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Database shutdown: " + ex.Message);
        }
    }
}

using System.IO;

namespace WinPulse.Core;

/// <summary>Well-known locations used by WinPulse at runtime.</summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinPulse");

    public static string DatabasePath => Path.Combine(DataDirectory, "winpulse.db");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
    public static string LogFile => Path.Combine(LogDirectory, "app.log");
    public static string UpdateDirectory => Path.Combine(DataDirectory, "update");
    public static string BackupDirectory => Path.Combine(DataDirectory, "backups");

    /// <summary>Folder that contains the running WinPulse.exe (install directory).</summary>
    public static string InstallDirectory =>
        Path.GetDirectoryName(ExecutablePath) ?? AppContext.BaseDirectory;

    public static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WinPulse.exe");

    /// <summary>True when running from the per-user install folder used by the updater.</summary>
    public static bool IsInstalledBuild
    {
        get
        {
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "WinPulse");
            return string.Equals(
                Path.TrimEndingDirectorySeparator(InstallDirectory),
                Path.TrimEndingDirectorySeparator(expected),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(UpdateDirectory);
    }
}

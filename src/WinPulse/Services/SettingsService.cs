using Microsoft.Win32;
using WinPulse.Core;

namespace WinPulse.Services;

/// <summary>Typed access to persisted user preferences (stored in SQLite).</summary>
public sealed class SettingsService
{
    private readonly Database _db;

    public SettingsService(Database db) => _db = db;

    public int ScanIntervalMinutes
    {
        get => ParseInt(_db.GetSetting("scan_interval"), 10, 1, 240);
        set => _db.SetSetting("scan_interval", value.ToString());
    }

    public bool AutoScan
    {
        get => ParseBool(_db.GetSetting("auto_scan"), true);
        set => _db.SetSetting("auto_scan", value ? "1" : "0");
    }

    public bool AutoUpdateCheck
    {
        get => ParseBool(_db.GetSetting("auto_update_check"), true);
        set => _db.SetSetting("auto_update_check", value ? "1" : "0");
    }

    public bool WatchFolders
    {
        get => ParseBool(_db.GetSetting("watch_folders"), true);
        set => _db.SetSetting("watch_folders", value ? "1" : "0");
    }

    public bool StartWithWindows
    {
        get => StartupService.IsEnabled();
        set => StartupService.SetEnabled(value);
    }

    /// <summary>Keep running in the system tray when the window is closed.</summary>
    public bool CloseToTray
    {
        get => ParseBool(_db.GetSetting("close_to_tray"), true);
        set => _db.SetSetting("close_to_tray", value ? "1" : "0");
    }

    public string Theme
    {
        get => _db.GetSetting("theme") ?? "Dark";
        set => _db.SetSetting("theme", value);
    }

    /// <summary>Optional GitHub token, only needed when the repo/releases are private.</summary>
    public string GitHubToken
    {
        get => _db.GetSetting("github_token") ?? string.Empty;
        set => _db.SetSetting("github_token", string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public int CleanupAgeDays
    {
        get => ParseInt(_db.GetSetting("cleanup_age_days"), 30, 1, 3650);
        set => _db.SetSetting("cleanup_age_days", value.ToString());
    }

    public long LargeFileThresholdBytes
    {
        get => ParseLong(_db.GetSetting("large_file_bytes"), 100L * 1024 * 1024);
        set => _db.SetSetting("large_file_bytes", value.ToString());
    }

    public DateTime? LastScan
    {
        get
        {
            var raw = _db.GetSetting("last_scan_utc");
            return string.IsNullOrWhiteSpace(raw) ? null : DbTime.FromDb(raw);
        }
    }

    public void MarkScanCompleted() => _db.SetSetting("last_scan_utc", DbTime.NowDb());

    private static int ParseInt(string? raw, int fallback, int min, int max)
    {
        return int.TryParse(raw, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;
    }

    private static long ParseLong(string? raw, long fallback)
    {
        return long.TryParse(raw, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static bool ParseBool(string? raw, bool fallback) => raw switch
    {
        "1" or "true" or "True" => true,
        "0" or "false" or "False" => false,
        _ => fallback,
    };
}

/// <summary>Per-user "start with Windows" registration (HKCU Run key, no admin needed).</summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WinPulse";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                var command = $"\"{AppPaths.ExecutablePath}\" --minimized";
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not update startup registration", ex);
        }
    }
}

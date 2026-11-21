using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;
using WinPulse.Core;
using WinPulse.Models;

namespace WinPulse.Services;

/// <summary>Reads the uninstall registry so WinPulse can show installed programs.</summary>
public sealed class InstalledAppsService
{
    private readonly Database _db;

    public InstalledAppsService(Database db) => _db = db;

    public List<InstalledApp> GetInstalledApps()
    {
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

        ReadHive(RegistryHive.LocalMachine, RegistryView.Registry64, apps);
        ReadHive(RegistryHive.LocalMachine, RegistryView.Registry32, apps);
        ReadHive(RegistryHive.CurrentUser, RegistryView.Default, apps);

        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public List<FileEntry> GetInstallerFiles(int limit = 200) => _db.QueryInstallers(limit);

    private static void ReadHive(RegistryHive hive, RegistryView view, Dictionary<string, InstalledApp> apps)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null)
            {
                return;
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var subKey = uninstall.OpenSubKey(subKeyName);
                    if (subKey is null)
                    {
                        continue;
                    }

                    var name = (subKey.GetValue("DisplayName") as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (Convert.ToInt32(subKey.GetValue("SystemComponent") ?? 0, CultureInfo.InvariantCulture) == 1)
                    {
                        continue;
                    }

                    if (subKey.GetValue("ParentKeyName") is string)
                    {
                        continue; // Windows updates are not apps.
                    }

                    var releaseType = subKey.GetValue("ReleaseType") as string;
                    if (string.Equals(releaseType, "Update", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(releaseType, "Hotfix", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (apps.ContainsKey(name))
                    {
                        continue;
                    }

                    apps[name] = new InstalledApp
                    {
                        Name = name,
                        Version = subKey.GetValue("DisplayVersion") as string,
                        Publisher = subKey.GetValue("Publisher") as string,
                        InstallDate = ParseInstallDate(subKey.GetValue("InstallDate") as string),
                        SizeBytes = ParseSize(subKey.GetValue("EstimatedSize")),
                        UninstallString = subKey.GetValue("UninstallString") as string,
                        QuietUninstallString = subKey.GetValue("QuietUninstallString") as string,
                        InstallLocation = subKey.GetValue("InstallLocation") as string,
                        DisplayIcon = NormalizeIconPath(subKey.GetValue("DisplayIcon") as string),
                    };
                }
                catch
                {
                    // Skip malformed entries.
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read uninstall keys for {hive}/{view}: {ex.Message}");
        }
    }

    private static long ParseSize(object? raw)
    {
        var kb = raw switch
        {
            int value => value,
            long value => value,
            string text when long.TryParse(text, out var parsed) => parsed,
            _ => 0L,
        };

        return kb <= 0 ? 0 : kb * 1024L;
    }

    private static DateTime? ParseInstallDate(string? raw)
    {
        if (raw is { Length: 8 } &&
            DateTime.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return null;
    }

    private static string? NormalizeIconPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim().Trim('"');
        var comma = value.LastIndexOf(',');
        if (comma > 2)
        {
            value = value[..comma];
        }

        return value.Trim('"');
    }

    public static OperationResult Uninstall(InstalledApp app)
    {
        var command = !string.IsNullOrWhiteSpace(app.QuietUninstallString)
            ? app.QuietUninstallString!
            : app.UninstallString;

        if (string.IsNullOrWhiteSpace(command))
        {
            return new OperationResult(false, "This app does not provide an uninstall command.");
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + command + "\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
            });

            return new OperationResult(true, $"Launched the uninstaller for {app.Name}.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, ex.Message);
        }
    }
}

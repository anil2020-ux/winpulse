using System.Reflection;
using System.Runtime.InteropServices;

namespace WinPulse.Core;

/// <summary>Bring an already-running WinPulse window to the foreground.</summary>
public static class WindowActivator
{
    private const int SwRestore = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static void ActivateExisting()
    {
        try
        {
            var handle = FindWindow(null, "WinPulse");
            if (handle == IntPtr.Zero)
            {
                return;
            }

            ShowWindow(handle, SwRestore);
            SetForegroundWindow(handle);
        }
        catch
        {
            // Best effort only.
        }
    }
}

/// <summary>Assembly metadata exposed to the UI and the updater.</summary>
public static class AppInfo
{
    public const string ProductName = "WinPulse";
    public const string GitHubOwner = "anil2020-ux";
    public const string GitHubRepo = "winpulse";
    public const string GitHubUrl = "https://github.com/" + GitHubOwner + "/" + GitHubRepo;

    public static string Version { get; } = ReadVersion();
    public static Version SemanticVersion { get; } = ParseVersion(Version);

    private static string ReadVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return asm.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    private static Version ParseVersion(string text)
    {
        var cleaned = (text ?? string.Empty).Trim().TrimStart('v', 'V');
        return System.Version.TryParse(cleaned, out var parsed) ? parsed : new Version(1, 0, 0);
    }
}

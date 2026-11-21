using System.Runtime.InteropServices;
using WinPulse.Core;

namespace WinPulse;

/// <summary>
/// Headless update CLI used for verification and automation:
///   WinPulse.exe --check-updates   prints current/latest version
///   WinPulse.exe --update          downloads and stages the update, then exits
/// </summary>
public static class UpdateCli
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    private static readonly object ProgressGate = new();
    private static int LastReportedPercent;

    public static int Run(string[] args)
    {
        AttachConsole(-1);
        var install = args.Any(argument => argument.Equals("--update", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"WinPulse {AppInfo.Version} update check");
        var service = new Services.UpdateService(AppHost.Settings);
        var result = service.CheckAsync().GetAwaiter().GetResult();

        if (!result.Success)
        {
            Console.WriteLine("FAILED: " + result.Message);
            return 1;
        }

        Console.WriteLine("Repository: " + AppInfo.GitHubUrl);
        Console.WriteLine("Latest: " + (result.Update?.Version ?? "unknown"));
        Console.WriteLine(result.UpdateAvailable ? "UPDATE AVAILABLE" : "UP TO DATE");

        if (!install || !result.UpdateAvailable || result.Update is null)
        {
            return 0;
        }

        var progress = new Progress<double>(percent =>
        {
            var rounded = (int)Math.Round(percent / 10) * 10;
            if (rounded <= 0)
            {
                return;
            }

            lock (ProgressGate)
            {
                if (rounded <= LastReportedPercent)
                {
                    return;
                }

                LastReportedPercent = rounded;
            }

            Console.WriteLine($"Downloading: {rounded}%");
        });

        var download = service.DownloadAsync(result.Update, progress).GetAwaiter().GetResult();
        if (!download.Success || download.FilePath is null)
        {
            Console.WriteLine("FAILED: " + download.Message);
            return 1;
        }

        Console.WriteLine(download.Message);
        var apply = service.ApplyAndRestart(download.FilePath);
        Console.WriteLine(apply.Message);
        return apply.Success ? 0 : 1;
    }
}

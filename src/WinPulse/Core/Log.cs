using System.IO;
using System.Text;

namespace WinPulse.Core;

/// <summary>Simple rolling file logger so crashes are diagnosable in the field.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public static void Install()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            var info = new FileInfo(AppPaths.LogFile);
            if (info.Exists && info.Length > MaxBytes)
            {
                var old = Path.Combine(AppPaths.LogDirectory, "app.previous.log");
                File.Delete(old);
                File.Move(info.FullName, old);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }

        Info($"=== WinPulse {Core.AppInfo.Version} started on {Environment.OSVersion} ===");
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
              .Append(" [").Append(level).Append("] ")
              .Append(message);
            if (ex is not null)
            {
                sb.AppendLine().Append(ex);
            }

            lock (Gate)
            {
                File.AppendAllText(AppPaths.LogFile, sb.ToString() + Environment.NewLine);
            }
        }
        catch
        {
            // Ignore logging failures.
        }
    }
}

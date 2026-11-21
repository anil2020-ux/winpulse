using System.Runtime.InteropServices;
using System.Text;
using WinPulse.Core;
using WinPulse.Models;
using WinPulse.Services;

namespace WinPulse;

/// <summary>
/// Headless verification suite (dotnet run -- --selftest). Exercises the database,
/// scanner, filtering, file operations, previews, cleanup and updater comparison.
/// </summary>
public static class SelfTest
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    private const int AttachParentProcess = -1;

    public static int Run(string[] args)
    {
        AttachConsole(AttachParentProcess);

        var failures = new List<string>();
        var passed = 0;

        void Check(string name, bool ok, string detail = "")
        {
            if (ok)
            {
                passed++;
                Console.WriteLine($"  PASS  {name}");
            }
            else
            {
                failures.Add(name + (detail.Length > 0 ? ": " + detail : string.Empty));
                Console.WriteLine($"  FAIL  {name} {detail}");
            }
        }

        Console.WriteLine($"WinPulse self-test {AppInfo.Version}");

        try
        {
            Check("byte formatting", Formats.Bytes(1536) == "1.5 KB", Formats.Bytes(1536));
            Check("zero bytes", Formats.Bytes(0) == "0 B");
            Check("image category", FileKinds.CategoryOf(".png") == FileKinds.Image);
            Check("installer category", FileKinds.CategoryOf(".msi") == FileKinds.Installer);
            Check("screenshot detection", FileKinds.IsScreenshot("Screenshot 2026-05-01 101010.png", null, ".png"));
            Check("non-screenshot image", !FileKinds.IsScreenshot("vacation.png", null, ".png"));
            Check("newer version", UpdateService.IsNewer("1.0.0", "1.0.1"));
            Check("same version", !UpdateService.IsNewer("1.0.0", "1.0.0"));

            var sandbox = Path.Combine(Path.GetTempPath(), "WinPulseSelfTest-" + Guid.NewGuid().ToString("N"));
            var dataDirectory = Path.Combine(sandbox, "watched");
            var copyDirectory = Path.Combine(sandbox, "copy");
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(copyDirectory);

            File.WriteAllText(Path.Combine(dataDirectory, "Screenshot 2026-05-01 101010.png"), new string('x', 2000));
            File.WriteAllText(Path.Combine(dataDirectory, "report.pdf"), new string('y', 1_200_000));
            File.WriteAllText(Path.Combine(dataDirectory, "setup.exe"), new string('z', 9000));
            File.WriteAllText(Path.Combine(dataDirectory, "notes.txt"), "hello winpulse");

            using var db = new Database(Path.Combine(sandbox, "test.db"));
            db.Initialize(seedDefaults: false);
            db.AddRoot(dataDirectory, "TestRoot");

            var settings = new SettingsService(db);
            using var indexer = new FileIndexer(db, settings);
            var scan = indexer.ScanAsync().GetAwaiter().GetResult();
            Check("initial scan completes", scan.Phase == ScanPhase.Done, scan.Message);
            Check("initial scan counted files", scan.FilesIndexed == 4, scan.FilesIndexed.ToString());

            var allFiles = db.QueryFiles(new FileQuery { Limit = 100 }, out var total);
            Check("query returns all files", total == 4, total.ToString());
            Check("screenshot flagged", allFiles.Any(file => file.IsScreenshot && file.Name.StartsWith("Screenshot")));
            var installers = db.QueryFiles(new FileQuery { InstallersOnly = true }, out var installerTotal);
            Check("installer listed", installers.Count == 1 && installerTotal == 1);
            Check("search works", db.QueryFiles(new FileQuery { Search = "report" }, out var searchTotal).Count == 1 && searchTotal == 1);
            Check("size filter works", db.QueryFiles(new FileQuery { MinSize = 500_000 }, out var largeTotal).Count == 1 && largeTotal == 1);
            Check("activity events recorded", db.GetRecentEvents(20).Count >= 4);

            File.WriteAllText(Path.Combine(dataDirectory, "newfile.txt"), "new");
            var rescan = indexer.ScanAsync().GetAwaiter().GetResult();
            Check("incremental scan sees new file", rescan.NewFiles >= 1, rescan.NewFiles.ToString());

            var notes = allFiles.First(file => file.Name == "notes.txt");
            var rename = FileOperations.Rename(notes.FullPath, "notes-renamed.txt");
            Check("rename operation", rename.Success, rename.Message);
            var renamedPath = Path.Combine(dataDirectory, "notes-renamed.txt");
            Check("renamed file exists", File.Exists(renamedPath));

            var copy = FileOperations.Copy(new[] { renamedPath }, copyDirectory);
            Check("copy operation", copy.Success && File.Exists(Path.Combine(copyDirectory, "notes-renamed.txt")), copy.Message);

            var preview = new PreviewService().Describe(renamedPath);
            Check("text preview", preview.Kind == PreviewKind.Text && preview.Text == "hello winpulse", preview.Text ?? "null");
            Check("image preview kind", new PreviewService().Describe(Path.Combine(dataDirectory, "report.pdf")).Kind == PreviewKind.Pdf);

            File.WriteAllText(Path.Combine(dataDirectory, "report-copy.pdf"), new string('y', 1_200_000));
            indexer.ScanAsync().GetAwaiter().GetResult();
            var cleanup = new CleanupService(db).BuildAsync(30, 5_000_000).GetAwaiter().GetResult();
            Check("duplicate detection", cleanup.Duplicates.Count >= 1, cleanup.Duplicates.Count.ToString());
            Check("no false large files", cleanup.LargeFiles.Count == 0, cleanup.LargeFiles.Count.ToString());

            var stats = db.GetStats(30);
            Check("stats counted files", stats.TotalFiles >= 5, stats.TotalFiles.ToString());

            db.AddDiskSample(400L * 1024 * 1024 * 1024, 950L * 1024 * 1024 * 1024);
            db.AddDiskSample(398L * 1024 * 1024 * 1024, 950L * 1024 * 1024 * 1024);
            var diskHistory = db.GetDiskHistory(DateTime.Now.AddDays(-1));
            Check("disk history persists samples", diskHistory.Count >= 2 && diskHistory[0].FreeBytes > 0,
                diskHistory.Count.ToString());

            db.ClearIndex();
            Check("clear index empties table", db.QueryFiles(new FileQuery { Limit = 10 }, out var afterClear).Count == 0 && afterClear == 0);

            try
            {
                Directory.Delete(sandbox, recursive: true);
            }
            catch
            {
                // Temp cleanup is best effort.
            }
        }
        catch (Exception ex)
        {
            failures.Add("Unhandled exception: " + ex);
            Console.WriteLine(ex);
        }

        Console.WriteLine();
        if (failures.Count == 0)
        {
            Console.WriteLine($"SELFTEST OK - {passed} checks passed");
            return 0;
        }

        Console.WriteLine($"SELFTEST FAILED - {failures.Count} failure(s), {passed} passed");
        foreach (var failure in failures)
        {
            Console.WriteLine("  * " + failure);
        }

        return 1;
    }
}

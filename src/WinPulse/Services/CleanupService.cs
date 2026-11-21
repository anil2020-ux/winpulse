using System.Security.Cryptography;
using WinPulse.Models;

namespace WinPulse.Services;

/// <summary>Finds large files, stale downloads, old screenshots and likely duplicates.</summary>
public sealed class CleanupService
{
    private const long DuplicateMinBytes = 1024 * 1024;
    private const int HashBytes = 256 * 1024;

    private readonly Database _db;

    public CleanupService(Database db) => _db = db;

    public Task<CleanupReport> BuildAsync(
        int ageDays,
        long largeFileThresholdBytes,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Build(ageDays, largeFileThresholdBytes, progress, cancellationToken),
            cancellationToken);
    }

    private CleanupReport Build(
        int ageDays,
        long largeFileThresholdBytes,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var report = new CleanupReport();
        var cutoff = DateTime.Now.AddDays(-Math.Max(1, ageDays));

        progress?.Report("Finding large files…");
        report.LargeFiles.AddRange(_db.QueryLargeFiles(largeFileThresholdBytes, 150));
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report("Checking stale downloads…");
        var downloadsRoot = _db.GetRoots()
            .FirstOrDefault(r => r.Label.Equals("Downloads", StringComparison.OrdinalIgnoreCase));
        report.OldDownloads.AddRange(_db.QueryOldFiles(cutoff, downloadsRoot?.Path, screenshotsOnly: false, 250));
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report("Looking for old screenshots…");
        report.OldScreenshots.AddRange(_db.QueryOldFiles(cutoff, rootPath: null, screenshotsOnly: true, 250));
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report("Hunting duplicates…");
        var candidates = _db.QueryDuplicateCandidates(DuplicateMinBytes, maxGroups: 300);
        var checkedGroups = 0;

        foreach (var sizeGroup in candidates.GroupBy(file => file.Size))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var byHash = new Dictionary<string, List<FileEntry>>(StringComparer.Ordinal);

            foreach (var file in sizeGroup)
            {
                var hash = HashHead(file.FullPath);
                if (hash is null)
                {
                    continue;
                }

                if (!byHash.TryGetValue(hash, out var list))
                {
                    byHash[hash] = list = new List<FileEntry>();
                }

                list.Add(file);
            }

            foreach (var copies in byHash.Values.Where(list => list.Count > 1))
            {
                var group = new DuplicateGroup { SizeBytes = copies[0].Size };
                group.Files.AddRange(copies.OrderByDescending(file => file.Modified));
                report.Duplicates.Add(group);
            }

            checkedGroups++;
            if (checkedGroups % 25 == 0)
            {
                progress?.Report($"Hashing files… {checkedGroups} size groups checked");
            }

            if (report.Duplicates.Count >= 100)
            {
                break;
            }
        }

        report.Duplicates.Sort((left, right) => right.ReclaimableBytes.CompareTo(left.ReclaimableBytes));
        progress?.Report("Cleanup scan finished.");
        return report;
    }

    private static string? HashHead(string path)
    {
        try
        {
            using var sha = SHA256.Create();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.SequentialScan);
            var buffer = new byte[HashBytes];
            var total = 0;
            int read;
            while (total < buffer.Length &&
                   (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
            {
                total += read;
            }

            return Convert.ToHexString(sha.ComputeHash(buffer, 0, total));
        }
        catch
        {
            return null;
        }
    }
}

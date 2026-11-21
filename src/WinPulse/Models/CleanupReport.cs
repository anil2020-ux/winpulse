using WinPulse.Core;

namespace WinPulse.Models;

public sealed class DuplicateGroup
{
    public long SizeBytes { get; set; }
    public List<FileEntry> Files { get; set; } = new();

    public string SizeText => Formats.Bytes(SizeBytes);
    public string CountText => $"{Files.Count} copies";
    public long ReclaimableBytes => SizeBytes * Math.Max(0, Files.Count - 1);
    public string ReclaimableText => Formats.Bytes(ReclaimableBytes);
    public string DirectoryText => Files.Count == 0 ? string.Empty : Files[0].Folder;
}

/// <summary>Everything the Cleanup view needs, computed on a background thread.</summary>
public sealed class CleanupReport
{
    public List<FileEntry> LargeFiles { get; } = new();
    public List<FileEntry> OldDownloads { get; } = new();
    public List<FileEntry> OldScreenshots { get; } = new();
    public List<DuplicateGroup> Duplicates { get; } = new();
    public DateTime GeneratedAt { get; } = DateTime.Now;

    public long LargeFilesBytes => LargeFiles.Sum(f => f.Size);
    public long OldDownloadsBytes => OldDownloads.Sum(f => f.Size);
    public long OldScreenshotsBytes => OldScreenshots.Sum(f => f.Size);
    public long DuplicateBytes => Duplicates.Sum(d => d.ReclaimableBytes);

    public string LargeFilesSizeText => Formats.Bytes(LargeFilesBytes);
    public string OldDownloadsSizeText => Formats.Bytes(OldDownloadsBytes);
    public string OldScreenshotsSizeText => Formats.Bytes(OldScreenshotsBytes);
    public string DuplicateSizeText => Formats.Bytes(DuplicateBytes);

    public long TotalReclaimableBytes => LargeFilesBytes + OldScreenshotsBytes + DuplicateBytes;
    public string TotalReclaimableText => Formats.Bytes(TotalReclaimableBytes);
    public int TotalSuggestions => LargeFiles.Count + OldScreenshots.Count + Duplicates.Sum(d => d.Files.Count - 1);
}

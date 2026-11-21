using WinPulse.Core;

namespace WinPulse.Models;

/// <summary>Metadata about a GitHub release that can be installed in-app.</summary>
public sealed class UpdateInfo
{
    public string Tag { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public string AssetName { get; set; } = string.Empty;
    public long AssetSizeBytes { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public bool IsNewer { get; set; }

    public string PublishedText => PublishedAt == DateTime.MinValue ? "—" : Formats.DateShort(PublishedAt);
    public string AssetSizeText => Formats.Bytes(AssetSizeBytes);
    public string Headline => string.IsNullOrWhiteSpace(Title) || Title.Equals(Tag, StringComparison.OrdinalIgnoreCase)
        ? $"WinPulse {Version}"
        : Title;
}

/// <summary>Result of checking GitHub releases.</summary>
public sealed class UpdateCheckResult
{
    public bool Success { get; set; }
    public bool UpdateAvailable { get; set; }
    public string Message { get; set; } = string.Empty;
    public UpdateInfo? Update { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.Now;

    public static UpdateCheckResult Failed(string message) => new() { Success = false, Message = message };
    public static UpdateCheckResult UpToDate() => new() { Success = true, Message = "You are on the latest version." };
}

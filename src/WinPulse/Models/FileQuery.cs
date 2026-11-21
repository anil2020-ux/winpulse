namespace WinPulse.Models;

/// <summary>Filter criteria used by the Files view and cleanup queries.</summary>
public sealed class FileQuery
{
    public string? Search { get; set; }
    public IReadOnlyList<string>? Categories { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public long? MinSize { get; set; }
    public long? MaxSize { get; set; }
    public bool ScreenshotsOnly { get; set; }
    public bool InstallersOnly { get; set; }
    public string? RootPath { get; set; }
    public string Sort { get; set; } = "modified_desc";
    public int Limit { get; set; } = 1000;
    public bool IncludeProtected { get; set; } = true;
}

namespace WinPulse.Models;

/// <summary>A folder that WinPulse watches and indexes.</summary>
public sealed class RootFolder
{
    public long Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int FileCount { get; set; }
    public long TotalSize { get; set; }

    public string SizeText => Core.Formats.Bytes(TotalSize);
    public string CountText => $"{FileCount:N0} files";
    public string Display => string.IsNullOrWhiteSpace(Label) ? System.IO.Path.GetFileName(Path) : Label;
}

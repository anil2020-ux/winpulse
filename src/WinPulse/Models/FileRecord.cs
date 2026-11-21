namespace WinPulse.Models;

/// <summary>Raw file metadata produced by the scanner before it is stored.</summary>
public sealed class FileRecord
{
    public string FullPath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
    public string RootLabel { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public string Category { get; init; } = Core.FileKinds.Other;
    public bool IsScreenshot { get; init; }
    public long Size { get; init; }
    public DateTime Created { get; init; }
    public DateTime Modified { get; init; }
}

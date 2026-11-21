using WinPulse.Core;

namespace WinPulse.Models;

/// <summary>One running process row.</summary>
public sealed class ProcessRow
{
    public int Pid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double CpuPercent { get; set; }
    public long MemoryBytes { get; set; }
    public string? FilePath { get; set; }
    public string? Company { get; set; }
    public string? WindowTitle { get; set; }
    public string? UserName { get; set; }
    public DateTime? StartTime { get; set; }
    public bool IsResponding { get; set; } = true;

    public string CpuText => $"{CpuPercent:0.0}%";
    public string MemoryText => Formats.Bytes(MemoryBytes);
    public string StartText => StartTime is null ? "—" : Formats.DateTimeShort(StartTime.Value);
    public string DisplayName => string.IsNullOrWhiteSpace(Description) ? Name : Description!;
    public string Location => string.IsNullOrWhiteSpace(FilePath)
        ? "—"
        : System.IO.Path.GetDirectoryName(FilePath) ?? "—";
}

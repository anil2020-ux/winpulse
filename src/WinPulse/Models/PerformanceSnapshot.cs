using WinPulse.Core;

namespace WinPulse.Models;

/// <summary>Live machine metrics sampled on a timer.</summary>
public sealed class PerformanceSnapshot
{
    public double CpuPercent { get; set; }
    public long RamTotalBytes { get; set; }
    public long RamUsedBytes { get; set; }
    public long RamAvailableBytes { get; set; }
    public double RamPercent { get; set; }
    public long DiskFreeBytes { get; set; }
    public long DiskTotalBytes { get; set; }
    public double DiskFreePercent { get; set; }
    public int ProcessCount { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public DateTime SampledAt { get; set; } = DateTime.Now;

    public string RamUsedText => $"{Formats.Bytes(RamUsedBytes)} / {Formats.Bytes(RamTotalBytes)}";
    public string RamPercentText => Formats.Percent(RamPercent);
    public string CpuPercentText => Formats.Percent(CpuPercent);
    public string DiskFreeText => Formats.Bytes(DiskFreeBytes);
    public string DiskTotalText => Formats.Bytes(DiskTotalBytes);
}

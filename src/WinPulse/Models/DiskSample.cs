using WinPulse.Core;

namespace WinPulse.Models;

/// <summary>One periodic disk-space reading for the history chart.</summary>
public sealed class DiskSample
{
    public DateTime At { get; set; }
    public long FreeBytes { get; set; }
    public long TotalBytes { get; set; }

    public double FreeGb => FreeBytes / 1024d / 1024d / 1024d;
    public double UsedGb => (TotalBytes - FreeBytes) / 1024d / 1024d / 1024d;
    public string FreeText => Formats.Bytes(FreeBytes);
    public string AtText => At.ToString("MMM d, HH:mm");
}

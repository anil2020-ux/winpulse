using WinPulse.Core;

namespace WinPulse.Models;

/// <summary>Aggregated size for one folder on the dashboard analytics card.</summary>
public sealed class FolderStat
{
    public string Folder { get; set; } = string.Empty;
    public int Count { get; set; }
    public long Size { get; set; }
    public double Percent { get; set; }

    public string SizeText => Formats.Bytes(Size);
    public string CountText => $"{Count:N0} files";
    public string PercentText => $"{Percent:0.#}%";
    public string DisplayFolder => string.IsNullOrEmpty(Folder) ? "—" : Folder;
}

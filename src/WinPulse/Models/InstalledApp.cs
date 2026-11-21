using WinPulse.Core;

namespace WinPulse.Models;

/// <summary>An application installed on the machine (from the uninstall registry).</summary>
public sealed class InstalledApp
{
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public DateTime? InstallDate { get; set; }
    public long SizeBytes { get; set; }
    public string? UninstallString { get; set; }
    public string? QuietUninstallString { get; set; }
    public string? InstallLocation { get; set; }
    public string? DisplayIcon { get; set; }
    public bool IsStoreApp { get; set; }

    public string VersionText => string.IsNullOrWhiteSpace(Version) ? "—" : Version!;
    public string PublisherText => string.IsNullOrWhiteSpace(Publisher) ? "Unknown publisher" : Publisher!;
    public string InstallDateText => InstallDate is null ? "—" : Formats.DateShort(InstallDate.Value);
    public string SizeText => SizeBytes > 0 ? Formats.Bytes(SizeBytes) : "—";
    public bool CanUninstall => !string.IsNullOrWhiteSpace(UninstallString) || !string.IsNullOrWhiteSpace(QuietUninstallString);
}

/// <summary>An installer package discovered in a watched folder.</summary>
public sealed class InstallerPackage
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime Downloaded { get; set; }

    public string SizeText => Formats.Bytes(Size);
    public string DownloadedText => Formats.DateTimeShort(Downloaded);
    public string DownloadedRelative => Formats.Relative(Downloaded);
}

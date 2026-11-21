using WinPulse.Core;

namespace WinPulse.Models;

public sealed class RamModuleSpec
{
    public long CapacityBytes { get; set; }
    public uint SpeedMhz { get; set; }
    public string Manufacturer { get; set; } = string.Empty;
    public string Slot { get; set; } = string.Empty;

    public string CapacityText => Formats.Bytes(CapacityBytes);
    public string SpeedText => SpeedMhz == 0 ? "—" : $"{SpeedMhz} MHz";
}

public sealed class GpuSpec
{
    public string Name { get; set; } = string.Empty;
    public string DriverVersion { get; set; } = string.Empty;
    public long VramBytes { get; set; }

    public string VramText => VramBytes > 0 ? Formats.Bytes(VramBytes) : "Shared";
}

public sealed class DiskSpec
{
    public string Drive { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public string Model { get; set; } = string.Empty;

    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double UsedPercent => TotalBytes <= 0 ? 0 : UsedBytes * 100.0 / TotalBytes;
    public string UsedPercentText => $"{UsedPercent:0}% used";
    public string FreeText => $"{Formats.Bytes(FreeBytes)} free";
    public string TotalText => Formats.Bytes(TotalBytes);
}

/// <summary>Static hardware/OS snapshot, loaded once and cached.</summary>
public sealed class SystemSpecs
{
    public string ComputerName { get; set; } = Environment.MachineName;
    public string UserName { get; set; } = Environment.UserName;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string OsCaption { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string OsBuild { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string CpuName { get; set; } = string.Empty;
    public int CpuCores { get; set; }
    public int CpuThreads { get; set; }
    public uint CpuMaxClockMhz { get; set; }
    public long RamTotalBytes { get; set; }
    public List<RamModuleSpec> RamModules { get; set; } = new();
    public List<GpuSpec> Gpus { get; set; } = new();
    public string BoardManufacturer { get; set; } = string.Empty;
    public string BoardProduct { get; set; } = string.Empty;
    public string BiosVersion { get; set; } = string.Empty;
    public string BiosDate { get; set; } = string.Empty;
    public List<DiskSpec> Disks { get; set; } = new();
    public DateTime BootTime { get; set; }
    public DateTime OsInstallDate { get; set; }

    public string CpuSummary => CpuCores > 0
        ? $"{CpuCores} cores / {CpuThreads} threads"
        : $"{Environment.ProcessorCount} threads";

    public string CpuClockText => Formats.Mhz(CpuMaxClockMhz);

    public string RamText => Formats.Bytes(RamTotalBytes);
    public string RamModulesText
    {
        get
        {
            if (RamModules.Count == 0)
            {
                return RamTotalBytes > 0 ? RamText : "—";
            }

            var speed = RamModules.FirstOrDefault()?.SpeedMhz is { } mhz && mhz > 0
                ? $"  •  {mhz} MHz"
                : string.Empty;

            return RamModules.Count > 4
                ? $"{RamModules.Count} modules{speed}"
                : string.Join(" + ", RamModules.Select(module => module.CapacityText)) + speed;
        }
    }

    public string ComputerSummary => $"{Manufacturer} {Model}".Trim();
    public string UptimeText => Formats.Duration(DateTime.Now - BootTime);
    public string OsSummary => $"{OsCaption}  •  build {OsBuild}";
    public string GpuNames => Gpus.Count == 0 ? "—" : string.Join(", ", Gpus.Select(g => g.Name));
    public string PrimaryGpu => Gpus.FirstOrDefault()?.Name ?? "—";
    public string PrimaryGpuDriver => Gpus.FirstOrDefault()?.DriverVersion ?? "—";
    public string BoardText => $"{BoardManufacturer} {BoardProduct}".Trim();
    public string BiosText => string.IsNullOrWhiteSpace(BiosDate) ? BiosVersion : $"{BiosVersion}  •  {BiosDate}";
    public string OsInstallText => OsInstallDate == default ? "—" : Formats.DateShort(OsInstallDate);
    public int RamModuleCount => RamModules.Count;
}

using System.Globalization;
using System.Management;
using WinPulse.Core;
using WinPulse.Models;

namespace WinPulse.Services;

/// <summary>Gathers static hardware and OS information through WMI (cached after first load).</summary>
public sealed class SystemInfoService
{
    private readonly object _gate = new();
    private SystemSpecs? _cached;

    public SystemSpecs GetSpecs()
    {
        lock (_gate)
        {
            return _cached ??= LoadSpecs();
        }
    }

    private static SystemSpecs LoadSpecs()
    {
        var specs = new SystemSpecs
        {
            Architecture = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit",
            BootTime = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64),
        };

        try
        {
            var os = Query("SELECT Caption, Version, BuildNumber, InstallDate, LastBootUpTime FROM Win32_OperatingSystem").FirstOrDefault();
            if (os is not null)
            {
                specs.OsCaption = Str(os, "Caption");
                specs.OsVersion = Str(os, "Version");
                specs.OsBuild = Str(os, "BuildNumber");
                specs.OsInstallDate = WmiDate(Str(os, "InstallDate")) ?? DateTime.MinValue;
                specs.BootTime = WmiDate(Str(os, "LastBootUpTime")) ?? specs.BootTime;
            }

            var cpu = Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor").FirstOrDefault();
            if (cpu is not null)
            {
                specs.CpuName = Str(cpu, "Name").Trim();
                specs.CpuCores = (int)UInt(cpu, "NumberOfCores");
                specs.CpuThreads = (int)UInt(cpu, "NumberOfLogicalProcessors");
                specs.CpuMaxClockMhz = (uint)UInt(cpu, "MaxClockSpeed");
            }

            if (specs.CpuCores == 0)
            {
                specs.CpuCores = Environment.ProcessorCount;
                specs.CpuThreads = Environment.ProcessorCount;
            }

            var system = Query("SELECT Name, Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault();
            if (system is not null)
            {
                specs.ComputerName = Str(system, "Name");
                specs.Manufacturer = Str(system, "Manufacturer");
                specs.Model = Str(system, "Model");
                specs.RamTotalBytes = Long(system, "TotalPhysicalMemory");
            }

            foreach (var module in Query("SELECT Capacity, Speed, Manufacturer, DeviceLocator FROM Win32_PhysicalMemory"))
            {
                specs.RamModules.Add(new RamModuleSpec
                {
                    CapacityBytes = Long(module, "Capacity"),
                    SpeedMhz = (uint)UInt(module, "Speed"),
                    Manufacturer = Str(module, "Manufacturer").Trim(),
                    Slot = Str(module, "DeviceLocator").Trim(),
                });
            }

            foreach (var gpu in Query("SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController"))
            {
                var name = Str(gpu, "Name").Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                specs.Gpus.Add(new GpuSpec
                {
                    Name = name,
                    DriverVersion = Str(gpu, "DriverVersion").Trim(),
                    VramBytes = Long(gpu, "AdapterRAM"),
                });
            }

            var board = Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
            if (board is not null)
            {
                specs.BoardManufacturer = Str(board, "Manufacturer").Trim();
                specs.BoardProduct = Str(board, "Product").Trim();
            }

            var bios = Query("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
            if (bios is not null)
            {
                specs.BiosVersion = Str(bios, "SMBIOSBIOSVersion").Trim();
                specs.BiosDate = WmiDate(Str(bios, "ReleaseDate")) is { } released
                    ? Formats.DateShort(released)
                    : Str(bios, "ReleaseDate");
            }

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                {
                    continue;
                }

                try
                {
                    specs.Disks.Add(new DiskSpec
                    {
                        Drive = drive.Name.TrimEnd('\\'),
                        Label = drive.VolumeLabel,
                        FileSystem = drive.DriveFormat,
                        TotalBytes = drive.TotalSize,
                        FreeBytes = drive.AvailableFreeSpace,
                    });
                }
                catch
                {
                    // Skip drives that disappear or deny access.
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read full system specs", ex);
        }

        return specs;
    }

    private static List<Dictionary<string, string>> Query(string wql)
    {
        var results = new List<Dictionary<string, string>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            using var collection = searcher.Get();
            foreach (ManagementObject item in collection)
            {
                try
                {
                    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (PropertyData property in item.Properties)
                    {
                        values[property.Name] = property.Value?.ToString() ?? string.Empty;
                    }

                    results.Add(values);
                }
                finally
                {
                    item.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"WMI query failed: {wql} ({ex.Message})");
        }

        return results;
    }

    private static string Str(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

    private static long Long(Dictionary<string, string> values, string key) =>
        long.TryParse(Str(values, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static ulong UInt(Dictionary<string, string> values, string key) =>
        ulong.TryParse(Str(values, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static DateTime? WmiDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(raw);
        }
        catch
        {
            return null;
        }
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using WinPulse.Models;

namespace WinPulse.Services;

/// <summary>Live CPU, memory, disk and process counters using native Win32 calls (no WMI latency).</summary>
public sealed class PerformanceService
{
    private readonly object _gate = new();
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _hasPrevious;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    public PerformanceSnapshot Sample()
    {
        var snapshot = new PerformanceSnapshot();

        lock (_gate)
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                var idleTicks = idle.ToUInt64();
                var kernelTicks = kernel.ToUInt64();
                var userTicks = user.ToUInt64();

                if (_hasPrevious)
                {
                    var idleDelta = idleTicks - _prevIdle;
                    var kernelDelta = kernelTicks - _prevKernel;
                    var userDelta = userTicks - _prevUser;
                    var total = kernelDelta + userDelta;
                    if (total > 0)
                    {
                        snapshot.CpuPercent = Math.Clamp((1.0 - (double)idleDelta / total) * 100.0, 0, 100);
                    }
                }

                _prevIdle = idleTicks;
                _prevKernel = kernelTicks;
                _prevUser = userTicks;
                _hasPrevious = true;
            }

            var memory = new MemoryStatusEx();
            if (GlobalMemoryStatusEx(memory))
            {
                snapshot.RamTotalBytes = (long)memory.TotalPhys;
                snapshot.RamAvailableBytes = (long)memory.AvailPhys;
                snapshot.RamUsedBytes = Math.Max(0, (long)memory.TotalPhys - (long)memory.AvailPhys);
                snapshot.RamPercent = memory.TotalPhys == 0
                    ? 0
                    : snapshot.RamUsedBytes * 100.0 / memory.TotalPhys;
            }
        }

        try
        {
            var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var systemDrive = new DriveInfo(systemRoot);
            if (systemDrive.IsReady)
            {
                snapshot.DiskTotalBytes = systemDrive.TotalSize;
                snapshot.DiskFreeBytes = systemDrive.AvailableFreeSpace;
                snapshot.DiskFreePercent = snapshot.DiskTotalBytes == 0
                    ? 0
                    : snapshot.DiskFreeBytes * 100.0 / snapshot.DiskTotalBytes;
            }
        }
        catch
        {
            // Disk info is best effort.
        }

        try
        {
            var processes = Process.GetProcesses();
            snapshot.ProcessCount = processes.Length;
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
        catch
        {
            // Process count is cosmetic.
        }

        snapshot.SampledAt = DateTime.Now;
        return snapshot;
    }
}

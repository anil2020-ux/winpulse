using System.Diagnostics;
using WinPulse.Core;
using WinPulse.Models;

namespace WinPulse.Services;

/// <summary>Samples running processes and computes CPU usage from TotalProcessorTime deltas.</summary>
public sealed class ProcessService
{
    private readonly object _gate = new();
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _previous = new();
    private readonly Dictionary<string, (string? Description, string? Company)> _metadataCache =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ProcessRow> Snapshot()
    {
        var rows = new List<ProcessRow>();
        var now = DateTime.UtcNow;
        var cores = Math.Max(1, Environment.ProcessorCount);

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not enumerate processes: {ex.Message}");
            return rows;
        }

        foreach (var process in processes)
        {
            try
            {
                var row = new ProcessRow
                {
                    Pid = process.Id,
                    Name = process.ProcessName,
                    MemoryBytes = process.WorkingSet64,
                };

                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch
                {
                    // Access denied for elevated processes - expected.
                }

                row.FilePath = path;
                row.WindowTitle = TryGet(() => process.MainWindowTitle);

                DateTime? startTime = null;
                try
                {
                    startTime = process.StartTime;
                }
                catch
                {
                    // Not accessible for elevated or short-lived processes.
                }

                row.StartTime = startTime;

                TimeSpan cpuTime;
                try
                {
                    cpuTime = process.TotalProcessorTime;
                }
                catch
                {
                    cpuTime = TimeSpan.Zero;
                }

                lock (_gate)
                {
                    if (_previous.TryGetValue(process.Id, out var previous))
                    {
                        var elapsedMs = (now - previous.At).TotalMilliseconds;
                        var usedMs = (cpuTime - previous.Cpu).TotalMilliseconds;
                        if (elapsedMs > 0 && usedMs > 0)
                        {
                            row.CpuPercent = Math.Clamp(usedMs / (elapsedMs * cores) * 100.0, 0, 100);
                        }
                    }

                    _previous[process.Id] = (cpuTime, now);
                }

                if (path is not null)
                {
                    var metadata = GetMetadata(path);
                    row.Description = metadata.Description;
                    row.Company = metadata.Company;
                }

                rows.Add(row);
            }
            catch
            {
                // Process exited while sampling.
            }
            finally
            {
                process.Dispose();
            }
        }

        lock (_gate)
        {
            if (_previous.Count > 2000)
            {
                _previous.Clear();
            }
        }

        return rows
            .OrderByDescending(r => r.CpuPercent)
            .ThenByDescending(r => r.MemoryBytes)
            .ToList();
    }

    private (string? Description, string? Company) GetMetadata(string path)
    {
        lock (_gate)
        {
            if (_metadataCache.TryGetValue(path, out var cached))
            {
                return cached;
            }
        }

        (string? Description, string? Company) result = (null, null);
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            result = (string.IsNullOrWhiteSpace(info.FileDescription) ? null : info.FileDescription,
                      string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName);
        }
        catch
        {
            // Some binaries cannot be inspected.
        }

        lock (_gate)
        {
            _metadataCache[path] = result;
        }

        return result;
    }

    public static OperationResult EndTask(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            return new OperationResult(true, $"Ended {process.ProcessName}.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, ex.Message);
        }
    }

    private static T? TryGet<T>(Func<T> getter, T? fallback = default)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }
}

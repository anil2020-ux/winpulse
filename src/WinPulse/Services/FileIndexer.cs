using System.Diagnostics;
using System.IO;
using System.Timers;
using WinPulse.Core;
using WinPulse.Models;

namespace WinPulse.Services;

/// <summary>
/// Background indexer: walks enabled roots in batches, records new/removed files and
/// optionally watches folders for changes so the index stays fresh.
/// </summary>
public sealed class FileIndexer : IDisposable
{
    private readonly Database _db;
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private System.Timers.Timer? _debounce;
    private CancellationTokenSource? _scanCts;
    private bool _isScanning;
    private bool _disposed;

    public FileIndexer(Database db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    public event EventHandler<ScanProgress>? ProgressChanged;

    public bool IsScanning
    {
        get
        {
            lock (_stateGate)
            {
                return _isScanning;
            }
        }
    }

    public void CancelScan()
    {
        lock (_stateGate)
        {
            _scanCts?.Cancel();
        }
    }

    /// <summary>Runs a full incremental scan. Returns a terminal progress report.</summary>
    public async Task<ScanProgress> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken externalToken = default)
    {
        if (_disposed)
        {
            return new ScanProgress { Phase = ScanPhase.Cancelled };
        }

        if (!await _scanGate.WaitAsync(0).ConfigureAwait(false))
        {
            return new ScanProgress { Phase = ScanPhase.Indexing, Message = "A scan is already running." };
        }

        try
        {
            lock (_stateGate)
            {
                _isScanning = true;
                _scanCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            }

            var token = _scanCts!.Token;
            return await Task.Run(() => ScanCore(progress, token), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new ScanProgress { Phase = ScanPhase.Cancelled };
        }
        catch (Exception ex)
        {
            Log.Error("Scan failed", ex);
            return new ScanProgress { Phase = ScanPhase.Failed, Message = ex.Message };
        }
        finally
        {
            lock (_stateGate)
            {
                _isScanning = false;
                _scanCts?.Dispose();
                _scanCts = null;
            }

            _scanGate.Release();
        }
    }

    private ScanProgress ScanCore(IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var roots = _db.GetRoots()
            .Where(r => r.Enabled && Directory.Exists(r.Path))
            .ToList();

        if (roots.Count == 0)
        {
            var empty = new ScanProgress { Phase = ScanPhase.Done, Message = "No folders to scan. Add a folder in Settings." };
            progress?.Report(empty);
            ProgressChanged?.Invoke(this, empty);
            return empty;
        }

        var totalFiles = 0;
        var totalNew = 0;
        var totalRemoved = 0;

        for (var index = 0; index < roots.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var root = roots[index];
            var scanStart = DbTime.NowDb();
            Report(progress, new ScanProgress
            {
                Phase = ScanPhase.Discovering,
                Root = root.Display,
                RootsDone = index,
                RootsTotal = roots.Count,
                FilesIndexed = totalFiles,
                Message = $"Scanning {root.Display}",
            });

            var buffer = new List<FileRecord>(2048);
            var found = 0;

            foreach (var record in FileScanner.Enumerate(root.Path, root.Label, token))
            {
                buffer.Add(record);
                found++;

                if (buffer.Count >= 2000)
                {
                    _db.UpsertFiles(buffer, scanStart);
                    buffer.Clear();
                }

                if (found % 500 == 0)
                {
                    Report(progress, new ScanProgress
                    {
                        Phase = ScanPhase.Indexing,
                        Root = root.Display,
                        RootsDone = index,
                        RootsTotal = roots.Count,
                        FilesFound = found,
                        FilesIndexed = totalFiles + found,
                        CurrentPath = record.FullPath,
                        Elapsed = stopwatch.Elapsed,
                    });
                }
            }

            _db.UpsertFiles(buffer, scanStart);
            _db.MarkMissing(root.Path, scanStart);

            var added = _db.GetNewSince(scanStart);
            var removed = _db.GetRemovedSince(scanStart);
            if (added.Count > 0 || removed.Count > 0)
            {
                _db.AddEvents(added.Concat(removed));
            }

            totalFiles += found;
            totalNew += added.Count;
            totalRemoved += removed.Count;

            Report(progress, new ScanProgress
            {
                Phase = ScanPhase.Finalizing,
                Root = root.Display,
                RootsDone = index + 1,
                RootsTotal = roots.Count,
                FilesFound = found,
                FilesIndexed = totalFiles,
                NewFiles = totalNew,
                RemovedFiles = totalRemoved,
                Elapsed = stopwatch.Elapsed,
            });
        }

        _settings.MarkScanCompleted();
        stopwatch.Stop();

        var done = new ScanProgress
        {
            Phase = ScanPhase.Done,
            FilesIndexed = totalFiles,
            NewFiles = totalNew,
            RemovedFiles = totalRemoved,
            RootsDone = roots.Count,
            RootsTotal = roots.Count,
            Elapsed = stopwatch.Elapsed,
            Message = $"Indexed {totalFiles:N0} files ({totalNew:N0} new, {totalRemoved:N0} removed) in {stopwatch.Elapsed.TotalSeconds:0.0}s",
        };

        ProgressChanged?.Invoke(this, done);
        progress?.Report(done);
        return done;
    }

    private void Report(IProgress<ScanProgress>? progress, ScanProgress report)
    {
        progress?.Report(report);
        ProgressChanged?.Invoke(this, report);
    }

    // ------------------------------------------------------------- watching

    public void RestartWatching()
    {
        StopWatching();
        if (!_settings.WatchFolders || _disposed)
        {
            return;
        }

        _debounce = new System.Timers.Timer(20_000) { AutoReset = false };
        _debounce.Elapsed += OnDebounceElapsed;

        foreach (var root in _db.GetRoots().Where(r => r.Enabled && Directory.Exists(r.Path)))
        {
            try
            {
                var watcher = new FileSystemWatcher(root.Path)
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite | NotifyFilters.Size,
                };

                watcher.Created += OnFileSystemChanged;
                watcher.Changed += OnFileSystemChanged;
                watcher.Deleted += OnFileSystemChanged;
                watcher.Renamed += OnFileSystemRenamed;
                watcher.Error += (_, args) => Log.Warn($"Watcher error on {root.Path}: {args.GetException().Message}");
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not watch {root.Path}: {ex.Message}");
            }
        }

        Log.Info($"Watching {_watchers.Count} folder(s) for changes.");
    }

    private void StopWatching()
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch
            {
                // Ignore teardown problems.
            }
        }

        _watchers.Clear();

        if (_debounce is not null)
        {
            _debounce.Stop();
            _debounce.Elapsed -= OnDebounceElapsed;
            _debounce.Dispose();
            _debounce = null;
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e) => Debounce();

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e) => Debounce();

    private void Debounce()
    {
        var timer = _debounce;
        if (timer is null || _disposed)
        {
            return;
        }

        try
        {
            timer.Stop();
            timer.Start();
        }
        catch
        {
            // Timer race during shutdown.
        }
    }

    private void OnDebounceElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_disposed || IsScanning)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ScanAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error("Change-triggered scan failed", ex);
            }
        });
    }

    public void Dispose()
    {
        _disposed = true;
        CancelScan();
        StopWatching();
        _scanGate.Dispose();
    }
}

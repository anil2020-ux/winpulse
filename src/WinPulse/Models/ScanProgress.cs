namespace WinPulse.Models;

public enum ScanPhase
{
    Idle,
    Discovering,
    Indexing,
    Finalizing,
    Done,
    Cancelled,
    Failed,
}

/// <summary>Progress report emitted while the indexer walks watched folders.</summary>
public sealed class ScanProgress
{
    public ScanPhase Phase { get; init; } = ScanPhase.Idle;
    public string Root { get; init; } = string.Empty;
    public int FilesFound { get; init; }
    public int FilesIndexed { get; init; }
    public int NewFiles { get; init; }
    public int RemovedFiles { get; init; }
    public int RootsDone { get; init; }
    public int RootsTotal { get; init; }
    public string? CurrentPath { get; init; }
    public string Message { get; init; } = string.Empty;
    public TimeSpan Elapsed { get; init; }

    public bool IsRunning => Phase is ScanPhase.Discovering or ScanPhase.Indexing or ScanPhase.Finalizing;

    public string Summary => Phase switch
    {
        ScanPhase.Discovering => $"Scanning {Root}…",
        ScanPhase.Indexing => $"Indexing {Root}… {FilesIndexed:N0} files",
        ScanPhase.Finalizing => "Saving index…",
        ScanPhase.Done => $"Indexed {FilesIndexed:N0} files ({NewFiles:N0} new, {RemovedFiles:N0} removed)",
        ScanPhase.Cancelled => "Scan cancelled",
        ScanPhase.Failed => string.IsNullOrEmpty(Message) ? "Scan failed" : Message,
        _ => "Idle",
    };
}

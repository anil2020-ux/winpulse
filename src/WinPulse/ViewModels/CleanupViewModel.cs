using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WinPulse.Core;
using WinPulse.Models;
using WinPulse.Services;

namespace WinPulse.ViewModels;

/// <summary>Cleanup recommendations with one-click recycle-bin removal.</summary>
public sealed partial class CleanupViewModel : ObservableObject, IActivatableViewModel
{
    private readonly MainViewModel _main;

    public ObservableCollection<CleanupItem> Items { get; } = new();
    public ObservableCollection<FileEntry> SelectedFiles { get; } = new();

    public IReadOnlyList<string> FilterOptions { get; } = new[]
    {
        "All suggestions", "Large files", "Stale downloads", "Old screenshots", "Duplicates",
    };

    [ObservableProperty] private string _selectedFilter = "All suggestions";
    [ObservableProperty] private CleanupReport? _report;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _progressText = "Run a scan to find space you can safely reclaim.";
    [ObservableProperty] private bool _hasScanned;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string _suggestionSummary = "Not scanned yet";
    [ObservableProperty] private string _largeFilesText = "0 files";
    [ObservableProperty] private string _oldDownloadsText = "0 files";
    [ObservableProperty] private string _oldScreenshotsText = "0 files";
    [ObservableProperty] private string _duplicatesText = "0 groups";
    [ObservableProperty] private int _itemCount;
    [ObservableProperty] private string _reclaimableText = "—";
    [ObservableProperty] private CleanupItem? _selectedItem;

    public ICollectionView ItemsView { get; }
    public PreviewViewModel Preview { get; } = new();

    public CleanupViewModel(MainViewModel main)
    {
        _main = main;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
    }

    public void OnActivated()
    {
        if (!HasScanned && !IsScanning)
        {
            _ = ScanAsync();
        }
    }

    public void OnDeactivated()
    {
    }

    private bool FilterItem(object item)
    {
        if (item is not CleanupItem cleanup)
        {
            return false;
        }

        return SelectedFilter switch
        {
            "Large files" => cleanup.Reason == "Large file",
            "Stale downloads" => cleanup.Reason == "Stale download",
            "Old screenshots" => cleanup.Reason == "Old screenshot",
            "Duplicates" => cleanup.Reason == "Duplicate copy",
            _ => true,
        };
    }

    partial void OnSelectedFilterChanged(string value) => ItemsView.Refresh();

    [RelayCommand]
    private async Task ScanNow() => await ScanAsync();

    public async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        ProgressText = "Starting cleanup scan…";
        try
        {
            var progress = new Progress<string>(message => ProgressText = message);
            var report = await AppHost.Cleanup
                .BuildAsync(AppHost.Settings.CleanupAgeDays, AppHost.Settings.LargeFileThresholdBytes, progress)
                .ConfigureAwait(true);

            Report = report;
            BuildItems(report);
            HasScanned = true;
            ProgressText = Items.Count == 0
                ? "Nothing to clean up — your folders look tidy."
                : $"{Items.Count:N0} suggestions  •  up to {Formats.Bytes(Items.Sum(item => item.File.Size))} reclaimable";
            LargeFilesText = $"{report.LargeFiles.Count:N0} files  •  {report.LargeFilesSizeText}";
            OldDownloadsText = $"{report.OldDownloads.Count:N0} files  •  {report.OldDownloadsSizeText}";
            OldScreenshotsText = $"{report.OldScreenshots.Count:N0} files  •  {report.OldScreenshotsSizeText}";
            DuplicatesText = $"{report.Duplicates.Count:N0} groups  •  {report.DuplicateSizeText}";
        }
        catch (Exception ex)
        {
            Log.Error("Cleanup scan failed", ex);
            ProgressText = "Cleanup scan failed: " + ex.Message;
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void BuildItems(CleanupReport report)
    {
        Items.Clear();

        foreach (var file in report.LargeFiles)
        {
            Items.Add(new CleanupItem { File = file, Reason = "Large file", ReasonColorKey = "BrushWarning" });
        }

        foreach (var file in report.OldDownloads)
        {
            Items.Add(new CleanupItem { File = file, Reason = "Stale download", ReasonColorKey = "BrushOrange" });
        }

        foreach (var file in report.OldScreenshots)
        {
            Items.Add(new CleanupItem { File = file, Reason = "Old screenshot", ReasonColorKey = "BrushCyan" });
        }

        foreach (var group in report.Duplicates)
        {
            foreach (var duplicate in group.Files.Skip(1))
            {
                Items.Add(new CleanupItem { File = duplicate, Reason = "Duplicate copy", ReasonColorKey = "BrushPurple" });
            }
        }

        ItemCount = Items.Count;
        ReclaimableText = Items.Count == 0
            ? "0 B"
            : Formats.Bytes(Items.Sum(item => item.File.Size));
        SuggestionSummary = Items.Count == 0
            ? "Nothing to clean up — your folders look tidy."
            : $"{Items.Count:N0} suggestions  •  up to {ReclaimableText} reclaimable";
        ProgressText = Items.Count == 0
            ? "Your folders look tidy."
            : "Scan complete. Select items to move to the Recycle Bin, or protect files you want to keep.";
        SelectedCount = 0;
        SelectedFiles.Clear();
        SelectedItem = Items.FirstOrDefault();
    }

    partial void OnSelectedItemChanged(CleanupItem? value) => _ = Preview.LoadAsync(value?.File);

    public void SetSelection(System.Collections.IList selectedItems)
    {
        SelectedFiles.Clear();
        foreach (var item in selectedItems)
        {
            if (item is CleanupItem cleanup)
            {
                SelectedFiles.Add(cleanup.File);
            }
        }

        SelectedCount = SelectedFiles.Count;
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        List<FileEntry> targets;
        if (SelectedFiles.Count > 0)
        {
            targets = SelectedFiles.ToList();
        }
        else if (SelectedItem is { } selected)
        {
            targets = new List<FileEntry> { selected.File };
        }
        else
        {
            _main.Notify("Select one or more files to delete.");
            return;
        }

        if (targets.Count == 0)
        {
            _main.Notify("Nothing selected to delete.");
            return;
        }

        var confirmed = DialogService.Confirm(
            "Move to Recycle Bin",
            $"Move {targets.Count:N0} file(s) ({Formats.Bytes(targets.Sum(file => file.Size))}) to the Recycle Bin?\n\nRestore them any time from the Recycle Bin.",
            "Move to Recycle Bin");

        if (!confirmed)
        {
            return;
        }

        var result = await Task.Run(() => FileOperations.Recycle(targets.Select(file => file.FullPath).ToList()));
        if (!result.Success)
        {
            DialogService.Info("Delete failed", result.Message);
            return;
        }

        foreach (var file in targets)
        {
            AppHost.Database.DeleteRow(file.Id);
        }

        var removedIds = targets.Select(file => file.Id).ToHashSet();
        foreach (var item in Items.Where(item => removedIds.Contains(item.File.Id)).ToList())
        {
            Items.Remove(item);
        }

        ItemCount = Items.Count;
        ReclaimableText = Formats.Bytes(Items.Sum(item => item.File.Size));
        SelectedCount = 0;
        SelectedFiles.Clear();
        SelectedItem = Items.FirstOrDefault();
        SuggestionSummary = $"{Items.Count:N0} suggestions remaining";
        _main.Notify(result.Message);
        await _main.Dashboard.RefreshSlowAsync();
        _main.UpdateIndexSummary();
        await _main.Files.ReloadAsync();
    }

    [RelayCommand]
    private void ProtectSelected()
    {
        var targets = SelectedFiles.Count > 0
            ? SelectedFiles.ToList()
            : SelectedItem is { } selected
                ? new List<FileEntry> { selected.File }
                : new List<FileEntry>();

        if (targets.Count == 0)
        {
            return;
        }

        foreach (var file in targets)
        {
            AppHost.Database.SetProtected(file.Id, true);
        }

        var ids = targets.Select(file => file.Id).ToHashSet();
        foreach (var item in Items.Where(item => ids.Contains(item.File.Id)).ToList())
        {
            Items.Remove(item);
        }

        ItemCount = Items.Count;
        ReclaimableText = Formats.Bytes(Items.Sum(item => item.File.Size));
        SelectedCount = 0;
        SelectedFiles.Clear();
        SelectedItem = Items.FirstOrDefault();
        _main.Notify($"{targets.Count} file(s) protected from future cleanup suggestions.");
    }

    // ------------------------------------------------------- per-file actions

    [RelayCommand]
    private async Task DeleteDuplicateCopiesAsync()
    {
        var targets = Items
            .Where(item => item.Reason == "Duplicate copy")
            .Select(item => item.File)
            .ToList();

        if (targets.Count == 0)
        {
            _main.Notify("No duplicate copies to delete.");
            return;
        }

        var confirmed = DialogService.Confirm(
            "Delete duplicate copies",
            $"Move {targets.Count:N0} duplicate copies ({Formats.Bytes(targets.Sum(file => file.Size))}) to the Recycle Bin?\n\nThe newest copy in each duplicate group is kept.",
            "Move to Recycle Bin");

        if (!confirmed)
        {
            return;
        }

        var result = await Task.Run(() => FileOperations.Recycle(targets.Select(file => file.FullPath).ToList()));
        if (!result.Success)
        {
            DialogService.Info("Delete failed", result.Message);
            return;
        }

        foreach (var file in targets)
        {
            AppHost.Database.DeleteRow(file.Id);
        }

        var ids = targets.Select(file => file.Id).ToHashSet();
        foreach (var item in Items.Where(item => ids.Contains(item.File.Id)).ToList())
        {
            Items.Remove(item);
        }

        ItemCount = Items.Count;
        ReclaimableText = Formats.Bytes(Items.Sum(item => item.File.Size));
        SelectedItem = Items.FirstOrDefault();
        _main.Notify(result.Message);
        await _main.Dashboard.RefreshSlowAsync();
        _main.UpdateIndexSummary();
        await _main.Files.ReloadAsync();
    }

    private FileEntry? TargetFile => SelectedItem?.File;

    [RelayCommand]
    private void TogglePreview() => Preview.ToggleVisibility();

    [RelayCommand]
    private void Open()
    {
        if (TargetFile is not { } file)
        {
            return;
        }

        _main.Notify(FileOperations.Open(file.FullPath).Message);
    }

    [RelayCommand]
    private void Reveal()
    {
        if (TargetFile is not { } file)
        {
            return;
        }

        _main.Notify(FileOperations.RevealInExplorer(file.FullPath).Message);
    }

    [RelayCommand]
    private void OpenContainingFolder()
    {
        if (TargetFile is not { } file)
        {
            return;
        }

        _main.Notify(FileOperations.OpenFolder(file.Folder).Message);
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (TargetFile is not { } file)
        {
            return;
        }

        var newName = DialogService.Prompt("Rename file", "Enter a new name for this file:", file.Name, "Rename");
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == file.Name)
        {
            return;
        }

        var name = newName.Trim();
        var result = FileOperations.Rename(file.FullPath, name);
        if (!result.Success)
        {
            DialogService.Info("Rename failed", result.Message);
            return;
        }

        var newPath = Path.Combine(file.Folder, name);
        var extension = Path.GetExtension(newPath).ToLowerInvariant();
        var category = FileKinds.CategoryOf(extension, newPath);
        var isScreenshot = FileKinds.IsScreenshot(name, newPath, extension);

        AppHost.Database.UpdateAfterRename(file.Id, newPath, name, extension, category, isScreenshot);
        file.FullPath = newPath;
        file.Name = name;
        file.Extension = extension;
        file.Category = category;
        file.IsScreenshot = isScreenshot;

        ItemsView.Refresh();
        await Preview.LoadAsync(file);
        _main.Notify(result.Message);
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (TargetFile is not { } file)
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = "Copy file to folder", Multiselect = false };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = await Task.Run(() => FileOperations.Copy(new[] { file.FullPath }, dialog.FolderName));
        _main.Notify(result.Message);
        if (!result.Success)
        {
            DialogService.Info("Copy failed", result.Message);
        }

        _ = _main.RunScanAsync();
    }

    [RelayCommand]
    private async Task MoveAsync()
    {
        if (TargetFile is not { } file)
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = "Move file to folder", Multiselect = false };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = await Task.Run(() => FileOperations.Move(new[] { file.FullPath }, dialog.FolderName));
        _main.Notify(result.Message);
        if (!result.Success)
        {
            DialogService.Info("Move failed", result.Message);
            return;
        }

        AppHost.Database.DeleteRow(file.Id);
        if (SelectedItem is { } item)
        {
            Items.Remove(item);
        }

        ItemCount = Items.Count;
        ReclaimableText = Formats.Bytes(Items.Sum(entry => entry.File.Size));
        SelectedItem = Items.FirstOrDefault();
        _ = _main.RunScanAsync();
    }
}

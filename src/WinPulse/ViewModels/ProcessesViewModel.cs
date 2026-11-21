using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPulse.Core;
using WinPulse.Models;
using WinPulse.Services;

namespace WinPulse.ViewModels;

/// <summary>Live process explorer with end-task and reveal actions.</summary>
public sealed partial class ProcessesViewModel : ObservableObject, IActivatableViewModel
{
    private readonly DispatcherTimer _timer;
    private bool _refreshing;

    public ObservableCollection<ProcessRow> Processes { get; } = new();
    public ICollectionView ProcessesView { get; }

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ProcessRow? _selectedProcess;
    [ObservableProperty] private string _summary = "Loading processes…";

    public ProcessesViewModel()
    {
        ProcessesView = CollectionViewSource.GetDefaultView(Processes);
        ProcessesView.Filter = FilterProcess;
        _timer = new DispatcherTimer(
            TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => _ = RefreshAsync(), Dispatcher.CurrentDispatcher);
    }

    public void OnActivated()
    {
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }

        _ = RefreshAsync();
    }

    public void OnDeactivated() => _timer.Stop();

    private bool FilterProcess(object item)
    {
        if (item is not ProcessRow process)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return process.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               (process.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (process.Company?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
               process.Pid.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSearchTextChanged(string value) => ProcessesView.Refresh();

    public async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var selectedPid = SelectedProcess?.Pid;
            var snapshot = await Task.Run(() => AppHost.Processes.Snapshot()).ConfigureAwait(true);

            Processes.Clear();
            foreach (var process in snapshot)
            {
                Processes.Add(process);
            }

            SelectedProcess = selectedPid is { } pid
                ? Processes.FirstOrDefault(process => process.Pid == pid) ?? Processes.FirstOrDefault()
                : Processes.FirstOrDefault();

            Summary = $"{Processes.Count:N0} processes  •  {Formats.Bytes(Processes.Sum(process => process.MemoryBytes))} working set";
        }
        catch (Exception ex)
        {
            Log.Warn("Process refresh failed: " + ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    [RelayCommand]
    private async Task Refresh() => await RefreshAsync();

    [RelayCommand]
    private async Task EndTask()
    {
        if (SelectedProcess is not { } process)
        {
            return;
        }

        var confirmed = DialogService.Confirm(
            "End process",
            $"End “{process.Name}” (PID {process.Pid})?\n\nUnsaved work in this app may be lost.",
            "End process");

        if (!confirmed)
        {
            return;
        }

        var result = ProcessService.EndTask(process.Pid);
        if (!result.Success)
        {
            DialogService.Info("Could not end process", result.Message);
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenLocation()
    {
        if (SelectedProcess?.FilePath is not { Length: > 0 } path)
        {
            return;
        }

        var result = FileOperations.RevealInExplorer(path);
        if (!result.Success)
        {
            DialogService.Info("Could not open location", result.Message);
        }
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (SelectedProcess?.FilePath is not { Length: > 0 } path)
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(path);
        }
        catch
        {
            // Clipboard can be locked by another process.
        }
    }
}

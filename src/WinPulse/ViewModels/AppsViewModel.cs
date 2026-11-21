using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPulse.Core;
using WinPulse.Models;
using WinPulse.Services;

namespace WinPulse.ViewModels;

/// <summary>Installed applications plus installers sitting in Downloads.</summary>
public sealed partial class AppsViewModel : ObservableObject, IActivatableViewModel
{
    public ObservableCollection<InstalledApp> Apps { get; } = new();
    public ObservableCollection<FileEntry> Installers { get; } = new();
    public ICollectionView AppsView { get; }

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private InstalledApp? _selectedApp;
    [ObservableProperty] private FileEntry? _selectedInstaller;
    [ObservableProperty] private string _summary = "Loading installed apps…";
    [ObservableProperty] private string _installerSummary = string.Empty;
    [ObservableProperty] private bool _isLoading;

    public AppsViewModel()
    {
        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.Filter = FilterApp;
    }

    public void OnActivated() => _ = LoadAsync();

    public void OnDeactivated()
    {
    }

    private bool FilterApp(object item)
    {
        if (item is not InstalledApp app)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return app.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               (app.Publisher?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    partial void OnSearchTextChanged(string value) => AppsView.Refresh();

    public async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var apps = await Task.Run(() => AppHost.InstalledApps.GetInstalledApps()).ConfigureAwait(true);
            var installers = await Task.Run(() => AppHost.InstalledApps.GetInstallerFiles(150)).ConfigureAwait(true);

            var selectedName = SelectedApp?.Name;
            Apps.Clear();
            foreach (var app in apps)
            {
                Apps.Add(app);
            }

            SelectedApp = Apps.FirstOrDefault(app => app.Name == selectedName) ?? Apps.FirstOrDefault();
            Summary = $"{Apps.Count:N0} installed apps  •  {Formats.Bytes(Apps.Sum(app => app.SizeBytes))} tracked";

            Installers.Clear();
            foreach (var installer in installers)
            {
                Installers.Add(installer);
            }

            InstallerSummary = Installers.Count == 0
                ? "No installers found in watched folders"
                : $"{Installers.Count:N0} installers  •  {Formats.Bytes(Installers.Sum(file => file.Size))}";
        }
        catch (Exception ex)
        {
            Log.Error("Could not load installed apps", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task Refresh() => await LoadAsync();

    [RelayCommand]
    private void Uninstall()
    {
        if (SelectedApp is not { } app)
        {
            return;
        }

        var confirmed = DialogService.Confirm(
            "Uninstall application",
            $"Launch the uninstaller for “{app.Name}” {app.VersionText}?\n\nThe app's own uninstaller will open.",
            "Uninstall");

        if (!confirmed)
        {
            return;
        }

        var result = InstalledAppsService.Uninstall(app);
        if (!result.Success)
        {
            DialogService.Info("Could not uninstall", result.Message);
        }
    }

    [RelayCommand]
    private void OpenInstallLocation()
    {
        if (SelectedApp is not { } app)
        {
            return;
        }

        var directory = app.InstallLocation;
        if (string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(app.DisplayIcon))
        {
            directory = Path.GetDirectoryName(app.DisplayIcon);
        }

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            DialogService.Info("Location unavailable", "WinPulse could not find an install folder for this app.");
            return;
        }

        FileOperations.OpenFolder(directory);
    }

    [RelayCommand]
    private void OpenInstaller()
    {
        if (SelectedInstaller is not { } installer)
        {
            return;
        }

        var result = FileOperations.Open(installer.FullPath);
        if (!result.Success)
        {
            DialogService.Info("Could not open installer", result.Message);
        }
    }

    [RelayCommand]
    private void RevealInstaller()
    {
        if (SelectedInstaller is not { } installer)
        {
            return;
        }

        FileOperations.RevealInExplorer(installer.FullPath);
    }

    [RelayCommand]
    private void DeleteInstaller()
    {
        if (SelectedInstaller is not { } installer)
        {
            return;
        }

        var confirmed = DialogService.Confirm(
            "Delete installer",
            $"Move “{installer.Name}” ({installer.SizeText}) to the Recycle Bin?",
            "Move to Recycle Bin");

        if (!confirmed)
        {
            return;
        }

        var result = FileOperations.Recycle(new[] { installer.FullPath });
        if (result.Success)
        {
            AppHost.Database.DeleteRow(installer.Id);
            Installers.Remove(installer);
            InstallerSummary = Installers.Count == 0
                ? "No installers found in watched folders"
                : $"{Installers.Count:N0} installers  •  {Formats.Bytes(Installers.Sum(file => file.Size))}";
        }

        if (!result.Success)
        {
            DialogService.Info("Delete failed", result.Message);
        }
    }
}

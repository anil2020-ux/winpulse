using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPulse.Core;
using WinPulse.Models;
using WinPulse.Services;
using WinPulse.ViewModels;

namespace WinPulse.Views;

/// <summary>Side-by-side duplicate resolution: choose the keeper, recycle the rest.</summary>
public partial class DuplicateReviewWindow : Window
{
    private readonly ObservableCollection<DuplicateGroup> _groups;
    private readonly Action _onChanged;
    private readonly ObservableCollection<DuplicateCopy> _copies = new();
    private DuplicateGroup? _selectedGroup;

    public DuplicateReviewWindow(ObservableCollection<DuplicateGroup> groups, Action onChanged)
    {
        InitializeComponent();
        _groups = groups;
        _onChanged = onChanged;

        GroupsList.ItemsSource = _groups;
        CopiesItems.ItemsSource = _copies;

        Loaded += (_, _) =>
        {
            RefreshStats();
            GroupsList.SelectedIndex = _groups.Count > 0 ? 0 : -1;
            UpdateEmptyState();
        };
    }

    private void RefreshStats()
    {
        var files = _groups.Sum(group => Math.Max(0, group.Files.Count - 1));
        var bytes = _groups.Sum(group => group.ReclaimableBytes);
        StatsText.Text = $"{_groups.Count:N0} groups  •  {files:N0} extra copies  •  {Formats.Bytes(bytes)} reclaimable";
        SubtitleText.Text = _groups.Count == 0
            ? "You're all set."
            : "Pick the copy to keep in each group, then recycle the rest.";
    }

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_groups.Count == 0)
        {
            GroupTitle.Text = "No duplicate groups left";
            GroupHint.Text = "Everything was resolved.";
        }
    }

    private void GroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupsList.SelectedItem is not DuplicateGroup group)
        {
            _selectedGroup = null;
            _copies.Clear();
            return;
        }

        _selectedGroup = group;
        GroupTitle.Text = $"{group.SizeText}  •  {group.CountText}";
        GroupHint.Text = $"Reclaimable: {group.ReclaimableText}  •  click a copy to choose the keeper";

        _copies.Clear();
        for (var index = 0; index < group.Files.Count; index++)
        {
            var copy = new DuplicateCopy(group.Files[index]) { Keep = index == 0 };
            _copies.Add(copy);
        }

        _ = LoadThumbnailsAsync(_copies);
    }

    private static async Task LoadThumbnailsAsync(IReadOnlyList<DuplicateCopy> copies)
    {
        foreach (var copy in copies)
        {
            if (!FileKinds.IsImage(copy.File.Extension))
            {
                continue;
            }

            var path = copy.File.FullPath;
            var thumbnail = await Task.Run(() => LoadThumb(path));
            MainViewModel.Dispatch(() => copy.Thumbnail = thumbnail);
        }
    }

    private static ImageSource? LoadThumb(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.DecodePixelWidth = 360;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void CopyCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DuplicateCopy chosen })
        {
            return;
        }

        foreach (var copy in _copies)
        {
            copy.Keep = ReferenceEquals(copy, chosen);
        }
    }

    private void RecycleOthers_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGroup is not { } group)
        {
            return;
        }

        var keeper = _copies.FirstOrDefault(copy => copy.Keep) ?? _copies.FirstOrDefault();
        if (keeper is null)
        {
            return;
        }

        var targets = group.Files.Where(file => file.Id != keeper.File.Id).ToList();
        if (targets.Count == 0)
        {
            RemoveGroup(group);
            return;
        }

        var bytes = targets.Sum(file => file.Size);
        if (!Confirm($"Move {targets.Count} other copies ({Formats.Bytes(bytes)}) of “{keeper.File.Name}” to the Recycle Bin?\n\nKeeping: {keeper.File.FullPath}", "Recycle copies"))
        {
            return;
        }

        var result = FileOperations.Recycle(targets.Select(file => file.FullPath).ToList());
        if (!result.Success)
        {
            Info("Delete failed", result.Message);
            return;
        }

        foreach (var file in targets)
        {
            AppHost.Database.DeleteRow(file.Id);
        }

        RemoveGroup(group);
        _onChanged();
    }

    private void AutoResolve_Click(object sender, RoutedEventArgs e)
    {
        var targets = new List<FileEntry>();
        foreach (var group in _groups.ToList())
        {
            targets.AddRange(group.Files.Skip(1));
        }

        if (targets.Count == 0)
        {
            return;
        }

        var bytes = targets.Sum(file => file.Size);
        if (!Confirm($"Keep the newest copy in every group and move {targets.Count:N0} duplicates ({Formats.Bytes(bytes)}) to the Recycle Bin?", "Auto-resolve duplicates"))
        {
            return;
        }

        var result = FileOperations.Recycle(targets.Select(file => file.FullPath).ToList());
        if (!result.Success)
        {
            Info("Delete failed", result.Message);
            return;
        }

        foreach (var file in targets)
        {
            AppHost.Database.DeleteRow(file.Id);
        }

        _groups.Clear();
        _copies.Clear();
        RefreshStats();
        UpdateEmptyState();
        StatsText.Text = $"Resolved  •  {Formats.Bytes(bytes)} reclaimed";
        _onChanged();
    }

    private void RemoveGroup(DuplicateGroup group)
    {
        var index = _groups.IndexOf(group);
        _groups.Remove(group);
        RefreshStats();
        UpdateEmptyState();
        if (_groups.Count > 0)
        {
            GroupsList.SelectedIndex = Math.Clamp(index, 0, _groups.Count - 1);
        }
        else
        {
            _copies.Clear();
        }
    }

    private bool Confirm(string message, string confirmText)
    {
        var dialog = new PromptDialog { Owner = this };
        return dialog.ShowConfirm("Confirm", message, confirmText, danger: true);
    }

    private void Info(string title, string message)
    {
        var dialog = new PromptDialog { Owner = this };
        dialog.ShowInfo(title, message);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

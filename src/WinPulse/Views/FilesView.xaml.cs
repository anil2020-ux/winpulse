using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinPulse.ViewModels;

namespace WinPulse.Views;

public partial class FilesView : UserControl
{
    public FilesView()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => Focus();
    }

    private void FilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is FilesViewModel viewModel)
        {
            viewModel.SetSelection(FilesGrid.SelectedItems);
        }
    }

    private void FilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is FilesViewModel viewModel && viewModel.OpenCommand.CanExecute(null))
        {
            viewModel.OpenCommand.Execute(null);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not FilesViewModel viewModel)
        {
            return;
        }

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (control && e.Key == Key.R)
        {
            viewModel.RefreshCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            viewModel.DeleteCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            viewModel.RenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && viewModel.SelectedFile is not null)
        {
            viewModel.OpenCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void PlayMedia_Click(object sender, RoutedEventArgs e) => MediaPlayer.Play();

    private void PauseMedia_Click(object sender, RoutedEventArgs e) => MediaPlayer.Pause();

    private void StopMedia_Click(object sender, RoutedEventArgs e) => MediaPlayer.Stop();
}

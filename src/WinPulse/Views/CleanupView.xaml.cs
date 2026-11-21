using System.Windows.Controls;
using System.Windows.Input;
using WinPulse.ViewModels;

namespace WinPulse.Views;

public partial class CleanupView : UserControl
{
    public CleanupView()
    {
        InitializeComponent();
    }

    private void CleanupGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is CleanupViewModel viewModel)
        {
            viewModel.SetSelection(CleanupGrid.SelectedItems);
        }
    }

    private void CleanupGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CleanupViewModel viewModel && viewModel.OpenCommand.CanExecute(null))
        {
            viewModel.OpenCommand.Execute(null);
        }
    }
}

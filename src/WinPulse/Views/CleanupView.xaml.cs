using System.Windows.Controls;
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
}

using System.Windows.Controls;
using System.Windows.Input;
using WinPulse.ViewModels;

namespace WinPulse.Views;

public partial class ScreenshotsView : UserControl
{
    public ScreenshotsView()
    {
        InitializeComponent();
    }

    private void ScreenshotList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ScreenshotsViewModel viewModel && viewModel.OpenCommand.CanExecute(null))
        {
            viewModel.OpenCommand.Execute(null);
        }
    }
}

using System.Windows;

namespace WinPulse.Views;

/// <summary>Small modal used for rename prompts, confirmations and alerts.</summary>
public partial class PromptDialog : Window
{
    public PromptDialog()
    {
        InitializeComponent();
    }

    public string? ShowPrompt(string title, string message, string initialValue, string okText)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        ValueBox.Text = initialValue;
        ValueBox.Visibility = Visibility.Visible;
        OkButton.Content = okText;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };

        return ShowDialog() == true ? ValueBox.Text.Trim() : null;
    }

    public bool ShowConfirm(string title, string message, string confirmText, bool danger)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = confirmText;
        if (danger)
        {
            OkButton.Style = (Style)FindResource("DangerButton");
        }

        return ShowDialog() == true;
    }

    public void ShowInfo(string title, string message)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = "Got it";
        CancelButton.Visibility = Visibility.Collapsed;
        ShowDialog();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

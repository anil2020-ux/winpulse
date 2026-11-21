using System.Windows;
using WinPulse.Views;

namespace WinPulse.Core;

/// <summary>Small helpers for modal prompts and confirmations.</summary>
public static class DialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public static string? Prompt(string title, string message, string initialValue = "", string okText = "Save")
    {
        var dialog = new PromptDialog { Owner = Owner! };
        return dialog.ShowPrompt(title, message, initialValue, okText);
    }

    public static bool Confirm(string title, string message, string confirmText = "Delete", bool danger = true)
    {
        var dialog = new PromptDialog { Owner = Owner! };
        return dialog.ShowConfirm(title, message, confirmText, danger);
    }

    public static void Info(string title, string message)
    {
        var dialog = new PromptDialog { Owner = Owner! };
        dialog.ShowInfo(title, message);
    }
}

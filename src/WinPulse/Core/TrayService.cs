using Hardcodet.Wpf.TaskbarNotification;
using WinPulse.Core;

namespace WinPulse.Core;

/// <summary>Thin wrapper so view models can raise tray notifications without owning the icon.</summary>
public static class TrayService
{
    private static TaskbarIcon? _icon;

    public static bool IsAvailable => _icon is not null;

    public static void Initialize(TaskbarIcon icon) => _icon = icon;

    public static void Notify(string title, string message, BalloonIcon kind = BalloonIcon.Info)
    {
        try
        {
            _icon?.ShowBalloonTip(title, message, kind);
        }
        catch (Exception ex)
        {
            Log.Warn("Tray notification failed: " + ex.Message);
        }
    }
}

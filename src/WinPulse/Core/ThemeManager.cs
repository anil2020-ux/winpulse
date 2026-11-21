using System.Windows;
using System.Windows.Media;

namespace WinPulse.Core;

/// <summary>Swaps the palette brushes between dark and light themes at runtime.</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;

    public static void Apply(string? theme) =>
        Apply(!string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase));

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        void Set(string key, string darkHex, string lightHex) =>
            resources[key] = new SolidColorBrush(Parse(dark ? darkHex : lightHex));

        Set("BrushBg", "#0A0E15", "#F2F5FA");
        Set("BrushBgDeep", "#070A10", "#E8EDF5");
        Set("BrushSidebar", "#0D121B", "#FFFFFF");
        Set("BrushCard", "#121926", "#FFFFFF");
        Set("BrushCardHover", "#17202F", "#F6F8FC");
        Set("BrushCardAlt", "#0F1621", "#F8FAFD");
        Set("BrushBorder", "#1D2839", "#DCE3EE");
        Set("BrushBorderSoft", "#16202E", "#E7ECF4");
        Set("BrushHover", "#1A2434", "#EDF1F7");
        Set("BrushSelection", "#1B2B46", "#DCE9FF");
        Set("BrushInput", "#0C121C", "#F4F7FB");
        Set("BrushTextPrimary", "#EAF0FA", "#101828");
        Set("BrushTextSecondary", "#9AA8BF", "#52627A");
        Set("BrushTextMuted", "#5F6E85", "#7C8AA0");
        Set("BrushAccent", "#4C8DFF", "#2F6FE4");
        Set("BrushAccentHover", "#6AA2FF", "#4C8DFF");
        Set("BrushAccentSoft", "#15305A", "#DCE9FF");
        Set("BrushSuccess", "#2DD4A7", "#0E9F7A");
        Set("BrushSuccessSoft", "#0E3B31", "#D7F5EC");
        Set("BrushWarning", "#F5B84C", "#B7791F");
        Set("BrushWarningSoft", "#3D2F13", "#FDF1D8");
        Set("BrushDanger", "#F2555A", "#D92D36");
        Set("BrushDangerSoft", "#3E1A1F", "#FBE1E3");
        Set("BrushPurple", "#A78BFA", "#7C5CE0");
        Set("BrushCyan", "#38BDF8", "#0EA5E9");
        Set("BrushPink", "#F472B6", "#DB2777");
        Set("BrushOrange", "#FB923C", "#EA580C");

        var heroStart = Parse(dark ? "#1B3B6F" : "#DCE9FF");
        var heroMid = Parse(dark ? "#13294B" : "#EAF1FF");
        var heroEnd = Parse(dark ? "#121926" : "#FFFFFF");
        resources["BrushHero"] = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(heroStart, 0),
                new(heroMid, 0.55),
                new(heroEnd, 1),
            },
            new Point(0, 0),
            new Point(1, 1));
    }

    private static Color Parse(string hex) =>
        (Color)ColorConverter.ConvertFromString(hex)!;
}

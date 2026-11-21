using System.Globalization;

namespace WinPulse.Core;

/// <summary>Human friendly formatting helpers used across the UI.</summary>
public static class Formats
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Bytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes.ToString(CultureInfo.CurrentCulture)} B"
            : string.Format(CultureInfo.CurrentCulture, "{0:0.#} {1}", value, Units[unit]);
    }

    public static string Relative(DateTime local)
    {
        var delta = DateTime.Now - local;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalSeconds < 45)
        {
            return "just now";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"{(int)delta.TotalMinutes}m ago";
        }

        if (delta.TotalHours < 24)
        {
            return $"{(int)delta.TotalHours}h ago";
        }

        if (delta.TotalDays < 7)
        {
            return $"{(int)delta.TotalDays}d ago";
        }

        if (delta.TotalDays < 31)
        {
            return $"{(int)(delta.TotalDays / 7)}w ago";
        }

        if (delta.TotalDays < 365)
        {
            return $"{(int)(delta.TotalDays / 30)}mo ago";
        }

        return $"{(int)(delta.TotalDays / 365)}y ago";
    }

    public static string DateShort(DateTime local) =>
        local.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);

    public static string DateTimeShort(DateTime local) =>
        local.ToString("MMM d, yyyy  h:mm tt", CultureInfo.CurrentCulture);

    public static string Duration(TimeSpan span)
    {
        if (span.TotalMinutes < 1)
        {
            return "less than a minute";
        }

        if (span.TotalHours < 1)
        {
            return $"{(int)span.TotalMinutes} min";
        }

        if (span.TotalDays < 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }

        return $"{(int)span.TotalDays}d {span.Hours}h";
    }

    public static string Number(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Percent(double value) => $"{value:0}%";

    public static string Mhz(uint mhz) => mhz == 0 ? "—" : $"{mhz / 1000.0:0.0} GHz";
}

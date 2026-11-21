using System.Globalization;

namespace WinPulse.Core;

/// <summary>
/// WinPulse stores timestamps as sortable ISO-8601 UTC strings inside SQLite.
/// Helpers here convert to/from the local <see cref="DateTime"/> values used by the UI.
/// </summary>
public static class DbTime
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public static string ToDb(DateTime value) =>
        value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);

    public static string NowDb() => ToDb(DateTime.Now);

    public static DateTime FromDb(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.MinValue;
        }

        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var utc)
            ? utc.ToLocalTime()
            : DateTime.MinValue;
    }
}

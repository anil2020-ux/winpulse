using System.Text.RegularExpressions;

namespace WinPulse.Core;

/// <summary>File categorisation, screenshot detection and icon glyphs.</summary>
public static class FileKinds
{
    public const string Image = "Image";
    public const string Video = "Video";
    public const string Audio = "Audio";
    public const string Document = "Document";
    public const string Spreadsheet = "Spreadsheet";
    public const string Presentation = "Presentation";
    public const string Pdf = "PDF";
    public const string Text = "Text";
    public const string Code = "Code";
    public const string Archive = "Archive";
    public const string Installer = "Installer";
    public const string Program = "Program";
    public const string Other = "Other";
    public const string ScreenshotLabel = "Screenshot";

    public static readonly IReadOnlyList<string> AllCategories = new[]
    {
        Image, Video, Audio, Document, Spreadsheet, Presentation, Pdf,
        Text, Code, Archive, Installer, Program, Other,
    };

    public static readonly IReadOnlyList<string> InstallerExtensions = new[]
    {
        ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".msu",
    };

    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".heic", ".svg", ".avif",
    };

    private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v", ".mpg", ".mpeg", ".ts",
    };

    private static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".opus", ".mid", ".midi",
    };

    private static readonly HashSet<string> DocExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".odt", ".rtf", ".pages", ".epub", ".wpd",
    };

    private static readonly HashSet<string> SheetExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xls", ".xlsx", ".xlsm", ".ods", ".csv", ".tsv", ".numbers",
    };

    private static readonly HashSet<string> SlideExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ppt", ".pptx", ".odp", ".key",
    };

    private static readonly HashSet<string> TextExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".ini", ".cfg", ".conf", ".env", ".rtf", ".tex", ".nfo",
    };

    private static readonly HashSet<string> CodeExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".xaml", ".js", ".ts", ".jsx", ".tsx", ".py", ".java", ".kt", ".c", ".h", ".cpp",
        ".hpp", ".rs", ".go", ".php", ".rb", ".swift", ".sh", ".ps1", ".psm1", ".bat", ".cmd",
        ".html", ".htm", ".css", ".scss", ".less", ".sql", ".json", ".xml", ".yaml", ".yml",
        ".toml", ".vue", ".svelte", ".gitignore", ".dockerfile", ".gradle",
    };

    private static readonly HashSet<string> ArchiveExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab", ".jar", ".whl",
    };

    private static readonly HashSet<string> ProgramExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".msi",
    };

    private static readonly Regex ScreenshotName = new(
        @"(screenshot|screen[\s_-]?shot|snip(ping)?|capture|screen[\s_-]?clip)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string CategoryOf(string? extension, string? path = null)
    {
        var ext = Normalize(extension);
        if (ext.Length == 0)
        {
            return Other;
        }

        if (ImageExt.Contains(ext)) return Image;
        if (VideoExt.Contains(ext)) return Video;
        if (AudioExt.Contains(ext)) return Audio;
        if (ext == ".pdf") return Pdf;
        if (DocExt.Contains(ext)) return Document;
        if (SheetExt.Contains(ext)) return Spreadsheet;
        if (SlideExt.Contains(ext)) return Presentation;
        if (TextExt.Contains(ext)) return Text;
        if (CodeExt.Contains(ext)) return Code;
        if (ArchiveExt.Contains(ext)) return Archive;
        if (InstallerExtensions.Contains(ext)) return Installer;
        if (ProgramExt.Contains(ext)) return Program;
        return Other;
    }

    private static string Normalize(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var ext = extension.Trim();
        if (!ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        return ext.ToLowerInvariant();
    }

    public static bool IsImage(string? ext) => ImageExt.Contains(Normalize(ext));

    /// <summary>Heuristic: screenshots are images whose name/path looks like a screen capture.</summary>
    public static bool IsScreenshot(string? name, string? path, string? extension)
    {
        if (!IsImage(extension))
        {
            return false;
        }

        return ScreenshotName.IsMatch(name ?? string.Empty) ||
               ScreenshotName.IsMatch(path ?? string.Empty);
    }

    public static string GlyphFor(string? category) => category switch
    {
        Image => "\uE91B",
        Video => "\uE714",
        Audio => "\uE8D6",
        Pdf => "\uE8A5",
        Document => "\uE8A5",
        Spreadsheet => "\uE9F9",
        Presentation => "\uE786",
        Text => "\uE8A5",
        Code => "\uE943",
        Archive => "\uE8B7",
        Installer => "\uE7B8",
        Program => "\uE7B8",
        _ => "\uE7C3",
    };

    public static string GlyphForFile(string? category, string? extension)
    {
        var ext = Normalize(extension);
        return ext switch
        {
            ".exe" or ".dll" => "\uE7B8",
            ".pdf" => "\uE8A5",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "\uE8B7",
            ".mp3" or ".wav" or ".flac" => "\uE8D6",
            ".mp4" or ".mkv" or ".mov" or ".avi" => "\uE714",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => "\uE91B",
            _ => GlyphFor(category),
        };
    }

    public static string ColorKeyFor(string? category) => category switch
    {
        Image => "BrushCyan",
        Video => "BrushPurple",
        Audio => "BrushPink",
        Pdf => "BrushDanger",
        Document => "BrushAccent",
        Spreadsheet => "BrushSuccess",
        Presentation => "BrushOrange",
        Code => "BrushWarning",
        Archive => "BrushOrange",
        Installer => "BrushAccent",
        Program => "BrushAccent",
        _ => "BrushTextMuted",
    };
}

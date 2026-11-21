using System.Text;
using WinPulse.Core;

namespace WinPulse.Services;

public enum PreviewKind
{
    None,
    Image,
    Text,
    Pdf,
    Audio,
    Video,
    Unsupported,
}

public sealed class PreviewInfo
{
    public PreviewKind Kind { get; init; } = PreviewKind.None;
    public string TypeLabel { get; init; } = string.Empty;
    public string? Text { get; init; }
    public bool Truncated { get; init; }
    public string? Message { get; init; }
    public Uri? MediaSource { get; init; }
}

/// <summary>Decides how a file should be previewed and loads lightweight text content.</summary>
public sealed class PreviewService
{
    private const long MaxTextBytes = 512 * 1024;

    private static readonly HashSet<string> TextPreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv", ".tsv", ".srt", ".vtt", ".sub", ".reg", ".props", ".csproj", ".sln",
        ".gitignore", ".editorconfig", ".dockerfile", ".nfo", ".m3u", ".m3u8",
    };

    public PreviewInfo Describe(string path)
    {
        try
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var category = FileKinds.CategoryOf(extension, path);

            if (FileKinds.IsImage(extension))
            {
                return new PreviewInfo { Kind = PreviewKind.Image, TypeLabel = "Image" };
            }

            if (extension == ".pdf")
            {
                return new PreviewInfo { Kind = PreviewKind.Pdf, TypeLabel = "PDF document" };
            }

            if (category == FileKinds.Video)
            {
                return new PreviewInfo { Kind = PreviewKind.Video, TypeLabel = "Video", MediaSource = ToUri(path) };
            }

            if (category == FileKinds.Audio)
            {
                return new PreviewInfo { Kind = PreviewKind.Audio, TypeLabel = "Audio", MediaSource = ToUri(path) };
            }

            if (category is FileKinds.Text or FileKinds.Code || TextPreviewExtensions.Contains(extension))
            {
                return LoadText(path, extension, category);
            }

            if (extension == ".docx" || extension == ".xlsx" || extension == ".pptx" || extension == ".doc" ||
                extension == ".xls" || extension == ".ppt")
            {
                return new PreviewInfo
                {
                    Kind = PreviewKind.Unsupported,
                    TypeLabel = "Office document",
                    Message = "Office files cannot be rendered safely inside WinPulse. Use “Open” to view them in Word, Excel or PowerPoint.",
                };
            }

            return new PreviewInfo
            {
                Kind = PreviewKind.Unsupported,
                TypeLabel = category,
                Message = "No inline preview is available for this file type, but you can open it with its default app.",
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"Preview failed for {path}: {ex.Message}");
            return new PreviewInfo
            {
                Kind = PreviewKind.Unsupported,
                Message = "WinPulse could not read this file for preview.",
            };
        }
    }

    private static PreviewInfo LoadText(string path, string extension, string category)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return new PreviewInfo { Kind = PreviewKind.Unsupported, Message = "The file no longer exists." };
        }

        if (info.Length == 0)
        {
            return new PreviewInfo { Kind = PreviewKind.Text, TypeLabel = category, Text = string.Empty };
        }

        var length = (int)Math.Min(info.Length, MaxTextBytes);
        var buffer = new byte[length];
        int read;

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192, FileOptions.SequentialScan))
        {
            read = stream.Read(buffer, 0, buffer.Length);
        }

        if (Array.IndexOf(buffer, (byte)0, 0, read) >= 0)
        {
            return new PreviewInfo
            {
                Kind = PreviewKind.Unsupported,
                TypeLabel = category,
                Message = "This looks like a binary file, so there is no text preview.",
            };
        }

        using var reader = new StreamReader(new MemoryStream(buffer, 0, read), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();

        return new PreviewInfo
        {
            Kind = PreviewKind.Text,
            TypeLabel = category,
            Text = text,
            Truncated = info.Length > MaxTextBytes,
        };
    }

    private static Uri ToUri(string path) => new Uri(path);
}

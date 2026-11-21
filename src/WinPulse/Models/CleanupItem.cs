using WinPulse.Core;

namespace WinPulse.Models;

/// <summary>A cleanup suggestion: a file plus the reason WinPulse flagged it.</summary>
public sealed class CleanupItem
{
    public FileEntry File { get; init; } = new();
    public string Reason { get; init; } = string.Empty;
    public string ReasonColorKey { get; init; } = "BrushWarning";

    public string Name => File.Name;
    public string SizeText => File.SizeText;
    public string ModifiedText => File.ModifiedText;
    public string Folder => File.Folder;
    public string Glyph => File.Glyph;
    public string GlyphColorKey => File.GlyphColorKey;
    public string FullPath => File.FullPath;
}

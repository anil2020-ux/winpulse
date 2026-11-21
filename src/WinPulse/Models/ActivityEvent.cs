using WinPulse.Core;

namespace WinPulse.Models;

/// <summary>An activity event recorded while scanning (file appeared or disappeared).</summary>
public sealed class ActivityEvent
{
    public long Id { get; set; }
    public string Kind { get; set; } = "added";
    public string FullPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = FileKinds.Other;
    public long Size { get; set; }
    public DateTime At { get; set; }

    public string SizeText => Formats.Bytes(Size);
    public string AtText => Formats.Relative(At);
    public string Glyph => Kind == "deleted" ? "\uE74D" : FileKinds.GlyphForFile(Category, System.IO.Path.GetExtension(Name));
    public string KindLabel => Kind == "deleted" ? "Removed" : "Added";
}

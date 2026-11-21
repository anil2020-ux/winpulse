using WinPulse.Models;

namespace WinPulse.Models;

/// <summary>One row in the Ctrl+K command palette: a page, an action or an indexed file.</summary>
public sealed class PaletteItem
{
    public required string Title { get; init; }
    public string Subtitle { get; init; } = string.Empty;
    public string Glyph { get; init; } = "\uE721";
    public string Kind { get; init; } = "command";
    public string? CommandId { get; init; }
    public FileEntry? File { get; init; }
}

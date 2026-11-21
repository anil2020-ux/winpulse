using WinPulse.Core;

namespace WinPulse.Models;

/// <summary>One category row in the dashboard storage breakdown.</summary>
public sealed class CategoryStat
{
    public string Category { get; set; } = FileKinds.Other;
    public int Count { get; set; }
    public long Size { get; set; }
    public double Percent { get; set; }

    public string SizeText => Formats.Bytes(Size);
    public string CountText => $"{Count:N0} files";
    public string Glyph => FileKinds.GlyphFor(Category);
    public string ColorKey => FileKinds.ColorKeyFor(Category);
    public string PercentText => $"{Percent:0.#}%";
}

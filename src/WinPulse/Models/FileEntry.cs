using System.ComponentModel;
using WinPulse.Core;

namespace WinPulse.Models;

/// <summary>A single file known to WinPulse's local index.</summary>
public sealed class FileEntry : INotifyPropertyChanged
{
    private bool _keep;

    public long Id { get; set; }
    public string FullPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Root { get; set; } = string.Empty;
    public string RootLabel { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Category { get; set; } = FileKinds.Other;
    public bool IsScreenshot { get; set; }
    public long Size { get; set; }
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public bool Missing { get; set; }

    /// <summary>When true, cleanup suggestions will not offer this file for deletion.</summary>
    public bool Keep
    {
        get => _keep;
        set
        {
            if (_keep == value)
            {
                return;
            }

            _keep = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Keep)));
        }
    }

    public string Folder => System.IO.Path.GetDirectoryName(FullPath) ?? string.Empty;
    public string FolderName => System.IO.Path.GetFileName(Folder);
    public string SizeText => Formats.Bytes(Size);
    public string ModifiedText => Formats.DateTimeShort(Modified);
    public string CreatedText => Formats.DateTimeShort(Created);
    public string ModifiedRelative => Formats.Relative(Modified);
    public string AgeText => $"{Math.Max(0, (int)(DateTime.Now - Modified).TotalDays)}d old";
    public double AgeDays => Math.Max(0, (DateTime.Now - Modified).TotalDays);
    public string CategoryLabel => IsScreenshot ? FileKinds.ScreenshotLabel : Category;
    public string Glyph => FileKinds.GlyphForFile(Category, Extension);
    public string GlyphColorKey => FileKinds.ColorKeyFor(Category);
    public string ExtLabel => string.IsNullOrWhiteSpace(Extension) ? "FILE" : Extension.TrimStart('.').ToUpperInvariant();
    public string Tooltip => $"{FullPath}\n{SizeText}  •  modified {ModifiedText}";

    public event PropertyChangedEventHandler? PropertyChanged;
}

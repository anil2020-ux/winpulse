using System.ComponentModel;
using System.Windows.Media;

namespace WinPulse.Models;

/// <summary>Thumbnail tile in the Screenshots gallery.</summary>
public sealed class ScreenshotTile : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;

    public ScreenshotTile(FileEntry file) => File = file;

    public FileEntry File { get; }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value))
            {
                return;
            }

            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    public string Name => File.Name;
    public string Caption => $"{File.ModifiedText}  •  {File.SizeText}";
    public string Glyph => File.Glyph;
    public string Tooltip => File.FullPath;

    public event PropertyChangedEventHandler? PropertyChanged;
}

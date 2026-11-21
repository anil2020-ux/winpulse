using System.ComponentModel;
using System.Windows.Media;

namespace WinPulse.Models;

/// <summary>A single copy shown inside the duplicate review dialog.</summary>
public sealed class DuplicateCopy : INotifyPropertyChanged
{
    private bool _keep;
    private ImageSource? _thumbnail;

    public DuplicateCopy(FileEntry file) => File = file;

    public FileEntry File { get; }

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
    public string Meta => $"{File.SizeText}  •  {File.ModifiedText}";
    public string Folder => File.Folder;
    public string Glyph => File.Glyph;

    public event PropertyChangedEventHandler? PropertyChanged;
}

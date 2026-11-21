using System.Runtime.InteropServices;

namespace WinPulse.Core;

/// <summary>Resolves shell known folders (respects OneDrive redirection and moved folders).</summary>
public static class KnownFolders
{
    private static readonly Dictionary<string, Guid> FolderIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Downloads"] = new("374DE290-123F-4565-9164-39C4925E467B"),
        ["Desktop"] = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
        ["Documents"] = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"),
        ["Pictures"] = new("33E28130-4E1E-4676-835A-98395C3BC3BB"),
        ["Videos"] = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC"),
        ["Music"] = new("4BD8D571-6D19-48D3-BE97-422220080E43"),
        ["Screenshots"] = new("B7BEDE81-DF94-4682-A7D8-57A717845556"),
    };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    /// <summary>Returns the on-disk path for a shell known folder, or null.</summary>
    public static string? Get(string name)
    {
        if (!FolderIds.TryGetValue(name, out var id))
        {
            return null;
        }

        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out buffer) == 0 && buffer != IntPtr.Zero)
            {
                var path = Marshal.PtrToStringUni(buffer);
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    return path;
                }
            }
        }
        catch
        {
            // Fall through to Environment.GetFolderPath.
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }

        var special = name switch
        {
            "Downloads" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "Music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            _ => string.Empty,
        };

        return string.IsNullOrEmpty(special) ? null : special;
    }

    /// <summary>Default set of folders WinPulse indexes on first launch.</summary>
    public static IReadOnlyList<(string Label, string Path)> DefaultRoots()
    {
        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string label, string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && seen.Add(path!))
            {
                result.Add((label, path!));
            }
        }

        Add("Downloads", Get("Downloads"));
        Add("Desktop", Get("Desktop"));
        Add("Documents", Get("Documents"));
        Add("Pictures", Get("Pictures"));
        Add("Videos", Get("Videos"));
        return result;
    }
}

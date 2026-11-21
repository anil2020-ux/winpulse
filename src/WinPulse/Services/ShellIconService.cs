using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPulse.Core;

namespace WinPulse.Services;

/// <summary>Resolves Windows shell icons for file types (cached per extension).</summary>
public sealed class ShellIconService
{
    private readonly ConcurrentDictionary<string, ImageSource?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path, uint fileAttributes, ref ShellFileInfo info, uint infoSize, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x00000080;

    public ImageSource? GetIconForExtension(string extension)
    {
        var key = string.IsNullOrWhiteSpace(extension) ? ".file" : extension.ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        ImageSource? icon = null;
        try
        {
            var info = new ShellFileInfo();
            var fakePath = "file" + (key.StartsWith('.') ? key : "." + key);
            var handle = SHGetFileInfo(
                fakePath,
                FileAttributeNormal,
                ref info,
                (uint)Marshal.SizeOf<ShellFileInfo>(),
                ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes);

            if (handle != IntPtr.Zero && info.IconHandle != IntPtr.Zero)
            {
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        info.IconHandle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    icon = source;
                }
                finally
                {
                    DestroyIcon(info.IconHandle);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Shell icon lookup failed for {key}: {ex.Message}");
        }

        _cache[key] = icon;
        return icon;
    }

    public ImageSource? GetIconForPath(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return GetIconForExtension(extension);
        }

        return GetIconForExtension(".exe");
    }
}

using WinPulse.Core;
using WinPulse.Models;

namespace WinPulse.Services;

/// <summary>Walks watched folders quickly, skipping noisy/system directories.</summary>
public static class FileScanner
{
    private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".svn", ".hg", "$recycle.bin", "system volume information",
        "appdata", "windows", "$windows.~bt", "$windows.~ws", "programdata", "recovery",
        ".nuget", ".cache", "package cache", "__pycache__", ".venv", "venv", "site-packages",
        ".gradle", ".m2", "obj", "bin", ".vs", ".idea", "target", "dist", "build",
    };

    public static IEnumerable<FileRecord> Enumerate(string rootPath, string rootLabel, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootPath))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            string[] subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(current);
            }
            catch
            {
                subdirectories = Array.Empty<string>();
            }

            foreach (var subdirectory in subdirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new DirectoryInfo(subdirectory);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if ((info.Attributes & FileAttributes.System) != 0)
                    {
                        continue;
                    }

                    if (SkipDirectoryNames.Contains(info.Name))
                    {
                        continue;
                    }

                    pending.Push(subdirectory);
                }
                catch
                {
                    // Inaccessible folder - skip.
                }
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileRecord record;
                try
                {
                    var info = new FileInfo(file);
                    if (!info.Exists || (info.Attributes & FileAttributes.System) != 0)
                    {
                        continue;
                    }

                    var extension = info.Extension.ToLowerInvariant();
                    var category = FileKinds.CategoryOf(extension, info.FullName);
                    record = new FileRecord
                    {
                        FullPath = info.FullName,
                        Name = info.Name,
                        RootPath = rootPath,
                        RootLabel = rootLabel,
                        Extension = extension,
                        Category = category,
                        IsScreenshot = FileKinds.IsScreenshot(info.Name, info.FullName, extension),
                        Size = info.Length,
                        Created = info.CreationTime,
                        Modified = info.LastWriteTime,
                    };
                }
                catch
                {
                    continue;
                }

                yield return record;
            }
        }
    }
}

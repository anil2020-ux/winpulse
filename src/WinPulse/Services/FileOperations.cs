using System.Diagnostics;
using System.Runtime.InteropServices;
using WinPulse.Core;

namespace WinPulse.Services;

public sealed record OperationResult(bool Success, string Message = "")
{
    public static OperationResult Ok(string message) => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}

/// <summary>
/// File actions exposed by the UI. Deletes always go to the Recycle Bin so they are undoable.
/// </summary>
public static class FileOperations
{
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;
    private const ushort FofWantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr Window;
        public uint Function;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct operation);

    public static OperationResult Recycle(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return OperationResult.Fail("No files selected.");
        }

        try
        {
            var from = string.Join("\0", paths) + "\0\0";
            var operation = new ShFileOpStruct
            {
                Function = FoDelete,
                From = from,
                Flags = (ushort)(FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofWantNukeWarning),
            };

            var code = SHFileOperation(ref operation);
            if (operation.AnyOperationsAborted)
            {
                return OperationResult.Fail("Delete was cancelled.");
            }

            if (code != 0)
            {
                return OperationResult.Fail($"Windows could not move the item(s) to the Recycle Bin (code {code}).");
            }

            return OperationResult.Ok(paths.Count == 1
                ? "Moved to the Recycle Bin."
                : $"Moved {paths.Count} items to the Recycle Bin.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    public static OperationResult Rename(string path, string newName)
    {
        try
        {
            if (!File.Exists(path))
            {
                return OperationResult.Fail("That file no longer exists.");
            }

            newName = newName.Trim();
            if (newName.Length == 0)
            {
                return OperationResult.Fail("Enter a new name.");
            }

            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return OperationResult.Fail("The name contains characters Windows does not allow.");
            }

            var directory = Path.GetDirectoryName(path);
            if (directory is null)
            {
                return OperationResult.Fail("Could not resolve the file's folder.");
            }

            var target = Path.Combine(directory, newName);
            if (string.Equals(target, path, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Ok("Name unchanged.");
            }

            if (File.Exists(target) || Directory.Exists(target))
            {
                return OperationResult.Fail("A file or folder with that name already exists.");
            }

            File.Move(path, target);
            return OperationResult.Ok("Renamed.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    public static OperationResult Move(IReadOnlyList<string> paths, string targetDirectory)
    {
        if (!Directory.Exists(targetDirectory))
        {
            return OperationResult.Fail("The destination folder no longer exists.");
        }

        var moved = 0;
        var failures = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                var fileName = Path.GetFileName(path);
                var target = UniqueTarget(targetDirectory, fileName);
                File.Move(path, target);
                moved++;
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return Summarize("Moved", "move", moved, failures);
    }

    public static OperationResult Copy(IReadOnlyList<string> paths, string targetDirectory)
    {
        if (!Directory.Exists(targetDirectory))
        {
            return OperationResult.Fail("The destination folder no longer exists.");
        }

        var copied = 0;
        var failures = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                var fileName = Path.GetFileName(path);
                var target = UniqueTarget(targetDirectory, fileName);
                File.Copy(path, target);
                copied++;
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return Summarize("Copied", "copy", copied, failures);
    }

    private static OperationResult Summarize(string verb, string action, int count, List<string> failures)
    {
        if (failures.Count == 0)
        {
            return OperationResult.Ok(count == 1 ? $"{verb} 1 item." : $"{verb} {count} items.");
        }

        var detail = string.Join(Environment.NewLine, failures.Take(5));
        return count == 0
            ? OperationResult.Fail($"Could not {action} the item(s):{Environment.NewLine}{detail}")
            : OperationResult.Ok($"{verb} {count} item(s); {failures.Count} failed:{Environment.NewLine}{detail}");
    }

    private static string UniqueTarget(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; index < 10_000; index++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Could not find a free file name in the destination folder.");
    }

    public static OperationResult Open(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return OperationResult.Fail("That file no longer exists.");
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return OperationResult.Ok("Opening with the default app.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    public static OperationResult RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                return OperationResult.Fail("That item no longer exists.");
            }

            return OperationResult.Ok("Opened in File Explorer.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    public static OperationResult OpenFolder(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return OperationResult.Fail("That folder no longer exists.");
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
            return OperationResult.Ok("Opened folder.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }
}

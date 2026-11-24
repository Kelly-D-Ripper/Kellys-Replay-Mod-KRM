using System;
using System.Collections.Generic;
using System.IO;

namespace ReplayMod.Core;

// Removing from the library moves only the selected replay and its known
// camera-project files into a unique recovery folder, never permanent deletion.
public static class ReplayFileDelete
{
    private static readonly string[] Sidecars = { ".director.json", ".director.json.bak", ".director.json.tmp" };

    public static string MoveToTrash(string directory, string selectedPath)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string source = Path.GetFullPath(selectedPath);
        if (!string.Equals(Path.GetDirectoryName(source), root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(source), ".replay", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a recording from this replay library.");
        if (!File.Exists(source)) throw new FileNotFoundException("The selected recording no longer exists.", source);
        if (File.Exists(source + ".partial") || Directory.Exists(source + ".partial"))
            throw new IOException("This recording is still being saved.");

        string trash = Path.Combine(root, "Deleted recordings");
        if (Directory.Exists(trash) && (File.GetAttributes(trash) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The recovery folder cannot be a link.");
        Directory.CreateDirectory(trash);
        string destination = Path.Combine(trash, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        var paths = new List<string>();
        foreach (var suffix in Sidecars) if (File.Exists(source + suffix)) paths.Add(source + suffix);
        paths.Add(source);
        var locks = new List<FileStream>();
        var moved = new List<(string From, string To)>();
        try
        {
            foreach (string path in paths)
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked recording files cannot be deleted from the library.");
                locks.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Delete, 1));
            }
            try
            {
                foreach (string path in paths)
                {
                    string target = Path.Combine(destination, Path.GetFileName(path));
                    File.Move(path, target);
                    moved.Add((path, target));
                }
            }
            catch (Exception error)
            {
                var failures = new List<string>();
                for (int i = moved.Count - 1; i >= 0; i--)
                {
                    try { File.Move(moved[i].To, moved[i].From); }
                    catch (Exception restore) { failures.Add(Path.GetFileName(moved[i].To) + ": " + restore.Message); }
                }
                if (failures.Count > 0)
                    throw new IOException("Some files remain in " + destination + ". Restore them manually: " + string.Join("; ", failures), error);
                throw new IOException("Removal failed; the original files were restored. " + error.Message, error);
            }
        }
        finally { foreach (var held in locks) held.Dispose(); }
        return destination;
    }
}

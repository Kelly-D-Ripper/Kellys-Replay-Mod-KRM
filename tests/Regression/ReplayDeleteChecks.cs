using System;
using System.IO;
using ReplayMod.Core;

internal static class ReplayDeleteChecks
{
    public static void Run(Action<string, Action> test)
    {
        test("removing a recording keeps its camera project recoverable", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "replay-delete-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "flight.replay");
                File.WriteAllText(path, "recording");
                File.WriteAllText(path + ".director.json", "project");
                File.WriteAllText(path + ".director.json.bak", "backup");
                File.WriteAllText(Path.Combine(root, "other.replay"), "untouched");
                string trash = ReplayFileDelete.MoveToTrash(root, path);
                if (File.Exists(path) || File.ReadAllText(Path.Combine(trash, "flight.replay")) != "recording" ||
                    File.ReadAllText(Path.Combine(trash, "flight.replay.director.json")) != "project" ||
                    File.ReadAllText(Path.Combine(trash, "flight.replay.director.json.bak")) != "backup" ||
                    File.ReadAllText(Path.Combine(root, "other.replay")) != "untouched")
                    throw new Exception("Recording, project and unrelated file ownership must be preserved.");
            }
            finally { Directory.Delete(root, true); }
        });
        test("removal rejects unrelated paths and incomplete recordings", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "replay-delete-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "flight.replay");
                File.WriteAllText(path, "recording");
                File.WriteAllText(path + ".partial", "writer");
                try { ReplayFileDelete.MoveToTrash(root, path); throw new Exception("Pending writer was accepted."); }
                catch (IOException) { }
                try { ReplayFileDelete.MoveToTrash(root, Path.Combine(root, "..", "outside.replay")); throw new Exception("Outside path was accepted."); }
                catch (IOException) { }
                if (File.ReadAllText(path) != "recording") throw new Exception("Original changed.");
            }
            finally { Directory.Delete(root, true); }
        });
        test("repeated recording names remain independent in recovery", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "replay-delete-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "flight.replay");
                File.WriteAllText(path, "first");
                string first = ReplayFileDelete.MoveToTrash(root, path);
                File.WriteAllText(path, "second");
                string second = ReplayFileDelete.MoveToTrash(root, path);
                if (first == second || File.ReadAllText(Path.Combine(first, "flight.replay")) != "first" ||
                    File.ReadAllText(Path.Combine(second, "flight.replay")) != "second")
                    throw new Exception("Recovery copies must not overwrite one another.");
            }
            finally { Directory.Delete(root, true); }
        });
    }
}

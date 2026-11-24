using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ReplayMod.Diagnostics;

// Deliberately small local test protocol. No arbitrary paths, reflection,
// network listener or commands that can change a running multiplayer mission.
public sealed class BackgroundCommand
{
    public const int MaxBytes = 2048;
    public string Id, Action, File;
    public double Value;

    public static BackgroundCommand Parse(string text, string session)
    {
        if (text == null || text.Length > MaxBytes || string.IsNullOrEmpty(session)) throw new InvalidDataException("Invalid command size or session.");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            if (line.Length == 0) continue;
            int split = line.IndexOf('=');
            if (split < 1 || !fields.TryAddCompat(line.Substring(0, split), line.Substring(split + 1))) throw new InvalidDataException("Invalid or duplicate command field.");
        }
        foreach (var key in fields.Keys)
            if (key != "session" && key != "id" && key != "action" && key != "value" && key != "file") throw new InvalidDataException("Unknown command field.");
        if (!fields.TryGetValue("session", out var received) || received != session) throw new InvalidDataException("Command belongs to another test session.");
        if (!fields.TryGetValue("id", out var id) || id.Length < 1 || id.Length > 64) throw new InvalidDataException("Invalid command ID.");
        foreach (char c in id) if (!char.IsLetterOrDigit(c) || c > 127) throw new InvalidDataException("Command ID must be ASCII letters or digits.");
        if (!fields.TryGetValue("action", out var action)) throw new InvalidDataException("Missing action.");
        var command = new BackgroundCommand { Id = id, Action = action };
        switch (action)
        {
            case "status":
            case "pause":
            case "play":
                if (fields.ContainsKey("value") || fields.ContainsKey("file")) throw new InvalidDataException("Unexpected command argument.");
                break;
            case "seek":
            case "speed":
                if (fields.ContainsKey("file") || !fields.TryGetValue("value", out var number) ||
                    !double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out command.Value) || double.IsNaN(command.Value) || double.IsInfinity(command.Value) ||
                    (action == "seek" ? command.Value < 0 || command.Value > 604800 : command.Value < 0.1 || command.Value > 1)) throw new InvalidDataException("Invalid numeric command argument.");
                break;
            case "open":
                if (fields.ContainsKey("value") || !fields.TryGetValue("file", out var file) || file.Length < 8 || file.Length > 180 ||
                    !file.EndsWith(".replay", StringComparison.OrdinalIgnoreCase) || file.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0 ||
                    file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || file != Path.GetFileName(file)) throw new InvalidDataException("Open requires a replay filename in this game's Replays directory.");
                command.File = file;
                break;
            default: throw new InvalidDataException("Unsupported test action.");
        }
        return command;
    }
}

internal static class BackgroundCommandDictionary
{
    internal static bool TryAddCompat(this Dictionary<string, string> fields, string key, string value)
    { if (fields.ContainsKey(key)) return false; fields.Add(key, value); return true; }
}

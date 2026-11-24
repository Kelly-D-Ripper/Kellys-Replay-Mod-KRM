using System;
using System.Collections.Generic;

namespace ReplayMod.Diagnostics;

public static class ReplayStartup
{
    private static readonly Queue<string> stages = new Queue<string>();
    public static string Phase { get; private set; } = "Not initialized";
    public static long Heartbeats { get; private set; }
    public static string Trace => string.Join("\n",stages.ToArray());
    internal static void Mark(string phase)
    {
        Phase = phase;
        if(stages.Count == 16) stages.Dequeue();
        stages.Enqueue(DateTime.UtcNow.ToString("O") + " " + phase);
        Plugin.logger?.LogInfo("Replay startup: " + phase);
    }
    internal static void Tick() { Heartbeats++; }
}

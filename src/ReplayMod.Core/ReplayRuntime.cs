using UnityEngine;

namespace ReplayMod.Core;

public static class ReplayRuntime
{
    public static byte FormatMajor { get; set; } = 1;
    public static bool IsPlayback => ReplayManager.i != null && ReplayManager.i.GetState() == ModStates.Replay;
}

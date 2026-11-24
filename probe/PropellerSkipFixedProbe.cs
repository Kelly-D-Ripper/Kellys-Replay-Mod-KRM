using System;
using HarmonyLib;
using ReplayMod.Core;

// Diagnostic only: determine whether the native PropFan physics callback is
// responsible for the captured source's first nonzero audio block.
[HarmonyPatch(typeof(PropFan),"FixedUpdate")]
internal static class PropellerSkipFixedProbe
{
    internal static int skipped;
    internal static bool Enabled=>Environment.GetEnvironmentVariable("REPLAY_TEST_SKIP_PROP_FIXED")=="1";
    [HarmonyPrepare] private static bool Prepare()=>Enabled;
    [HarmonyPrefix,HarmonyPriority(Priority.First)] private static bool Prefix(PropFan __instance)
    {
        if(!ReplayRuntime.IsPlayback||__instance==null||__instance.aircraft==null||ReplayManager.i?.Playback?.OwnsReplayAircraft(__instance.aircraft)!=true)return true;
        skipped++;return false;
    }
}

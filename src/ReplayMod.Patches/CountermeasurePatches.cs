using HarmonyLib;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Patches;

// Observe the common actual-spawn path after native random spread. Recording
// commands or both command/RPC receivers would duplicate host/client flares.
[HarmonyPatch(typeof(IRFlare),nameof(IRFlare.LaunchFlare))]
internal static class ReplayFlareLaunchPatch
{
    [HarmonyPostfix]
    private static void Postfix(IRFlare __instance,Aircraft aircraft,Transform launchPoint)
    {Recorder.Active?.RecordFlareLaunch(__instance,aircraft,launchPoint);}
}
[HarmonyPatch(typeof(IRFlare),"Update")]
internal static class ReplayFlareEndPatch
{
    [HarmonyPostfix]
    private static void Postfix(IRFlare __instance)
    {if(__instance.burnTime<=0)Recorder.Active?.RecordCountermeasureEnd(__instance);}
}
[HarmonyPatch(typeof(RadarChaff),nameof(RadarChaff.LaunchChaff))]
internal static class ReplayChaffLaunchPatch
{
    [HarmonyPostfix]
    private static void Postfix(RadarChaff __instance,Aircraft aircraft,Transform launchPoint)
    {Recorder.Active?.RecordChaffLaunch(__instance,aircraft,launchPoint);}
}
[HarmonyPatch(typeof(RadarChaff),"Update")]
internal static class ReplayChaffEndPatch
{
    [HarmonyPostfix]
    private static void Postfix(RadarChaff __instance)
    {if(__instance.chaffTime<=0)Recorder.Active?.RecordCountermeasureEnd(__instance);}
}

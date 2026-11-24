using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(LevelInfo),"UpdateSimulation")]
public static class ReplayWorldSimulationPatch
{
    [HarmonyPrefix]
    public static bool Prefix(LevelInfo __instance)=>!ReplayWorldPlayback.OwnsEnvironment(__instance);
}
[HarmonyPatch(typeof(LevelInfo),"Update")]
public static class ReplayWorldWindPhasePatch
{
    [HarmonyPostfix]
    public static void Postfix(LevelInfo __instance)=>ReplayWorldPlayback.RefreshNativeWind(__instance);
}
[HarmonyPatch(typeof(CloudLayer),"Update")]
public static class ReplayWorldCloudPhasePatch
{
    [HarmonyPrefix]
    public static void Prefix(CloudLayer __instance)=>ReplayWorldPlayback.PrepareNativeCloud(__instance);
    [HarmonyPostfix]
    public static void Postfix(CloudLayer __instance)=>ReplayWorldPlayback.RefreshNativeCloud(__instance);
}
[HarmonyPatch(typeof(Hangar),"OpenDoors")]
public static class ReplayWorldHangarOpenPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Hangar __instance,ref UniTask __result)
    {if(!ReplayWorldPlayback.OwnsHangar(__instance))return true;__result=UniTask.CompletedTask;return false;}
}
[HarmonyPatch(typeof(Hangar),"CloseDoors")]
public static class ReplayWorldHangarClosePatch
{
    [HarmonyPrefix]
    public static bool Prefix(Hangar __instance,ref UniTask __result)
    {if(!ReplayWorldPlayback.OwnsHangar(__instance))return true;__result=UniTask.CompletedTask;return false;}
}

using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(NavLights.NavLight),"Toggle")]
internal static class ReplayAircraftLightOverridePatch
{
    [HarmonyPostfix]
    private static void AfterToggle(NavLights.NavLight __instance)
    {
        if(ReplayRuntime.IsPlayback&&__instance.aircraft!=null)
            ReplayManager.i?.Playback?.RefreshPlaybackLights(__instance.aircraft);
    }
}

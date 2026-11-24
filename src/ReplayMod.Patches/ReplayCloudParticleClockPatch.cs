using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(CloudLayer),"Update")]
public static class ReplayCloudParticleClockPatch
{
    // Unity calls Start before the first Update. Applying here avoids native
    // initialization replacing the clocks, without a scene scan or new timer.
    [HarmonyPrefix]
    public static void Prefix(CloudLayer __instance)=>ReplayCloudParticleClock.Ensure(__instance);
}

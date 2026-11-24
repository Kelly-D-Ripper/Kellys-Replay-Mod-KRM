using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(DamageParticles),"Awake")]
public static class ReplayRecordedFuelFlameAwakePatch
{
    [HarmonyPrefix]
    public static bool Prefix(DamageParticles __instance)=>!ReplayFuelFlameVisual.IsControlled(__instance);
}
[HarmonyPatch(typeof(DamageParticles),"SlowUpdate")]
public static class ReplayRecordedFuelFlameSlowPatch
{
    [HarmonyPrefix]
    public static bool Prefix(DamageParticles __instance)=>!ReplayFuelFlameVisual.IsControlled(__instance);
}
[HarmonyPatch(typeof(DamageParticles),"Update")]
public static class ReplayRecordedFuelFlameUpdatePatch
{
    [HarmonyPrefix]
    public static bool Prefix(DamageParticles __instance)=>!ReplayFuelFlameVisual.IsControlled(__instance);
}
[HarmonyPatch(typeof(DamageParticles),nameof(DamageParticles.ParentObjectCulled))]
public static class ReplayRecordedFuelFlameCullPatch
{
    [HarmonyPrefix]
    public static bool Prefix(DamageParticles __instance)=>!ReplayFuelFlameVisual.IsControlled(__instance);
}

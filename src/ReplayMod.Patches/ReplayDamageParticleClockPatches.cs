using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(DamageParticles),"DetachTimer")]
public static class ReplayDamageParticleDetachTimerPatch
{
    [HarmonyPrefix]
    public static bool Prefix(DamageParticles __instance,ref UniTask __result)
    {if(!ReplayDamageParticleMotion.CanOwn(__instance))return true;__result=UniTask.CompletedTask;return false;}
}
[HarmonyPatch(typeof(DamageParticles),"SlowUpdate")]
public static class ReplayDamageParticleSpeedPatch
{
    [HarmonyPrefix]
    public static void Prefix(DamageParticles __instance)=>ReplayDamageParticleMotion.Prepare(__instance);
}

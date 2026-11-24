using System;
using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(Gun), "SpawnBullet")]
internal static class ReplayGunShotPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Gun __instance)
    {
        if (ReplayGunPatchOwnership.ShouldSuppress(__instance)) return false;
        Recorder.Active?.BeginGunShot(__instance);
        return true;
    }
    [HarmonyFinalizer]
    private static Exception Finalizer(Gun __instance, Exception __exception)
    { Recorder.Active?.EndGunShot(__instance); return __exception; }
}

[HarmonyPatch(typeof(Gun), "FixedUpdate")]
internal static class ReplayGunSimulationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Gun __instance) => !ReplayGunPatchOwnership.ShouldSuppress(__instance);
}

internal static class ReplayGunPatchOwnership
{
    internal static bool ShouldSuppress(Gun gun) => ReplayRuntime.IsPlayback && gun != null &&
        (ReplayManager.i?.Playback?.OwnsReplayUnit(gun.attachedUnit) ?? false);
}

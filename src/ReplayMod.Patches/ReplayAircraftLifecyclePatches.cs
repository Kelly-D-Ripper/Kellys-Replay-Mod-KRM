using HarmonyLib;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Patches;

// Received RPCs call IDamageable.Detach directly and bypass Unit.DetachPart.
[HarmonyPatch(typeof(UnitPart),nameof(UnitPart.Detach),new[]{typeof(Vector3),typeof(Vector3)})]
public static class ReplayReceivedPartDetachPatch
{
    [HarmonyPrefix]public static void Prefix(UnitPart __instance)
    {if(!ReplayRuntime.IsPlayback&&__instance!=null)Recorder.Active?.RecordReceivedPartDetach(__instance.parentUnit,__instance.id);}
}

[HarmonyPatch(typeof(UnitPart),nameof(UnitPart.SpawnFragments))]
public static class ReplayRecordedPartFragmentsPatch
{
    [HarmonyPrefix]public static bool Prefix(UnitPart __instance)
        =>!ReplayRuntime.IsPlayback||ReplayRuntime.FormatMajor<5||!(__instance.parentUnit is Aircraft)||!ReplayVfxPlayback.IsOwnedActor(__instance.parentUnit)||ReplayPartFragmentScope.IsActive;
}
[HarmonyPatch(typeof(AeroPart),nameof(AeroPart.SpawnFragments))]
public static class ReplayRecordedAeroFragmentsPatch
{
    [HarmonyPrefix]public static bool Prefix(AeroPart __instance)
        =>!ReplayRuntime.IsPlayback||ReplayRuntime.FormatMajor<5||!(__instance.parentUnit is Aircraft)||!ReplayVfxPlayback.IsOwnedActor(__instance.parentUnit)||ReplayPartFragmentScope.IsActive;
}

// Pilot actor spawns are already present in the replay. Capsule disembarkation
// must not spawn a second live pilot after its recorded landing.
[HarmonyPatch(typeof(EscapeCapsule),nameof(EscapeCapsule.SpawnDisembarkingPilot))]
public static class ReplayCapsulePilotSpawnPatch
{
    [HarmonyPrefix]public static bool Prefix(EscapeCapsule __instance)
        =>!ReplayRuntime.IsPlayback||__instance.part==null||!ReplayVfxPlayback.IsOwnedActor(__instance.part.parentUnit);
}

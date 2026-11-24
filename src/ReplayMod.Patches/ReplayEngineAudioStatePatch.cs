using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

// Native engines can spool between recorded samples. Their audio methods run
// before Player.LateUpdate restores RPM, so restoring only at the render step
// leaves those methods reading native simulation values. Preserve the recorded
// RPM immediately before presentation; leave all native audio and effects code
// active and never synthesize an ignition or engine-running transition.
[HarmonyPatch(typeof(Turbofan), "Animate")]
public static class ReplayTurbofanAudioStatePatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)] public static void Prefix(Turbofan __instance)
    {
        if(ReplayRuntime.IsPlayback)ReplayManager.i.Playback?.RestoreRecordedEngineRpm(__instance.aircraft);
    }
}

[HarmonyPatch(typeof(Turbojet), "Animate")]
public static class ReplayTurbojetAudioStatePatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)] public static void Prefix(Turbojet __instance)
    {
        if(ReplayRuntime.IsPlayback)ReplayManager.i.Playback?.RestoreRecordedEngineRpm(__instance.aircraft);
    }
}

[HarmonyPatch(typeof(TurbineEngine), "Animate")]
public static class ReplayTurbineAudioStatePatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)] public static void Prefix(TurbineEngine __instance)
    {
        if(ReplayRuntime.IsPlayback)ReplayManager.i.Playback?.RestoreRecordedEngineRpm(__instance.aircraft);
    }
}

// These engines read RPM in different native callbacks from the jet engines.
[HarmonyPatch(typeof(ConstantSpeedProp), "PropAnimate")]
public static class ReplayPropellerAudioStatePatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)] public static void Prefix(ConstantSpeedProp __instance)
    {
        if(ReplayRuntime.IsPlayback)ReplayManager.i.Playback?.RestoreRecordedEngineRpm(__instance.aircraft);
    }
}

[HarmonyPatch(typeof(PropFan), "FixedUpdate")]
public static class ReplayPropFanAudioStatePatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)] public static void Prefix(PropFan __instance)
    {
        if(ReplayRuntime.IsPlayback)ReplayManager.i.Playback?.RestoreRecordedEngineRpm(__instance.aircraft);
    }
}

[HarmonyPatch(typeof(RotorShaft), "AnimateRotor")]
public static class ReplayRotorAudioStatePatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)] public static void Prefix(RotorShaft __instance)
    {
        if(ReplayRuntime.IsPlayback)ReplayManager.i.Playback?.RestoreRecordedEngineRpm(__instance.aircraft);
    }
}

[HarmonyPatch(typeof(DuctedFan), "Update")]
public static class ReplayDuctedFanStatePatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)] public static void Prefix(DuctedFan __instance)
    {
        if(ReplayRuntime.IsPlayback)ReplayManager.i.Playback?.RestoreRecordedEngineRpm(__instance.aircraft);
    }
}

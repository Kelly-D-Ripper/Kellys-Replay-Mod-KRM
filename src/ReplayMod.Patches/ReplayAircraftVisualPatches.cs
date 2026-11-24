using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

// Native sweep chooses an automatic mode for ownerless aircraft and overwrites
// customAxis1. The replay controller restores recorded sweep instead.
[HarmonyPatch(typeof(SwingWingController),"FixedUpdate")]
public static class ReplaySwingWingPatch
{
    [HarmonyPrefix]public static bool Prefix(SwingWingController __instance)
        =>!ReplayRuntime.IsPlayback||!(ReplayManager.i.Playback?.OwnsReplayAircraft(__instance.aircraft)??false);
}

// The gear will be down when it bloody well feels like it.
// Gear coroutines run from elapsed Unity time and cannot restore an arbitrary
// paused frame. Recorded visual channels take over only replay-owned aircraft.
[HarmonyPatch(typeof(LandingGear),"LandingGear_OnSetGear")]
public static class ReplayRecordedGearAnimationPatch
{
    [HarmonyPrefix]public static bool Prefix(LandingGear __instance)
        =>!ReplayRuntime.IsPlayback||!(ReplayManager.i.Playback?.HasRecordedAircraftVisuals(__instance.aircraft)??false);
}

[HarmonyPatch(typeof(Airbrake),"FixedUpdate")]
public static class ReplayAirbrakeForcePatch
{
    [HarmonyPrefix]public static bool Prefix(Airbrake __instance)
        =>!ReplayRuntime.IsPlayback||!(ReplayManager.i.Playback?.OwnsReplayAircraft(__instance.aircraft)??false);
}

[HarmonyPatch(typeof(TiltWingController),"FixedUpdate")]
public static class ReplayTiltWingPatch
{
    [HarmonyPrefix]public static bool Prefix(TiltWingController __instance)
        =>!ReplayRuntime.IsPlayback||!(ReplayManager.i.Playback?.OwnsReplayAircraft(__instance.aircraft)??false);
}

// Keep native nozzle thrust/audio/effects running, but do not let ownerless
// playback choose a new VTOL/reverse mode and overwrite recorded customAxis1.
[HarmonyPatch(typeof(DuctedThrustSystem),"ChooseOperatingMode")]
public static class ReplayDuctedThrustInputPatch
{
    [HarmonyPrefix]public static bool Prefix(DuctedThrustSystem __instance)
        =>!ReplayRuntime.IsPlayback||!(ReplayManager.i.Playback?.OwnsReplayAircraft(__instance.aircraft)??false);
}

[HarmonyPatch(typeof(TailHook),"FixedUpdate")]
public static class ReplayTailHookContactPatch
{
    [HarmonyPrefix]public static bool Prefix(TailHook __instance)
        =>!ReplayRuntime.IsPlayback||!(ReplayManager.i.Playback?.OwnsReplayAircraft(__instance.aircraft)??false);
}

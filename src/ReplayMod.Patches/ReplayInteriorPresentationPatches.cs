using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(Aircraft),nameof(Aircraft.SetCockpitRenderers))]
public static class ReplayCockpitRendererVisibilityPatch
{
    [HarmonyPostfix]public static void Postfix(Aircraft __instance,bool enabled)
        =>ReplayInteriorPresentation.NativeCockpitVisibilityChanged(__instance,enabled);
}
[HarmonyPatch(typeof(Pilot),nameof(Pilot.TogglePilotVisibility))]
public static class ReplaySeatedPilotVisibilityPatch
{
    [HarmonyPostfix]public static void Postfix(Pilot __instance,bool enabled)
        =>ReplayInteriorPresentation.NativePilotVisibilityChanged(__instance,enabled);
}
[HarmonyPatch(typeof(CameraStateManager),nameof(CameraStateManager.SwitchState))]
public static class ReplayCockpitOverlayModePatch
{
    [HarmonyPostfix]public static void Postfix(CameraStateManager __instance)
        =>ReplayInteriorPresentation.NativeCameraModeChanged(__instance);
}

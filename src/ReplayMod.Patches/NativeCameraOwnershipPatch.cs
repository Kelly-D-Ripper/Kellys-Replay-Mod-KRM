using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ReplayMod.Core;
using ReplayMod.Director;

namespace ReplayMod.Patches;

[HarmonyPatch]
public static class NativeCameraOwnershipPatch
{
    // Only native panel entry points. SetCamState and GetValues are internal
    // refreshes and must not cancel a studio driver merely by displaying it.
    public static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var name in new[] {
            nameof(CameraControlUI.OpenMenu),
            nameof(CameraControlUI.SetCamFree), nameof(CameraControlUI.SetCamCockpit),
            nameof(CameraControlUI.SetCamOrbit), nameof(CameraControlUI.SetCamChase),
            nameof(CameraControlUI.SetCamControlled), nameof(CameraControlUI.SetCamFlyby),
            nameof(CameraControlUI.CancelMovement), nameof(CameraControlUI.ReverseMovement),
            nameof(CameraControlUI.ApplyCamPos), nameof(CameraControlUI.ApplyCamAngle),
            nameof(CameraControlUI.ApplyPivotPos), nameof(CameraControlUI.ApplyPivotAngle),
            nameof(CameraControlUI.ReturnPivotToUnit), nameof(CameraControlUI.SwitchPivotUp),
            nameof(CameraControlUI.ToggleCameraTarget),
            nameof(CameraControlUI.ApplyFOV), nameof(CameraControlUI.ResetFOV),
            nameof(CameraControlUI.ApplyFOVSpeed), nameof(CameraControlUI.ApplyFOVInertia),
            nameof(CameraControlUI.ApplyInputSpeed), nameof(CameraControlUI.ApplyInputsToggle) })
            yield return AccessTools.DeclaredMethod(typeof(CameraControlUI), name);
    }

    [HarmonyPrefix]
    public static bool Prefix(CameraControlUI __instance, MethodBase __originalMethod)
    {
        if (!ReplayRuntime.IsPlayback || NativeCameraRefreshScope.IsActive) return true;
        bool opening = __originalMethod.Name == nameof(CameraControlUI.OpenMenu);
        // Sliders also invoke callbacks during native Start. This is not a user
        // request to abandon a camera path or take control of its replay clock.
        if (!opening && !__instance.isOpen) return true;
        var director = ReplayDirector.i;
        if (director?.IsBusy == true) return false;
        director?.EnterNativeCameraControls();
        if (opening || __originalMethod.Name == nameof(CameraControlUI.SetCamCockpit) ||
            __originalMethod.Name == nameof(CameraControlUI.SetCamOrbit) ||
            __originalMethod.Name == nameof(CameraControlUI.SetCamChase) ||
            __originalMethod.Name == nameof(CameraControlUI.SetCamControlled) ||
            __originalMethod.Name == nameof(CameraControlUI.SetCamFlyby))
            director?.RestoreNativeCameraTarget();
        return true;
    }
}

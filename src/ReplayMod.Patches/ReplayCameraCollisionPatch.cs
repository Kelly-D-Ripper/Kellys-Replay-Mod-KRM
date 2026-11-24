using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ReplayMod.Core;
using ReplayMod.Director;
using UnityEngine;

namespace ReplayMod.Patches;

// Redirect only native camera collision queries. Never alter world colliders,
// player physics, or the game camera outside replay playback.
public static class ReplayCameraCollision
{
    public static bool Bypass => ReplayRuntime.IsPlayback && ReplayDirector.i?.Project?.allowCameraThroughWorld == true;

    public static bool Linecast(Vector3 start, Vector3 end, out RaycastHit hit, int mask)
    {
        if (Bypass) { hit = default; return false; }
        return Physics.Linecast(start, end, out hit, mask);
    }

    public static float SeaFloor() => Bypass ? float.NegativeInfinity : Datum.LocalSeaY;

    public static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var code = instructions.ToList();
        var nativeCast = AccessTools.Method(typeof(Physics), "Linecast", new[] { typeof(Vector3), typeof(Vector3), typeof(RaycastHit).MakeByRefType(), typeof(int) });
        var nativeSea = AccessTools.PropertyGetter(typeof(Datum), "LocalSeaY");
        var casts = code.Where(i => i.Calls(nativeCast)).ToArray();
        var seas = code.Where(i => i.Calls(nativeSea)).ToArray();
        bool free = original.DeclaringType == typeof(CameraFreeState);
        if (casts.Length != 1 || (free && seas.Length != 1))
        {
            Plugin.logger.LogWarning("Replay camera collision option unavailable for " + original.DeclaringType.Name + ": game camera code changed.");
            return code;
        }
        casts[0].opcode = OpCodes.Call;
        casts[0].operand = AccessTools.Method(typeof(ReplayCameraCollision), nameof(Linecast));
        if (free)
        {
            seas[0].opcode = OpCodes.Call;
            seas[0].operand = AccessTools.Method(typeof(ReplayCameraCollision), nameof(SeaFloor));
        }
        return code;
    }
}

[HarmonyPatch(typeof(CameraFreeState), nameof(CameraFreeState.UpdateState))]
public static class ReplayFreeCameraCollisionPatch
{ [HarmonyTranspiler] public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code, MethodBase original) => ReplayCameraCollision.Rewrite(code, original); }

[HarmonyPatch(typeof(CameraControlledState), nameof(CameraControlledState.UpdateState))]
public static class ReplayControlledCameraCollisionPatch
{ [HarmonyTranspiler] public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code, MethodBase original) => ReplayCameraCollision.Rewrite(code, original); }

[HarmonyPatch(typeof(CameraOrbitState), "CameraMotion")]
public static class ReplayOrbitCameraCollisionPatch
{ [HarmonyTranspiler] public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code, MethodBase original) => ReplayCameraCollision.Rewrite(code, original); }

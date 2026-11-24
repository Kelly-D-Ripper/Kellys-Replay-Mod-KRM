using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Effects;
using ReplayMod.Core;
using ReplayMod.Director;
using UnityEngine;

namespace ReplayMod.Patches;

// The native foliage renderer builds its frustum in LateUpdate. Studio camera
// paths also move the camera in LateUpdate, at order 1000. Draw foliage after
// that movement so its compute culling uses the camera that actually renders.
[DefaultExecutionOrder(1100)]
public sealed class ReplayDetailRenderOrder : MonoBehaviour
{
    private static ReplayDetailRenderOrder instance;
    private static readonly MethodInfo nativeLateUpdate = AccessTools.Method(typeof(DetailRenderer), "LateUpdate");
    private readonly List<DetailRenderer> pending = new List<DetailRenderer>();
    private bool invoking;
    private bool failed;

    private void Awake() => instance = this;
    private void OnDisable() { pending.Clear(); if (instance == this) instance = null; }
    private void OnEnable() => instance = this;

    // Fall back to the native timing whenever the replay runner isn't ready.
    public static bool RunNativeNow(DetailRenderer renderer)
    {
        var runner = instance;
        if (runner == null || !runner.isActiveAndEnabled || runner.invoking || runner.failed || nativeLateUpdate == null ||
            !ReplayRuntime.IsPlayback || ReplayDirector.i == null || !ReplayDirector.i.isActiveAndEnabled ||
            ReplayManager.i?.Playback == null || ReplayManager.i.Playback.IsSeeking)
            return true;
        if (!runner.pending.Contains(renderer)) runner.pending.Add(renderer);
        return false;
    }

    private void LateUpdate()
    {
        if (pending.Count == 0) return;
        invoking = true;
        try
        {
            foreach (var renderer in pending)
            {
                if (renderer == null || !renderer.isActiveAndEnabled) continue;
                try { nativeLateUpdate.Invoke(renderer, null); }
                catch (Exception error)
                {
                    failed = true;
                    Plugin.logger.LogWarning("Replay foliage timing override failed; returning to native timing: " +
                        (error.InnerException ?? error).Message);
                    break;
                }
            }
        }
        finally { pending.Clear(); invoking = false; }
    }
}

[HarmonyPatch(typeof(DetailRenderer), "LateUpdate")]
public static class ReplayDetailRendererLateUpdatePatch
{
    [HarmonyPrefix] public static bool Prefix(DetailRenderer __instance) => ReplayDetailRenderOrder.RunNativeNow(__instance);
}

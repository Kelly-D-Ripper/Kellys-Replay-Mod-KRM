using System;
using ReplayMod.Misc;
using UnityEngine;

namespace ReplayMod.Core;

// Close shots need a smaller near plane than native external cameras' one metre.
// This does not enable hidden/destroyed renderers or alter shared shaders.
public static class ReplayCloseCameraVisibility
{
    public const float CloseNearPlane = .05f;
    private static readonly ReplayCloseCameraState<UnityEngine.Camera> state = new ReplayCloseCameraState<UnityEngine.Camera>();
    private static readonly Func<UnityEngine.Camera, bool> Alive = camera => camera != null;
    private static readonly Func<UnityEngine.Camera, float> Read = camera => camera.nearClipPlane;
    private static readonly Action<UnityEngine.Camera, float> Write = (camera, value) => camera.nearClipPlane = value;
    private static bool enabled, failed;
    public static bool Enabled => enabled;
    public static bool HasOverride => state.HasOverride;

    public static void SetEnabled(bool value)
    {
        enabled = value;
        if (!value) Clear();
    }

    // Called after the director/native camera has chosen the current shot.
    public static void Update(CameraStateManager camera)
    {
        bool scoped = enabled && ReplayRuntime.IsPlayback && ReplayManager.i?.Playback != null &&
            !GameManager.IsHeadless && !SceneHelper.IsTransitionPending && camera != null &&
            ReferenceEquals(SceneSingleton<CameraStateManager>.i, camera) && camera.mainCamera != null;
        if (!scoped) { Restore(); failed = false; return; }
        if (failed) return;
        try { state.Ensure(camera.mainCamera, CloseNearPlane, Alive, Read, Write); }
        catch (Exception error)
        {
            Restore(); failed = true;
            Plugin.logger.LogWarning("Replay close-camera clipping option unavailable; native camera continues: " + error.Message);
        }
    }

    public static void Clear()
    {
        enabled = false; failed = false; Restore();
    }

    private static void Restore()
    {
        if (state.Clear(Alive, Read, Write) > 0)
            Plugin.logger.LogWarning("Replay close-camera near-plane restore failed; camera ownership released.");
    }
}

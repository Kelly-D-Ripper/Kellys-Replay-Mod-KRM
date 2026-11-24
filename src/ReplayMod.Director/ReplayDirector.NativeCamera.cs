using ReplayMod.Camera;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Director;

public partial class ReplayDirector
{
    public void OpenNativeCameraControls()
    {
        if (!ReplayRuntime.IsPlayback || IsBusy) return;
        var panel = SceneSingleton<CameraControlUI>.i;
        if (panel == null) { Status = "The game's camera controls are not ready yet."; return; }
        EnterNativeCameraControls();
        RestoreNativeCameraTarget();
        panel.OpenMenu();
        Status = "Game camera controls opened. Use Controlled for the game's movement and rotation tools.";
    }

    // Called before deliberate native panel actions, never from its refresh.
    // Repeated actions leave the native Controlled pivot/movement state intact.
    public void EnterNativeCameraControls()
    {
        if (!ReplayRuntime.IsPlayback || IsBusy || NativeCameraRefreshScope.IsActive) return;
        var manager = SceneSingleton<CameraStateManager>.i;
        if (manager == null || manager.mainCamera == null) return;
        bool droveCamera = cameraOwned || CameraKeysEnabled || Project.rig != 0 || RailActive || TakeActive;
        float lens = CurrentCameraFov;
        CancelCameraTake();
        CancelEventShot();
        StopRail();
        ReleaseCamera();
        CameraKeysEnabled = false;
        Project.rig = 0;
        pendingNativeTarget = null;
        ResetRollOwnership();
        CameraManager.i?.StopCamFlight();
        ResetCameraSmoothing();
        Project.fov = lens;
        // ReleaseCamera restores its captured lens. Retain the visible studio
        // lens on handoff, but do not reset native wheel inertia on every edit.
        if (droveCamera) RestoreCameraLens(lens);
    }

    public void RestoreNativeCameraTarget()
    {
        if (!ReplayRuntime.IsPlayback || IsBusy || NativeCameraRefreshScope.IsActive) return;
        var manager = SceneSingleton<CameraStateManager>.i;
        if (manager == null || manager.followingUnit != null) return;
        var target = ResolveCameraActor(Project.target);
        if (target != null) manager.SetFollowingUnit(target);
    }
}

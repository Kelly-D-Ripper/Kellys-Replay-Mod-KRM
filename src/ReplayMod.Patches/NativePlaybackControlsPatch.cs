using HarmonyLib;
using ReplayMod.Core;
using ReplayMod.Director;
using UnityEngine;
using System;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(CameraFreeState), nameof(CameraFreeState.UpdateState))]
public static class NativeFreeCameraUiFocusPatch
{
    [HarmonyPrefix] public static bool Prefix(CameraStateManager cam)
    {
        if (!ReplayRuntime.IsPlayback || (ReplayDirector.i?.IsBusy != true && !MainMenu.HasInputFocus && !MainMenu.IsPointerOverUI)) return true;
        cam.cameraVelocity = Vector3.zero;
        return false;
    }
}

// The native camera panel shares Space and game time with replay playback. Keep
// its time controls connected to the same clock as the trailer toolbar.
// Native Update maps Space to the panel's camera movement. In a replay Space is
// play/pause (unless rebound); the panel's movement buttons still work by mouse.
internal static class NativeCameraSpaceKey
{
    internal static bool OwnedByReplay=>Input.GetKeyDown(KeyCode.Space)&&ConfigManager.TogglePause!=null&&
        ConfigManager.TogglePause.Value.MainKey==KeyCode.Space;
}
[HarmonyPatch(typeof(CameraControlUI), nameof(CameraControlUI.ApplyMovement))]
public static class NativeCameraMovementPatch
{
    [HarmonyPrefix] public static bool Prefix(CameraControlUI __instance)
    {
        if (!ReplayRuntime.IsPlayback) return true;
        // Native Update invokes this for Space even with the panel closed.
        // Preserve the suite's normal replay shortcut outside native controls.
        if (NativeCameraRefreshScope.IsActive || !__instance.isOpen || MainMenu.HasInputFocus || ReplayDirector.i?.IsBusy == true || NativeCameraSpaceKey.OwnedByReplay) return false;
        ReplayDirector.i?.EnterNativeCameraControls();
        return true;
    }
}
[HarmonyPatch(typeof(CameraControlUI), nameof(CameraControlUI.PauseMovement))]
public static class NativeCameraPauseMovementPatch
{
    [HarmonyPrefix] public static bool Prefix(CameraControlUI __instance)
    {
        if (!ReplayRuntime.IsPlayback) return true;
        if (NativeCameraRefreshScope.IsActive || !__instance.isOpen || MainMenu.HasInputFocus || ReplayDirector.i?.IsBusy == true || NativeCameraSpaceKey.OwnedByReplay) return false;
        ReplayDirector.i?.EnterNativeCameraControls();
        return true;
    }
}
[HarmonyPatch(typeof(CameraControlUI), nameof(CameraControlUI.PauseTime))]
public static class NativeCameraPauseTimePatch
{
    [HarmonyPrefix] public static bool Prefix()
    {
        if (!ReplayRuntime.IsPlayback) return true;
        ReplayManager.i.Playback?.Pause(); return false;
    }
}
[HarmonyPatch(typeof(CameraControlUI), nameof(CameraControlUI.PlayTime))]
public static class NativeCameraPlayTimePatch
{
    [HarmonyPrefix] public static bool Prefix()
    {
        if (!ReplayRuntime.IsPlayback) return true;
        ReplayManager.i.Playback?.Resume(); return false;
    }
}
[HarmonyPatch(typeof(CameraControlUI), nameof(CameraControlUI.ApplyTimeFactor))]
public static class NativeCameraTimeFactorPatch
{
    [HarmonyPrefix] public static bool Prefix(CameraControlUI __instance)
    {
        if (!ReplayRuntime.IsPlayback) return true;
        if (NativeCameraRefreshScope.IsActive) return false;
        float value = __instance.timeFactor.value;
        if (value <= 0) ReplayManager.i.Playback?.Pause();
        else
        {
            ReplayDirector.i?.SetPlaybackSpeed(value);
            __instance.timeFactor.SetValueWithoutNotify(Mathf.Clamp(value, .1f, 1));
            ReplayManager.i.Playback?.Resume();
        }
        return false;
    }
}

[HarmonyPatch(typeof(CameraControlUI), nameof(CameraControlUI.GetValues))]
public static class NativeCameraPanelRefreshPatch
{
    [HarmonyPrefix] public static void Prefix() => NativeCameraRefreshScope.Enter();
    [HarmonyPostfix] public static void Postfix(CameraControlUI __instance)
    {
        if(!ReplayRuntime.IsPlayback)return;
        var playback=ReplayManager.i.Playback;
        if(playback!=null&&__instance.timeFactor!=null)
        {
            __instance.timeFactor.SetValueWithoutNotify(playback.IsPaused?0:playback.PlaybackSpeed);
            __instance.UpdateValues();
        }
    }
    [HarmonyFinalizer] public static Exception Finalizer(Exception __exception)
    { NativeCameraRefreshScope.Exit(); return __exception; }
}

// GameplayUI's bound Slow Motion action uses SetTimeFactor directly, bypassing
// CameraControlUI. Route that key to the recorded clock as well; live gameplay
// retains the native behavior, including its original slow-motion factor.
[HarmonyPatch(typeof(GameplayUI), nameof(GameplayUI.SetTimeFactor))]
public static class NativeGameplayTimeFactorPatch
{
    [HarmonyPrefix] public static bool Prefix(float value)
    {
        if (!ReplayRuntime.IsPlayback) return true;
        ReplayManager.i.Playback?.SetNativePlaybackTimeFactor(value);
        return false;
    }
}
[HarmonyPatch(typeof(GameplayUI), nameof(GameplayUI.ResumeGame))]
public static class NativeResumePlaybackClockPatch
{
    [HarmonyPostfix] public static void Postfix()
    {
        if (ReplayRuntime.IsPlayback && ReplayManager.i.Playback?.IsSeeking == false)
            ReplayManager.i.Playback.ApplyPlaybackClock();
    }
}

// Pause means pause, Mitch. This is not a difficult concept.
// The native pause menu does not stop the replay clock by itself.
[HarmonyPatch(typeof(GameplayUI), nameof(GameplayUI.PauseGame))]
public static class NativePauseReplayPatch
{
    internal static readonly ReplayPauseMenuState<Player> state=new ReplayPauseMenuState<Player>();
    [HarmonyPrefix] public static void Prefix()
    {
        if (!ReplayRuntime.IsPlayback || GameplayUI.GameIsPaused) return;
        var player = ReplayManager.i.Playback;
        if (player == null || !player.enabled) return;
        state.Open(player,!player.IsPaused);
        player.Pause();
    }
}

[HarmonyPatch(typeof(GameplayUI), nameof(GameplayUI.ResumeGame))]
public static class NativeResumeReplayPatch
{
    [HarmonyPostfix] public static void Postfix()
    {
        var player=ReplayManager.i?.Playback;
        if(NativePauseReplayPatch.state.Close(player,ReplayRuntime.IsPlayback && player!=null && player.enabled && !GameplayUI.GameIsPaused))player.Resume();
    }
}

// Quit Mission must tear down replay ownership before the native world unloads.
[HarmonyPatch(typeof(QuitMissionButton), "onClick")]
public static class NativeQuitReplayPatch
{
    [HarmonyPrefix] public static bool Prefix()
    {
        var manager = ReplayManager.i;
        if (!ReplayRuntime.IsPlayback || manager == null) return true;
        NativePauseReplayPatch.state.Clear();
        if (!manager.IsTransitioning) _ = manager.SwitchState(ModStates.Idle);
        return false;
    }
}

// A deliberate native position/speed edit takes control back from a rail move.
[HarmonyPatch(typeof(CameraStateManager),nameof(CameraStateManager.SetCameraPosition),new[] {typeof(GlobalPosition),typeof(Quaternion)})]
public static class NativeManualCameraPositionPatch
{
    [HarmonyPrefix] public static void Prefix()
    {if(ReplayRuntime.IsPlayback)ReplayDirector.i?.StopRail();}
}
[HarmonyPatch(typeof(CameraControlUI),nameof(CameraControlUI.ApplyInputSpeed))]
public static class NativeManualCameraSpeedPatch
{
    [HarmonyPrefix] public static void Prefix()
    {if(ReplayRuntime.IsPlayback)ReplayDirector.i?.StopRail();}
}
[HarmonyPatch(typeof(CameraControlUI),nameof(CameraControlUI.SetCamFree))]
public static class NativeManualFreeCameraPatch
{
    [HarmonyPrefix] public static void Prefix()
    {if(ReplayRuntime.IsPlayback)ReplayDirector.i?.StopRail();}
}

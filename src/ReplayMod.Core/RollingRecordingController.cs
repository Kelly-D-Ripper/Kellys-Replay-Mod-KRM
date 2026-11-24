using System;
using System.Threading.Tasks;
using BepInEx.Configuration;
using UnityEngine;

namespace ReplayMod.Core;

// Coordinates the existing recorder without changing the game's mission or networking state.
public sealed class RollingRecordingController : MonoBehaviour
{
    public static RollingRecordingController Instance { get; private set; }
    private bool transitioning;
    private bool saving;
    private float nextCheck;
    private float retryAfter;
    private float noticeUntil;
    private string notice = "";
    private object pendingMission;
    private object pendingMap;
    private int appliedRetention;
    public bool Enabled => ConfigManager.ContinuousRecording?.Value ?? false;
    public int RetentionSeconds => Math.Max(30, Math.Min(300, ConfigManager.RecentClipSeconds?.Value ?? 120));
    public string SaveHotkeyLabel => ConfigManager.SaveRecentClip?.Value.ToString() ?? "F9";
    public bool Busy => transitioning || saving;
    public bool Capturing => Recorder.Active != null && Recorder.Active.IsRolling && Recorder.Active.IsRecording && Recorder.Active.SessionValid;
    public double BufferedSeconds => Capturing ? Recorder.Active.RollingDuration : 0;
    public long BufferBytes => Capturing ? Recorder.Active.RollingBytes : 0;
    public bool MemoryLimited => Capturing && Recorder.Active.RollingMemoryLimited;
    public bool NoticeActive => Time.unscaledTime < noticeUntil && notice.Length > 0;
    public string Notification => NoticeActive ? notice : "";
    public string Status
    {
        get
        {
            if (saving) return "Saving recent gameplay… Recording continues.";
            if (transitioning) return "Preparing recording…";
            if (Time.unscaledTime < noticeUntil && notice.Length > 0) return notice;
            if (Capturing)
            {
                string result = $"Keeping {Math.Floor(BufferedSeconds):0}s of recent gameplay. {SaveHotkeyLabel} saves a clip.";
                return MemoryLimited ? result + " Busy mission: the memory limit shortened this clip." : result;
            }
            if (!Enabled) return "Recent gameplay recording is off.";
            if (ReplayManager.i?.GetState() == ModStates.Replay) return "Recent gameplay recording resumes in your next mission.";
            if (ReplayManager.i?.GetState() == ModStates.Record) return "Full-session recording is active. Recent recording starts after it stops.";
            return "Ready to keep recent gameplay when a mission starts.";
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    public static bool IsUsableGameplay()
    {
        if (Application.isBatchMode || UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null ||
            ReplayManager.i == null || ReplayManager.i.IsFailedReplayWorld || ReplayManager.i.GetState() == ModStates.Replay || !MissionManager.IsRunning ||
            MissionManager.CurrentMission == null || SceneSingleton<GameplayUI>.i == null)
            return false;
        var settings = SceneSingleton<MapSettingsManager>.i;
        return settings != null && settings.MapLoader != null && !string.IsNullOrEmpty(settings.MapLoader.CurrentMap.Path);
    }

    public void SetEnabled(bool enabled)
    {
        if (ConfigManager.ContinuousRecording == null) return;
        ConfigManager.ContinuousRecording.Value = enabled;
        retryAfter = 0;
        if (enabled) SetNotice("Recent gameplay recording enabled. Save clips with " + SaveHotkeyLabel + ".", 4);
        else SetNotice("Recent gameplay recording disabled. Saved clips stay in your replay library.", 4);
        nextCheck = 0;
    }

    public void SetRetention(int seconds)
    {
        int selected = Math.Max(30, Math.Min(300, seconds));
        if (ConfigManager.RecentClipSeconds != null) ConfigManager.RecentClipSeconds.Value = selected;
        if (Capturing) Recorder.Active.SetRollingWindow(selected);
        appliedRetention = selected;
    }

    public bool AssignSaveHotkey(KeyCode key, out string reason)
    {
        if (!ConfigManager.CanAssignReplayHotkey(key, ConfigManager.SaveRecentClip, out reason)) return false;
        if (ConfigManager.SaveRecentClip == null)
        { reason = "Recording settings are not ready yet."; return false; }
        ConfigManager.SaveRecentClip.Value = new KeyboardShortcut(key);
        reason = "Save recent gameplay: " + SaveHotkeyLabel + ". Game key bindings may also use this key.";
        SetNotice(reason, 6);
        return true;
    }

    public async Task SaveRecentClip()
    {
        if (Busy) { SetNotice("A recording operation is already in progress.", 4); return; }
        if (!Capturing) { SetNotice("Enable recent gameplay recording during a mission first.", 5); return; }
        saving = true;
        try
        {
            string result = await ReplayManager.i.SaveRolling();
            SetNotice(result.TrimEnd('.') + ". Recording continues.", 8);
            Plugin.SafeLog(result);
        }
        catch (Exception ex)
        {
            SetNotice("Clip could not be saved: " + ex.Message, 8);
            Plugin.SafeLogError("Recent gameplay save failed: " + ex);
        }
        finally { saving = false; }
    }

    private void Update()
    {
        var manager = ReplayManager.i;
        if (manager == null || ConfigManager.ContinuousRecording == null) return;
        if(ReplayRecordingRecovery.IsBusy)return;
        if (!MainMenu.HasInputFocus && ConfigManager.ToggleCleanCapture?.Value.GetDown()!=true && !manager.IsTransitioning && ConfigManager.SaveRecentClip.Value.GetDown() && manager.GetState() != ModStates.Replay)
            _ = SaveRecentClip();
        if (Time.unscaledTime < nextCheck || transitioning || manager.IsTransitioning) return;
        nextCheck = Time.unscaledTime + 0.25f;
        // Finalize full-session recordings too: neither mode may silently cross a mission boundary.
        var recorder = Recorder.Active;
        if (manager.GetState() == ModStates.Record && recorder != null && (!recorder.SessionValid || !IsUsableGameplay()))
        { _ = ChangeState(ModStates.Idle); pendingMission = null; pendingMap = null; return; }
        if (manager.GetState() == ModStates.Replay) { pendingMission = null; pendingMap = null; return; }
        if (!Enabled)
        {
            pendingMission = null; pendingMap = null;
            if (recorder != null && recorder.IsRolling && manager.GetState() == ModStates.Record) _ = ChangeState(ModStates.Idle);
            return;
        }
        if (Capturing)
        {
            if (appliedRetention != RetentionSeconds) SetRetention(RetentionSeconds);
            return;
        }
        if (manager.GetState() != ModStates.Idle || !IsUsableGameplay() || Time.unscaledTime < retryAfter)
        { pendingMission = null; pendingMap = null; return; }
        // Require two stable checks so network/map initialization can finish before the baseline is captured.
        var currentMap = SceneSingleton<MapSettingsManager>.i;
        if (!ReferenceEquals(pendingMission, MissionManager.CurrentMission) || !ReferenceEquals(pendingMap, currentMap))
        { pendingMission = MissionManager.CurrentMission; pendingMap = currentMap; return; }
        manager.NextRecordingIsRolling = true;
        manager.RollingSeconds = RetentionSeconds;
        appliedRetention = RetentionSeconds;
        _ = ChangeState(ModStates.Record);
    }

    private async Task ChangeState(ModStates state)
    {
        if (transitioning) return;
        transitioning = true;
        try
        {
            await ReplayManager.i.SwitchState(state);
            if (state == ModStates.Record && !Capturing)
            { retryAfter = Time.unscaledTime + 5; SetNotice("Recording could not start yet. Waiting for the mission to finish loading.", 5); }
        }
        catch (Exception ex)
        { retryAfter = Time.unscaledTime + 5; SetNotice("Recording could not start: " + ex.Message, 6); Plugin.SafeLogError(ex.ToString()); }
        finally { transitioning = false; }
    }

    private void SetNotice(string text, float seconds)
    { notice = text; noticeUntil = Time.unscaledTime + seconds; }

    private void OnDestroy()
    { if (Instance == this) Instance = null; }
}

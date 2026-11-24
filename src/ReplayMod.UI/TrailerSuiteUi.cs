using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using ReplayMod.Camera;
using ReplayMod.Core;
using ReplayMod.Director;
using ReplayMod.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReplayMod.UI;

public sealed partial class TrailerSuiteUi : MonoBehaviour
{
    public static TrailerSuiteUi Instance { get; private set; }
    private const CursorFlags ReplayCursor = (CursorFlags)(1 << 29);
    private readonly NativeSuiteStyle style = new NativeSuiteStyle();
    private RectTransform root, bar, popout, tooltip, notification, keyCapturePanel;
    private Canvas canvas, seekCanvas;
    private GameObject seekCover, ownedEventSystem;
    private TextMeshProUGUI status, timeText, speedText, tooltipText, recordStatus, coverText, notificationText, railStatus, titleText;
    private Button[] navigationButtons;
    private Button settingsButton;
    private ScrollRect menuScroll;
    private float menuViewportHeight;
    private string currentScrollContext;
    private readonly Dictionary<string, float> menuOffsets = new Dictionary<string, float>();
    private SuiteLayoutPlan suiteLayout;
    private static readonly string[] WorkspaceMenus = { "Library", "Record", "Camera", "Events", "Scene", "Lights", "Help" };
    private TextMeshProUGUI helpCaptureInstructions, helpShortcutInstructions;
    private TextMeshProUGUI cameraFovLabel;
    private TextMeshProUGUI keyCaptureTitle, keyCaptureText;
    private Button playButton, pauseButton, frameBack, frameNext, speedLess, speedMore;
    private Button hideUiButton, hideUiKeyButton, helpHideUiButton, helpHideUiKeyButton, saveRecentKeyButton;
    private Button studioKeyButton, helpStudioKeyButton, helpRollLeftKey, helpRollRightKey, helpLevelKey;
    private Button cameraRollLeft, cameraRollRight, cameraLevel;
    private Button autoFrameSubjectButton;
    private Button pathStartTake, pathCancelTake, pathPlay, pathAttachMode, pathSmoothMode, pathStopMode;
    private TextMeshProUGUI pathStatus;
    private Button pathFovMode;
    private Slider pathSmoothnessSlider;
    private TextMeshProUGUI pathSmoothnessLabel;
    private Button helpExportLogsButton, openLogsFolderButton, copyLogsPathButton;
    private TextMeshProUGUI logExportStatus;
    private string shownExportPath, shownExportNotice;
    private bool shownExportBusy, exportStatusDirty = true;
    private Button railStart, railStop;
    private Button railMatchSpeed;
    private Button[] railDirections, railModes;
    private static readonly string[] RailNames = { "Forward", "Backward", "Left", "Right", "Up", "Down" };
    private Slider speed;
    private Slider cameraFov;
    private TimelineSlider timeline;
    private TimelineMarkerGraphic timelineMarkers;
    private TimelineTick[] shownTicks = Array.Empty<TimelineTick>();
    private Player markerPlayer;
    private DirectorProject markerProject;
    private int shownManualCount = -1, shownAutomaticCount = -1;
    private double shownMarkerDuration = -1;
    private bool snapToEvents;
    private Button snapEventsButton;
    private Player timelinePlayer;
    private double lastReadyTime;
    private double? pendingScrubTime;
    private bool open, operationBusy, loadingReplay, settingControls, capturingHotkey, relativeKey, keyEase = true, firstShow = true, showTutorial;
    private enum HotkeyCaptureTarget { None, SaveRecent, HideAllUi, Studio, RollLeft, RollRight, Level }
    private HotkeyCaptureTarget hotkeyCaptureTarget;
    private float keyRoll, speedKey = 1;
    private string currentMenu = "", markerName = "My bookmark", markerSearch = "", notice = "", selectedFile, lastDirectorStatus = "", hotkeyCaptureReason = "", operationStatus = "Preparing replay…";
    private float noticeUntil, nextRefresh, popoutWidth = 560, popoutHeight = 530, lastWidth, lastHeight, tooltipHeight = 84, tooltipWidth = 400, tooltipReadyAt;
    private ModStates previousState = (ModStates)(-1);
    private int previousRecordingMode = -1;
    private bool previousRollingEnabled;
    private int inputGuardUntilFrame = -1;
    private SuiteTooltip hovered;
    private readonly List<Selectable> replayControls = new List<Selectable>();
    private readonly List<Selectable> recordingControls = new List<Selectable>();
    private readonly List<Selectable> lensControls = new List<Selectable>();
    private readonly List<Selectable> pathEditControls = new List<Selectable>();
    private readonly List<Selectable> eventControls = new List<Selectable>();
    private readonly List<Selectable> recoveryControls = new List<Selectable>();

    public bool IsVisible => isActiveAndEnabled && open && ReplayDirector.i?.Clean != true;
    public bool ReconstructionCoverVisible => seekCover != null && seekCover.activeSelf;
    public bool PointerOverUi => IsVisible &&
        (Contains(bar) || Contains(popout) || Contains(keyCapturePanel));
    public bool HasInputFocus => StudioInputPolicy.BlocksReplayActions(capturingHotkey,
        Time.frameCount <= inputGuardUntilFrame, HasActiveTextEntry(), GUIUtility.keyboardControl != 0);
    internal bool CanUseStudioShortcut => StudioInputPolicy.CanToggle(Director?.Clean == true, capturingHotkey,
        Time.frameCount <= inputGuardUntilFrame, IsVisible, HasActiveTextEntry(), GUIUtility.keyboardControl != 0);
    private static bool HasActiveTextEntry()
    {
        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected == null || !selected.activeInHierarchy) return false;
        var tmp = selected.GetComponent<TMP_InputField>(); var legacy = selected.GetComponent<InputField>();
        bool focused = tmp != null ? tmp.isFocused : legacy?.isFocused == true;
        bool enabled = tmp != null ? tmp.isActiveAndEnabled && tmp.IsInteractable() : legacy != null && legacy.isActiveAndEnabled && legacy.IsInteractable();
        if (!focused || !enabled) return false;
        bool visible = true;
        for (var parent = selected.transform; parent != null; parent = parent.parent)
        {
            var parentCanvas = parent.GetComponent<Canvas>();
            var group = parent.GetComponent<CanvasGroup>();
            if ((parentCanvas != null && !parentCanvas.isActiveAndEnabled) || (group != null && group.alpha <= .001f)) { visible = false; break; }
        }
        return StudioInputPolicy.TextEntryActive(selected.activeInHierarchy, enabled, focused, visible);
    }
    private bool Contains(RectTransform rect) => rect != null && rect.gameObject.activeInHierarchy &&
        RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, null);
    private ReplayDirector Director => ReplayDirector.i;
    private ReplayManager Manager => ReplayManager.i;
    private bool Playback => ReplayRuntime.IsPlayback && Manager?.Playback != null;
    private bool Busy => operationBusy || Director?.IsBusy == true || Manager?.IsTransitioning == true;
    private string CleanKeyLabel => Director?.CleanCaptureHotkeyLabel ?? ConfigManager.ToggleCleanCapture?.Value.ToString() ?? "Hide key";
    private string StudioKeyLabel => ConfigManager.StudioHotkeyLabel;
    private string RollLeftKeyLabel => ConfigManager.CameraRollLeft?.Value.ToString() ?? "Z";
    private string RollRightKeyLabel => ConfigManager.CameraRollRight?.Value.ToString() ?? "C";
    private string LevelKeyLabel => ConfigManager.CameraLevel?.Value.ToString() ?? "X";
    private string HideUiDescription => "Hide all game UI and the cursor for OBS. Press " + CleanKeyLabel + " again to restore it. " + StudioKeyLabel + " reopens the studio.";

    private void Awake()
    {
        Instance = this; style.Sample(); RefreshAppearanceIfChanged(); BuildCanvas(); BuildBar(); BuildCover(); BuildIdentityCaption();
        EnsureEventSystem(); Layout(); bar.gameObject.SetActive(false); tooltip.gameObject.SetActive(false);
    }
    private void BuildCanvas()
    {
        root = Rect("Replay trailer suite", transform); canvas = root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32700;
        var scaler = root.gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        // One shared reference scale shrinks panels, text and pointer targets together.
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        root.gameObject.AddComponent<GraphicRaycaster>();
        tooltip = Rect("Control description", root); Box(tooltip, 0, 0, 400, 84); Fill(tooltip, SuiteColourRole.Background, false); Border(tooltip); tooltip.gameObject.AddComponent<RectMask2D>();
        tooltipText = Text(tooltip, "", 14, 8, 372, 68, 17); tooltipText.overflowMode = TextOverflowModes.Ellipsis;
        notification = Rect("Replay action notification", root); notification.anchorMin = notification.anchorMax = Vector2.zero; notification.pivot = Vector2.zero;
        notification.sizeDelta = new Vector2(740, 78); Fill(notification, SuiteColourRole.Background, false); Border(notification);
        notificationText = Text(notification, "", 14, 7, 712, 64, 18); notificationText.overflowMode = TextOverflowModes.Ellipsis;
        notification.gameObject.SetActive(false);
        keyCapturePanel = Rect("Assign studio shortcut", root); Fill(keyCapturePanel, SuiteColourRole.Background); Border(keyCapturePanel); keyCapturePanel.gameObject.AddComponent<RectMask2D>();
        keyCaptureTitle = Text(keyCapturePanel, "", 14, 8, 572, 30, 20);
        keyCaptureText = Text(keyCapturePanel, "", 14, 42, 572, 82, 18);
        keyCapturePanel.gameObject.SetActive(false);
    }
    private void BuildCover()
    {
        var coverRoot = Rect("Replay seek cover", transform); seekCanvas = coverRoot.gameObject.AddComponent<Canvas>();
        seekCanvas.renderMode = RenderMode.ScreenSpaceOverlay; seekCanvas.sortingOrder = 32760;
        var scaler = coverRoot.gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        coverRoot.gameObject.AddComponent<GraphicRaycaster>(); var fill = Rect("Hidden seek reconstruction", coverRoot); Stretch(fill); Fill(fill, Color.black);
        var label = Rect("Preparing replay", fill); label.anchorMin = label.anchorMax = new Vector2(.5f, .5f); label.sizeDelta = new Vector2(720, 100);
        coverText = label.gameObject.AddComponent<TextMeshProUGUI>(); style.Apply(coverText, 24); style.Unbind(coverText); coverText.color = new Color32(224, 224, 224, 255); coverText.text = "Preparing replay…\nRebuilding the scene and warming smoke trails."; coverText.alignment = TextAlignmentOptions.Center;
        seekCover = coverRoot.gameObject; seekCover.SetActive(false);
    }
    private void BuildBar()
    {
        bar = Rect("Bottom trailer controls", root); bar.anchorMin = bar.anchorMax = Vector2.zero; bar.pivot = Vector2.zero; Fill(bar, SuiteColourRole.Background); Border(bar);
        titleText = Text(bar, "KELLY'S REPLAY MOD", 0, 0, 160, 24, 17, true);
        titleText.enableAutoSizing = true; titleText.fontSizeMin = 10; titleText.fontSizeMax = 17;
        timeText = Text(bar, "Open Library to choose a replay", 0, 0, 430, 24, 18);
        status = Text(bar, StudioKeyLabel + " opens and closes the studio", 0, 0, 600, 24, 16, true);
        timeline = (TimelineSlider)Slider(bar, "Replay timeline", "Release to jump and pause. Amber ticks are recorded events, green ticks are bookmarks, and blue diamonds are camera path points. Optional event snapping is in Settings. Hold Shift to bypass snapping.", 0, 0, 1200, 24, 0, 1, 0, v =>
        {
            if (settingControls) return; double time = ScrubTime(v);
            if (snapToEvents && Duration > 0) timeline.SetValueWithoutNotify((float)(time / Duration));
            timeText.text = "Seek to " + Clock(time) + " / " + Clock(Duration);
        }, true);
        var ticks = Rect("Clustered event ticks", timeline.transform); Stretch(ticks); timelineMarkers = ticks.gameObject.AddComponent<TimelineMarkerGraphic>(); timelineMarkers.raycastTarget = false; timelineMarkers.SetAccent(style.Accent);
        timeline.DragStarted = () => { if (Playback) Director.PausePlayback(); };
        timeline.Released = v =>
        {
            if (!TimelineUsable) return;
            pendingScrubTime = ScrubTime(v); Director.Seek(pendingScrubTime.Value, null, false);
        };
        playButton = Button(bar, "Play", "Play at the selected speed. Space also switches between play and pause.", 0, 0, 90, 40, () => Director?.PlayPlayback(), true);
        pauseButton = Button(bar, "Pause", "Pause to position the camera or choose an exact moment.", 0, 0, 90, 40, () => Director?.PausePlayback());
        frameBack = Button(bar, "<", "Previous frame and pause; set the frame interval in Settings. Comma controls slow motion.", 0, 0, 38, 40, () => { if (Playback) Director.Seek(Math.Max(0, Manager.GetCurrentVirtualTime() - 1.0 / Director.FrameRate), 0, false); });
        frameNext = Button(bar, ">", "Next frame and pause. Period is the shortcut; set the frame interval in Settings.", 0, 0, 38, 40, () => { if (Playback) Manager.Playback.StepForward(1.0 / Director.FrameRate); });
        speedLess = Button(bar, "−", "Reduce speed by 0.1×. When time-lapse is active, return to 1.0×.", 0, 0, 32, 40, () => SetSpeed((Director?.ManualSpeed ?? 1) > 1 ? 1 : (Director?.ManualSpeed ?? 1) - .1f));
        speed = Slider(bar, "Playback speed", "0.1× slow motion to 1.0× normal. Faster time-lapse speeds are in Settings → Playback. Camera speed is separate.", 0, 0, 130, 40, .1f, 1, 1, v => { if (!settingControls) SetSpeed(v); });
        speedMore = Button(bar, "+", "Increase speed by 0.1× up to normal speed. For time-lapse, use Settings → Playback.", 0, 0, 32, 40, () => SetSpeed((Director?.ManualSpeed ?? 1) + .1f));
        speedText = Text(bar, "1.0×", 0, 0, 62, 40, 18);
        string[] tips = { "Open a saved replay or recover an interrupted recording.", "Record a whole mission or save the last 30 seconds to 5 minutes.", "Choose a view, FOV, target, camera path, rail or cockpit shot.", "Find launches, hits, ejections and your own bookmarks.", "Set replay weather, time of day and aircraft lights.", "Place virtual lights for a shot. Drag their boxes, change beam and colour, then save the project.", "Quick-start guide, keyboard shortcuts and support logs." };
        navigationButtons = new Button[WorkspaceMenus.Length];
        for (int n = 0; n < WorkspaceMenus.Length; n++)
        { string menu = WorkspaceMenus[n]; navigationButtons[n] = Button(bar, menu, tips[n], 0, 0, 82, 40, () => OpenMenu(menu)); }
        hideUiButton = Button(bar, "Hide UI · " + CleanKeyLabel, HideUiDescription, 0, 0, 164, 40, HideUi, true);
        settingsButton = Button(bar, "Settings", "Playback options, aircraft motion smoothing, speed ramps and GUI colours. Shortcuts are in Help → Shortcuts.", 0, 0, 92, 40, () => OpenMenu("Settings"));
        replayControls.AddRange(new Selectable[] { playButton, pauseButton, frameBack, frameNext, speed, speedLess, speedMore });
        tooltip.SetAsLastSibling();
    }
    private double Duration => Math.Max(0, Manager?.GetDuration() ?? 0);
    private bool TimelineUsable => Playback && Manager.Playback.enabled && !operationBusy && !loadingReplay && Manager.IsTransitioning != true;
    private double ScrubTime(float value)
    {
        double time = Math.Max(0, Math.Min(Duration, value * Duration));
        if (!snapToEvents || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return time;
        return TimelineMarkers.Snap(shownTicks, time, Duration, timeline.GetComponent<RectTransform>().rect.width * canvas.scaleFactor);
    }
    private void RefreshTimelineMarkers()
    {
        timelineMarkers.SetCameraKeys(Playback && Director != null ? Director.Project.cameras : null, Duration);
        if (!Playback || Director == null)
        {
            if (markerPlayer != null || shownTicks.Length > 0) { shownTicks = Array.Empty<TimelineTick>(); timelineMarkers.SetTicks(shownTicks); }
            markerPlayer = null; markerProject = null; shownManualCount = shownAutomaticCount = -1; shownMarkerDuration = -1; return;
        }
        var player = Manager.Playback; var project = Director.Project;
        int automatic = player.AutomaticBookmarks?.Count ?? 0, manual = project.bookmarks.Count;
        if (markerPlayer == player && ReferenceEquals(markerProject, project) && automatic == shownAutomaticCount && manual == shownManualCount && shownMarkerDuration == Duration) return;
        markerPlayer = player; markerProject = project; shownAutomaticCount = automatic; shownManualCount = manual; shownMarkerDuration = Duration;
        var marks = (player.AutomaticBookmarks ?? new List<Bookmark>()).Select(b => new TimelineMark(b.time, false)).Concat(project.bookmarks.Select(b => new TimelineMark(b.time, true)));
        shownTicks = TimelineMarkers.Cluster(marks, Duration); timelineMarkers.SetTicks(shownTicks);
    }
    private static string Clock(double seconds)
    { var ts = TimeSpan.FromSeconds(Math.Max(0, seconds)); return ts.TotalHours >= 1 ? ((int)ts.TotalHours) + ":" + ts.Minutes.ToString("00") + ":" + ts.Seconds.ToString("00") + "." + (ts.Milliseconds / 100) : ts.Minutes.ToString("00") + ":" + ts.Seconds.ToString("00") + "." + (ts.Milliseconds / 100); }
    private void SetSpeed(float value)
    { Director?.SetPlaybackSpeed(Mathf.Round(Mathf.Clamp(value, .1f, 1) * 10) / 10); }
    private void HideUi()
    {
        CancelHotkeyCapture(); ClearSelection(); HideTooltip(null); notification.gameObject.SetActive(false);
        // Keep the open menu and its scroll position so restoring UI resumes editing.
        Director?.SetClean(true);
        if (Director != null && !Director.Clean) Notice(Director.Status);
    }
    public void Toggle()
    {
        if (Director?.TakeActive == true) { Director.CancelCameraTake(); Director.SetClean(false); open = true; ShowFirstUse(); RestoreStudioPresentation(); return; }
        if (Director?.Clean == true) { Director.SetClean(false); open = true; } else open = !open;
        if (!open) { timeline?.CancelDrag(); CancelHotkeyCapture(); ClearSelection(); HideTooltip(null); CursorManager.SetFlag(ReplayCursor, false); EnsureEventSystem(); }
        else { ShowFirstUse(); RestoreStudioPresentation(); }
    }
    private void ShowFirstUse() { if (firstShow) { firstShow = false; Notice("Choose Camera for views and FOV. Help has a short guide and shortcuts."); } }
    public void OpenCameraTools() { if (Director?.TakeActive == true) Director.CancelCameraTake(); open = true; Director?.SetClean(false); RestoreStudioPresentation(); OpenMenu("Camera"); }
    public void OpenLibrary()
    {
        if (Director?.TakeActive == true) Director.CancelCameraTake();
        CancelHotkeyCapture(); open = true; firstShow = false; Director?.SetClean(false); RestoreStudioPresentation(); currentMenu = "Library"; RebuildMenu();
    }
    private void RestoreStudioPresentation()
    {
        // Explicit opening repairs only our own UI objects; no native menu must
        // be opened merely to unlock the cursor or initialize uGUI input.
        enabled = true;
        if (root != null) root.gameObject.SetActive(true);
        if (canvas != null) { canvas.enabled = true; var raycaster = canvas.GetComponent<GraphicRaycaster>(); if (raycaster != null) raycaster.enabled = true; }
        ClearSelection(); CursorManager.SetFlag(ReplayCursor, IsVisible); EnsureEventSystem();
    }
    private static void ClearSelection() { EventSystem.current?.SetSelectedGameObject(null); GUIUtility.keyboardControl = 0; }
    private bool UsableNativeEventSystem(EventSystem system) => system != null && system.gameObject != ownedEventSystem &&
        StudioInputPolicy.UsableNativeEvents(system.isActiveAndEnabled, system.GetComponents<BaseInputModule>().Any(m => m.isActiveAndEnabled));
    private void EnsureEventSystem()
    {
        // Preserve a healthy current native system. An enabled but unusable
        // native system must not strand either an alternate native or our own.
        var current = EventSystem.current;
        var existing = UsableNativeEventSystem(current) ? current : FindObjectsOfType<EventSystem>().FirstOrDefault(UsableNativeEventSystem);
        if (existing != null)
        {
            if (current != existing) EventSystem.current = existing;
            if (ownedEventSystem != null)
                ownedEventSystem.SetActive(false);
            return;
        }
        if (!IsVisible) { if (ownedEventSystem != null) ownedEventSystem.SetActive(false); return; }
        if (ownedEventSystem == null)
        { ownedEventSystem = new GameObject("Replay UI event system", typeof(EventSystem), typeof(StandaloneInputModule)); ownedEventSystem.transform.SetParent(transform, false); }
        else ownedEventSystem.SetActive(true);
        EventSystem.current = ownedEventSystem.GetComponent<EventSystem>();
    }
    private static void Place(RectTransform rect, OverlayBox box)
    { Box(rect, box.X, box.Y, box.Width, box.Height); }
    private static void Place(Component component, OverlayBox box)
    { Place(component.GetComponent<RectTransform>(), box); }
    private void Layout()
    {
        var size = root.rect.size;
        // Screen-space canvas dimensions may not have resolved during Awake.
        if (size.x < 320 || size.y < 240)
        {
            var canvasBounds = SuiteLayout.CanvasForScreen(Screen.width > 0 ? Screen.width : 1920, Screen.height > 0 ? Screen.height : 1080);
            size = new Vector2(canvasBounds.Width, canvasBounds.Height);
        }
        lastWidth = size.x; lastHeight = size.y;
        suiteLayout = SuiteLayout.Calculate(size.x, size.y);
        bar.sizeDelta = new Vector2(size.x, suiteLayout.DockHeight); bar.anchoredPosition = Vector2.zero;
        Place(titleText, suiteLayout.Title); Place(timeText, suiteLayout.Time); Place(status, suiteLayout.Status); Place(timeline, suiteLayout.Timeline);
        Place(playButton, suiteLayout.Play); Place(pauseButton, suiteLayout.Pause); Place(frameBack, suiteLayout.FrameBack); Place(frameNext, suiteLayout.FrameNext);
        Place(speedLess, suiteLayout.SpeedLess); Place(speed, suiteLayout.Speed); Place(speedMore, suiteLayout.SpeedMore); Place(speedText, suiteLayout.SpeedText);
        for (int n = 0; n < navigationButtons.Length; n++) Place(navigationButtons[n], suiteLayout.Navigation[n]);
        Place(hideUiButton, suiteLayout.HideUi); Place(settingsButton, suiteLayout.Settings);
        popoutHeight = suiteLayout.Popout.Height; popoutWidth = suiteLayout.Popout.Width;
        if (popout != null) Place(popout, suiteLayout.Popout);
    }
    private void Update()
    {
        RefreshAppearanceIfChanged();
        if (Manager == null) return;
        if (timelinePlayer != Manager.Playback)
        { timeline.CancelDrag(); pendingScrubTime = null; timelinePlayer = Manager.Playback; lastReadyTime = Math.Max(0, Manager.GetCurrentVirtualTime()); }
        var state = Manager.GetState();
        // Rolling and full recording both use ModStates.Record, so the state
        // alone does not tell the open Record menu when its buttons must change.
        int recordingMode = state != ModStates.Record ? 0 : Recorder.Active?.IsRolling == true ? 1 : 2;
        bool rollingEnabled = RollingRecordingController.Instance?.Enabled == true;
        bool stateChanged = state != previousState;
        bool recordMenuChanged = recordingMode != previousRecordingMode || rollingEnabled != previousRollingEnabled;
        if (stateChanged)
        {
            previousState = state; if (state == ModStates.Replay) { open = true; RestoreStudioPresentation(); ClosePopout(); ShowFirstUse(); Notice("Drag the timeline to find a moment. Pause, choose a camera, then play."); }
            if (currentMenu == "Record" || currentMenu == "Library") RebuildMenu();
        }
        else if (currentMenu == "Record" && recordMenuChanged) RebuildMenu();
        previousRecordingMode = recordingMode; previousRollingEnabled = rollingEnabled;
        bool seeking = Playback && (Manager.Playback.IsSeeking || Manager.Playback.IsReloading);
        bool showing = IsVisible; bar.gameObject.SetActive(showing); if (popout != null) popout.gameObject.SetActive(showing && !seeking && !loadingReplay);
        if (!showing && ownedEventSystem != null && ownedEventSystem.activeSelf) ownedEventSystem.SetActive(false);
        if (!showing && hovered?.AllowWhenClosed != true) HideTooltip(null);
        seekCover.SetActive(loadingReplay || seeking);
        if (seekCover.activeSelf)
        {
            seekCanvas.enabled = true;
            // The masked world stays covered; the trailer bar sits above it so
            // a new scrub can supersede an in-progress reconstruction.
            seekCanvas.sortingOrder = seeking && showing && TimelineUsable ? 32690 : 32760;
            double? wanted = Manager.Playback?.SeekTargetTime ?? pendingScrubTime;
            coverText.text = seeking ? "Preparing " + (wanted.HasValue ? Clock(wanted.Value) : "replay frame") + "…\n" + (Manager.Playback.SeekPhase ?? "Rebuilding the scene and warming smoke trails.") + "\nDrag the timeline to choose another moment." : "Opening replay…\nLoading the scene and recording data.";
        }
        if (root.rect.width != lastWidth || root.rect.height != lastHeight) { timeline.CancelDrag(); Layout(); if (currentMenu != "") RebuildMenu(); }
        if (!TimelineUsable) timeline.CancelDrag();
        timeline.interactable = TimelineUsable;
        if (snapEventsButton != null) snapEventsButton.interactable = TimelineUsable;
        RefreshTimelineMarkers();
        timeline.CheckRelease();
        if (capturingHotkey) ReadHotkey();
        RefreshBindingLabels();
        UpdateKeyCapturePanel();
        UpdateLogExportControls(); RefreshMotionControls(); RefreshStudioLightControls();
        if (showing && !HasInputFocus && Input.GetKeyDown(KeyCode.Escape) && currentMenu != "") ClosePopout();
        bool enabled = Playback && !Busy; foreach (var control in replayControls) if (control != null) control.interactable = enabled;
        playButton.interactable = enabled && Manager.Playback.IsPaused; pauseButton.interactable = enabled && !Manager.Playback.IsPaused;
        settingControls = true;
        double? requested = Playback ? Manager.Playback.SeekTargetTime ?? pendingScrubTime : null;
        if (Playback && !seeking) { lastReadyTime = Math.Max(0, Manager.GetCurrentVirtualTime()); pendingScrubTime = null; requested = null; }
        if (!timeline.Dragging) timeline.SetValueWithoutNotify(Duration > 0 ? (float)((seeking && requested.HasValue ? requested.Value : Math.Max(0, Manager.GetCurrentVirtualTime())) / Duration) : 0);
        speed.SetValueWithoutNotify(Mathf.Min(1,Director?.ManualSpeed ?? 1)); speedText.text = (Playback && Director.SpeedRampEnabled ? Manager.Playback.PlaybackSpeed : Director?.ManualSpeed ?? 1).ToString("0.#") + "×"; settingControls = false;
        if (!timeline.Dragging) timeText.text = Playback ?
            seeking ? "Seek " + Clock(requested ?? lastReadyTime) + " / " + Clock(Duration) :
            Clock(Manager.GetCurrentVirtualTime()) + " / " + Clock(Duration) + "  ·  " + (Manager.Playback.IsBuffering ? "BUFFERING" : Manager.Playback.IsPaused ? "PAUSED" : "PLAYING") :
            state == ModStates.Record ? "RECORDING YOUR GAMEPLAY" : "Open Library to play a saved recording";
        var rolling = RollingRecordingController.Instance;
        if (Director != null && Director.Status != lastDirectorStatus)
        { lastDirectorStatus = Director.Status; if (!string.IsNullOrEmpty(lastDirectorStatus)) Notice(lastDirectorStatus); }
        foreach (var control in recordingControls) if (control != null) control.interactable = !Busy && rolling?.Busy != true;
        foreach (var control in lensControls) if (control != null) control.interactable = Playback && !Busy && Director?.TakeActive != true;
        if (autoFrameSubjectButton != null) autoFrameSubjectButton.GetComponentInChildren<TMP_Text>().text = "Auto frame: " + (Director.AutoFrameSubject ? "ON" : "OFF");
        foreach (var control in pathEditControls) if (control != null) control.interactable = Playback && !Busy && Director?.TakeActive != true;
        foreach (var control in eventControls) if (control != null) control.interactable = Playback && !Busy && Director?.TakeActive != true;
        foreach (var control in recoveryControls) if (control != null) control.interactable = CanRecoverRecording;
        RefreshReplayRenameControls();
        RefreshCockpitSeatControls();
        if (pathStatus != null && Director != null)
        {
            pathStatus.text = Director.TakeStatus + "\n" + (Director.PathAttached ? "attached to selected unit" : "world path") + " · " + (Director.PathSmooth ? "smooth " + (Director.PathSmoothness * 100).ToString("0") + "%" : "straight segments");
            if (pathFovMode != null) pathFovMode.GetComponentInChildren<TMP_Text>().text = "Animate FOV / zoom: " + (Director.PathAnimatesFov ? "ON" : "OFF");
            bool ready = Playback && !Busy && !Director.TakeActive && Director.PathPointCount >= 2;
            pathStartTake.interactable = ready; pathPlay.interactable = ready; pathCancelTake.interactable = Director.TakeActive;
            pathAttachMode.GetComponentInChildren<TMP_Text>().text = "Follow selected subject: " + (Director.PathAttached ? "ON" : "OFF");
            pathSmoothMode.GetComponentInChildren<TMP_Text>().text = "Smooth curves: " + (Director.PathSmooth ? "ON" : "OFF");
            if (pathSmoothnessSlider != null)
            {
                pathSmoothnessSlider.interactable = Playback && !Busy && !Director.TakeActive && Director.PathSmooth;
                pathSmoothnessSlider.SetValueWithoutNotify(Director.PathSmoothness * 100);
                if (pathSmoothnessLabel != null) pathSmoothnessLabel.text = "Path smoothness: " + (Director.PathSmoothness * 100).ToString("0") + "%";
            }
            if (pathStopMode != null) pathStopMode.GetComponentInChildren<TMP_Text>().text = "Stop at last point: " + (Director.StopAtPathEnd ? "ON" : "OFF");
        }
        if (cameraFov != null && Director != null)
        { float fov = Director.CurrentCameraFov; cameraFov.SetValueWithoutNotify(fov); cameraFovLabel.text = "FOV / zoom: " + fov.ToString("0.0") + "°"; }
        if (railStatus != null && Director != null)
        {
            railStatus.text = Director.RailActive ? (Director.RailFollowsView ? "Following view · " : "Straight line · ") + Director.RailDirection.ToString().ToLowerInvariant() + " at " + Director.RailEffectiveSpeed.ToString("0.0") + " m/s" + (Director.RailMatchesTargetSpeed ? " · matching target" : "") + " · Stop to finish" : "Rail stopped · choose Straight line or Follow view, then Start";
            railStart.interactable = Playback && !Busy && !Director.RailActive; railStop.interactable = Playback && Director.RailActive;
            railMatchSpeed.interactable = Playback && !Busy; railMatchSpeed.GetComponentInChildren<TMP_Text>().text = "Match subject speed: " + (Director.RailMatchesTargetSpeed ? "ON" : "OFF");
            for (int n = 0; n < railModes.Length; n++)
            { railModes[n].interactable = Playback && !Busy; railModes[n].GetComponentInChildren<TMP_Text>().text = ((Director.RailFollowsView ? 1 : 0) == n ? "> " : "") + (n == 0 ? "Straight line" : "Follow view"); }
            for (int n = 0; n < railDirections.Length; n++)
            {
                railDirections[n].interactable = Playback && !Busy;
                railDirections[n].GetComponentInChildren<TMP_Text>().text = ((int)Director.RailDirection == n ? "> " : "") + RailNames[n];
            }
        }
        status.text = capturingHotkey ? "Assign " + CaptureActionLabel() + " · press one key, Esc cancels" : Busy ? (operationBusy ? operationStatus : "Preparing replay…") : Time.unscaledTime < noticeUntil ? notice : Playback ? (Director.SpeedRampEnabled ? "Saved speed ramps active · speed slider takes control" : "Space: play / pause   " + CleanKeyLabel + ": hide all UI   " + StudioKeyLabel + ": studio") : state == ModStates.Record ? (Recorder.Active?.IsRolling == true ? "Keeping recent gameplay · " + (rolling?.SaveHotkeyLabel ?? "F9") + " saves a clip" : "Full mission recording · stop before leaving the mission") : rolling?.Enabled == true ? "Constant recording ready · starts when a mission loads" : "Record a mission, then make your shots in Library";
        if(!capturingHotkey && !Busy && Time.unscaledTime >= noticeUntil && Playback && Manager.Playback.PlaybackSpeed>1)
            status.text = "Time-lapse target " + Manager.Playback.PlaybackSpeed.ToString("0.#") + "× · " + (Manager.Playback.IsBuffering ? "buffering replay" : "may run slower under load");
        if (!capturingHotkey && Playback && !string.IsNullOrEmpty(Manager.Playback.PlaybackFailure)) status.text = "Playback stopped: " + Manager.Playback.PlaybackFailure;
        else if (seeking && !capturingHotkey) status.text = "Preparing requested moment · drag again to replace it";
        if (recordStatus != null) recordStatus.text = rolling == null ? "Recording controller unavailable" : rolling.Status + "\n" + (rolling.Capturing ? "Buffered " + Clock(rolling.BufferedSeconds) + " · " + (rolling.BufferBytes / 1048576.0).ToString("0") + " MiB" + (rolling.MemoryLimited ? " · memory limit reduced retained time" : "") : "Save key: " + rolling.SaveHotkeyLabel);
        bool hasNotice = Time.unscaledTime < noticeUntil || rolling?.NoticeActive == true;
        notification.gameObject.SetActive(hasNotice && !showing && Director?.Clean != true && !seekCover.activeSelf);
        if (notification.gameObject.activeSelf)
        { notificationText.text = Time.unscaledTime < noticeUntil ? notice : rolling.Notification; notification.anchoredPosition = new Vector2(18, 18); notification.SetAsLastSibling(); }
        else if (showing && rolling?.NoticeActive == true && Time.unscaledTime >= noticeUntil) status.text = rolling.Notification;
        if (Time.unscaledTime >= nextRefresh) { EnsureEventSystem(); nextRefresh = Time.unscaledTime + 2; }
        UpdateTooltip();
    }
    private void LateUpdate()
    {
        Director?.SetStudioLightEditorVisible(IsVisible && currentMenu == "Lights" && LightsEditable);
        // The cover intentionally masks scene reconstruction. Always explain
        // it, including during clean capture, instead of showing a blank screen.
        if (coverText != null) coverText.enabled = true;
        bool cursor = IsVisible;
        if (CursorManager.GetFlag(ReplayCursor) != cursor || (cursor && (!CursorManager.Visible || !Cursor.visible || Cursor.lockState != CursorLockMode.None)))
            CursorManager.SetFlag(ReplayCursor, cursor);
    }
    private void OnDisable()
    { FlushAppearance(); open = false; Director?.SetStudioLightEditorVisible(false); timeline?.CancelDrag(); CancelHotkeyCapture(); CursorManager.SetFlag(ReplayCursor, false); if (ownedEventSystem != null) ownedEventSystem.SetActive(false); if (root != null) root.gameObject.SetActive(false); if (seekCover != null) seekCover.SetActive(false); }
    private void OnApplicationFocus(bool focused) { if (!focused) timeline?.CancelDrag(); }
    private void OnDestroy() { Canvas.willRenderCanvases -= RefreshIdentityCaption; FlushAppearance(); if (Instance == this) Instance = null; }
    internal void ShowTooltip(SuiteTooltip source, string description)
    {
        if (capturingHotkey || Director?.Clean == true || (!IsVisible && source.AllowWhenClosed != true)) return; hovered = source; tooltipText.text = description;
        tooltipWidth = Mathf.Min(400, Mathf.Max(100, root.rect.width - 16));
        var preferred = tooltipText.GetPreferredValues(description, tooltipWidth - 28, 0);
        tooltipHeight = Mathf.Clamp(preferred.y + 20, 64, Mathf.Min(260, root.rect.height - 16));
        Box(tooltipText.rectTransform, 14, 8, tooltipWidth - 28, tooltipHeight - 16);
        tooltipReadyAt = Time.unscaledTime + .35f; tooltip.gameObject.SetActive(false); tooltip.SetAsLastSibling();
    }
    internal void HideTooltip(SuiteTooltip source)
    { if (source != null && hovered != source) return; hovered = null; if (tooltip != null) tooltip.gameObject.SetActive(false); }
    private void UpdateTooltip()
    {
        if (hovered == null) return;
        if (Time.unscaledTime < tooltipReadyAt) return;
        tooltip.gameObject.SetActive(true);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, Input.mousePosition, null, out var point))
        { var rect = root.rect; var box = OverlayPlacement.NearPointer(rect.width, rect.height, point.x - rect.xMin, rect.yMax - point.y, tooltipWidth, tooltipHeight); Box(tooltip, box.X, box.Y, box.Width, box.Height); }
    }
    private string CaptureActionLabel()
    {
        switch (hotkeyCaptureTarget)
        {
            case HotkeyCaptureTarget.Studio: return "studio shortcut";
            case HotkeyCaptureTarget.HideAllUi: return "Hide All UI shortcut";
            case HotkeyCaptureTarget.RollLeft: return "roll left shortcut";
            case HotkeyCaptureTarget.RollRight: return "roll right shortcut";
            case HotkeyCaptureTarget.Level: return "Level camera shortcut";
            default: return "Save recent clip shortcut";
        }
    }
    private void UpdateKeyCapturePanel()
    {
        bool show = capturingHotkey && IsVisible; keyCapturePanel.gameObject.SetActive(show); if (!show) return;
        keyCaptureTitle.text = "SET " + CaptureActionLabel().ToUpperInvariant();
        keyCaptureText.text = hotkeyCaptureReason.Length > 0 ? hotkeyCaptureReason + "\nPress another key. Esc cancels." : "Press one keyboard key. Esc cancels.\nStudio and Hide All UI use separate shortcuts.";
        float width = Mathf.Min(600, root.rect.width - 16);
        float height = Mathf.Clamp(keyCaptureText.GetPreferredValues(keyCaptureText.text, width - 28, 0).y + 62, 128, 230);
        var box = OverlayPlacement.AboveBar(root.rect.width, root.rect.height, width, height, suiteLayout.DockHeight);
        Box(keyCapturePanel, box.X, box.Y, box.Width, box.Height);
        Box(keyCaptureTitle.rectTransform, 14, 8, box.Width - 28, 30); Box(keyCaptureText.rectTransform, 14, 42, box.Width - 28, box.Height - 50);
        keyCapturePanel.SetAsLastSibling();
    }
    private void Notice(string value) { notice = value; noticeUntil = Time.unscaledTime + 8; }
    private async void ExportLogs()
    {
        if (ReplayDiagnostics.IsExporting) { Notice("Logs are already being collected."); return; }
        Notice("Collecting logs… You can keep playing.");
        try
        {
            await ReplayDiagnostics.ExportAsync();
            if (this != null) Notice("Logs saved. Help → Support → Open logs folder. Send the ZIP with your bug report.");
        }
        catch (Exception error)
        {
            if (this != null) Notice("Could not export logs: " + error.Message);
            Plugin.logger.LogError(error);
        }
        finally { if (this != null) UpdateLogExportControls(); }
    }
    private void UpdateLogExportControls()
    {
        bool exporting = ReplayDiagnostics.IsExporting;
        if (helpExportLogsButton != null) helpExportLogsButton.interactable = !exporting;
        bool saved = !string.IsNullOrEmpty(ReplayDiagnostics.LastExportPath);
        if (openLogsFolderButton != null) openLogsFolderButton.interactable = saved;
        if (copyLogsPathButton != null) copyLogsPathButton.interactable = saved;
        if (logExportStatus != null && (exportStatusDirty || shownExportBusy != exporting || shownExportPath != ReplayDiagnostics.LastExportPath || shownExportNotice != ReplayDiagnostics.LastExportNotice))
        {
            shownExportBusy = exporting; shownExportPath = ReplayDiagnostics.LastExportPath; shownExportNotice = ReplayDiagnostics.LastExportNotice; exportStatusDirty = false;
            logExportStatus.text = exporting ? "Collecting recent logs… Recording and playback continue." :
                shownExportNotice?.StartsWith("Log export failed:", StringComparison.Ordinal) == true ? shownExportNotice + (saved ? "\nPrevious ZIP: " + Path.GetFileName(shownExportPath) : "") :
                saved ? "Saved " + Path.GetFileName(shownExportPath) + "\n" + shownExportNotice :
                !string.IsNullOrEmpty(shownExportNotice) ? shownExportNotice :
                "Export once after the problem happens. Send the ZIP and describe what you pressed, what went wrong and the replay time.";
        }
    }
    private void OpenLogsFolder()
    {
        try
        {
            string directory = Path.GetDirectoryName(ReplayDiagnostics.LastExportPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) { Notice("Export logs first to create a support ZIP."); return; }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = directory, UseShellExecute = true, Verb = "open" });
        }
        catch (Exception error) { Notice("Could not open logs folder: " + error.Message); }
    }
    private void CopyLogsPath()
    {
        if (string.IsNullOrEmpty(ReplayDiagnostics.LastExportPath)) { Notice("Export logs first."); return; }
        GUIUtility.systemCopyBuffer = ReplayDiagnostics.LastExportPath; Notice("Support ZIP path copied.");
    }
    private async void Run(Func<Task> action, bool cover = false, string message = null)
    {
        if (operationBusy || Manager?.IsTransitioning == true || RollingRecordingController.Instance?.Busy == true)
        { Notice("Please wait for the current recording or replay operation."); return; }
        operationBusy = true; loadingReplay = cover; operationStatus = message ?? "Preparing replay…";
        try { await action(); } catch (Exception ex) { Notice(ex.Message); Plugin.logger.LogError(ex); } finally { operationBusy = false; loadingReplay = false; }
    }
    private void RememberMenuPosition()
    {
        if (menuScroll != null && !string.IsNullOrEmpty(currentScrollContext))
            menuOffsets[currentScrollContext] = Mathf.Max(0, menuScroll.content.anchoredPosition.y);
        menuScroll = null; currentScrollContext = null;
    }
    private void RestoreMenuPosition()
    {
        if (menuScroll == null || string.IsNullOrEmpty(currentScrollContext)) return;
        if (menuOffsets.TryGetValue(currentScrollContext, out float offset))
        {
            float maximum = Mathf.Max(0, menuScroll.content.sizeDelta.y - menuViewportHeight);
            menuScroll.content.anchoredPosition = new Vector2(0, Mathf.Min(offset, maximum));
        }
        menuScroll.StopMovement();
    }
    private void RefreshMenuSelection()
    {
        if (navigationButtons != null) for (int n = 0; n < navigationButtons.Length; n++) SelectButton(navigationButtons[n], currentMenu == WorkspaceMenus[n]);
        SelectButton(settingsButton, currentMenu == "Settings");
    }
    private void ClosePopout()
    {
        RememberMenuPosition(); currentMenu = ""; CancelHotkeyCapture(); recordStatus = null; railStatus = null; railDirections = railModes = null; railStart = railStop = railMatchSpeed = null;
        ClearCameraAndKeyControls(); ClearLogMenuControls(); ClearStudioLightControls(); ClearReplayRenameControls(); lensControls.Clear(); recordingControls.Clear(); eventControls.Clear(); recoveryControls.Clear();
        ClearSelection(); HideTooltip(null); if (popout != null) { popout.gameObject.SetActive(false); Destroy(popout.gameObject); }
        popout = null; RefreshMenuSelection();
    }
    private void ClearCameraAndKeyControls()
    {
        helpHideUiButton = helpHideUiKeyButton = saveRecentKeyButton = helpStudioKeyButton = helpRollLeftKey = helpRollRightKey = helpLevelKey = null;
        studioKeyButton = hideUiKeyButton = snapEventsButton = null;
        cameraRollLeft = cameraRollRight = cameraLevel = autoFrameSubjectButton = null; pathStartTake = pathCancelTake = pathPlay = pathAttachMode = pathSmoothMode = pathStopMode = null; pathSmoothnessSlider = null; pathSmoothnessLabel = null; pathFovMode = null;
        pathStatus = null; pathEditControls.Clear(); helpCaptureInstructions = helpShortcutInstructions = null; cameraFov = null; cameraFovLabel = null; motionSubjectText = null;
    }
    private void ClearLogMenuControls() { helpExportLogsButton = openLogsFolderButton = copyLogsPathButton = null; logExportStatus = null; exportStatusDirty = true; }
    private void OpenMenu(string name)
    { if (currentMenu == name && popout != null) { ClosePopout(); return; } CancelHotkeyCapture(); currentMenu = name; RebuildMenu(); }
    private void RebuildMenuAtTop()
    {
        // Selecting a file reveals its required Open/Recover action above the list.
        if (menuScroll != null && currentScrollContext == MenuTaskKey) menuScroll.content.anchoredPosition = Vector2.zero;
        menuOffsets[MenuTaskKey] = 0; RebuildMenu();
    }
    private void RebuildMenu()
    {
        if (currentMenu == "") return; RememberMenuPosition(); ClearSelection(); HideTooltip(null); recordStatus = null; railStatus = null; railDirections = railModes = null; railStart = railStop = railMatchSpeed = null;
        ClearCameraAndKeyControls(); ClearLogMenuControls(); ClearStudioLightControls(); ClearReplayRenameControls(); lensControls.Clear(); recordingControls.Clear(); eventControls.Clear(); recoveryControls.Clear();
        if (popout != null) { popout.gameObject.SetActive(false); Destroy(popout.gameObject); }
        popout = Rect(currentMenu + " popout", root); Place(popout, suiteLayout.Popout); Fill(popout, SuiteColourRole.Background); Border(popout);
        switch (currentMenu) { case "Library": LibraryMenu(); break; case "Record": RecordMenu(); break; case "Camera": CameraMenu(); break; case "Events": EventsMenu(); break; case "Scene": SceneMenu(); break; case "Lights": LightsMenu(); break; case "Settings": SettingsMenu(); break; default: HelpMenu(); break; }
        RestoreMenuPosition(); RefreshMenuSelection();
        popout.gameObject.SetActive(IsVisible && Manager.Playback?.IsSeeking != true && !loadingReplay);
        tooltip.SetAsLastSibling();
    }
    private void ReadHotkey()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { CancelHotkeyCapture(); Notice("Hotkey unchanged."); RefreshBindingLabels(); return; }
        foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
        {
            if (key == KeyCode.None || (int)key >= (int)KeyCode.Mouse0 || !Input.GetKeyDown(key)) continue;
            string reason; bool assigned;
            if (hotkeyCaptureTarget == HotkeyCaptureTarget.HideAllUi)
            { if (Director == null) return; assigned = Director.AssignCleanCaptureHotkey(key, out reason); }
            else if (hotkeyCaptureTarget == HotkeyCaptureTarget.Studio)
                assigned = ConfigManager.AssignStudioHotkey(key, out reason);
            else if (hotkeyCaptureTarget == HotkeyCaptureTarget.RollLeft || hotkeyCaptureTarget == HotkeyCaptureTarget.RollRight || hotkeyCaptureTarget == HotkeyCaptureTarget.Level)
                assigned = ConfigManager.AssignCameraHotkey(hotkeyCaptureTarget == HotkeyCaptureTarget.RollLeft ? ConfigManager.CameraRollLeft : hotkeyCaptureTarget == HotkeyCaptureTarget.RollRight ? ConfigManager.CameraRollRight : ConfigManager.CameraLevel, key, out reason);
            else
            { var rolling = RollingRecordingController.Instance; if (rolling == null) return; assigned = rolling.AssignSaveHotkey(key, out reason); }
            if (!assigned) { hotkeyCaptureReason = reason; Notice(reason); continue; }
            CancelHotkeyCapture(); Notice(reason); RefreshBindingLabels(); return;
        }
    }
    private void BeginHotkeyCapture(HotkeyCaptureTarget target)
    {
        ClearSelection(); capturingHotkey = true; hotkeyCaptureTarget = target; hotkeyCaptureReason = ""; HideTooltip(null);
        Notice("Assign " + CaptureActionLabel() + ". Press one key; Esc cancels."); RefreshBindingLabels(); UpdateKeyCapturePanel();
    }
    private void CancelHotkeyCapture()
    { if (capturingHotkey) inputGuardUntilFrame = Time.frameCount + 1; capturingHotkey = false; hotkeyCaptureTarget = HotkeyCaptureTarget.None; hotkeyCaptureReason = ""; if (keyCapturePanel != null) keyCapturePanel.gameObject.SetActive(false); }
    private void RefreshBindingLabels()
    {
        string cleanLabel = "Hide UI · " + CleanKeyLabel;
        string cleanKeyLabel = capturingHotkey && hotkeyCaptureTarget == HotkeyCaptureTarget.HideAllUi ? "PRESS A KEY · Esc cancels" : "Hide UI key: " + CleanKeyLabel;
        foreach (var button in new[] { hideUiButton, helpHideUiButton }) if (button != null)
            { button.GetComponentInChildren<TMP_Text>().text = cleanLabel; button.GetComponent<SuiteTooltip>().Description = HideUiDescription; }
        foreach (var button in new[] { hideUiKeyButton, helpHideUiKeyButton }) if (button != null) button.GetComponentInChildren<TMP_Text>().text = cleanKeyLabel;
        foreach (var button in new[] { studioKeyButton, helpStudioKeyButton }) if (button != null) button.GetComponentInChildren<TMP_Text>().text = capturingHotkey && hotkeyCaptureTarget == HotkeyCaptureTarget.Studio ? "PRESS A KEY · Esc cancels" : "Studio key: " + StudioKeyLabel;
        if (cameraRollLeft != null) cameraRollLeft.GetComponentInChildren<TMP_Text>().text = "Roll left · " + RollLeftKeyLabel;
        if (cameraRollRight != null) cameraRollRight.GetComponentInChildren<TMP_Text>().text = "Roll right · " + RollRightKeyLabel;
        if (cameraLevel != null) cameraLevel.GetComponentInChildren<TMP_Text>().text = "Level · " + LevelKeyLabel;
        if (helpRollLeftKey != null) helpRollLeftKey.GetComponentInChildren<TMP_Text>().text = "Roll left key: " + RollLeftKeyLabel;
        if (helpRollRightKey != null) helpRollRightKey.GetComponentInChildren<TMP_Text>().text = "Roll right key: " + RollRightKeyLabel;
        if (helpLevelKey != null) helpLevelKey.GetComponentInChildren<TMP_Text>().text = "Level camera key: " + LevelKeyLabel;
        if (saveRecentKeyButton != null) saveRecentKeyButton.GetComponentInChildren<TMP_Text>().text = capturingHotkey && hotkeyCaptureTarget == HotkeyCaptureTarget.SaveRecent ? "PRESS A NEW SAVE KEY · Esc cancels" : "Change save hotkey: " + (RollingRecordingController.Instance?.SaveHotkeyLabel ?? "Save key");
        if (helpCaptureInstructions != null) helpCaptureInstructions.text = "Choose 0.1× to 1.0× playback speed. Start camera take jumps to the first point and hides UI, then waits five seconds before playback. Start OBS yourself during that delay. " + StudioKeyLabel + " cancels the take and restores the studio. " + CleanKeyLabel + " separately hides or restores all UI.";
        if (helpShortcutInstructions != null) helpShortcutInstructions.text = StudioKeyLabel + " · Show or hide studio\n" + CleanKeyLabel + " · Hide all UI / restore all UI\n" + RollLeftKeyLabel + " / " + RollRightKeyLabel + " · Hold to roll camera\n" + LevelKeyLabel + " · Level camera\nSpace · Play / pause\nComma · Slow motion; Period · Next frame\nSave recent clip · assign in Record";
    }
}




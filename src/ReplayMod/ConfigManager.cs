using System;
using BepInEx.Configuration;
using UnityEngine;
using ReplayMod.UI;

namespace ReplayMod;

public static class ConfigManager
{
	public static ConfigEntry<KeyboardShortcut> ToggleMenu { get; set; }

	public static ConfigEntry<KeyboardShortcut> ToggleRecord { get; set; }

	public static ConfigEntry<KeyboardShortcut> TogglePause { get; set; }

	public static ConfigEntry<KeyboardShortcut> CreateCameraWaypoint { get; set; }

	public static ConfigEntry<KeyboardShortcut> DeleteLastCameraWaypoint { get; set; }

	public static ConfigEntry<KeyboardShortcut> MirrorControlPoint { get; set; }

	public static ConfigEntry<KeyboardShortcut> DeselectWaypoint { get; set; }

	public static ConfigEntry<bool> EnableDebugLogs { get; set; }
	public static ConfigEntry<bool> EnableBackgroundControl { get; private set; }

	public static ConfigEntry<bool> ContinuousRecording { get; private set; }

	public static ConfigEntry<int> RecentClipSeconds { get; private set; }

	public static ConfigEntry<KeyboardShortcut> SaveRecentClip { get; private set; }
	public static ConfigEntry<KeyboardShortcut> ToggleCleanCapture { get; private set; }
	public static ConfigEntry<KeyboardShortcut> CameraRollLeft { get; private set; }
	public static ConfigEntry<KeyboardShortcut> CameraRollRight { get; private set; }
	public static ConfigEntry<KeyboardShortcut> CameraLevel { get; private set; }
	public static ConfigEntry<string> UiThemePreset { get; private set; }
	public static ConfigEntry<string> UiAccentColour { get; private set; }
	public static ConfigEntry<string> UiPanelColour { get; private set; }
	public static ConfigEntry<string> UiButtonColour { get; private set; }
	public static ConfigEntry<string> UiTextColour { get; private set; }
	public static ConfigEntry<float> UiPanelOpacity { get; private set; }
	private static ConfigFile appearanceConfig;
	public static string StudioHotkeyLabel => ToggleMenu?.Value.ToString() ?? "F5";
	internal static SuiteThemeSettings ReadAppearance()=>SuiteThemeSettings.Create(
		UiThemePreset?.Value,UiAccentColour?.Value,UiPanelColour?.Value,UiButtonColour?.Value,UiTextColour?.Value,UiPanelOpacity?.Value??SuiteThemeSettings.NativeOpacity);
	internal static void SaveAppearance(SuiteThemeSettings settings)
	{
		if(appearanceConfig==null||settings==null)return;
		settings=settings.Copy();bool automaticSave=appearanceConfig.SaveOnConfigSet;
		try
		{
			// A slider previews immediately but commits one bounded six-key transaction after a pause.
			appearanceConfig.SaveOnConfigSet=false;
			UiThemePreset.Value=settings.Preset;UiAccentColour.Value=settings.Accent;UiPanelColour.Value=settings.Panel;
			UiButtonColour.Value=settings.Button;UiTextColour.Value=settings.Text;UiPanelOpacity.Value=settings.Opacity;
			appearanceConfig.Save();
		}
		finally {appearanceConfig.SaveOnConfigSet=automaticSave;}
	}

	public static bool CanAssignReplayHotkey(KeyCode key, ConfigEntry<KeyboardShortcut> except, out string reason)
	{
		if (key == KeyCode.None || key == KeyCode.Escape || key == KeyCode.Return || key == KeyCode.KeypadEnter ||
			key == KeyCode.Comma || key == KeyCode.Period || (int)key >= (int)KeyCode.Mouse0 ||
			key == KeyCode.LeftShift || key == KeyCode.RightShift || key == KeyCode.LeftControl || key == KeyCode.RightControl ||
			key == KeyCode.LeftAlt || key == KeyCode.RightAlt || key == KeyCode.LeftCommand || key == KeyCode.RightCommand ||
			key == KeyCode.LeftWindows || key == KeyCode.RightWindows || key == KeyCode.AltGr)
		{ reason = "That key is reserved. Choose another keyboard key."; return false; }
		var shortcuts = new[] { ToggleMenu, ToggleRecord, TogglePause, CreateCameraWaypoint, DeleteLastCameraWaypoint,
			MirrorControlPoint, DeselectWaypoint, SaveRecentClip, ToggleCleanCapture, CameraRollLeft, CameraRollRight, CameraLevel };
		foreach (var shortcut in shortcuts)
			if (shortcut != null && !ReferenceEquals(shortcut, except) && shortcut.Value.MainKey == key)
			{ reason = key + " already controls another replay action."; return false; }
		reason = ""; return true;
	}
	public static bool AssignStudioHotkey(KeyCode key, out string reason)
	{
		if (ToggleMenu == null) { reason = "Studio settings are not ready yet."; return false; }
		if (!CanAssignReplayHotkey(key, ToggleMenu, out reason)) return false;
		ToggleMenu.Value = new KeyboardShortcut(key);
		reason = "Studio shortcut: " + StudioHotkeyLabel + ". Hide All UI uses its own key.";
		return true;
	}
	public static bool AssignCameraHotkey(ConfigEntry<KeyboardShortcut> setting, KeyCode key, out string reason)
	{
		if (setting == null || (setting != CameraRollLeft && setting != CameraRollRight && setting != CameraLevel))
		{ reason = "Camera settings are not ready yet."; return false; }
		if (!CanAssignReplayHotkey(key, setting, out reason)) return false;
		setting.Value = new KeyboardShortcut(key);
		reason = "Camera shortcut saved: " + key + "."; return true;
	}

	public static void Configure(ConfigFile config)
	{
		appearanceConfig=config;
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		ToggleMenu = config.Bind("Hotkeys", "Toggle menu", new KeyboardShortcut(KeyCode.F5),
			"Show or hide Kelly's Replay Mod. Reassign this in Help / Shortcuts. This is separate from Hide All UI.");
		ToggleRecord = config.Bind<KeyboardShortcut>("Hotkeys", "Toggle record", new KeyboardShortcut(KeyCode.I, Array.Empty<KeyCode>()), (ConfigDescription)null);
		TogglePause = config.Bind<KeyboardShortcut>("Hotkeys", "Toggle pause", new KeyboardShortcut(KeyCode.Space, Array.Empty<KeyCode>()), (ConfigDescription)null);
		CreateCameraWaypoint = config.Bind<KeyboardShortcut>("Hotkeys", "Add camera waypoint", new KeyboardShortcut(KeyCode.F6, Array.Empty<KeyCode>()), (ConfigDescription)null);
		DeleteLastCameraWaypoint = config.Bind<KeyboardShortcut>("Hotkeys", "Delete last camera waypoint", new KeyboardShortcut(KeyCode.F7, Array.Empty<KeyCode>()), (ConfigDescription)null);
		MirrorControlPoint = config.Bind<KeyboardShortcut>("Hotkeys", "Mirror control point", new KeyboardShortcut(KeyCode.L, Array.Empty<KeyCode>()), (ConfigDescription)null);
		DeselectWaypoint = config.Bind<KeyboardShortcut>("Hotkeys", "Deselect point", new KeyboardShortcut(KeyCode.Backspace, Array.Empty<KeyCode>()), (ConfigDescription)null);
		EnableDebugLogs = config.Bind<bool>("Debug", "Enable logs", false, (ConfigDescription)null);
		EnableBackgroundControl = config.Bind("Debug", "Enable background test control", false,
			"Developer testing only. Enables a session-scoped local command/status file channel under Replays/Diagnostics/BackgroundControl and keeps this client updating in the background. Disabled by default.");
		ContinuousRecording = config.Bind("Recording", "Keep recent gameplay", false,
			"Automatically keep recent gameplay in memory during missions. Save a clip with the recent-clip hotkey. No replay file is created until you save.");
		RecentClipSeconds = config.Bind("Recording", "Recent clip seconds", 120,
			new ConfigDescription("How much recent gameplay to keep. Heavy missions may retain less because the event buffer is limited to 128 MiB.", new AcceptableValueRange<int>(30, 300)));
		SaveRecentClip = config.Bind("Hotkeys", "Save recent gameplay", new KeyboardShortcut(KeyCode.F9),
			"Save the recent gameplay buffer as a separate replay while recording continues. Can also be reassigned in the recording menu.");
		ToggleCleanCapture = config.Bind("Hotkeys", "Toggle clean capture", new KeyboardShortcut(KeyCode.F8),
			"Hide or restore all game and replay UI, cursor and camera editing guides for capture. Works during gameplay and replay playback. Reassign this in the studio. Your configured Toggle menu shortcut also restores the studio.");
		CameraRollLeft = config.Bind("Hotkeys", "Camera roll left", new KeyboardShortcut(KeyCode.Z),
			"Hold to roll the free camera left. Works during paused, slowed and hidden replay playback. Reassign in Help.");
		CameraRollRight = config.Bind("Hotkeys", "Camera roll right", new KeyboardShortcut(KeyCode.C),
			"Hold to roll the free camera right. Works during paused, slowed and hidden replay playback. Reassign in Help.");
		CameraLevel = config.Bind("Hotkeys", "Level camera", new KeyboardShortcut(KeyCode.X),
			"Level the replay camera: remove pitch and roll while keeping position, heading and FOV. Reassign in Help.");
		UiThemePreset = config.Bind("Appearance", "Theme", "Native",
			new ConfigDescription("Kelly's Replay Mod colours only. Change live in Settings / Appearance. Custom uses the four RGB hex colours below.",new AcceptableValueList<string>("Native","Amber","Blue","Neutral","Custom")));
		UiAccentColour = config.Bind("Appearance", "Custom accent", "#58E187","Custom highlight and slider-fill colour in #RRGGBB. Invalid values fall back to native green.");
		UiPanelColour = config.Bind("Appearance", "Custom panel", "#050A05","Custom panel colour in #RRGGBB. Invalid values fall back to the native dark green.");
		UiButtonColour = config.Bind("Appearance", "Custom button", "#3E463C","Custom button/input colour in #RRGGBB. Invalid values fall back to native olive.");
		UiTextColour = config.Bind("Appearance", "Custom text", "#E0E0E0","Preferred custom text colour in #RRGGBB. Text is adjusted automatically when needed to stay readable against panels and buttons.");
		UiPanelOpacity = config.Bind("Appearance", "Panel opacity", SuiteThemeSettings.NativeOpacity,
			new ConfigDescription("Preferred replay panel opacity. Actual opacity may increase to keep text readable over the game. Does not change the black seek cover or the game's own UI.",new AcceptableValueRange<float>(.65f,1)));
	}
}

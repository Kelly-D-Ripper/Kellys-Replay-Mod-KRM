using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.Camera;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Misc;
using UnityEngine;

namespace ReplayMod;

[BepInPlugin("com.chiester3105.replayMod", "Kelly's Replay Mod", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
	public const int AppId = 12549;

	private const int maxActionsPerFrame = 10;

	private static ConcurrentQueue<Action> _executionQueue = new ConcurrentQueue<Action>();
	private GameObject ownedRuntime;

	public static ManualLogSource logger { get; private set; }

	public static Plugin Instance { get; private set; }

	public static ReplayManager ReplayManager { get; private set; }

	public static byte Major { get; private set; }

	public static byte Minor { get; private set; }

	public static byte Patch { get; private set; }

	public void Awake()
	{
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Expected O, but got Unknown
		try
		{
			logger = Logger;
			Instance = this;
			if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
			{ Logger.LogInfo("ReplayMod trailer tools are client only; headless host skipped."); enabled = false; return; }
			if(ReplayRuntimeHost.Instance!=null){Logger.LogWarning("ReplayMod runtime already exists; duplicate initialization skipped.");enabled=false;return;}
			ConfigManager.Configure(Config);
			ReplayMod.Diagnostics.ReplayStartup.Mark("Plugin configured");
			var runtime = new GameObject("ReplayMod Runtime");
			ownedRuntime=runtime;
			runtime.hideFlags = HideFlags.HideAndDontSave;
			UnityEngine.Object.DontDestroyOnLoad(runtime);
			runtime.AddComponent<ReplayRuntimeHost>();
			try
			{
				ReplayMod.Diagnostics.ReplayDiagnostics.Initialize();
				runtime.AddComponent<ReplayMod.Diagnostics.ReplayDiagnosticsSampler>();
			}
			catch(Exception diagnosticsError){Logger.LogWarning("Replay diagnostics initialization failed: "+diagnosticsError.Message);}
			runtime.AddComponent<CameraManager>();
			ReplayManager = runtime.AddComponent<ReplayManager>();
			runtime.AddComponent<ReplayMod.Director.ReplayDirector>();
			runtime.AddComponent<ReplayMod.Director.CleanCaptureImguiGuard>();
			runtime.AddComponent<RollingRecordingController>();
			runtime.AddComponent<ReplayMod.Diagnostics.ReplayBackgroundControl>();
			logger = Logger;
			logger.LogInfo((object)"Plugin Awake started");
			Harmony val = new Harmony("ReplayMod");
			val.PatchAll();
			logger.LogInfo((object)"Harmony patches applied");
			LoadVersionInfo();
			logger.LogInfo((object)"Kelly's Replay Mod 1.0 loaded");
			ReplayEventFactory.RegisterTypes();
			ReplayMod.Diagnostics.ReplayStartup.Mark("Awake complete; waiting for runtime Start");
		}
		catch (Exception arg)
		{
			if(ownedRuntime!=null)UnityEngine.Object.Destroy(ownedRuntime);
			UnityEngine.Debug.LogError($"Plugin failed to load: {arg}");
		}
	}

	public void Start()
	{
		ReplayMod.Diagnostics.ReplayStartup.Mark("Plugin Start");
	}

	private void OnDestroy()
	{
		if(ReplayRuntimeHost.Instance==null)ReplayMod.Diagnostics.ReplayDiagnostics.Shutdown();
		else ReplayMod.Diagnostics.ReplayStartup.Mark("Plugin host destroyed; owned runtime retained");
	}

	public async Task EncyclopediaLookup()
	{
		await Task.Delay(11000);
		await UniTask.SwitchToMainThread();
		try
		{
			foreach (KeyValuePair<string, UnitDefinition> kvp in Encyclopedia.Lookup)
			{
				if (kvp.Key != kvp.Value.jsonKey)
				{
					logger.LogError((object)("key " + kvp.Key + ", jsonKey " + kvp.Value.jsonKey));
				}
			}
			foreach (KeyValuePair<string, WeaponMount> kvp2 in Encyclopedia.WeaponLookup)
			{
				DebugLog(kvp2.Key + ": " + kvp2.Value.jsonKey);
			}
		}
		catch (Exception ex)
		{
			Exception e = ex;
			logger.LogError((object)e);
		}
	}

	internal static void DrainActions()
	{
		int num = 0;
		Action result;
		while (num++ < 10 && _executionQueue.TryDequeue(out result))
		{
			result();
		}
	}

	public static void SafeLog(string message)
	{
		_executionQueue.Enqueue(delegate
		{
			logger.LogInfo((object)message);
		});
	}

	public static void SafeLogError(string message)
	{
		_executionQueue.Enqueue(delegate
		{
			logger.LogError((object)message);
		});
	}

	public static void SafeLogWarning(string message)
	{
		_executionQueue.Enqueue(delegate
		{
			logger.LogWarning((object)message);
		});
	}

	private void LoadVersionInfo()
	{
		string[] array = "1.0.0".Split(new char[1] { '.' });
		Major = byte.Parse(array[0]);
		Minor = byte.Parse(array[1]);
		Patch = byte.Parse(array[2]);
	}

	public static void DebugLog(string message)
	{
		if (ConfigManager.EnableDebugLogs.Value)
		{
			logger.LogInfo((object)("[Debug] " + message));
		}
	}
}




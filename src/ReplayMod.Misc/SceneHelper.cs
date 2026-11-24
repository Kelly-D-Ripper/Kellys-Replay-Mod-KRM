using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReplayMod.Misc;

public static class SceneHelper
{
	private static readonly ReplayOperationBarrier transitions = new ReplayOperationBarrier();
	public static bool IsTransitionPending => transitions.IsBusy;
	public static string TransitionPhase { get; private set; } = "";
	private static readonly TimeSpan TransitionBudget = TimeSpan.FromSeconds(120);

	// All replay world changes use this barrier, including recovery after a failed
	// seek. Native StopAsync can return early when another stop is still unloading.
	public static async UniTask StopReplayConnectionAsync(CancellationToken cancellation=default)
	{
		await UniTask.SwitchToMainThread();
		await transitions.RunAsync(StopCoreAsync,TransitionBudget,cancellation);
	}
	private static async Task StopCoreAsync()
	{
		await UniTask.SwitchToMainThread();
		var manager=NetworkManagerNuclearOption.i;
		if(manager==null)return;
		TransitionPhase="Waiting for previous unload";
		if(manager.stopping)
			await WaitForAsync(()=>!manager.stopping&&!NetworkManagerNuclearOption.IsLoadingScene,"previous scene unload");
		if(manager.Server.Active||manager.Client.Active||MissionManager.IsRunning||!IsMenuScene())
		{
			TransitionPhase="Unloading replay world";
			await manager.StopAsync(setDisconnectReason:true);
		}
		await WaitForAsync(()=>!manager.stopping&&!NetworkManagerNuclearOption.IsLoadingScene&&
			!manager.Server.Active&&!manager.Client.Active&&!MissionManager.IsRunning&&IsMenuScene(),"scene unload");
		// Native OnDestroy may clear CurrentMission and singleton slots. Never set
		// the next mission until destruction of the old scene has settled.
		await UniTask.NextFrame();await UniTask.NextFrame();
		if(manager.stopping||NetworkManagerNuclearOption.IsLoadingScene||manager.Server.Active||manager.Client.Active)
			throw new IOException("The previous replay world did not finish unloading");
		TransitionPhase="";
	}
	private static bool IsMenuScene()
	{
		var scene=SceneManager.GetActiveScene();
		return scene.path==MapLoader.MainMenu||scene.name==MapLoader.MainMenu||
			scene.path==MapLoader.MultiplayerMenu||scene.name==MapLoader.MultiplayerMenu;
	}
	private static async UniTask WaitForAsync(Func<bool> ready,string stage)
	{
		float start=Time.realtimeSinceStartup;
		while(!ready())
		{
			if(Time.realtimeSinceStartup-start>90)throw new TimeoutException("Timed out waiting for replay "+stage);
			await UniTask.NextFrame();
		}
	}
	private static bool CurrentSceneObject(Component component)
	{
		if(component==null)return false;
		var scene=component.gameObject.scene;
		return scene.IsValid()&&scene.isLoaded&&scene.handle==SceneManager.GetActiveScene().handle;
	}
	public static Mission CreateEmptyMission(MapKey mapKey, string missionName)
	{
		// The Free Flight template contains map-specific airbases and scripted
		// objectives. Keeping those while replacing only its MapKey can abort an
		// editor/custom terrain during native StartMission. A native new mission
		// supplies just its hidden mission-start objective and fresh load state.
		Mission mission = new Mission(missionName);
		mission.MapKey = mapKey;
		mission.missionSettings.playerMode = PlayerMode.Singleplayer;
		mission.missionSettings.allowEventContent = true;
		// Playback owns recorded wreck lifetimes. The native zero defaults of a
		// newly authored mission would immediately expire the wrecks otherwise.
		mission.missionSettings.wrecksMaxNumber = int.MaxValue;
		mission.missionSettings.wrecksDecayTime = float.MaxValue;
		var factions=GameAssets.i?.defaultFactions;
		if(factions==null||factions.Length==0)throw new IOException("The game's default replay factions are unavailable");
		foreach(var faction in factions)
			if(faction!=null)
			{
				mission.EnsureFactionExists(faction,out var entry);
				entry.preventJoin=true;entry.preventDonation=true;entry.AIAircraftLimit=0;
			}
		return mission;
	}

	public static MapKey GetRecordingMapKey()
	{
		var loader=SceneSingleton<MapSettingsManager>.i?.MapLoader;
		if(loader==null)throw new InvalidOperationException("Start a loaded mission before recording");
		var loaded=loader.CurrentMap;
		var mission=MissionManager.CurrentMission?.MapKey??default;
		if(!ReplayMapIdentity.TrySelectRecordingMap((byte)loaded.Type,loaded.Path,(byte)mission.Type,mission.Path,
			(type,path)=>loader.CanLoad(new MapKey((MapKey.KeyType)type,path)),out var selectedType,out var selectedPath))
			throw new InvalidOperationException("The active mission has no available terrain key. Wait for it to finish loading before recording.");
		return new MapKey((MapKey.KeyType)selectedType,selectedPath);
	}
	internal static void ValidateReplayMap(MapKey key)
	{
		var loader=NetworkManagerNuclearOption.i?.mapLoader;
		if(loader==null)throw new IOException("Game map loader is unavailable");
		if(!ReplayMapIdentity.IsExplicitTerrain((byte)key.Type,key.Path)||!loader.CanLoad(key))
			throw new IOException(ReplayMapIdentity.LoadFailure("Map is unavailable",(byte)key.Type,key.Path,null));
	}
	private static IOException MapLoadFailure(string stage,MapKey key)
	{
		var reason=GameManager.disconnectInfo;
		return new IOException(ReplayMapIdentity.LoadFailure(stage,(byte)key.Type,key.Path,
			reason!=null&&reason.ShowReason?reason.Message:null));
	}

	public static async UniTask<bool> LoadSceneAsync(MapKey key, string missionName = "__ReplayEmpty",CancellationToken cancellation=default)
	{
		await UniTask.SwitchToMainThread();
		// Validate the stored terrain before tearing down any existing scene.
		ValidateReplayMap(key);
		await transitions.RunAsync(async ()=>
		{
			await StopCoreAsync();
			var manager=NetworkManagerNuclearOption.i;
			if(manager==null)throw new IOException("Game network manager is unavailable");
			Mission mission=CreateEmptyMission(key,missionName);
			MissionManager.SetMission(mission,checkIfSame:false);
			GameManager.ClearDisconnectReason();
			TransitionPhase="Loading replay map";
			await manager.StartHostAsync(new HostOptions(SocketType.Offline,GameState.SinglePlayer,key));
			// Native StartHostAsync can log a failed map load and complete normally.
			if(!manager.Server.Active||!manager.Client.Active)
				throw MapLoadFailure("Map failed to start",key);
			TransitionPhase="Waiting for replay mission";
			await WaitForAsync(()=>
			{
				if(!manager.Server.Active||!manager.Client.Active||!ReferenceEquals(MissionManager.CurrentMission,mission))
					throw MapLoadFailure("Mission was interrupted while loading",key);
				var settings=SceneSingleton<MapSettingsManager>.i;
				var level=NetworkSceneSingleton<LevelInfo>.i;
				return !manager.stopping&&!NetworkManagerNuclearOption.IsLoadingScene&&MissionManager.IsRunning&&
					manager.Server.LocalPlayer!=null&&manager.Server.LocalPlayer.SceneIsReady&&
					CurrentSceneObject(NetworkSceneSingleton<Spawner>.i)&&CurrentSceneObject(settings)&&
					CurrentSceneObject(level)&&CurrentSceneObject(NetworkSceneSingleton<MissionManager>.i)&&
					level.LoadedMapSettings!=null&&settings.MapLoader!=null&&key.Equals(settings.MapLoader.CurrentMap);
			},"mission readiness");
			await UniTask.NextFrame();
			if(!ReferenceEquals(MissionManager.CurrentMission,mission)||!MissionManager.IsRunning)
				throw MapLoadFailure("Mission was cleared during startup",key);
			TransitionPhase="";
			Plugin.SafeLog("Replay world ready: "+missionName);
		},TransitionBudget,cancellation);
		return true;
	}

	public static async UniTask RestartMisison()
	{
		await MissionManager.RestartMission();
	}

	public static async UniTask<bool> LoadMapOnly(MapKey mapKey)
	{
		await UniTask.WaitUntil(() => NetworkManagerNuclearOption.i != null);
		MapLoader mapLoader = NetworkManagerNuclearOption.i.mapLoader;
		MapLoader.LoadResult result = await mapLoader.Load(mapKey);
		if (result == MapLoader.LoadResult.ChangedScene || result == MapLoader.LoadResult.ChangedWorldPrefab || result == MapLoader.LoadResult.AlreadyLoaded)
		{
			await UniTask.WaitUntil(() => NetworkSceneSingleton<Spawner>.i != null && NetworkSceneSingleton<LevelInfo>.i != null);
			return true;
		}
		return false;
	}
}

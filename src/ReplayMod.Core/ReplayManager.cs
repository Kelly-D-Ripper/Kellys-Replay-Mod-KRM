using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Cysharp.Threading.Tasks;
using NuclearOption.Networking;
using NuclearOption.SceneLoading;
using ReplayMod.Camera;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Core;

public class ReplayManager : MonoBehaviour
{
	public const double AircraftUpdateInterval = 0.05;

	public const double GroundVehicleUpdateInterval = 0.4;

	public const double BuildingUpdateInterval = 60.0;

	public const double UpdateInputsInterval = 0.05;

	public const double TurretsUpdateInterval = 0.05;

	public const int PlayerBoundedCapacity = 20000;

	private ModStates _currentState;
	private readonly SemaphoreSlim _transitionGate = new SemaphoreSlim(1, 1);
	private bool _recordCommandRunning;
	private bool _handlingPlaybackFailure;
	private bool _handlingRecordingFailure;
	private object _failedReplayMission;

	private Recorder _recorder;

	private Player _player;

	private MainMenu _mainPanel;

	private VisualWaypoints _waypoints;

	private string _lastFile = string.Empty;

	private string _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Replays");

	public Action onReset;

	public static ReplayManager i { get; private set; }
	public Player Playback => _player;
	public string LastRecordingStopStatus {get;private set;}="";
	public bool IsTransitioning => _transitionGate.CurrentCount == 0;
	// A partially loaded replay world must never become a new live recording.
	public bool IsFailedReplayWorld => _failedReplayMission != null && ReferenceEquals(_failedReplayMission, MissionManager.CurrentMission);
	public bool NextRecordingIsRolling;
	public double RollingSeconds=120;

	public double virtualTime { get; set; } = 0.0;

	public bool UseCaching { get; private set; } = false;

	public bool IgnorePilotsSpawn { get; private set; } = false;

	public double WorldSnapshotsInterval { get; private set; } = 20.0;

	public void Configure()
	{
	}

	private void Awake()
	{
	}

	public void Start()
	{
		try
		{
			if (i != null)
			{
				UnityEngine.Object.Destroy(base.gameObject);
				return;
			}
			i = this;
			UnityEngine.Object.DontDestroyOnLoad(base.gameObject);
			_mainPanel = base.gameObject.AddComponent<MainMenu>();
			_waypoints = base.gameObject.AddComponent<VisualWaypoints>();
			base.gameObject.AddComponent<ReplayAircraftVisualLatePass>();
			base.gameObject.AddComponent<ReplayMod.Director.ReplayCameraPathPreview>();
			onReset = (Action)Delegate.Combine(onReset, new Action(_waypoints.Restore));
			_recorder = base.gameObject.AddComponent<Recorder>();
			_player = base.gameObject.AddComponent<Player>();
			_recorder.enabled = false;
			_player.enabled = false;
			_currentState = ModStates.Idle;
			Plugin.logger.LogInfo((object)"Replay manager initialized");
			ReplayMod.Diagnostics.ReplayStartup.Mark("Replay manager initialized");
		}
		catch (Exception ex)
		{
			Plugin.logger.LogError((object)ex);
		}
	}

	public void Update()
	{
		if(!IsTransitioning&&_currentState==ModStates.Record&&_recorder!=null&&!string.IsNullOrEmpty(_recorder.RecordingFailure))
		{
			if(!_handlingRecordingFailure){_handlingRecordingFailure=true;_ = HandleRecordingFailure(_recorder.RecordingFailure);}
			return;
		}
		if (!IsTransitioning && _currentState == ModStates.Replay && _player != null &&
			!string.IsNullOrEmpty(_player.PlaybackFailure))
		{
			if (!_handlingPlaybackFailure)
			{
				_handlingPlaybackFailure = true;
				_failedReplayMission = MissionManager.CurrentMission;
				_ = HandlePlaybackFailure(_player, _player.PlaybackFailure);
			}
			return;
		}
		HandleInputs();
	}
	private async Task HandleRecordingFailure(string reason)
	{
		try { await SwitchState(ModStates.Idle); SetStatus("Recording stopped: "+reason+". Open the studio ("+ConfigManager.StudioHotkeyLabel+") and export logs before trying again."); }
		finally { _handlingRecordingFailure=false; }
	}
	public async void RecordingSessionEnded(Recorder recorder)
	{
		if(_currentState!=ModStates.Record||!ReferenceEquals(_recorder,recorder))return;
		try
		{
			await SwitchState(ModStates.Idle);
			SetStatus("Mission ended. "+LastRecordingStopStatus);
		}
		catch(Exception error){Plugin.logger.LogError(error);SetStatus("Could not finish recording: "+error.Message+". Check Library → Recovery.");}
	}

	private async Task HandlePlaybackFailure(Player failedPlayer, string reason)
	{
		try
		{
			// StopPlaying waits for any active seek to finish its cleanup. The
			// faulted player freezes its own clock until this offline teardown ends.
			if (_player == failedPlayer && _currentState == ModStates.Replay)
				await SwitchState(ModStates.Idle);
			if (_currentState == ModStates.Idle)
				SetStatus("Replay stopped: " + reason + ". Open Library to try another replay.");
		}
		catch (Exception ex) { Plugin.logger.LogError(ex); SetStatus("Replay cleanup failed: " + ex.Message); }
		finally { _handlingPlaybackFailure = false; }
	}

	public double GetIntervalForUnit(Unit unit)
	{
		if (!(unit is Aircraft))
		{
			if (!(unit is Missile))
			{
				if (!(unit is GroundVehicle))
				{
					if (!(unit is Ship))
					{
						if (unit is Building)
						{
							return 60.0;
						}
						return 0.2;
					}
					return 0.4;
				}
				return 0.4;
			}
			return 0.05;
		}
		return 0.05;
	}

	public async Task SwitchState(ModStates newState)
	{
		await _transitionGate.WaitAsync();
		bool replayConnectionReplaced = false;
		bool ownsReplayClock = _currentState == ModStates.Replay;
		try
		{
			if (newState == _currentState) return;
			if(newState==ModStates.Record&&ReplayRecordingRecovery.IsBusy)
				throw new InvalidOperationException("Wait for recording recovery to finish before starting a recording.");
			var previous = _currentState;
			if (previous == ModStates.Record && _recorder != null)
			{ await _recorder.StopRecording(); LastRecordingStopStatus=_recorder.LastSaveNotice; _recorder.enabled = false; }
			if (previous == ModStates.Replay && _player != null)
			{
				ReplayMod.Director.ReplayDirector.i?.Close();
				await _player.StopPlayingAndDestroy(); _player = null;
				if (CameraManager.i != null) CameraManager.i.ClearPath();
				await ReplayMod.Misc.SceneHelper.StopReplayConnectionAsync();
			}
			_currentState = newState;
			if (newState == ModStates.Record)
			{
				if (_recorder != null) await _recorder.StopAndDestroy();
				_recorder = gameObject.AddComponent<Recorder>();
				_recorder.enabled = true; _lastFile = GetFilePath(); _waypoints.Reset();
				await _recorder.StartRecording(_lastFile, GetMapPrefabName(),NextRecordingIsRolling?RollingSeconds:0); NextRecordingIsRolling=false;virtualTime = 0;
			}
			else if (newState == ModStates.Replay)
			{
				if (!File.Exists(_lastFile)) throw new FileNotFoundException("Replay does not exist", _lastFile);
				if (NetworkManagerNuclearOption.i == null) throw new InvalidOperationException("Game network manager is unavailable");
				if ((NetworkManagerNuclearOption.i.Client != null && NetworkManagerNuclearOption.i.Client.Active) ||
					(NetworkManagerNuclearOption.i.Server != null && NetworkManagerNuclearOption.i.Server.Active))
				{
					if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.ResumeGame();
					await ReplayMod.Misc.SceneHelper.StopReplayConnectionAsync();
				}
				ownsReplayClock = true;
				TimeScaleManager.Scale = 0;
				if (_player != null) await _player.StopPlayingAndDestroy();
				_player = gameObject.AddComponent<Player>();
				// File existence and active live-connection teardown have completed.
				// Connections created from here belong to this offline replay attempt.
				replayConnectionReplaced = true;
				await _player.StartPlaying(_lastFile);
				await ReplayMod.Director.ReplayDirector.i.Open(_lastFile);
				_failedReplayMission = null;
			}
			else
			{
				if (_recorder != null) _recorder.enabled = false;
				if (_player != null) _player.enabled = false;
				// Stopping a recording does not own the live mission's pause or time scale.
				if (previous == ModStates.Replay) TimeScaleManager.Scale = 1;
			}
		}
		catch (Exception ex)
		{
			Plugin.logger.LogError(ex);
			if (newState == ModStates.Replay)
			{
				try { ReplayMod.Director.ReplayDirector.i?.Close(); }
				catch (Exception cleanup) { Plugin.logger.LogError(cleanup); }
				if (replayConnectionReplaced) _failedReplayMission = MissionManager.CurrentMission;
			}
			if (_player != null)
			{
				try { await _player.StopPlayingAndDestroy(); }
				catch (Exception cleanup) { Plugin.logger.LogError(cleanup); }
				_player = null;
			}
			if (newState == ModStates.Replay && replayConnectionReplaced && NetworkManagerNuclearOption.i != null)
			{
				try { await ReplayMod.Misc.SceneHelper.StopReplayConnectionAsync(); }
				catch (Exception cleanup) { Plugin.logger.LogError(cleanup); }
			}
			if (_recorder != null) _recorder.enabled = false;
			_currentState = ModStates.Idle;
			if (ownsReplayClock) TimeScaleManager.Scale = 1;
			if (ReplayMod.Director.ReplayDirector.i != null)
				ReplayMod.Director.ReplayDirector.i.Status = (newState == ModStates.Replay ? "Replay could not be opened: " : "Operation failed: ") + ex.Message;
		}
		finally { _transitionGate.Release(); }
	}

	private string GetMapPrefabName()
	{
		string result = "UnknownMap";
		MapSettingsManager mapSettingsManager = SceneSingleton<MapSettingsManager>.i;
		if ((object)mapSettingsManager != null)
		{
			MapLoader mapLoader = mapSettingsManager.MapLoader;
			if ((object)mapLoader != null)
			{
				_ = mapLoader.CurrentMap;
				if (true)
				{
					result = SceneSingleton<MapSettingsManager>.i.MapLoader.CurrentMap.Path;
					goto IL_0051;
				}
			}
		}
		Plugin.logger.LogWarning((object)"Current map not available, using 'UnknownMap'");
		goto IL_0051;
		IL_0051:
		return result;
	}

	private string GetFilePath()
	{
		string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
		string text = Path.Combine(baseDirectory, "Replays");
		if (!Directory.Exists(text))
		{
			Directory.CreateDirectory(text);
		}
		string text2 = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
		string mapPrefabName = GetMapPrefabName();
			string text3 = mapPrefabName + "_" + text2 + "_" + Guid.NewGuid().ToString("N").Substring(0,8) + ".replay";
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			text3 = text3.Replace(oldChar, '_');
		}
		return Path.Combine(text, text3);
	}

	private void HandleInputs()
	{
		// Opening the tools is independent of playback/scene operation gates.
		// Real text entry and shortcut assignment still own keyboard input.
		if(ConfigManager.ToggleMenu.Value.GetDown() && MainMenu.CanToggleStudio)
		{_mainPanel.Toggle();return;}
		if(ConfigManager.ToggleCleanCapture?.Value.GetDown()==true)return;
		if(MainMenu.BlockHotkeys)return;
		if(IsTransitioning)return;
		if(_currentState==ModStates.Replay && _player?.IsSeeking==true)return;
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		if (ConfigManager.ToggleRecord.Value.GetDown() && _currentState!=ModStates.Replay)
		{
			_ = ToggleMissionRecording();
		}
		if (ConfigManager.TogglePause.Value.GetDown() && _currentState == ModStates.Replay && _player != null)
		{
			_player.TogglePause();
		}
	}

	private async Task ToggleMissionRecording()
	{
		if (_recordCommandRunning || IsTransitioning || _currentState == ModStates.Replay) return;
		_recordCommandRunning = true;
		try
		{
			bool stoppingMission = _currentState == ModStates.Record && _recorder?.IsRolling != true;
			if (!stoppingMission && (!RollingRecordingController.IsUsableGameplay() || IsFailedReplayWorld))
			{ SetStatus("Start or join a mission before recording."); return; }
			RollingRecordingController.Instance?.SetEnabled(false);
			NextRecordingIsRolling = false;
			if (_currentState == ModStates.Record) await SwitchState(ModStates.Idle);
			if (stoppingMission) { SetStatus(LastRecordingStopStatus); return; }
			await SwitchState(ModStates.Record);
			if (_currentState == ModStates.Record && _recorder?.IsRolling != true)
				SetStatus("Recording mission. Press the record key again to stop and save.");
		}
		catch (Exception ex) { Plugin.logger.LogError(ex); SetStatus("Recording failed: " + ex.Message); }
		finally { _recordCommandRunning = false; }
	}
	private static void SetStatus(string status)
	{ if (ReplayMod.Director.ReplayDirector.i != null) ReplayMod.Director.ReplayDirector.i.Status = status; }

	public bool ShouldContinue(uint id)
	{
		if (_player == null || _currentState != ModStates.Replay)
		{
			return true;
		}
		return _player.ShouldContinue(id);
	}

	public ModStates GetState()
	{
		return _currentState;
	}

	public string[] GetFiles()
	{
		if (!Directory.Exists(_path))
		{
			Directory.CreateDirectory(_path);
		}
		return (from s in Directory.EnumerateFiles(_path)
			where Path.GetExtension(s).Equals(".replay", StringComparison.OrdinalIgnoreCase)
			select s).ToArray();
	}

	public async UniTask TimelineJump(double time)
	{
		if (_player != null && _player.enabled && _currentState == ModStates.Replay)
		{
			await _player.TimelineJump(time,ReplayMod.Director.ReplayDirector.i?.Project.preRoll ?? 0);
		}
	}

	public bool TryGetReplayInfo(string fileName, out Header header)
	{
		try
		{
			string path = Path.Combine(_path, fileName);
			using FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
			using BinaryReader br = new BinaryReader(input);
			header = Header.Read(br);
			return true;
		}
		catch (Exception arg)
		{
			Plugin.logger.LogInfo((object)$"Exception while reading header:\n{arg}");
			header = default(Header);
			return false;
		}
	}

	public async UniTask StartPlaying(string fileName)
	{
		string requested=Path.Combine(_path,fileName);
		// Keep a live mission, recording or existing replay intact if the file
		// cannot be read. Playback will build its seek index after this check.
		await _transitionGate.WaitAsync();
		try
		{
			var network=NetworkManagerNuclearOption.i;
			bool activeConnection=network!=null&&((network.Client!=null&&network.Client.Active)||(network.Server!=null&&network.Server.Active));
			if(MissionManager.IsRunning||_currentState!=ModStates.Idle||activeConnection)
			{
				var inspected=await Task.Run(()=>ReplayIntegrityCheck.Inspect(requested));
				ReplayMod.Misc.SceneHelper.ValidateReplayMap(new MapKey((MapKey.KeyType)inspected.MapType,inspected.MapPath));
				var missing=inspected.UnitDefinitions.Where(key=>!Encyclopedia.Lookup.ContainsKey(key)).OrderBy(key=>key,StringComparer.Ordinal).ToArray();
				if(missing.Length>0)throw new InvalidDataException("Replay requires missing unit definitions: "+string.Join(", ",missing.Take(20))+(missing.Length>20?" (and "+(missing.Length-20)+" more)":""));
			}
			_lastFile=requested;
		}
		catch(Exception error) when(!(error is OperationCanceledException))
		{
			string message="Replay could not be opened: "+error.Message+". The current "+(_currentState==ModStates.Replay?"replay":"session")+" continues.";
			SetStatus(message);throw new InvalidDataException(message,error);
		}
		finally{_transitionGate.Release();}
		await SwitchState(ModStates.Idle);
		await SwitchState(ModStates.Replay);
	}

	public double GetDuration()
	{
		if (_player != null)
		{
			return _player.replayDuration;
		}
		return -1.0;
	}

	public double GetCurrentVirtualTime()
	{
		if (_player != null)
		{
			return _player.currentVirtualTime;
		}
		return -1.0;
	}

	public void ResumePlayback() { if (_player != null) _player.Resume(); }
	public void PausePlayback() { if (_player != null) _player.Pause(); }
	public async Task<string> SaveRolling()
	{
		if(_recorder==null || !_recorder.IsRolling)throw new InvalidOperationException("Start rolling recording first");
		var recorder=_recorder;var path=GetFilePath();await recorder.SaveRolling(path);
		return "Saved rolling clip: "+Path.GetFileName(path)+(string.IsNullOrEmpty(recorder.CaptureIssueNotice)?"":"."+recorder.CaptureIssueNotice);
	}

	public void TogglePause()
	{
		_player?.TogglePause();
	}

	public void StopCamFlight()
	{
		_waypoints.SetRenderActive(flag: true);
	}
	public void SetCaptureOverlayHidden(bool hidden) => _waypoints?.SetCaptureHidden(hidden);

	public void StartCamFlight()
	{
		_waypoints.SetRenderActive(flag: false);
	}
}

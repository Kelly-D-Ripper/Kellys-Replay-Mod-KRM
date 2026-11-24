using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using ReplayMod.Patches;
using ReplayMod.Patches.WeaponPatches;
using UnityEngine;

namespace ReplayMod.Core;

[DefaultExecutionOrder(10000)]
public partial class Recorder : MonoBehaviour
{
	public static Recorder Active { get; private set; }
	private volatile bool _acceptEvents;
	private bool _subscribed;
	private RollingReplayBuffer _rolling;
	private object _recordingMission;
	private object _recordingRunner;
	private MapSettingsManager _recordingMap;
	private int _rollingSaveInProgress;
	private Task _rollingWriteTask;
	private TaskCompletionSource<Task> _rollingDeferredStart;
	private string _rollingDeferredPath;
	private readonly Dictionary<Aircraft, Action> _touchdowns = new Dictionary<Aircraft, Action>();
	public bool IsRolling => _rolling != null;
	public double RollingDuration => _rolling == null ? 0 : Math.Max(0, _virtualTime - _rolling.CutTime);
	public long RollingBytes => _rolling?.Bytes ?? 0;
	public bool RollingMemoryLimited => _rolling?.MemoryLimited ?? false;
	public bool IsRecording => _acceptEvents;
	public string RecordingFailure => _recordWriter?.Failure;
	public string RecordingPath => _recordWriter?.PartialPath;
	public long RecordingQueuedBytes => _recordWriter?.QueuedBytes??0;
	public string LastSaveNotice {get;private set;}="";
	public string CaptureIssueNotice => CaptureFailureCount>0?" Some replay details may be missing; export logs for support.":"";
	public bool SessionValid => MissionManager.IsRunning && _recordingMap != null &&
		ReferenceEquals(_recordingMap, SceneSingleton<MapSettingsManager>.i) &&
		ReferenceEquals(_recordingMission, MissionManager.CurrentMission) &&
		ReferenceEquals(_recordingRunner, MissionManager.Runner);
	public void SetRollingWindow(int seconds) => _rolling?.SetWindow(Math.Max(30, Math.Min(300, seconds)), _virtualTime);
	private Dictionary<Gun, ushort> _gunIndices = new Dictionary<Gun, ushort>();
	private readonly Dictionary<uint,int> _captureFaults = new Dictionary<uint,int>();
	public int CaptureFailureCount {get;private set;}
	private Dictionary<PersistentID, Unit> _unitsOnTrack = new Dictionary<PersistentID, Unit>();
	private sealed class PendingUnitRegistration
	{
		public Unit unit;
		public float since;
		public readonly List<byte[]> earlyEvents=new List<byte[]>();
		public int eventBytes;
		public bool eventOverflow;
	}
	private const int MaxPendingEventsPerUnit=128, MaxPendingEventBytesPerUnit=128*1024, MaxPendingEventBytes=4*1024*1024;
	private int _pendingEventBytes;
	private readonly Dictionary<PersistentID,PendingUnitRegistration> _pendingUnitRegistrations=new Dictionary<PersistentID,PendingUnitRegistration>();
	private readonly List<PersistentID> _pendingUnitScratch=new List<PersistentID>();
	private readonly HashSet<PersistentID> _spawnQueued=new HashSet<PersistentID>();

	private Dictionary<PersistentID, List<Turret>> _turretsOnTrack = new Dictionary<PersistentID, List<Turret>>();

	private List<string> _usedUnits = new List<string>();

	private double _virtualTime = 0.0;

	private HashSet<uint> _despawnSkip = new HashSet<uint>();

	private Dictionary<PersistentID, double> _lastPositionUpdateTime = new Dictionary<PersistentID, double>();

	private Dictionary<PersistentID, double> _lastInputUpdateTime = new Dictionary<PersistentID, double>();

	private Dictionary<PersistentID, double> _lastTurretUpdateTime = new Dictionary<PersistentID, double>();

	private long _eventCount = 0L;

	private double _duration = 0.0;

	private Task _fileWriter;
	private RecoverableReplayWriter _recordWriter;
	private bool _recordingReservation;

	private CancellationTokenSource _cts;

	private MapKey _currentMapKey;

	private string _currentMap;

	private void Start()
	{
		Plugin.logger.LogInfo((object)"Recorder created");
		UnityEngine.Object.DontDestroyOnLoad(this);
	}

	private void OnEnable()
	{
		Subscribe();
	}

	private void OnDisable()
	{
		Unsubscribe();
	}

	private async Task OnDestroy()
	{
		await StopRecording();
	}

	private void Subscribe()
	{
		if (_subscribed) return;
		_subscribed = true;
		PartDamagePatch.OnDamage += HandleDamage;
		EjectionVisualPatch.OnEjection += HandleEjection;
		UnitRegistryPatch.onRegisterUnit = (Action<Unit, PersistentID>)Delegate.Combine(UnitRegistryPatch.onRegisterUnit, new Action<Unit, PersistentID>(HandleUnitRegister));
		UnitRegistryPatch.onUnregisterUnit = (Action<Unit>)Delegate.Combine(UnitRegistryPatch.onUnregisterUnit, new Action<Unit>(HandleUnitUnregister));
		UnitPatch.onPartDetached = (Action<Unit, byte>)Delegate.Combine(UnitPatch.onPartDetached, new Action<Unit, byte>(HandlePartDetach));
		AircraftPatch.onSetGear = (Action<Aircraft, bool>)Delegate.Combine(AircraftPatch.onSetGear, new Action<Aircraft, bool>(HandleSetGear));
		ReplayFuelTankStatusPatch.OnStatus += HandleFuelStatus;
		ReplayUnitStatusPatch.OnStatus += HandleUnitStatus;
		ReplayMissileDetonationPatch.OnDetonation += HandleRecordedDetonation;
		WeaponManagerPatch.onWeaponManagerFire = (Action<WeaponManager>)Delegate.Combine(WeaponManagerPatch.onWeaponManagerFire, new Action<WeaponManager>(HandleWeaponManagerFire));
		WeaponManagerPatch.onSetActiveStation = (Action<WeaponManager, byte>)Delegate.Combine(WeaponManagerPatch.onSetActiveStation, new Action<WeaponManager, byte>(HandleSetActiveStation));
	}

	private void Unsubscribe()
	{
		if (!_subscribed) return;
		_subscribed = false;
		foreach(var pair in _touchdowns) if(pair.Key != null) pair.Key.OnTouchdown -= pair.Value;
		_touchdowns.Clear();
		PartDamagePatch.OnDamage -= HandleDamage;
		EjectionVisualPatch.OnEjection -= HandleEjection;
		UnitRegistryPatch.onRegisterUnit = (Action<Unit, PersistentID>)Delegate.Remove(UnitRegistryPatch.onRegisterUnit, new Action<Unit, PersistentID>(HandleUnitRegister));
		UnitRegistryPatch.onUnregisterUnit = (Action<Unit>)Delegate.Remove(UnitRegistryPatch.onUnregisterUnit, new Action<Unit>(HandleUnitUnregister));
		UnitPatch.onPartDetached = (Action<Unit, byte>)Delegate.Remove(UnitPatch.onPartDetached, new Action<Unit, byte>(HandlePartDetach));
		AircraftPatch.onSetGear = (Action<Aircraft, bool>)Delegate.Remove(AircraftPatch.onSetGear, new Action<Aircraft, bool>(HandleSetGear));
		ReplayFuelTankStatusPatch.OnStatus -= HandleFuelStatus;
		ReplayUnitStatusPatch.OnStatus -= HandleUnitStatus;
		ReplayMissileDetonationPatch.OnDetonation -= HandleRecordedDetonation;
		WeaponManagerPatch.onWeaponManagerFire = (Action<WeaponManager>)Delegate.Remove(WeaponManagerPatch.onWeaponManagerFire, new Action<WeaponManager>(HandleWeaponManagerFire));
		WeaponManagerPatch.onSetActiveStation = (Action<WeaponManager, byte>)Delegate.Remove(WeaponManagerPatch.onSetActiveStation, new Action<WeaponManager, byte>(HandleSetActiveStation));
	}

	private void HandleUnitRegister(Unit unit, PersistentID id)
	{
		try { RegisterUnitWhenReady(unit,id,false); }
		catch(Exception error) { HandleRegistrationFailure(unit,id,error); }
	}

	private void RegisterUnitWhenReady(Unit unit,PersistentID id,bool allowFallback)
	{
		if (!_acceptEvents || !SessionValid || unit == null) return;
		if(id.Id==0)return;
		if(_unitsOnTrack.TryGetValue(id,out var existing))
		{
			if(!ReferenceEquals(existing,unit)){CaptureFailureCount++;Plugin.SafeLogWarning("Replay unit ID collision: #"+id.Id);}
			return;
		}
		_pendingUnitRegistrations.TryGetValue(id,out var pending);
		if(pending!=null&&!ReferenceEquals(pending.unit,unit))
		{
			CaptureFailureCount++;Plugin.SafeLogWarning("Replay unit ID was reused before initialization: #"+id.Id);
			DiscardPendingUnitRegistration(id);pending=null;
		}
		if((unit.definition==null||unit is Aircraft waiting&&(waiting.loadout==null||waiting.loadout.weapons==null))&&!allowFallback)
		{
			if(pending==null)
				_pendingUnitRegistrations[id]=new PendingUnitRegistration{unit=unit,since=Time.realtimeSinceStartup};
			return;
		}
		var pendingEvents=pending;
		if(unit.definition==null){DiscardPendingUnitRegistration(id);CaptureFailureCount++;Plugin.SafeLogWarning("Unit definition was not ready for replay unit #"+id.Id);return;}
		// Native impact presentation can leave an invisible missile object alive
		// for its sound/effect tails. Starting a recording during that interval
		// must not introduce a new flying actor for the spent body.
		if (unit is Missile spentMissile && IsNativelyTerminalMissile(spentMissile)) { DiscardPendingUnitRegistration(id); return; }
		var initialVelocity=unit.rb!=null?unit.rb.velocity:Vector3.zero;
		if(!ReplayPoseValidation.Finite(unit.transform.position.ToGlobalPosition(),unit.transform.rotation,initialVelocity))
		{
			if(!allowFallback)
			{if(pending==null)_pendingUnitRegistrations[id]=new PendingUnitRegistration{unit=unit,since=Time.realtimeSinceStartup};return;}
			throw new InvalidDataException("Unit spawn has a non-finite position, rotation or velocity");
		}
		if (!_usedUnits.Contains(unit.definition.jsonKey))
		{
			_usedUnits.Add(unit.definition.jsonKey);
		}
		ForgetRecordingMissileMotion(id.Id);
		_unitsOnTrack.Add(id, unit);
		_lastPositionUpdateTime.Add(id, _virtualTime);
		HandleWeaponStationAsync(unit).Forget();
		SpawnEvent spawnEvent = ReplayEventFactory.GetEvent<SpawnEvent>();
		try
		{
		spawnEvent.Time = _virtualTime;
		spawnEvent.unitId = id.Id;
		spawnEvent.jsonKey = unit.definition.jsonKey;
		if (unit.rb != null)
		{
			spawnEvent.startingVelocity = unit.rb.velocity;
		}
		else
		{
			spawnEvent.startingVelocity = new Vector3(0f, 0f, 0f);
		}
		spawnEvent.rotation = unit.transform.rotation;
		spawnEvent.pos = unit.transform.position.ToGlobalPosition();
		if (unit.NetworkHQ != null)
		{
			spawnEvent.factionName = unit.NetworkHQ.faction.factionName;
		}
		else
		{
			spawnEvent.factionName = ReplaySpawnPolicy.NoFaction;
		}
		if (unit is Missile missile)
		{
			spawnEvent.ownerId = missile.ownerID.Id;
		}
		else
		{
			spawnEvent.ownerId = id.Id;
		}
		if (unit is Aircraft aircraft)
		{
			spawnEvent.isAircraft = true;
			spawnEvent.liveryId = aircraft.LiveryKey.Id;
			spawnEvent.liveryIndex = aircraft.LiveryKey.Index;
			spawnEvent.liveryType = aircraft.LiveryKey.Type;
			if(aircraft.loadout?.weapons!=null)spawnEvent.weapons=new List<WeaponMount>(aircraft.loadout.weapons);
			else
			{
				var sets=aircraft.weaponManager?.hardpointSets;
				spawnEvent.weapons=sets==null?new List<WeaponMount>():sets.Select(set=>set?.weaponMount).ToList();
				CaptureFailureCount++;
				Plugin.SafeLogWarning("Aircraft loadout was not ready; replay used available hardpoint data for "+unit.definition.jsonKey+". Some stores may be missing.");
			}
		}
		else
		{
			spawnEvent.isAircraft = false;
		}
		NoteEjectionCaptureSpawn(unit);
		var queuedSpawn=spawnEvent;spawnEvent=null;QueueEvent(queuedSpawn);_spawnQueued.Add(id);
		TakePendingUnitRegistration(id);FlushPendingUnitEvents(pendingEvents);
		}
		finally { if(spawnEvent!=null)ReplayEventFactory.Return(spawnEvent); }
		RecordActorIdentity(unit,true);
		RecordVfxState(unit, true);
		RecordWorldActor(unit);
		if (unit is Missile initialMissile) RecordMissileStatus(initialMissile);
		if (unit is Aircraft initialAircraft)
		{
			Action touchdown = () => { if (_acceptEvents && !initialAircraft.airborne) { var e=ReplayEventFactory.GetEvent<LandingEvent>(); e.unitId=initialAircraft.persistentID.Id; e.Time=_virtualTime; QueueEvent(e); } };
			_touchdowns[initialAircraft]=touchdown; initialAircraft.OnTouchdown += touchdown;
			HandleSetGear(initialAircraft, initialAircraft.gearDeployed);
			var input = ReplayEventFactory.GetEvent<UpdateInputsEvent>();
			input.id = id.Id; input.Time = _virtualTime;
			if (initialAircraft.controlInputs != null) input.CopyFromControlInputs(initialAircraft.controlInputs);
			QueueEvent(input);
			RecordStatus(initialAircraft);
			if (initialAircraft.weaponManager != null && initialAircraft.weaponManager.currentWeaponStation != null)
				HandleSetActiveStation(initialAircraft.weaponManager, initialAircraft.weaponManager.currentWeaponStation.Number);
		}
		if (unit.partLookup != null)
			foreach (var part in unit.partLookup)
				if (part != null && part.hitPoints != 100f) HandleDamage(part, 0, 0, 0, 0);
        RecordInitialAircraftPartMotion(unit);
	}

	private void HandleRegistrationFailure(Unit unit,PersistentID id,Exception error)
	{
		CaptureFailureCount++;
		if(!_spawnQueued.Contains(id))
		{
			_unitsOnTrack.Remove(id);_lastPositionUpdateTime.Remove(id);_lastInputUpdateTime.Remove(id);_lastTurretUpdateTime.Remove(id);_turretsOnTrack.Remove(id);
			DiscardPendingUnitRegistration(id);
		}
		Plugin.SafeLogWarning("Could not fully capture replay unit #"+id.Id+": "+error);
	}

	private void ProcessPendingUnitRegistrations(bool force=false)
	{
		if(_pendingUnitRegistrations.Count==0)return;
		_pendingUnitScratch.Clear();_pendingUnitScratch.AddRange(_pendingUnitRegistrations.Keys);
		foreach(var id in _pendingUnitScratch)
		{
			if(!_pendingUnitRegistrations.TryGetValue(id,out var pending))continue;
			if(pending.unit==null){DiscardPendingUnitRegistration(id);CaptureFailureCount++;Plugin.SafeLogWarning("A unit disappeared before its replay spawn could be captured: #"+id.Id);continue;}
			bool ready=pending.unit.definition!=null&&(!(pending.unit is Aircraft a)||a.loadout?.weapons!=null);
			bool expired=force||Time.realtimeSinceStartup-pending.since>=.25f;
			if(!ready&&!expired)continue;
			try{RegisterUnitWhenReady(pending.unit,id,expired);}
			catch(Exception error){HandleRegistrationFailure(pending.unit,id,error);}
		}
		_pendingUnitScratch.Clear();
	}

	private PendingUnitRegistration TakePendingUnitRegistration(PersistentID id)
	{
		if(!_pendingUnitRegistrations.TryGetValue(id,out var pending))return null;
		_pendingUnitRegistrations.Remove(id);_pendingEventBytes-=pending.eventBytes;
		return pending;
	}

	private void DiscardPendingUnitRegistration(PersistentID id)
	{
		var pending=TakePendingUnitRegistration(id);
		if(pending==null)return;
		ForgetPendingCountermeasures(pending.unit);
		if(!ReferenceEquals(pending.unit,null))ForgetRecordingGuns(pending.unit);
	}

	// Native callbacks can arrive after registration but before the loadout is
	// ready. Keep their payloads out of the replay until its spawn has been
	// written. The writer requires nondecreasing times, so retime them to the
	// spawn's time when the pending registration is resolved.
	private bool IsTrackedOrPending(Unit unit)
	{
		if(IsTracked(unit))return true;
		return _acceptEvents&&unit!=null&&_pendingUnitRegistrations.TryGetValue(unit.persistentID,out var pending)&&ReferenceEquals(unit,pending.unit);
	}

	private bool QueueEventForUnit(Unit unit,IReplayEvent value)
	{
		if(unit==null||!_pendingUnitRegistrations.TryGetValue(unit.persistentID,out var pending)||!ReferenceEquals(unit,pending.unit))
		{QueueEvent(value);return true;}
		try
		{
			if(pending.eventOverflow)return false;
			var data=RollingReplayBuffer.Encode(value);
			if(pending.earlyEvents.Count>=MaxPendingEventsPerUnit||data.Length>MaxPendingEventBytesPerUnit-pending.eventBytes||data.Length>MaxPendingEventBytes-_pendingEventBytes)
			{
				pending.eventOverflow=true;CaptureFailureCount++;
				Plugin.SafeLogWarning("Too many early events for initializing replay unit #"+unit.persistentID.Id+"; some details may be missing.");return false;
			}
			pending.earlyEvents.Add(data);pending.eventBytes+=data.Length;_pendingEventBytes+=data.Length;return true;
		}
		catch(Exception error){CaptureFailureCount++;Plugin.SafeLogWarning("Could not preserve early replay event for unit #"+unit.persistentID.Id+": "+error.Message);return false;}
		finally{ReplayEventFactory.Return(value);}
	}

	private void FlushPendingUnitEvents(PendingUnitRegistration pending)
	{
		if(pending==null)return;
		foreach(var data in pending.earlyEvents)
		{
			IReplayEvent value=null;
			try{value=RollingReplayBuffer.Decode(data);value.Time=_virtualTime;var queued=value;value=null;QueueEvent(queued);}
			catch(Exception error){CaptureFailureCount++;Plugin.SafeLogWarning("Could not restore an early replay event: "+error.Message);}
			finally{if(value!=null)ReplayEventFactory.Return(value);}
		}
	}

	private void HandleUnitUnregister(Unit unit)
	{
		if(unit!=null&&_pendingUnitRegistrations.TryGetValue(unit.persistentID,out var pending)&&ReferenceEquals(pending.unit,unit))
		{
			try{RegisterUnitWhenReady(unit,unit.persistentID,true);}
			catch(Exception error){HandleRegistrationFailure(unit,unit.persistentID,error);}
		}
		if (!IsTracked(unit)) return;
		_spawnQueued.Remove(unit.persistentID);
		ForgetRecordingMissileMotion(unit);ForgetEjectionCapture(unit);
		if(unit is Aircraft a && _touchdowns.TryGetValue(a,out var handler)) { a.OnTouchdown-=handler; _touchdowns.Remove(a); }
		ForgetRecordingAircraftVisuals(unit.persistentID.Id);
		ForgetRecordingCargo(unit.persistentID.Id);
		ForgetRecordingMissileMounts(unit.persistentID.Id);
		ForgetRecordingCargoParachutes(unit.persistentID.Id);
		ForgetRecordingLights(unit.persistentID.Id);
		ForgetRecordingVfx(unit);
		ForgetRecordingGuns(unit);
		ReplayCountermeasureIdentity.Forget(unit as Aircraft);
		ForgetRecordingActorIdentity(unit);
		ForgetRecordingWorldActor(unit);
		RecordFinalAircraftMotion(unit);
		ForgetRecordingAircraftLifecycle(unit.persistentID.Id);
		_captureFaults.Remove(unit.persistentID.Id);
		_lastInputUpdateTime.Remove(unit.persistentID); _lastTurretUpdateTime.Remove(unit.persistentID);
		if (unit.persistentID.Id != 0)
		{
			if (_despawnSkip.Contains(unit.persistentID.Id))
			{
				_despawnSkip.Remove(unit.persistentID.Id);
				return;
			}
			_unitsOnTrack.Remove(unit.persistentID);
			_lastPositionUpdateTime.Remove(unit.persistentID);
			DespawnEvent despawnEvent = ReplayEventFactory.GetEvent<DespawnEvent>();
			despawnEvent.Time = _virtualTime;
			despawnEvent.unitId = unit.persistentID.Id;
			despawnEvent.isMissileDetonated = false;
			_turretsOnTrack.Remove(unit.persistentID);
			QueueEvent(despawnEvent);
		}
	}

	private void HandleMissileDetonate(Missile missile)
	{
		if (!_acceptEvents) return;
		_spawnQueued.Remove(missile.persistentID);
		_unitsOnTrack.Remove(missile.persistentID);
		_lastPositionUpdateTime.Remove(missile.persistentID);
		DespawnEvent despawnEvent = ReplayEventFactory.GetEvent<DespawnEvent>();
		despawnEvent.Time = _virtualTime;
		despawnEvent.unitId = missile.persistentID.Id;
		despawnEvent.isMissileDetonated = true;
		QueueEvent(despawnEvent);
		_despawnSkip.Add(missile.persistentID.Id);
	}

	private void HandlePartDetach(Unit unit, byte partId)
	{
		if (!NoteObservedPartDetach(unit,partId)) return;
		DetachPartEvent detachPartEvent = ReplayEventFactory.GetEvent<DetachPartEvent>();
		detachPartEvent.Time = _virtualTime;
		detachPartEvent.unitID = unit.persistentID.Id;
		detachPartEvent.partID = partId;
		QueueEvent(detachPartEvent);
	}

	private void HandleSetGear(Aircraft aircraft, bool deployed)
	{
		if (!_acceptEvents) return;
		SetGearEvent setGearEvent = ReplayEventFactory.GetEvent<SetGearEvent>();
		setGearEvent.deployed = deployed;
		setGearEvent.Time = _virtualTime;
		setGearEvent.unitId = aircraft.persistentID.Id;
		QueueEventForUnit(aircraft,setGearEvent);
	}

	private async UniTask HandleWeaponStationAsync(Unit unit)
	{
		await UniTask.WhenAny(UniTask.WaitUntil(() => unit == null || unit.weaponStations != null), UniTask.WaitForSeconds(1));
		if (!_acceptEvents || !SessionValid || unit == null || unit.weaponStations == null ||
			!_unitsOnTrack.TryGetValue(unit.persistentID, out var tracked) || !ReferenceEquals(tracked, unit))
		{
			return;
		}
		foreach (WeaponStation ws in unit.weaponStations)
		{
			foreach (Turret t in ws.Turrets)
			{
				if (_turretsOnTrack.TryGetValue(unit.persistentID, out var list))
				{
					list.Add(t);
				}
				else
				{
					_turretsOnTrack[unit.persistentID] = new List<Turret> { t };
				}
				list = null;
			}
		}
	}

	private void HandleSetActiveStation(WeaponManager manager, byte stationIdx)
	{
		if (!_acceptEvents) return;
		SetActiveStationEvent setActiveStationEvent = ReplayEventFactory.GetEvent<SetActiveStationEvent>();
		setActiveStationEvent.Time = _virtualTime;
		setActiveStationEvent.unitId = manager.aircraft.persistentID.Id;
		setActiveStationEvent.stationIdx = stationIdx;
		QueueEventForUnit(manager.aircraft,setActiveStationEvent);
	}

	private void HandleWeaponManagerFire(WeaponManager manager)
	{
		if (!_acceptEvents) return;
		WeaponManagerFireEvent weaponManagerFireEvent = ReplayEventFactory.GetEvent<WeaponManagerFireEvent>();
		weaponManagerFireEvent.Time = _virtualTime;
		weaponManagerFireEvent.unitId = manager.aircraft.persistentID.Id;
		QueueEventForUnit(manager.aircraft,weaponManagerFireEvent);
	}

	private void QueueEvent(IReplayEvent e)
	{
		try
		{
			if (!SessionValid || !_acceptEvents) return;
			uint invalidId=0;string invalidKind=null;
			if(e is UpdatePositionEvent move&&!ReplayPoseValidation.Finite(move.position,move.rotation,move.velocity))
			{invalidId=move.unitId;invalidKind="movement";}
			else if(e is UpdateTurretEvent turret&&!turret.HasFiniteAngles())
			{invalidId=turret.attachedUnitId;invalidKind="turret";}
			else if(e is PartDamageEvent damage&&!damage.HasFiniteValues())
			{invalidId=damage.unitId;invalidKind="damage";}
			if(invalidKind!=null)
			{
				CaptureFailureCount++;
				if(!_captureFaults.ContainsKey(invalidId))Plugin.SafeLogWarning("Replay skipped a non-finite "+invalidKind+" event for unit #"+invalidId+"; other capture continues.");
				_captureFaults[invalidId]=_captureFaults.TryGetValue(invalidId,out var failures)?failures+1:1;return;
			}
			if (_rolling != null) { _rolling.Add(e); return; }
			if (_recordWriter == null || !_recordWriter.TryEnqueue(e)) _acceptEvents=false;
		}
		finally { ReplayEventFactory.Return(e); }
	}
	private void HandleDamage(UnitPart part, float pierce, float blast, float fire, float impact)
	{
		if (!_acceptEvents || part == null || part.parentUnit == null) return;
		var e = ReplayEventFactory.GetEvent<PartDamageEvent>();
		e.Time = _virtualTime; e.unitId = part.parentUnit.persistentID.Id; e.partId = part.id;
		e.hitPoints = part.hitPoints; e.pierce = pierce; e.blast = blast; e.fire = fire; e.impact = impact;
		QueueEventForUnit(part.parentUnit,e);
	}
	private void HandleEjection(Aircraft aircraft, bool capsule)
	{
		if (!_acceptEvents || aircraft == null) return;
		NoteAircraftEjection(aircraft);
		var e = ReplayEventFactory.GetEvent<EjectionEvent>();
		e.Time = _virtualTime; e.unitId = aircraft.persistentID.Id; e.capsule = capsule; QueueEventForUnit(aircraft,e);
	}
	private void RecordMissileStatus(Missile missile)
	{
		if (IsRecordingMissileTerminal(missile)) return;
		var e = ReplayEventFactory.GetEvent<MissileStatusEvent>();
		e.Time = _virtualTime; e.unitId = missile.persistentID.Id; e.stage = missile.motorStage;
		e.age = missile.timeSinceSpawn; e.throttle = missile.throttle; e.thrust = missile.engineCurrentThrust; e.booster = missile.boosterIsAttached;
		int n = missile.motors.Length; e.fuel = new float[n]; e.delay = new float[n]; e.activated = new bool[n];
		for (int i = 0; i < n; i++) { e.fuel[i] = missile.motors[i].fuelMass; e.delay[i] = missile.motors[i].delayTimer; e.activated[i] = missile.motors[i].activated; }
		QueueEvent(e);
	}

	private void RecordStatus(Aircraft aircraft)
	{
		var e = ReplayEventFactory.GetEvent<AircraftStatusEvent>();
		e.Time = _virtualTime; e.unitId = aircraft.persistentID.Id; e.ignition = aircraft.Ignition;
		e.rpm = new float[aircraft.engines?.Count??0];
		for (int i = 0; i < e.rpm.Length; i++) e.rpm[i] = aircraft.engines[i]!=null?aircraft.engines[i].GetRPM():0;
		QueueEvent(e);
		if(aircraft.engineStates!=null&&aircraft.engineStates.Count>0)
		{
			var cores=ReplayEventFactory.GetEvent<EngineCoreStateEvent>();
			cores.Time=_virtualTime;cores.unitId=aircraft.persistentID.Id;cores.ignition=aircraft.Ignition;
			cores.rpm=new float[aircraft.engineStates.Count];
			for(int i=0;i<cores.rpm.Length;i++)cores.rpm[i]=aircraft.engineStates[i]!=null?aircraft.engineStates[i].GetRPM():0;
			QueueEvent(cores);
		}
	}
	public void RecordBullet(BulletSim sim, BulletSim.Bullet bullet, Color color, float tracerSize)
	{
		RecordResolvedBullet(sim, bullet, color, tracerSize);
	}

	private void Update()
	{
		if (!_acceptEvents || _cts == null || _cts.IsCancellationRequested)
		{
			return;
		}
		// A recorder survives scene loads. Never append the next mission to the old map header.
		if (!SessionValid) { _acceptEvents = false; ReplayManager.i?.RecordingSessionEnded(this); return; }
		ProcessPendingUnitRegistrations();
		foreach (KeyValuePair<PersistentID, Unit> item in _unitsOnTrack)
		{
			try
			{
			PersistentID key = item.Key;
			Unit value = item.Value;
			if (value == null || !_lastPositionUpdateTime.TryGetValue(key, out var value2))
			{
				continue;
			}
			RecordVfxState(value);
			double intervalForUnit = ReplayManager.i.GetIntervalForUnit(value);
			if (!(value is Aircraft) && !(value is Missile) && _virtualTime - value2 > intervalForUnit)
			{
				_lastPositionUpdateTime[key] = _virtualTime;
				UpdatePositionEvent updatePositionEvent = ReplayEventFactory.GetEvent<UpdatePositionEvent>();
				updatePositionEvent.position = value.transform.position.ToGlobalPosition();
				updatePositionEvent.rotation = value.transform.rotation;
				if (value.rb != null)
				{
					updatePositionEvent.velocity = value.rb.velocity;
				}
				updatePositionEvent.unitId = value.persistentID.Id;
				updatePositionEvent.Time = _virtualTime;
				QueueEvent(updatePositionEvent);
			}
			if (value is Aircraft aircraft)
			{
				if (!_lastInputUpdateTime.TryGetValue(key, out var value3))
				{
					_lastInputUpdateTime[key] = _virtualTime;
				}
				if (_virtualTime - value3 >= ReplayManager.UpdateInputsInterval)
				{
					_lastInputUpdateTime[key] = _virtualTime;
					RecordStatus(aircraft);
					if (aircraft.controlInputs != null)
					{
					UpdateInputsEvent updateInputsEvent = ReplayEventFactory.GetEvent<UpdateInputsEvent>();
					updateInputsEvent.CopyFromControlInputs(aircraft.controlInputs);
					updateInputsEvent.id = aircraft.persistentID.Id;
					updateInputsEvent.Time = _virtualTime;
					QueueEvent(updateInputsEvent);
					}
				}
			}
			}
			catch(Exception error)
			{
				CaptureFailureCount++;
				uint failedId=item.Key.Id;
				if(!_captureFaults.ContainsKey(failedId))Plugin.logger.LogWarning("Replay actor capture failed for "+failedId+"; other actors continue: "+error);
				_captureFaults[failedId]=_captureFaults.TryGetValue(failedId,out var count)?count+1:1;
			}
		}
		foreach (KeyValuePair<PersistentID, List<Turret>> item2 in _turretsOnTrack)
		{
			if (_lastTurretUpdateTime.TryGetValue(item2.Key, out var _))
			{
				if (_virtualTime - _lastTurretUpdateTime[item2.Key] < ReplayManager.TurretsUpdateInterval)
				{
					continue;
				}
				_lastTurretUpdateTime[item2.Key] = _virtualTime;
				foreach (Turret item3 in item2.Value)
				{
					if (item3 == null || item3.currentWeaponStation == null) continue;
					UpdateTurretEvent updateTurretEvent = ReplayEventFactory.GetEvent<UpdateTurretEvent>();
					updateTurretEvent.Time = _virtualTime;
					updateTurretEvent.turretIdx = item3.turretIndex;
					updateTurretEvent.attachedUnitId = item2.Key.Id;
					updateTurretEvent.elevationAngle = item3.elevationAngle;
					updateTurretEvent.traverseAngle = item3.traverseAngle;
					updateTurretEvent.weaponStationIdx = item3.currentWeaponStation.Number;
					QueueEvent(updateTurretEvent);
				}
			}
			else
			{
				_lastTurretUpdateTime[item2.Key] = _virtualTime;
			}
		}
		RecordWorldState();
		_virtualTime += Time.deltaTime;
		_rolling?.Trim(_virtualTime);
	}

	public async Task StartRecording(string filePath, string mapPrefabName, double rollingSeconds = 0)
	{
		try
		{
			if (_cts != null)
			{
				await StopRecording();
			}
			if(!ReplayRecordingRecovery.TryBeginRecording())throw new InvalidOperationException("Wait for recording recovery or a pending file save to finish");
			_recordingReservation=true;
			if (mapPrefabName == "UnknownMap" || !MissionManager.IsRunning || SceneSingleton<MapSettingsManager>.i == null ||
				SceneSingleton<MapSettingsManager>.i.MapLoader == null)
			{
				throw new InvalidOperationException("Start a mission before recording");
			}
			_virtualTime = 0.0;
			LastSaveNotice="";
			_rollingWriteTask=null;_rollingDeferredStart=null;_rollingDeferredPath=null;
			_captureFaults.Clear();_pendingUnitRegistrations.Clear();_pendingEventBytes=0;_spawnQueued.Clear();CaptureFailureCount=0;
			ClearRecordingCargoParachutes(); ClearRecordingCargo(); ClearRecordingMissileMounts(); ClearRecordingAircraftVisuals(); ClearRecordingLights(); ClearRecordingVfx(); ClearRecordingGuns(); ClearRecordingWorld(); ClearRecordingAircraftLifecycle(); ClearRecordingCountermeasures(); ClearRecordingActorIdentities(); ClearRecordingMissileMotion(); ClearEjectionCapture();
			_stopTask=null;
			if(rollingSeconds>0) _rolling=new RollingReplayBuffer(rollingSeconds);
			_currentMap = mapPrefabName;
			_currentMapKey = ReplayMod.Misc.SceneHelper.GetRecordingMapKey();
			_recordingMission = MissionManager.CurrentMission;
			_recordingRunner = MissionManager.Runner;
			_recordingMap = SceneSingleton<MapSettingsManager>.i;
			_recordWriter=rollingSeconds>0?null:new RecoverableReplayWriter(filePath);
			_eventCount = 0;
			Subscribe();
			ReplayMod.Misc.EventsHelper.Reset();
			_cts = new CancellationTokenSource();
			_acceptEvents = true;
			Active = this;
			Unit[] units = UnityEngine.Object.FindObjectsOfType<Unit>();
			Plugin.SafeLog($"Units {units.Length}");
			Queue<Unit> other = new Queue<Unit>();
			Unit[] array = units;
			foreach (Unit unit in array)
			{
				if (!(unit is Missile))
				{
					HandleUnitRegister(unit, unit.persistentID);
				}
				else
				{
					other.Enqueue(unit);
				}
			}
			while (other.Count > 0)
			{
				Unit u = other.Dequeue();
				HandleUnitRegister(u, u.persistentID);
			}
			RecordWorldState();
			CaptureExistingCountermeasures();
			if (_rolling != null) return;
			_recordWriter.Start(CreateRecordingHeader());_fileWriter=_recordWriter.Completion;
			if(await Task.WhenAny(_recordWriter.Ready,Task.Delay(5000))!=_recordWriter.Ready)throw new TimeoutException("Recording file startup is still waiting for the disk. Capture stopped safely.");
			await _recordWriter.Ready;
			Plugin.SafeLog("Recoverable format-"+ReplayRecordingHeader.CurrentFormat+" recorder started; disk backlog capped at 64 MiB");
		}
		catch (Exception ex)
		{
			_acceptEvents = false; Active = null;
			ClearRecordingCargoParachutes(); ClearRecordingCargo(); ClearRecordingMissileMounts(); ClearRecordingAircraftVisuals(); ClearRecordingLights(); ClearRecordingVfx(); ClearRecordingGuns(); ClearRecordingWorld(); ClearRecordingAircraftLifecycle(); ClearRecordingCountermeasures(); ClearRecordingActorIdentities(); ClearRecordingMissileMotion(); ClearEjectionCapture();
			Unsubscribe();_rolling?.Dispose();_rolling=null;_cts?.Dispose();_cts=null;
			_unitsOnTrack.Clear(); _lastPositionUpdateTime.Clear(); _lastInputUpdateTime.Clear();
			_lastTurretUpdateTime.Clear(); _turretsOnTrack.Clear(); _despawnSkip.Clear(); _usedUnits.Clear();
			_gunIndices.Clear(); _captureFaults.Clear();_pendingUnitRegistrations.Clear();_pendingEventBytes=0;_spawnQueued.Clear();
			var failedWriter=_recordWriter;failedWriter?.Complete(_virtualTime);ReleaseRecordingReservation(failedWriter);_recordWriter=null;
			_fileWriter=null;
			Exception ex2 = ex;
			Plugin.logger.LogError((object)ex2);
			throw;
		}
	}

	private Task _stopTask;
	public Task StopRecording() => _stopTask ?? (_stopTask = StopRecordingCore());

	private async Task StopRecordingCore()
	{
		if (_cts == null) { ClearRecordingMissileMotion(); ClearEjectionCapture(); return; }
		if(SessionValid)ProcessPendingUnitRegistrations(true);
		else if(_pendingUnitRegistrations.Count>0){CaptureFailureCount++;Plugin.SafeLogWarning("Some replay units were still initializing when the mission ended.");}
		string pendingClipFailure=null;
		if(_rolling!=null&&_rollingDeferredStart!=null)
		{
			var deferred=_rollingDeferredStart;var path=_rollingDeferredPath;
			_rollingDeferredStart=null;_rollingDeferredPath=null;
			try{deferred.TrySetResult(BeginRollingWrite(path));}
			catch(Exception error){pendingClipFailure="Recent clip could not be saved: "+error.Message;deferred.TrySetException(error);Plugin.SafeLogWarning(pendingClipFailure);}
		}
		_pendingUnitRegistrations.Clear();_pendingEventBytes=0;_spawnQueued.Clear();
		_duration = _virtualTime; _acceptEvents = false; if (Active == this) Active = null; Unsubscribe();
		_captureFaults.Clear();
		ClearRecordingCargoParachutes(); ClearRecordingCargo(); ClearRecordingMissileMounts(); ClearRecordingAircraftVisuals(); ClearRecordingLights(); ClearRecordingVfx(); ClearRecordingGuns(); ClearRecordingWorld(); ClearRecordingAircraftLifecycle(); ClearRecordingCountermeasures(); ClearRecordingActorIdentities(); ClearRecordingMissileMotion(); ClearEjectionCapture();
		if(_rolling != null)
		{
			LastSaveNotice=pendingClipFailure??(_rollingWriteTask!=null&&!_rollingWriteTask.IsCompleted?"Recent gameplay buffer stopped; its saved clip is still writing in the background.":
				_rollingWriteTask?.IsFaulted==true?"Recent clip saving failed. If an interrupted file was created, use Library → Recover recordings.":"Recent gameplay buffer stopped.");
			LastSaveNotice+=CaptureIssueNotice;
			_rolling.Dispose(); _rolling=null; _cts.Dispose(); _cts=null;
			ReleaseRecordingReservation(null);
			_unitsOnTrack.Clear(); _lastPositionUpdateTime.Clear(); _lastInputUpdateTime.Clear(); _lastTurretUpdateTime.Clear(); _turretsOnTrack.Clear(); _despawnSkip.Clear(); _usedUnits.Clear(); _gunIndices.Clear(); return;
		}
		var writer=_recordWriter;ReplayRecordingRecovery.TrackPendingWrite(writer?.Completion);writer?.Complete(_duration);
		try
		{
			if(writer!=null&&await writer.WaitForCompletionAsync())
			{
				var saved=await writer.Completion;_eventCount=saved.Events;
				if(saved.Completed){LastSaveNotice="Mission saved: "+Path.GetFileName(saved.Path)+"."+CaptureIssueNotice;Plugin.SafeLog($"Replay saved: {saved.Events} events, {saved.Duration:F2}s");}
				else{LastSaveNotice="Recording stopped. "+saved.Failure+" If an interrupted file was created, use Library → Recover recordings.";Plugin.SafeLogWarning(LastSaveNotice);}
			}
			else if(writer!=null){LastSaveNotice="Recording stopped; its file is still saving in the background. Wait before recovering or starting another recording."+CaptureIssueNotice;Plugin.SafeLogWarning(LastSaveNotice);}
		}
		finally
		{
			_cts.Dispose();ReleaseRecordingReservation(writer);
			_unitsOnTrack.Clear(); _lastPositionUpdateTime.Clear(); _lastInputUpdateTime.Clear();
			_lastTurretUpdateTime.Clear(); _turretsOnTrack.Clear(); _despawnSkip.Clear(); _usedUnits.Clear(); _gunIndices.Clear();
			_cts = null; _fileWriter = null; _recordWriter=null;
		}
	}

	public async Task StopAndDestroy()
	{
		await StopRecording();
		UnityEngine.Object.Destroy(this);
	}

	private void OnApplicationQuit()
	{
		StopRecording();
		if(!ReplayRecordingRecovery.WaitForPendingWritesOnQuit())
			Plugin.SafeLogWarning("Recording is still saving as the game exits; use Library → Recovery if a partial remains.");
	}

	private ReplayRecordingHeader CreateRecordingHeader(bool includeSessionResources = true)
	{
		string role=NuclearOption.Networking.NetworkManagerNuclearOption.i?.Server?.Active==true?"host":"client";
		var plugins=BepInEx.Bootstrap.Chainloader.PluginInfos.OrderBy(p=>p.Key,StringComparer.Ordinal).Take(256).Select(p=>p.Key+" | "+p.Value.Metadata.Name+" | "+p.Value.Metadata.Version).ToArray();
		return new ReplayRecordingHeader {MapType=(byte)_currentMapKey.Type,MapPath=_currentMapKey.Path,Resources=includeSessionResources?_usedUnits.ToArray():Array.Empty<string>(),GameAssemblyPath=typeof(Unit).Assembly.Location,RecorderAssemblyPath=typeof(Recorder).Assembly.Location,
			Metadata=ReplayRecordingProvenance.Build(Application.version,Application.unityVersion,PluginInfo.Version,role,typeof(Unit).Assembly.GetName().Version?.ToString(),plugins)};
	}
	private void ReleaseRecordingReservation(RecoverableReplayWriter writer)
	{
		if(!_recordingReservation)return;_recordingReservation=false;
		var pending=writer?.Completion??_rollingWriteTask;
		ReplayRecordingRecovery.TrackPendingWrite(pending);
		ReplayRecordingRecovery.EndRecordingWhenComplete(pending);
	}

	private Task BeginRollingWrite(string path)
	{
		if(_rolling==null)throw new InvalidOperationException("Recent gameplay recording is not active");
		var events=_rolling.Snapshot(_virtualTime);
		double duration=Math.Max(0,_virtualTime-_rolling.CutTime);
		if(duration<.1||events.Count==0)throw new InvalidOperationException("Wait a moment for some gameplay to be recorded");
		var header=CreateRecordingHeader(includeSessionResources:false);
		// The background writer owns immutable bytes. The live ring can continue or stop safely.
		_rollingWriteTask=Task.Run(() => {var tmp=path+".partial";using(var f=new FileStream(tmp,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read))using(var w=new BinaryWriter(f))
			{header.Write(w);var flushClock=System.Diagnostics.Stopwatch.StartNew();foreach(var data in events){w.Write(ReplayRecordTransport.FrameLegacyEvent(data));if(flushClock.ElapsedMilliseconds>=1000){w.Flush();flushClock.Restart();}}header.FinalizeHeader(w,duration,events.Count);w.Flush();f.Flush(true);}ReplayIntegrityCheck.Check(tmp);File.Move(tmp,path);});
		ReplayRecordingRecovery.TrackPendingWrite(_rollingWriteTask);
		return _rollingWriteTask;
	}

	public async Task SaveRolling(string path)
	{
		if (Interlocked.CompareExchange(ref _rollingSaveInProgress, 1, 0) != 0)
			throw new InvalidOperationException("A recent clip is already being saved");
		TaskCompletionSource<Task> deferred=null;
		try
		{
			if (_rolling == null || !_acceptEvents || !SessionValid) throw new InvalidOperationException("Recent gameplay recording is not active");
			_rollingWriteTask=null;
			deferred=new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
			_rollingDeferredStart=deferred;_rollingDeferredPath=path;
			// A hotkey can land in the same frame as a registry callback. Flush
			// initialized units before taking the cut, or briefly wait for their
			// normal initialization/fallback instead of silently omitting them.
			float deadline=Time.realtimeSinceStartup+.5f;
			while(_pendingUnitRegistrations.Count>0)
			{
				ProcessPendingUnitRegistrations();
				if(_pendingUnitRegistrations.Count==0)break;
				if(Time.realtimeSinceStartup>=deadline)throw new InvalidOperationException("Some units are still initializing. Try saving the recent clip again in a moment.");
				await UniTask.NextFrame();
				if(deferred.Task.IsCompleted){await (await deferred.Task);return;}
				if(_rolling==null||!_acceptEvents||!SessionValid)throw new InvalidOperationException("Recent gameplay recording ended before the clip could be saved.");
			}
			var write=BeginRollingWrite(path);
			deferred.TrySetResult(write);_rollingDeferredStart=null;_rollingDeferredPath=null;
			await write;
		}
		catch(Exception){LastSaveNotice="Recent clip saving failed. If an interrupted file was created, use Library → Recover recordings.";throw;}
		finally
		{
			if(ReferenceEquals(_rollingDeferredStart,deferred)){_rollingDeferredStart=null;_rollingDeferredPath=null;}
			Interlocked.Exchange(ref _rollingSaveInProgress, 0);
		}
	}

}

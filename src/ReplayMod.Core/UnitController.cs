using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Cysharp.Threading.Tasks;
using Mirage;
using NuclearOption.Jobs;
using NuclearOption.SavedMission;
using NuclearOption.SceneLoading;
using ReplayMod.Data;
using ReplayMod.Events.ConcreteEvents;
using ReplayMod.Misc;
using UnityEngine;

namespace ReplayMod.Core;

public partial class UnitController
{
	private readonly Dictionary<Unit, Rigidbody[]> _replayBodies = new Dictionary<Unit, Rigidbody[]>();
	private readonly Dictionary<Unit, Gun[]> _replayGuns = new Dictionary<Unit, Gun[]>();
	private readonly Dictionary<Unit, MountedMissile[]> _missileMounts = new Dictionary<Unit, MountedMissile[]>();
	private readonly Dictionary<uint, float[]> _engineRpm = new Dictionary<uint, float[]>();
	private readonly Dictionary<uint, ReplayEngineTimeline> _engineTimelines = new Dictionary<uint, ReplayEngineTimeline>();
	private readonly Dictionary<uint, float[]> _engineCoreRpm = new Dictionary<uint, float[]>();
	private readonly Dictionary<uint, ReplayEngineTimeline> _engineCoreTimelines = new Dictionary<uint, ReplayEngineTimeline>();
	private sealed class ReplayBoosterVisual
	{
		public VLSBooster booster;
		public ReplayBoosterState state;
	}
	private readonly Dictionary<Missile, List<ReplayBoosterVisual>> _replayBoosters = new Dictionary<Missile, List<ReplayBoosterVisual>>();
	private readonly HashSet<VLSBooster> _ownedBoosters = new HashSet<VLSBooster>();
	private int _boosterCleanupTicks;
	private Dictionary<uint, PersistentID> _unitMap = new Dictionary<uint, PersistentID>();
	private readonly ReplayActorRegistry<Unit> _ownedActors = new ReplayActorRegistry<Unit>(u => u != null, u => u.persistentID.Id);

	private Dictionary<PersistentID, uint> _reverseUnitMap = new Dictionary<PersistentID, uint>();

	private Dictionary<PersistentID, bool> _isRbDisabled = new Dictionary<PersistentID, bool>();

	private MapKey _currentMap;

	private const short MAX_STALE_SNAPSHOTS = 500;

	private Dictionary<uint, List<Inputs>> _inputs = new Dictionary<uint, List<Inputs>>();

	private HashSet<uint> _pidsOnDestroy = new HashSet<uint>();

	private int _generated = 0;
	private readonly ReplayPartHealthPresentation _partHealthPresentation = new ReplayPartHealthPresentation();

	private Dictionary<uint, List<TurretSnapshot>> _queuedSnapshots = new Dictionary<uint, List<TurretSnapshot>>();

	private Dictionary<Turret, List<TurretSnapshot>> _turretSnapshots = new Dictionary<Turret, List<TurretSnapshot>>();

	private Dictionary<uint, List<PositionSnapshot>> _unitSnapshots = new Dictionary<uint, List<PositionSnapshot>>();
	private readonly Dictionary<uint, double> _spawnTimes = new Dictionary<uint, double>();
	private static readonly Func<PositionSnapshot, double> PositionTime = p => p.time;
	private static readonly Func<Inputs, double> InputTime = p => p.time;
	private static readonly Func<TurretSnapshot, double> TurretTime = p => p.time;

	public UnitController(MapKey key)
	{
		_currentMap = key;
	}
	public IEnumerable<KeyValuePair<uint,Unit>> Actors()
	{
		foreach(var id in _unitMap.Keys.ToArray())if(TryGetUnit(id,out _,out var unit) && unit!=null)yield return new KeyValuePair<uint,Unit>(id,unit);
	}
	public Unit FindActor(uint id) => TryGetUnit(id,out _,out var unit)?unit:null;

	public void Execute(ReplayMod.Events.ConcreteEvents.SpawnEvent e)
	{
		try
		{
			// Recorded IDs can be reused. No transform/control/turret state from the
			// previous lifetime may survive the new spawn.
			DespawnUnit(e.unitId);
			if (!Encyclopedia.Lookup.TryGetValue(e.jsonKey, out var value))
			{
				throw new InvalidDataException("Missing replay unit definition: " + e.jsonKey);
			}
			Unit unit = null;
			if (NetworkSceneSingleton<Spawner>.i == null)
			{
				throw new InvalidOperationException("Replay spawner is unavailable");
			}
			FactionHQ factionHQ = FactionRegistry.HqFromName(e.factionName);
			bool neutralVehicle = value is VehicleDefinition || value is AircraftDefinition || value is ShipDefinition || value is MissileDefinition;
			if (factionHQ == null && !ReplaySpawnPolicy.CanSpawnWithoutFaction(value is BuildingDefinition, value is SceneryDefinition, HasContainerSpawnPath(value), e.factionName, neutralVehicle))
			{
				throw new InvalidDataException($"Replay unit {e.jsonKey} cannot spawn: faction '{e.factionName}' is unavailable");
			}
			UnitDefinition unitDefinition = value;
			UnitDefinition unitDefinition2 = unitDefinition;
			unit = ((unitDefinition2 is VehicleDefinition def) ? SpawnVehicle(e, factionHQ, def) : ((unitDefinition2 is MissileDefinition def2) ? SpawnMissile(e, factionHQ, def2) : ((unitDefinition2 is AircraftDefinition def3) ? SpawnAircraft(e, factionHQ, def3) : ((unitDefinition2 is ShipDefinition def4) ? SpawnShip(e, factionHQ, def4) : ((unitDefinition2 is BuildingDefinition def5) ? SpawnBuilding(e, factionHQ, def5) : ((!(unitDefinition2 is SceneryDefinition def6)) ? SpawnPilotOrContainer(e, factionHQ, value) : SpawnScenery(e, factionHQ, def6)))))));
			if (!(unit != null))
			{
				return;
			}
			_unitMap[e.unitId] = unit.persistentID;
			_ownedActors.Track(e.unitId, unit.persistentID.Id, unit);
			_reverseUnitMap[unit.persistentID] = e.unitId;
			_pidsOnDestroy.Remove(unit.persistentID.Id);
			ReplayVfxPlayback.RegisterActor(unit);
			_isRbDisabled[unit.persistentID] = false;
			_spawnTimes[e.unitId] = e.Time;
            ReplayInteriorPresentation.ActorSpawned(unit);
			if (unit is Aircraft initialAircraft && initialAircraft.controlInputs != null)
			{
				var controls = initialAircraft.controlInputs;
				_inputs[e.unitId] = new List<Inputs> {new Inputs {time=e.Time,brake=controls.brake,pitch=controls.pitch,
					yaw=controls.yaw,roll=controls.roll,throttle=controls.throttle,customAxis1=controls.customAxis1}};
			}
			_unitSnapshots[e.unitId] = new List<PositionSnapshot>
			{
				new PositionSnapshot { time = e.Time, position = e.pos, rotation = e.rotation, velocity = e.startingVelocity }
			};
			ConfigureReplayPhysics(unit);
			if(ReplayRuntime.FormatMajor>=10&&(unit is GroundVehicle||unit is Container))ReplayCargoParachutes.StopNative(ReplayCargoParachutes.Find(unit));
            TrackMapBuildingActor(e.unitId,unit);
			if (unit is Missile missile) CaptureBoosterVisuals(missile);
		}
		catch (Exception ex)
		{
			Plugin.logger.LogError((object)$"Spawn exception, spawnEvent params: {e.jsonKey} {ex}");
			if (e.jsonKey == null)
			{
				Plugin.SafeLogWarning("json key null");
			}

			throw;
		}
	}

	private Unit SpawnVehicle(ReplayMod.Events.ConcreteEvents.SpawnEvent e, FactionHQ hq, VehicleDefinition def)
	{
		return NetworkSceneSingleton<Spawner>.i.SpawnVehicle(def.unitPrefab, e.pos, e.rotation, e.startingVelocity, hq, null, 1f, holdPosition: false, null);
	}

	private Unit SpawnAircraft(ReplayMod.Events.ConcreteEvents.SpawnEvent e, FactionHQ hq, AircraftDefinition def)
	{
		LiveryKey livery = new LiveryKey(e.liveryType, e.liveryIndex, e.liveryId.ToString());
		Loadout loadout = new Loadout
		{
			// SpawnEvent is returned to its pool immediately after execution.
			weapons = e.weapons != null ? new List<WeaponMount>(e.weapons) : new List<WeaponMount>()
		};
		return NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null, def.unitPrefab, loadout, 1f, livery, e.pos, e.rotation, e.startingVelocity, null, hq, null, 1f, 1f);
	}

	private Unit SpawnMissile(ReplayMod.Events.ConcreteEvents.SpawnEvent e, FactionHQ hq, MissileDefinition def)
	{
		Plugin.DebugLog("Trying to spawn missile");
		TryGetUnit(e.ownerId, out var _, out var unit);
		GameObject gameObject = UnityEngine.Object.Instantiate(def.unitPrefab, e.pos.ToLocalPosition(), e.rotation);
		gameObject.GetComponent<Rigidbody>().velocity = e.startingVelocity;
		Missile component = gameObject.GetComponent<Missile>();
		component.NetworkHQ = ((unit != null) ? unit.NetworkHQ : hq);
		component.NetworkunitName = component.definition.unitName;
		component.NetworkownerID = ((unit != null) ? unit.persistentID : PersistentID.None);
		component.SetTarget(null);
		component.NetworkstartPosition = e.pos;
		component.NetworkstartOffsetFromOwner = unit != null ? e.pos.ToLocalPosition() - unit.transform.position : Vector3.zero;
		component.NetworkstartingVelocity = e.startingVelocity;
		component.NetworkstartRotation = e.rotation;
		NetworkSceneSingleton<Spawner>.i.ServerObjectManager.Spawn(gameObject);
		return component;
	}

	private Unit SpawnShip(ReplayMod.Events.ConcreteEvents.SpawnEvent e, FactionHQ hq, ShipDefinition def)
	{
		Plugin.logger.LogInfo((object)$"Trying to spawn {def.unitName}, key {e.jsonKey}, type {def.shipType}");
		return NetworkSceneSingleton<Spawner>.i.SpawnShip(def.unitPrefab, e.pos, e.rotation, hq, GetUniqueName(), 1f, holdPosition: false);
	}

	private Unit SpawnBuilding(ReplayMod.Events.ConcreteEvents.SpawnEvent e, FactionHQ hq, BuildingDefinition def)
	{
		return NetworkSceneSingleton<Spawner>.i.SpawnBuilding(def.unitPrefab, e.pos, e.rotation, hq, null, null, capturable: false, null);
	}

	private Unit SpawnScenery(ReplayMod.Events.ConcreteEvents.SpawnEvent e, FactionHQ hq, SceneryDefinition def)
	{
		return NetworkSceneSingleton<Spawner>.i.SpawnScenery(def.unitPrefab, e.pos, e.rotation, null);
	}

	private static bool HasContainerSpawnPath(UnitDefinition definition)
	{
		// Match the dispatch above, then positively identify the native root
		// component. Unknown plain definitions must not gain a neutral exemption.
		return definition != null && !(definition is VehicleDefinition) && !(definition is MissileDefinition) &&
			!(definition is AircraftDefinition) && !(definition is ShipDefinition) && !(definition is BuildingDefinition) &&
			!(definition is SceneryDefinition) && definition.code != "PILOT" && definition.unitPrefab != null &&
			definition.unitPrefab.GetComponent<Container>() != null;
	}

	private Unit SpawnPilotOrContainer(ReplayMod.Events.ConcreteEvents.SpawnEvent e, FactionHQ hq, UnitDefinition def)
	{
		if (def.code == "PILOT")
		{
			if (ReplayManager.i.IgnorePilotsSpawn)
			{
				return null;
			}
			return NetworkSceneSingleton<Spawner>.i.SpawnPilot(def.unitPrefab, e.pos, e.rotation, hq, null);
		}
		return NetworkSceneSingleton<Spawner>.i.SpawnContainer(def.unitPrefab, e.pos, e.rotation, hq, null);
	}

	public bool TryGetUnit(uint id, out PersistentID pid, out Unit unit)
	{
		pid = default(PersistentID);
		unit = null;
		if (!_unitMap.TryGetValue(id, out pid))
		{
			return false;
		}
		// Native UnitRegistry intentionally excludes map-hidden buildings. Resolve
		// only this controller's actor lifetime, never an unrelated reused PID.
		return _ownedActors.TryGet(id, pid.Id, out unit);
	}

	public void MoveUnit(uint id, List<PositionSnapshot> positions, double currentTime)
	{
		MoveUnit(id,positions,currentTime,true);
	}
	private void MoveUnit(uint id,List<PositionSnapshot> positions,double currentTime,bool smoothPresentation)
	{
		if (!TryGetUnit(id, out var _, out var unit) || positions == null || positions.Count == 0)
		{
			return;
		}
		if (RestoreTerminalMissilePose(id, unit)) return;
		ConfigureReplayPhysics(unit);
        if(unit is Building&&RestoreReplayBuildingPose(id,unit,currentTime))return;
		_motionPresentationPoses.Remove(id);
		if (!SnapshotHistory.Locate(positions, currentTime, PositionTime, out int num, out int next, out float value))
		{
			// Retain the exact spawn transform until the first recorded sample is due.
			return;
		}
		if (num == next)
		{
			SetUnitTransform(unit, positions[num]);
			ReplayMotionSmoothing.TrimHistory(positions,currentTime);
			return;
		}
		PositionSnapshot positionSnapshot = positions[num];
		PositionSnapshot positionSnapshot2 = positions[next];
		Vector3 position = Vector3.Lerp(positionSnapshot.position.ToLocalPosition(), positionSnapshot2.position.ToLocalPosition(), value);
		Quaternion rotation = Quaternion.Slerp(positionSnapshot.rotation, positionSnapshot2.rotation, value);
		Vector3 velocity = Vector3.Lerp(positionSnapshot.velocity, positionSnapshot2.velocity, value);
		if(smoothPresentation&&SmoothMotion(id,unit,positions,currentTime,out var smoothed))
		{
			var presentation=new MotionPresentationPose {time=currentTime,rawPosition=position,rawRotation=rotation,
				renderPosition=smoothed.position.ToLocalPosition(),renderRotation=smoothed.rotation};
			_motionPresentationPoses[id]=presentation;
			position=presentation.renderPosition;rotation=presentation.renderRotation;velocity=smoothed.velocity;
		}
		unit.speed = velocity.magnitude;
		unit.transform.SetPositionAndRotation(position, rotation);
		if (unit.rb != null) { unit.rb.position = position; unit.rb.rotation = rotation; unit.rb.angularVelocity = Vector3.zero; }
		if (unit.rb != null && !unit.rb.isKinematic)
		{
			unit.rb.velocity = velocity;
		}
		ReplayMotionSmoothing.TrimHistory(positions,currentTime);
	}

	private void SetUnitTransform(Unit unit, PositionSnapshot snapshot)
	{
		unit.speed = snapshot.velocity.magnitude;
		unit.transform.SetPositionAndRotation(snapshot.position.ToLocalPosition(), snapshot.rotation);
		if (unit.rb != null) { unit.rb.position = snapshot.position.ToLocalPosition(); unit.rb.rotation = snapshot.rotation; unit.rb.angularVelocity = Vector3.zero; }
		if (unit.rb != null && !unit.rb.isKinematic)
		{
			unit.rb.velocity = snapshot.velocity;
		}
	}

	private void ConfigureReplayPhysics(Unit unit)
	{
		if (!_replayBodies.TryGetValue(unit, out var bodies)) { bodies = unit.GetComponentsInChildren<Rigidbody>(); _replayBodies[unit] = bodies; }
		foreach (var body in bodies) if (body != null) { body.detectCollisions = false; body.useGravity = false; }
		if (_isRbDisabled.TryGetValue(unit.persistentID, out var configured) && configured) return;
		if (unit is Aircraft aircraft && !aircraft.simplePhysics) aircraft.SetSimplePhysics();
		foreach (var rb in unit.GetComponentsInChildren<Rigidbody>())
		{
			rb.detectCollisions = false;
			rb.useGravity = false;
		}
		if (unit is Aircraft)
			foreach (var part in unit.GetComponentsInChildren<AeroPart>()) part.UnregisterFromJob();
		_isRbDisabled[unit.persistentID] = true;
	}

	private void EnableRb(Aircraft aircraft)
	{
		aircraft.SetComplexPhysics();
	}

	private void CaptureBoosterVisuals(Missile missile)
	{
		if (ReplayRuntime.FormatMajor < 2 || !missile.boosterIsAttached) return;
		var visuals = new List<ReplayBoosterVisual>();
		foreach (var booster in missile.GetComponentsInChildren<VLSBooster>(true))
		{
			if (booster == null || booster.separated) continue;
			visuals.Add(new ReplayBoosterVisual {booster=booster,state=new ReplayBoosterState(booster.delayTimer,booster.activated)});
			booster.enabled = false; _ownedBoosters.Add(booster);
		}
		if (visuals.Count > 0) _replayBoosters[missile] = visuals;
	}

	private void ApplyBoosterVisuals(Missile missile, MissileStatusEvent snapshot)
	{
		if (!_replayBoosters.TryGetValue(missile, out var visuals)) return;
		foreach (var visual in visuals)
		{
			var booster = visual.booster; if (booster == null) continue;
			switch (visual.state.Apply(snapshot.booster, snapshot.age))
			{
				case ReplayBoosterTransition.Activate: booster.Activate(); break;
				case ReplayBoosterTransition.Separate:
					if (!booster.separated) booster.Burnout();
					// Separated debris keeps its native fall/splash. No remaining
					// fuel means FixedUpdate cannot push the replay missile.
					booster.fuelMass = 0; booster.activated = true; booster.enabled = true;
					break;
			}
		}
	}

	// Main-thread only. Also used when a replay closes without a scene change.
	public void ReleaseOwnedPresentationObjects()
	{
		ReplayInteriorPresentation.Clear();ReplayCloseCameraVisibility.Clear();
		ClearMotionSmoothing();
		ClearMissileImpacts();
		_actorIdentities.Clear();
		ClearAircraftVisuals();
		ClearPlaybackLights();
		ClearFuelFlames();
		ClearReplayGuns();
		ClearReplayCountermeasures();
		ClearWorldPlayback();ClearMapBuildingPlayback();ClearEjectionEyes();
		ReplayCloudParticleClock.Clear();
		ClearAircraftPartMotion();
		ReplayVfxPlayback.ClearOwnedObjects();
		foreach (var booster in _ownedBoosters) if (booster != null) UnityEngine.Object.Destroy(booster.gameObject);
		_ownedBoosters.Clear(); _replayBoosters.Clear();
	}

	public async UniTask ResetWorld()
	{
		try
		{
			await UniTask.SwitchToMainThread();
			ReleaseOwnedPresentationObjects();
			_unitSnapshots.Clear();
			_inputs.Clear();
			_replayBodies.Clear(); _replayGuns.Clear();
			_missileMounts.Clear();
			ClearCargoMounts();
			ClearMissileMounts();
			ClearCargoParachutes();
			_engineRpm.Clear();
			_engineTimelines.Clear();
			_engineCoreRpm.Clear();
			_engineCoreTimelines.Clear();
			_queuedSnapshots.Clear();
			_turretSnapshots.Clear();
			_unitMap.Clear();
			_ownedActors.Clear();
			_reverseUnitMap.Clear();
			_spawnTimes.Clear();
			_pidsOnDestroy.Clear();
			Unit[] onScene = UnityEngine.Object.FindObjectsOfType<Unit>();
			Unit[] array = onScene;
			Aircraft a = default(Aircraft);
			foreach (Unit u in array)
			{
				u.transform.position = new GlobalPosition(-40000f, -40000f, -40000f).ToLocalPosition();
				int num;
				if (_isRbDisabled.TryGetValue(u.persistentID, out var disabled) && disabled)
				{
					a = u as Aircraft;
					num = (((object)a != null) ? 1 : 0);
				}
				else
				{
					num = 0;
				}
				if (num != 0)
				{
					// Replay units are destroyed; do not recreate physical joints.
				}
				if (u is GroundVehicle vehicle)
				{
					vehicle.wreckage = null;
				}
				else if (u is Building building)
				{
					building.wreckage = null;
				}
				_pidsOnDestroy.Add(u.persistentID.Id);
				UnityEngine.Object.Destroy(u.gameObject);
				_turretSnapshots.Clear();
				a = null;
			}
			_isRbDisabled.Clear();
			await UniTask.NextFrame();await UniTask.NextFrame();
			_pidsOnDestroy.Clear();
			Plugin.logger.LogInfo((object)"Reset finished");
		}
		catch (Exception ex)
		{
			Exception e = ex;
			UnityEngine.Debug.LogError($"Reset world failed: {e}");
			throw;
		}
	}

	public void Execute(UpdateInputsEvent e)
	{
		if (AcceptSample(e.id, e.Time))
		{
			Inputs item = new Inputs
			{
				brake = e.brake,
				pitch = e.pitch,
				yaw = e.yaw,
				customAxis1 = e.customAxis1,
				roll = e.roll,
				throttle = e.throttle,
				time = e.Time
			};
			if (!_inputs.TryGetValue(e.id, out var samples)) _inputs[e.id] = samples = new List<Inputs>();
			SnapshotHistory.Insert(samples, item, InputTime);
		}
	}

	private void UpdateInputs(uint id, List<Inputs> inputs, double time)
	{
		if (inputs == null || inputs.Count == 0 || !TryGetUnit(id, out var _, out var unit) || !(unit is Aircraft aircraft))
		{
			return;
		}
		if (!SnapshotHistory.Locate(inputs, time, InputTime, out int num, out int next, out float t))
		{
			// The first sample is still in the future. Initial inputs are recorded at spawn.
			return;
		}
		if (num == next)
		{
			ApplyInputs(aircraft, inputs[num].brake, inputs[num].pitch, inputs[num].yaw, inputs[num].roll, inputs[num].throttle, inputs[num].customAxis1);
			SnapshotHistory.Trim(inputs, num, MAX_STALE_SNAPSHOTS);
			return;
		}
		Inputs inputs2 = inputs[num];
		Inputs inputs3 = inputs[next];
		float brake = Mathf.Lerp(inputs2.brake, inputs3.brake, t);
		float yaw = Mathf.Lerp(inputs2.yaw, inputs3.yaw, t);
		float roll = Mathf.Lerp(inputs2.roll, inputs3.roll, t);
		float pitch = Mathf.Lerp(inputs2.pitch, inputs3.pitch, t);
		float throttle = Mathf.Lerp(inputs2.throttle, inputs3.throttle, t);
		float customAxis = Mathf.Lerp(inputs2.customAxis1, inputs3.customAxis1, t);
		ApplyInputs(aircraft, brake, pitch, yaw, roll, throttle, customAxis);
		SnapshotHistory.Trim(inputs, num, MAX_STALE_SNAPSHOTS);
	}

	private void ApplyInputs(Aircraft aircraft, float brake, float pitch, float yaw, float roll, float throttle, float customAxis1)
	{
		ControlInputs controlInputs = aircraft.controlInputs;
		if (controlInputs != null)
		{
			controlInputs.roll = roll;
			controlInputs.pitch = pitch;
			controlInputs.yaw = yaw;
			controlInputs.brake = brake;
			controlInputs.throttle = throttle;
			controlInputs.customAxis1 = customAxis1;
		}
	}

	public void Execute(DetachPartEvent e)
	{
		ApplyRecordedPartDetach(e);
	}

	public void DespawnUnit(uint unitId, bool detonate = false, double? eventTime = null)
	{
		if (eventTime.HasValue) PositionActorForEffect(unitId, eventTime.Value);
		TryGetUnit(unitId, out var pid, out var unit);
		ForgetMapBuildingActor(unitId);ForgetEjectionEye(unitId);
		ForgetMissileImpact(unitId);
		ForgetReplayGuns(unit);
		ForgetWorldActor(unitId);
		ForgetAircraftPartMotion(unitId);
		ForgetAircraftVisuals(unitId);
		ForgetCargoMounts(unitId);
		ForgetMissileMounts(unitId);
		ForgetCargoParachutes(unitId);
		ForgetPlaybackLights(unitId);
		ForgetMotionSmoothing(unitId);
		_actorIdentities.Forget(unitId);
		ReplayVfxPlayback.ForgetActor(unit);
		_unitMap.Remove(unitId); _spawnTimes.Remove(unitId);
		_ownedActors.Remove(unitId);
		_reverseUnitMap.Remove(pid); _isRbDisabled.Remove(pid);
		_unitSnapshots.Remove(unitId); _inputs.Remove(unitId); _queuedSnapshots.Remove(unitId);
		_engineRpm.Remove(unitId);
		_engineTimelines.Remove(unitId);
		_engineCoreRpm.Remove(unitId);
		_engineCoreTimelines.Remove(unitId);
		foreach (var turret in _turretSnapshots.Where(p => p.Value.Any(s => s.ownerId == unitId)).Select(p => p.Key).ToArray())
			_turretSnapshots.Remove(turret);
		if (unit != null)
		{
			_replayBodies.Remove(unit); _replayGuns.Remove(unit); _missileMounts.Remove(unit);
			if (unit is Missile oldMissile) _replayBoosters.Remove(oldMissile);
			if (detonate && unit is Missile missile)
			{
				ReplayMod.Patches.ReplayMissileNativeDetonationPatch.DetonateRecordedDespawn(missile);
			}
			else
			{
				UnityEngine.Object.Destroy(unit.gameObject);
			}
		}
	}

	private bool AcceptSample(uint id, double time) => _spawnTimes.TryGetValue(id, out var birth) && time >= birth && TryGetUnit(id, out _, out _);

	private void PositionActorForEffect(uint id, double time)
	{
		// Actions execute before the frame's final world interpolation. Sample
		// this actor at the action time so fast missiles and aircraft emit their
		// explosion, debris, canopy or muzzle effects at the recorded position.
		if (_unitSnapshots.TryGetValue(id, out var positions)) MoveUnit(id, positions, time,false);
	}

	public void SetGear(SetGearEvent e)
	{
		ApplyRecordedGear(e);
	}

	private void DisablePhysics(Unit unit)
	{
		if (!(unit == null))
		{
			Rigidbody rb = unit.rb;
			if (rb != null)
			{
				rb.isKinematic = true;
				rb.useGravity = false;
				rb.velocity = Vector3.zero;
				rb.angularVelocity = Vector3.zero;
			}
		}
	}

	private void DisableJobs(GroundVehicle unit)
	{
		JobManager.Remove(ref unit.JobPart);
		GroundVehicle.DisposeJobFields(ref unit.JobFields);
	}

	public bool ShouldContinuePatchedMethod(uint id)
	{
		if (_pidsOnDestroy.Contains(id))
		{
			return false;
		}
		return true;
	}

	private string GetUniqueName()
	{
		return $"Unit N{_generated++}";
	}

	public void Execute(UpdateTurretEvent e)
	{
		if (!AcceptSample(e.attachedUnitId, e.Time)) return;
		var item = TurretSnapshot.Create(e);
		if (AttachTurretSnapshot(item)) return;
		if (!_queuedSnapshots.TryGetValue(e.attachedUnitId, out var pending))
			_queuedSnapshots[e.attachedUnitId] = pending = new List<TurretSnapshot>();
		// A station can initialize a frame after spawn. Retry on the main thread;
		// no delayed task can attach an old lifetime's sample after a seek/reset.
		int duplicate = pending.FindIndex(s => s.time == item.time && s.weaponStationIdx == item.weaponStationIdx && s.turretIdx == item.turretIdx);
		if (duplicate >= 0) pending[duplicate] = item; else pending.Add(item);
	}

	private bool AttachTurretSnapshot(TurretSnapshot snapshot)
	{
		if (!TryGetUnit(snapshot.ownerId, out _, out var unit) || unit.weaponStations == null) return false;
		var station = unit.weaponStations.FirstOrDefault(s => s != null && s.Number == snapshot.weaponStationIdx);
		if (station == null || station.Turrets == null) return false;
		var turret = station.Turrets.FirstOrDefault(t => t != null && t.turretIndex == snapshot.turretIdx);
		if (turret == null) return true;
		if (!_turretSnapshots.TryGetValue(turret, out var list))
		{
			_turretSnapshots[turret] = list = new List<TurretSnapshot>
			{
				new TurretSnapshot {time=_spawnTimes[snapshot.ownerId],ownerId=snapshot.ownerId,
					weaponStationIdx=snapshot.weaponStationIdx,turretIdx=snapshot.turretIdx,
					elevationAngle=turret.elevationAngle,traverseAngle=turret.traverseAngle}
			};
		}
		SnapshotHistory.Insert(list, snapshot, TurretTime); turret.enabled = false; return true;
	}

	private void FlushQueuedTurrets()
	{
		foreach (var id in _queuedSnapshots.Keys.ToArray())
		{
			var samples = _queuedSnapshots[id];
			samples.RemoveAll(s => AttachTurretSnapshot(s));
			if (samples.Count == 0 || !TryGetUnit(id, out _, out _)) _queuedSnapshots.Remove(id);
		}
	}

	public void MoveTurrets(double time)
	{
		MoveTurretsAt(time,null);
	}

	private void MoveTurretsAt(double time,uint? ownerId)
	{
		foreach (KeyValuePair<Turret, List<TurretSnapshot>> turretSnapshot3 in _turretSnapshots)
		{
			List<TurretSnapshot> value = turretSnapshot3.Value;
			if (value == null || value.Count == 0 || (ownerId.HasValue && value[0].ownerId != ownerId.Value))
			{
				continue;
			}
			Turret key = turretSnapshot3.Key;
			if (!(key == null))
			{
				if (!SnapshotHistory.Locate(value, time, TurretTime, out int num, out int next, out float value2)) continue;
				if (num == next)
				{
					SetTurretTransform(key, value[num].elevationAngle, value[num].traverseAngle);
					SnapshotHistory.Trim(value, num, 12);
					continue;
				}
				TurretSnapshot turretSnapshot = value[num];
				TurretSnapshot turretSnapshot2 = value[next];
				float elevation = SnapshotHistory.InterpolateAngle(turretSnapshot.elevationAngle, turretSnapshot2.elevationAngle, value2);
				float traverse = SnapshotHistory.InterpolateAngle(turretSnapshot.traverseAngle, turretSnapshot2.traverseAngle, value2);
				SetTurretTransform(key, elevation, traverse);
				SnapshotHistory.Trim(value, num, 12);
			}
		}
	}

	private void SetTurretTransform(Turret turret, float elevation, float traverse)
	{
		if (turret.elevationTransform != null) turret.elevationTransform.localEulerAngles = new Vector3(elevation, 0f, 0f);
		turret.transform.localEulerAngles = new Vector3(0f, traverse, 0f);
		turret.elevationAngle = elevation;
		turret.traverseAngle = traverse;
	}

	public void Execute(SetActiveStationEvent e)
	{
		if (TryGetUnit(e.unitId, out var _, out var unit) && unit is Aircraft aircraft &&
			aircraft.weaponManager != null && unit.weaponStations != null && e.stationIdx < unit.weaponStations.Count)
		{
			aircraft.weaponManager.SetActiveStation(e.stationIdx);
			Plugin.DebugLog($"[SetActiveStation] idx: {e.stationIdx}");
		}
	}

	public void WeaponManagerFire(WeaponManagerFireEvent e)
	{
		if (TryGetUnit(e.unitId, out var _, out var unit) && unit is Aircraft aircraft)
		{
			if(ReplayRuntime.FormatMajor>=10&&aircraft.weaponManager?.currentWeaponStation?.Weapons?.Any(w=>w is MountedCargo)==true)return;
			if (aircraft.weaponManager.currentWeaponStation.WeaponInfo.gun)
			{
				FireGun(aircraft);
			}
			else
			{
				aircraft.weaponManager.Fire();
			}
			Plugin.DebugLog("[WeaponManagerFire] weaponInfo: " + aircraft.weaponManager.currentWeaponStation.WeaponInfo.weaponName);
			if (unit.rb != null)
			{
				Plugin.DebugLog($"Velocity: {unit.rb.velocity}");
			}
		}
	}

	private void FireGun(Aircraft aircraft)
	{
		// Legacy manager events contain no muzzle, velocity or spread.
		// New BulletEvents contain the actual simulated projectile state.
	}

	public void Execute(PartDamageEvent e)
	{
		MarkMotionDamaged(e.unitId);
		PositionActorForEffect(e.unitId, e.Time);
		if (!TryGetUnit(e.unitId, out _, out var unit)) return;
		var part = unit.partLookup?.FirstOrDefault(p => p != null && p.id == e.partId);
		if (part == null) return;
        if (_partHealthPresentation.TryRestoreSnapshot(part,e)) return;
		part.hitPoints = e.hitPoints + e.pierce + e.blast + e.fire + e.impact;
		part.ApplyDamage(e.pierce, e.blast, e.fire, e.impact);
        _partHealthPresentation.RefreshDamageShader(part,e);
	}

	public void Execute(EjectionEvent e)
	{
		MarkMotionDamaged(e.unitId);
		PositionActorForEffect(e.unitId, e.Time);
		if (!TryGetUnit(e.unitId, out _, out var unit) || !(unit is Aircraft aircraft)) return;
		// Pilot spawns are recorded separately; do not create duplicate pilots.
		if (e.capsule) aircraft.RpcEscapeCapsule();
		else aircraft.RpcJettisonCanopy();
	}

	public void Execute(BulletEvent e)
	{
		PositionActorForEffect(e.unitId, e.Time);
		if (!TryGetUnit(e.unitId, out _, out var unit)) return;
		if (!_replayGuns.TryGetValue(unit, out var guns)) { guns = unit.GetComponentsInChildren<Gun>(true); _replayGuns[unit] = guns; }
		if (e.gunIndex >= guns.Length) return;
		var gun = guns[e.gunIndex];
		if (gun == null) return;
		if (gun.bulletSim == null)
		{
			gun.bulletSim = BulletSim.Create(unit, gun, null);
			gun.bulletSim.gameObject.AddComponent<ReplayBulletLifetime>();
		}
		var sim = gun.bulletSim;
		sim.visualOnly = true;
		sim.tracerSize = e.tracerSize;
		PlayReplayGunShot(gun, e.Time);
		if (gun.muzzleParticles != null) foreach (var particles in gun.muzzleParticles) if (particles != null) particles.Play();
		if (!e.active || e.life <= 0) return;
		BulletSim.TracerView tracer = null;
		if (e.tracer && !GameManager.IsHeadless)
		{
			tracer = BulletSim.GetTracer();
			tracer.Setup(e.position.ToLocalPosition(), e.velocity.sqrMagnitude > 0 ? Quaternion.LookRotation(e.velocity) : Quaternion.identity,
				new Vector3(e.tracerSize, e.tracerSize, e.velocity.magnitude * Time.fixedDeltaTime), e.color);
		}
		var bullet = new BulletSim.Bullet(e.velocity, tracer, e.life, false, e.explosive, null)
		{
			position = e.position, impacted = e.impacted, reliability = e.reliability
		};
		sim.bullets.Add(bullet);
	}

	public void Execute(AircraftStatusEvent e)
	{
		if (!AcceptSample(e.unitId, e.Time) || !TryGetUnit(e.unitId, out _, out var unit) || !(unit is Aircraft)) return;
		if (!_engineTimelines.TryGetValue(e.unitId, out var timeline))
			_engineTimelines[e.unitId] = timeline = new ReplayEngineTimeline();
		// The event returns to a pool; the timeline owns its own RPM data.
		timeline.Add(e);
	}
	public void Execute(EngineCoreStateEvent e)
	{
		if (!AcceptSample(e.unitId, e.Time) || !TryGetUnit(e.unitId, out _, out var unit) || !(unit is Aircraft)) return;
		if (!_engineCoreTimelines.TryGetValue(e.unitId, out var timeline))
			_engineCoreTimelines[e.unitId] = timeline = new ReplayEngineTimeline();
		timeline.Add(e);
	}

	public void RestoreRecordedRpm(Aircraft aircraft)
	{
		if (aircraft == null || !_reverseUnitMap.TryGetValue(aircraft.persistentID, out var recordedId) ||
			!TryGetUnit(recordedId, out _, out var current) || !ReferenceEquals(current, aircraft)) return;
		if (_engineRpm.TryGetValue(recordedId, out var recordedRpm)) ApplyEngineRpm(aircraft.engines, recordedRpm);
		if (_engineCoreRpm.TryGetValue(recordedId, out var coreRpm)) ApplyEngineRpm(aircraft.engineStates, coreRpm);
	}

	internal List<(AudioSource source,AudioClip clip)> PrimeRecordedPropAudio()
	{
		List<(AudioSource source,AudioClip clip)> primed=null;
		foreach(var pair in _engineRpm)
		{
			if(!TryGetUnit(pair.Key,out _,out var unit)||!(unit is Aircraft aircraft)||aircraft.engines==null||aircraft.controlInputs==null)continue;
			for(int i=0;i<aircraft.engines.Count&&i<pair.Value.Length;i++)
			{
				if(!(aircraft.engines[i] is PropFan prop))continue;
				var source=prop.source;
				if(source==null||source.mute||!source.isPlaying||source.volume>.0001f)continue;
				if(ReplayPropAudioPolicy.TryInitialVolume(pair.Value[i],prop.nominalRPM,prop.powerRatio,aircraft.controlInputs.throttle,
					prop.volumeBase,prop.rpmVolumePortion,prop.aoaVolumePortion,out var volume))
				{
					source.volume=volume;var clip=source.clip;
					if(clip!=null&&source.loop&&clip.loadState==AudioDataLoadState.Loaded&&clip.loadType==AudioClipLoadType.DecompressOnLoad)
						(primed??(primed=new List<(AudioSource,AudioClip)>())).Add((source,clip));
				}
			}
		}
		return primed;
	}

	// A freshly restored, silent loop can report playing while Unity outputs
	// silence until a later physics update. Re-arm only the sources just primed,
	// and retain their sample position; ordinary audible pause/resume is untouched.
	internal static void WakePrimedPropAudio(List<(AudioSource source,AudioClip clip)> primed)
	{
		if(primed==null)return;
		foreach(var entry in primed)
		{
			var source=entry.source;var clip=entry.clip;
			if(source==null||clip==null||source.clip!=clip||source.mute||!source.isPlaying||source.volume<=.0001f)continue;
			bool stopped=false;
			try
			{
				int sample=source.timeSamples;
				if(!ReplayPropAudioPolicy.CanWakeLoop(source.loop,clip.loadState==AudioDataLoadState.Loaded,
					clip.loadType==AudioClipLoadType.DecompressOnLoad,sample,clip.samples))continue;
				source.Stop();stopped=true;source.Play();stopped=false;source.timeSamples=sample;
			}
			catch(Exception error)
			{
				if(stopped&&source!=null)try{source.Play();}catch(Exception){}
				Plugin.SafeLogWarning("Could not restore propeller audio on playback start: "+error.Message);
			}
		}
	}

	// Jet turbine loops normally start on the next physics tick. At slow replay
	// speeds that tick can be noticeably late, so start only recorded running
	// cores on the clock transition, with the native sound-cone condition intact.
	internal void StartRecordedJetAudio()
	{
		foreach(var pair in _engineCoreRpm)
		{
			if(!TryGetUnit(pair.Key,out _,out var unit)||!(unit is Aircraft aircraft)||aircraft.engineStates==null)continue;
			for(int i=0;i<aircraft.engineStates.Count&&i<pair.Value.Length;i++)
			{
				float rpm=pair.Value[i],maxRpm,maxPitch;AudioSource source;bool outsideCone=false;
				if(aircraft.engineStates[i] is Turbojet jet)
				{source=jet.turbineAudio;maxRpm=jet.maxRPM;maxPitch=jet.turbineMaxPitch;outsideCone=jet.outOfSoundCone;}
				else if(aircraft.engineStates[i] is Turbofan fan)
				{source=fan.turbineAudio;maxRpm=fan.maxRPM;maxPitch=fan.turbineMaxPitch;}
				else continue;
				if(!ReplayPropAudioPolicy.CanStartJet(rpm,maxRpm,maxPitch,outsideCone)||source==null||source.mute||source.isPlaying||!source.enabled||!source.gameObject.activeInHierarchy||!source.loop||source.clip==null||source.clip.loadState!=AudioDataLoadState.Loaded)continue;
				try{source.pitch=rpm/maxRpm*maxPitch;source.Play();}
				catch(Exception error){Plugin.SafeLogWarning("Could not start recorded jet audio: "+error.Message);}
			}
		}
	}

	private static void ApplyEngineRpm(IList<IEngine> engines, float[] recordedRpm)
	{
		if (engines == null) return;
		for (int i = 0; i < engines.Count && i < recordedRpm.Length; i++)
		{
			var engine = engines[i];
			float rpm = recordedRpm[i];
			if (engine is Turbofan turbofan) turbofan.currentRPM = rpm;
			else if (engine is Turbojet turbojet) turbojet.rpm = rpm;
			else if (engine is TurbineEngine turbine)
			{
				turbine.currentRPM = rpm;
				turbine.RPMRatio = turbine.maxRPM > 0 ? rpm / turbine.maxRPM : 0;
			}
			else if (engine is PropFan propFan) propFan.currentRPM = rpm;
			else if (engine is DuctedFan ductedFan) ductedFan.rpm = rpm;
			else if (engine is ConstantSpeedProp prop) prop.RPM = rpm;
			else if (engine is RotorShaft rotor && ReplayRotorSpeedPolicy.TryConvert(rpm,rotor.angularSpeedLimit,out float speed,out float ratio))
			{rotor.angularSpeed=speed;rotor.angularSpeedRatio=ratio;}
		}
	}

	public void Execute(MissileStatusEvent e)
	{
		if (!TryGetUnit(e.unitId, out _, out var unit) || !(unit is Missile missile) || IsTerminalMissile(e.unitId, missile)) return;
		PositionActorForEffect(e.unitId, e.Time);
		ApplyBoosterVisuals(missile, e);
		for (int i = 0; i < missile.motors.Length && i < e.fuel.Length; i++)
		{
			var motor = missile.motors[i];
			bool wasBurning = motor.activated && motor.fuelMass > 0 && motor.delayTimer <= 0;
			bool burning = e.activated[i] && e.fuel[i] > 0 && e.delay[i] <= 0 && i == e.stage;
			if (burning && !wasBurning) motor.Activate(missile);
			else if (!burning && wasBurning) motor.Burnout(false);
			motor.fuelMass = e.fuel[i]; motor.delayTimer = e.delay[i]; motor.activated = e.activated[i];
		}
		missile.motorStage = e.stage;
		missile.motor = e.stage >= 0 && e.stage < missile.motors.Length ? missile.motors[e.stage] : null;
		missile.timeSinceSpawn = e.age; missile.throttle = e.throttle;
		missile.engineCurrentThrust = e.thrust; missile.boosterIsAttached = e.booster;
	}

	public void Update(double time)
	{
		SmoothedAircraftCount=0;
		if (++_boosterCleanupTicks >= 120) { _ownedBoosters.RemoveWhere(b => b == null); _boosterCleanupTicks = 0; }
		FlushQueuedTurrets();
		FlushCargoMounts();
		FlushMissileMounts();
		MoveTurrets(time);
		foreach (KeyValuePair<uint, List<PositionSnapshot>> unitSnapshot in _unitSnapshots)
		{
			MoveUnit(unitSnapshot.Key, unitSnapshot.Value, time);
			if(_motionPresentationPoses.ContainsKey(unitSnapshot.Key))SmoothedAircraftCount++;
		}
		foreach (KeyValuePair<uint, List<Inputs>> input in _inputs)
		{
			UpdateInputs(input.Key, input.Value, time);
		}
		foreach (var pair in _engineTimelines)
		{
			if (!TryGetUnit(pair.Key, out _, out var unit) || !(unit is Aircraft aircraft) ||
				!pair.Value.SampleAt(time, out bool ignition, out var rpm)) continue;
			aircraft.NetworkIgnition = ignition;
			_engineRpm[pair.Key] = rpm;
			ApplyEngineRpm(aircraft.engines, rpm);
		}
		foreach (var pair in _engineCoreTimelines)
		{
			if (!TryGetUnit(pair.Key, out _, out var unit) || !(unit is Aircraft aircraft) ||
				!pair.Value.SampleAt(time, out bool ignition, out var rpm)) continue;
			aircraft.NetworkIgnition = ignition;
			_engineCoreRpm[pair.Key] = rpm;
			ApplyEngineRpm(aircraft.engineStates, rpm);
		}
		RefreshRecordedAircraftParts(time);
		UpdateAircraftVisuals(time);
		RefreshReplayLights();
		RefreshRecordedAircraftParts(time);
		UpdateReplayGunAudio(time);
		UpdateReplayCountermeasures(time);
		UpdateWorld(time);RefreshReplayMapBuildings(time);
		UpdateCargoParachutes(time);
		RefreshFuelFlames(time);
		ReplayDamageParticleMotion.Refresh(time);
	}

	public bool OwnsReplayUnit(Unit unit)
	{
		return unit != null && _reverseUnitMap.TryGetValue(unit.persistentID, out var id) &&
			TryGetUnit(id, out _, out var current) && ReferenceEquals(current, unit);
	}

	public void Execute(UpdatePositionEvent e)
	{
		if (!AcceptSample(e.unitId, e.Time)) return;
		if (IsTerminalMissile(e.unitId, FindActor(e.unitId))) return;
		uint unitId = e.unitId;
		PositionSnapshot item = new PositionSnapshot
		{
			position = e.position,
			rotation = e.rotation,
			velocity = e.velocity,
			time = e.Time
		};
		if (!_unitSnapshots.TryGetValue(unitId, out var value)) _unitSnapshots[unitId] = value = new List<PositionSnapshot>();
		SnapshotHistory.Insert(value, item, PositionTime);
	}
	public void Execute(MissileStoresEvent e)
	{
		if(!TryGetUnit(e.unitId,out _,out var unit) || !(unit is Aircraft))return;
		if(!_missileMounts.TryGetValue(unit,out var mounts))_missileMounts[unit]=mounts=unit.GetComponentsInChildren<MountedMissile>(true);
		if(mounts.Length!=e.ammo.Length){Plugin.DebugLog("Mounted missile snapshot no longer matches loadout; skipped");return;}
		for(int n=0;n<mounts.Length;n++)if(mounts[n]!=null){mounts[n].ammo=e.ammo[n];mounts[n].fired=e.fired[n];mounts[n].gameObject.SetActive(e.active[n]);}
		if(unit.weaponStations!=null)foreach(var station in unit.weaponStations)
		{
			station.AccountAmmo();
			if(station.Weapons.Any(w=>w is MountedMissile))
			{int index=0;while(index<station.Weapons.Count && (station.Weapons[index]==null || station.Weapons[index].ammo<=0))index++;station.weaponIndex=index;}
		}
	}
}

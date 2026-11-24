using System;
using System.Collections.Generic;
using System.Linq;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

// Add always takes ownership of its argument. Snapshots and clones own independent
// pooled events; Clear/Dispose return only events still owned by this reducer.
public sealed class ReplaySeekState : IDisposable
{
    private readonly Dictionary<uint, SpawnEvent> spawns = new Dictionary<uint, SpawnEvent>();
    private readonly Dictionary<uint, PositionSnapshot> positions = new Dictionary<uint, PositionSnapshot>();
    private readonly bool retainMotionHistory;
    private readonly Dictionary<uint, List<PositionSnapshot>> motionHistory = new Dictionary<uint, List<PositionSnapshot>>();
    private readonly Dictionary<string, IReplayEvent> states = new Dictionary<string, IReplayEvent>();
    private readonly Dictionary<uint, long> spawnBytes = new Dictionary<uint, long>();
    private readonly Dictionary<string, long> stateBytes = new Dictionary<string, long>();
    private long estimatedBytes;
    private readonly ReplayCountermeasureState countermeasures = new ReplayCountermeasureState();
    private readonly ReplayFuelFlameState fuelFlames = new ReplayFuelFlameState();
    private readonly ReplayGunSpinState gunSpins = new ReplayGunSpinState();

    // Includes conservative dictionary/event/array overhead. Updated incrementally
    // so indexing does not repeatedly serialize the whole checkpoint.
    public long EstimatedBytes => estimatedBytes + countermeasures.EstimatedBytes + fuelFlames.EstimatedBytes + gunSpins.EstimatedBytes;
    public void ExpireCountermeasures(double time) => countermeasures.Expire(time);
    public ReplaySeekState(bool retainMotionHistory = false) { this.retainMotionHistory = retainMotionHistory; }

    private void Forget(uint id)
    {
        gunSpins.Forget(id);
        fuelFlames.DetachOwner(id);
        if (spawns.TryGetValue(id, out var previous))
        {
            spawns.Remove(id);
            estimatedBytes -= spawnBytes[id];
            spawnBytes.Remove(id);
            ReplayEventFactory.Return(previous);
        }
        if (positions.Remove(id)) estimatedBytes -= 160;
        if (motionHistory.TryGetValue(id, out var history))
        {
            estimatedBytes -= 160L * history.Count;
            motionHistory.Remove(id);
        }
        foreach (var key in states.Keys.Where(k => k.StartsWith(id + ":", StringComparison.Ordinal)).ToArray())
        {
            var old = states[key];
            states.Remove(key);
            estimatedBytes -= stateBytes[key];
            stateBytes.Remove(key);
            ReplayEventFactory.Return(old);
        }
    }

    public void Add(IReplayEvent e)
    {
        if (e == null) return;
        // Countermeasures are local presentation objects rather than Units;
        // their launch and fading trails survive an aircraft despawn.
        if (e is CountermeasureEvent countermeasure) { countermeasures.Add(countermeasure); return; }
        if (e is FuelFlameStateEvent flame) { fuelFlames.Add(flame); return; }
        countermeasures.Expire(e.Time);
        bool retained = false;
        try
        {
            if (double.IsNaN(e.Time) || double.IsInfinity(e.Time)) return;
            if (e is SpawnEvent spawn)
            {
                long size = Estimate(spawn);
                Forget(spawn.unitId);
                spawns.Add(spawn.unitId, spawn);
                spawnBytes.Add(spawn.unitId, size);
                estimatedBytes += size;
                retained = true;
                return;
            }
            if (e is DespawnEvent despawn) { Forget(despawn.unitId); return; }
            // the missile knows where it is, because it knows where it isn't
            // After impact, the invisible root is only there for audio; don't
            // recreate it as a live projectile or replay its explosion.
            if (e is MissileDetonationEvent impact)
            { if (IsAlive(impact.unitId, impact.Time)) Forget(impact.unitId); return; }
            if (e is GunBulletEvent gunShot) { if (IsAlive(gunShot.unitId, gunShot.Time)) gunSpins.Add(gunShot); return; }
            if (e is BulletEvent legacyShot) { if (IsAlive(legacyShot.unitId, legacyShot.Time)) gunSpins.Add(legacyShot); return; }
            if (e is GunSpinStateEvent spin) { if (IsAlive(spin.unitId, spin.Time)) gunSpins.Add(spin); return; }
            if (e is UpdatePositionEvent move)
            {
                if (!IsAlive(move.unitId, move.Time)) return;
                if (positions.TryGetValue(move.unitId, out var previous) && previous.time > move.Time) return;
                if (!positions.ContainsKey(move.unitId)) estimatedBytes += 160;
                var pose = PositionSnapshot.Create(move);
                positions[move.unitId] = pose;
                if (retainMotionHistory && spawns[move.unitId].isAircraft)
                {
                    if (!motionHistory.TryGetValue(move.unitId, out var history)) motionHistory[move.unitId] = history = new List<PositionSnapshot>();
                    int oldCount = history.Count;
                    SnapshotHistory.Insert(history, pose, p => p.time);
                    ReplayMotionSmoothing.TrimHistory(history, pose.time);
                    if (history.Count > ReplayMotionSmoothing.MaxWindowSamples + 1) history.RemoveRange(0, history.Count - ReplayMotionSmoothing.MaxWindowSamples - 1);
                    estimatedBytes += 160L * (history.Count - oldCount);
                }
                return;
            }

            string key = null; uint id = 0;
            if (e is UpdateInputsEvent input) { id = input.id; key = $"{id}:inputs"; }
            else if (e is AircraftStatusEvent status) { id = status.unitId; key = $"{id}:status"; }
            else if (e is EngineCoreStateEvent core) { id = core.unitId; key = $"{id}:engine-cores"; }
            else if (e is ActorIdentityEvent identity) { if (!identity.HasValidPayload()) return; id = identity.unitId; key = $"{id}:identity"; }
            else if (e is MissileStatusEvent missile) { id = missile.unitId; key = $"{id}:missile"; }
            else if (e is MissileStoresEvent stores) { id = stores.unitId; key = $"{id}:stores"; }
            else if (e is CargoMountStateEvent cargo) { id = cargo.unitId; key = $"{id}:cargo-mounts"; }
            else if (e is MissileMountStateEvent missileMounts) { id = missileMounts.unitId; key = $"{id}:missile-mounts"; }
            else if (e is CargoParachuteStateEvent chutes) { id = chutes.unitId; key = $"{id}:cargo-parachutes"; }
            else if (e is AircraftVisualLayoutEvent layout) { id = layout.unitId; key = $"{id}:visual-layout"; }
            else if (e is AircraftVisualStateEvent visual) { id = visual.unitId; key = $"{id}:visual-state"; }
            else if (e is AircraftPartMotionEvent motion) { id = motion.unitId; key = $"{id}:part-motion"; }
            else if (e is AircraftLightingEvent lighting) { id = lighting.unitId; key = $"{id}:lighting"; }
            else if (e is HangarDoorStateEvent door) { id = door.unitId; key = $"{id}:door:{door.hangarPath}"; door.blendSeconds = 0; }
            else if (e is WorldEnvironmentStateEvent) key = "world:environment";
            else if (e is FuelTankStatusEvent fuel) { id = fuel.unitId; key = $"{id}:fuel:{fuel.partId}"; fuel.suppressBurst = true; }
            else if (e is PilotEjectionStateEvent eye) { if (!eye.HasValidPayload()) return; id = eye.unitId; key = $"{id}:ejection-eye"; }
            else if (e is BuildingStateEvent building) { if (!building.HasValidPayload()) return; id = building.unitId; key = $"{id}:building"; }
            else if (e is UnitStatusEvent unit) { id = unit.unitId; key = $"{id}:unitstatus"; }
            else if (e is SetGearEvent gear) { id = gear.unitId; key = $"{id}:gear"; gear.Instant = true; }
            else if (e is SetActiveStationEvent station) { id = station.unitId; key = $"{id}:station"; }
            else if (e is PartDamageEvent damage) { id = damage.unitId; key = $"{id}:damage:{damage.partId}"; }
            else if (e is DetachPartEvent detach) { id = detach.unitID; key = $"{id}:detach:{detach.partID}"; }
            else if (e is EjectionEvent eject) { id = eject.unitId; key = $"{id}:eject:{eject.capsule}"; }
            else if (e is UpdateTurretEvent turret) { id = turret.attachedUnitId; key = $"{id}:turret:{turret.weaponStationIdx}:{turret.turretIdx}"; }
            if (key == null || (!(e is WorldEnvironmentStateEvent) && !IsAlive(id, e.Time))) return;
            if (states.TryGetValue(key, out var old) && old.Time > e.Time) return;
            long bytes = Estimate(e) + key.Length * 2;
            if (old != null) { estimatedBytes -= stateBytes[key]; ReplayEventFactory.Return(old); }
            states[key] = e; stateBytes[key] = bytes; estimatedBytes += bytes; retained = true;
        }
        finally { if (!retained) ReplayEventFactory.Return(e); }
    }

    private bool IsAlive(uint id, double time) => spawns.TryGetValue(id, out var spawn) && time >= spawn.Time;
    private static long Estimate(IReplayEvent e)
    {
        if (e is SpawnEvent spawn)
            return 512L + ((spawn.jsonKey?.Length ?? 0) + (spawn.factionName?.Length ?? 0)) * 2L + (spawn.weapons?.Count ?? 0) * 64L;
        if (e is AircraftStatusEvent aircraft) return 512L + aircraft.rpm.Length * 32L;
        if (e is EngineCoreStateEvent core) return 512L + core.rpm.Length * 32L;
        if (e is ActorIdentityEvent identity) return 512L + (identity.playerName.Length + identity.callsign.Length) * 2L;
        if (e is AircraftVisualStateEvent visual) return 512L + visual.poses.Length * 64L;
        if (e is AircraftPartMotionEvent motion) return 512L + motion.parts.Length * 80L;
        if (e is AircraftLightingEvent lighting) return 512L + lighting.keys.Sum(key => 64L + key.Length * 2L);
        if (e is HangarDoorStateEvent door) return 512L + door.hangarPath.Length * 2L;
        if (e is AircraftVisualLayoutEvent layout) return 512L + layout.channels.Sum(s => 64L + s.Length * 2L);
        if (e is MissileStatusEvent missile) return 512L + (missile.fuel.Length + missile.delay.Length) * 32L + missile.activated.Length * 16L;
        if (e is MissileStoresEvent stores) return 512L + stores.ammo.Length * 32L + (stores.fired.Length + stores.active.Length) * 16L;
        if (e is CargoMountStateEvent cargo) return 512L + cargo.mounts.Sum(p=>160L+(p.key?.Length??0)*2L);
        if (e is MissileMountStateEvent missileMounts) return 512L + missileMounts.mounts.Sum(p=>160L+(p.key?.Length??0)*2L);
        if (e is CargoParachuteStateEvent chutes) return 512L + chutes.parachutes.Sum(p=>256L+(p.key?.Length??0)*2L);
        return 512;
    }

    private static IReplayEvent CloneEvent(IReplayEvent e)
    {
        if (e is SpawnEvent spawn)
        {
            var result = ReplayEventFactory.GetEvent<SpawnEvent>();
            result.Time = spawn.Time; result.unitId = spawn.unitId; result.ownerId = spawn.ownerId;
            result.pos = spawn.pos; result.rotation = spawn.rotation; result.startingVelocity = spawn.startingVelocity;
            result.jsonKey = spawn.jsonKey; result.factionName = spawn.factionName; result.isAircraft = spawn.isAircraft;
            result.liveryType = spawn.liveryType; result.liveryId = spawn.liveryId; result.liveryIndex = spawn.liveryIndex;
            result.weapons = spawn.weapons == null ? null : new List<WeaponMount>(spawn.weapons);
            return result;
        }
        var copy = RollingReplayBuffer.Decode(RollingReplayBuffer.Encode(e));
        // Instant is a playback-only flag, deliberately absent from file formats.
        if (copy is SetGearEvent gear && e is SetGearEvent original) gear.Instant = original.Instant;
        // A checkpoint restores the final HP, not the historical hit which
        // produced it. Re-emitting that impulse can rerun native destruction
        // callbacks and burst effects during opening/rewind/recent-clip restore.
        if (copy is PartDamageEvent damage) damage.pierce = damage.blast = damage.fire = damage.impact = 0;
        return copy;
    }

    public ReplaySeekState Clone()
    {
        var copy = new ReplaySeekState(retainMotionHistory);
        try
        {
            foreach (var pair in spawns)
            {
                copy.spawns.Add(pair.Key, (SpawnEvent)CloneEvent(pair.Value));
                copy.spawnBytes.Add(pair.Key, spawnBytes[pair.Key]);
            }
            foreach (var pair in positions) copy.positions.Add(pair.Key, pair.Value);
            foreach (var pair in motionHistory) copy.motionHistory.Add(pair.Key, new List<PositionSnapshot>(pair.Value));
            foreach (var pair in states)
            {
                copy.states.Add(pair.Key, CloneEvent(pair.Value));
                copy.stateBytes.Add(pair.Key, stateBytes[pair.Key]);
            }
            copy.estimatedBytes = estimatedBytes;
            countermeasures.CloneTo(copy.countermeasures);
            fuelFlames.CloneTo(copy.fuelFlames);
            gunSpins.CloneTo(copy.gunSpins);
            return copy;
        }
        catch { copy.Clear(); throw; }
    }

    public void Drain(Queue<IReplayEvent> destination)
    {
        // Copy first, so a failed snapshot leaves the reducer reusable.
        CopyTo(destination); Clear();
    }

    public void CopyTo(Queue<IReplayEvent> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        var snapshot = new Queue<IReplayEvent>();
        try
        {
            foreach (var e in states.Values.OfType<WorldEnvironmentStateEvent>()) snapshot.Enqueue(CloneEvent(e));
            foreach (var original in spawns.Values.OrderBy(e => e.ownerId != e.unitId).ThenBy(e => e.Time))
            {
                var spawn = (SpawnEvent)CloneEvent(original);
                if (positions.TryGetValue(spawn.unitId, out var position))
                { spawn.pos = position.position; spawn.rotation = position.rotation; spawn.startingVelocity = position.velocity; }
                snapshot.Enqueue(spawn);
            }
            // Keep the actual last sample time. Folding pose into Spawn alone loses
            // the interpolation anchor between this sample and the first future one.
            IEnumerable<KeyValuePair<uint, PositionSnapshot>> poseCopies = retainMotionHistory ?
                motionHistory.SelectMany(pair => pair.Value.Select(pose => new KeyValuePair<uint, PositionSnapshot>(pair.Key, pose)))
                    .Concat(positions.Where(pair => !motionHistory.ContainsKey(pair.Key))) : positions;
            foreach (var pair in poseCopies.OrderBy(p => p.Value.time).ThenBy(p => p.Key))
            {
                var move = ReplayEventFactory.GetEvent<UpdatePositionEvent>();
                move.unitId = pair.Key; move.CopyFromSnapshot(pair.Value); snapshot.Enqueue(move);
            }
            foreach (var e in states.Values.OfType<AircraftVisualLayoutEvent>().OrderBy(e => e.Time)) snapshot.Enqueue(CloneEvent(e));
            foreach (var e in states.Values.Where(e => !(e is AircraftVisualLayoutEvent) && !(e is WorldEnvironmentStateEvent)).OrderBy(e => e.Time)) snapshot.Enqueue(CloneEvent(e));
            countermeasures.CopyTo(snapshot);
            fuelFlames.CopyTo(snapshot);
            gunSpins.CopyTo(snapshot);
            while (snapshot.Count > 0) destination.Enqueue(snapshot.Dequeue());
        }
        finally { while (snapshot.Count > 0) ReplayEventFactory.Return(snapshot.Dequeue()); }
    }

    public void Clear()
    {
        foreach (var e in spawns.Values) ReplayEventFactory.Return(e);
        foreach (var e in states.Values) ReplayEventFactory.Return(e);
        spawns.Clear(); positions.Clear(); motionHistory.Clear(); states.Clear(); spawnBytes.Clear(); stateBytes.Clear(); countermeasures.Clear(); fuelFlames.Clear(); gunSpins.Clear(); estimatedBytes = 0;
    }

    public void Dispose() => Clear();
}

using System;
using System.Collections.Generic;
using System.Linq;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

public partial class Recorder
{
    private readonly Dictionary<string, byte[]> _lastVfxState = new Dictionary<string, byte[]>();
    private readonly Dictionary<uint, double> _lastVfxPoll = new Dictionary<uint, double>();
    private bool IsTracked(Unit unit) => _acceptEvents && unit != null &&
        _unitsOnTrack.TryGetValue(unit.persistentID, out var tracked) && ReferenceEquals(unit, tracked);

    private void QueueVfxState(IReplayEvent e, string key)
    {
        e.Time = 0;
        // Burst suppression is a playback instruction, not part of the fuel state.
        var fuel = e as FuelTankStatusEvent;
        bool suppress = fuel != null && fuel.suppressBurst;
        if (fuel != null) fuel.suppressBurst = false;
        byte[] payload = RollingReplayBuffer.Encode(e);
        if (fuel != null) fuel.suppressBurst = suppress;
        e.Time = _virtualTime;
        if (_lastVfxState.TryGetValue(key, out var previous) && previous.SequenceEqual(payload))
        { ReplayEventFactory.Return(e); return; }
        _lastVfxState[key] = payload;
        QueueEvent(e);
    }

    private void RecordVfxState(Unit unit, bool initial = false)
    {
        if(initial)EnsureFuelFlameUnit(unit);
        uint id = unit.persistentID.Id;
        if (!initial && _lastVfxPoll.TryGetValue(id, out var last) && _virtualTime - last < .2) return;
        _lastVfxPoll[id] = _virtualTime;
        QueueVfxState(ReplayVfxPlayback.CaptureUnitStatus(unit), id + ":status");
        foreach (var fuel in ReplayVfxPlayback.CaptureFuelStates(unit))
            QueueVfxState(fuel, id + ":fuel:" + fuel.partId);
    }

    private void HandleFuelStatus(FuelTank tank, bool ruptured, bool onFire)
    {
        if (tank == null || tank.part == null || !IsTracked(tank.part.parentUnit)) return;
        var e = ReplayEventFactory.GetEvent<FuelTankStatusEvent>();
        e.unitId = tank.part.parentUnit.persistentID.Id; e.partId = tank.part.id;
        e.ruptured = ruptured; e.onFire = onFire; e.flameActive = tank.fireEffectSpawn != null;
        e.suppressBurst = false;
        QueueVfxState(e, e.unitId + ":fuel:" + e.partId);
    }

    private void HandleUnitStatus(Unit unit)
    {
        if (IsTracked(unit)) QueueVfxState(ReplayVfxPlayback.CaptureUnitStatus(unit), unit.persistentID.Id + ":status");
    }

    private void HandleRecordedDetonation(Missile missile, GlobalPosition position, Vector3 normal, bool armed, bool hitArmor, bool hitTerrain)
    {
        if (!IsTracked(missile)||IsRecordingMissileTerminal(missile)) return;
        // The RPC has resolved the authoritative impact position and hidden or
        // disabled the body. Keep its actor lifetime for the native delayed
        // unregister, but never publish its stationary aftermath as flight.
        MarkRecordingMissileTerminal(missile);
        var e = ReplayEventFactory.GetEvent<MissileDetonationEvent>();
        e.unitId = missile.persistentID.Id; e.Time = _virtualTime; e.position = position;
        e.normal = normal; e.armed = armed; e.hitArmor = hitArmor; e.hitTerrain = hitTerrain;
        QueueEvent(e);
    }

    private void ForgetRecordingVfx(Unit unit)
    {
        uint id = unit.persistentID.Id;
        _lastVfxPoll.Remove(id);
        string prefix = id + ":";
        foreach (string key in _lastVfxState.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            _lastVfxState.Remove(key);
        ReplayVfxPlayback.ForgetCapturedUnit(unit);
        ForgetFuelFlameUnit(id);
    }
    private void ClearRecordingVfx()
    { _lastVfxState.Clear(); _lastVfxPoll.Clear(); ReplayVfxPlayback.ClearCaptureCache();ClearRecordingFuelFlames(); }
}

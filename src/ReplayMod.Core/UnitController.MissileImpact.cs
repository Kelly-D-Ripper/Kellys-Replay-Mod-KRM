using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReplayMod.Core;

public partial class UnitController
{
    private sealed class TerminalMissile
    {
        public Missile actor;
        public GlobalPosition point;
    }
    private readonly Dictionary<uint, TerminalMissile> _missileImpacts = new Dictionary<uint, TerminalMissile>();

    internal bool IsTerminalMissile(uint id, Unit unit)
        => unit != null && _missileImpacts.TryGetValue(id, out var terminal) && ReferenceEquals(terminal.actor, unit);

    internal bool CanApplyMissileImpact(uint id, Missile missile, double time)
        => missile != null && TryGetUnit(id, out _, out var current) && ReferenceEquals(current, missile) &&
            _spawnTimes.TryGetValue(id, out var birth) && !double.IsNaN(time) && !double.IsInfinity(time) && time >= birth;

    internal void RecordMissileImpact(uint id, Missile missile, GlobalPosition point, double time)
    {
        if (!CanApplyMissileImpact(id, missile, time) ||
            !ReplayVfxPolicy.IsFinite(point.x) || !ReplayVfxPolicy.IsFinite(point.y) || !ReplayVfxPolicy.IsFinite(point.z)) return;
        var terminal = new TerminalMissile {actor=missile, point=point};
        _missileImpacts[id] = terminal;
        PinMissileImpact(terminal);
    }

    private bool RestoreTerminalMissilePose(uint id, Unit unit)
    {
        if (!IsTerminalMissile(id, unit)) return false;
        PinMissileImpact(_missileImpacts[id]);
        return true;
    }

    private static void PinMissileImpact(TerminalMissile terminal)
    {
        var missile = terminal.actor;
        if (missile == null) return;
        missile.disabled = true;
        missile.speed = 0;
        var point = terminal.point.ToLocalPosition();
        var body = missile.rb;
        if (body != null)
        {
            body.detectCollisions = false;
            body.useGravity = false;
            if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            // Freeze only after impact. Flying missile physics and the recorded
            // velocity used by native smoke/audio keep their existing behavior.
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            body.position = point;
        }
        missile.transform.position = point;
    }

    private void ForgetMissileImpact(uint id) => _missileImpacts.Remove(id);
    private void ClearMissileImpacts() => _missileImpacts.Clear();
}

using System;
using System.Collections.Generic;
using System.Linq;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

public partial class UnitController
{
    private readonly Dictionary<Gun, ReplayGunAudioPlayback> _replayGunAudio = new Dictionary<Gun, ReplayGunAudioPlayback>();
    private readonly Dictionary<Unit, Dictionary<ReplayGunAddressKey, Gun>> _resolvedReplayGuns = new Dictionary<Unit, Dictionary<ReplayGunAddressKey, Gun>>();
    private sealed class ReplaySpinningGun
    {
        internal readonly ReplayGunSpin spin = new ReplayGunSpin();
        internal Transform target;
        internal Quaternion origin;
    }
    private readonly Dictionary<Gun, ReplaySpinningGun> _replaySpinningGuns = new Dictionary<Gun, ReplaySpinningGun>();

    private ReplaySpinningGun ReplaySpin(Gun gun)
    {
        if (gun == null || gun.spinTransform == null || float.IsNaN(gun.spinRate) || float.IsInfinity(gun.spinRate) || gun.spinRate <= 0 || !OwnsReplayUnit(gun.attachedUnit)) return null;
        if (!_replaySpinningGuns.TryGetValue(gun, out var state))
            _replaySpinningGuns[gun] = state = new ReplaySpinningGun {target = gun.spinTransform, origin = gun.spinTransform.localRotation};
        return state.target == gun.spinTransform ? state : null;
    }

    private void UpdateReplayGunSpins(double time)
    {
        foreach (var pair in _replaySpinningGuns)
        {
            var gun = pair.Key; var state = pair.Value;
            if (gun == null || state.target == null || !OwnsReplayUnit(gun.attachedUnit)) continue;
            // Keep local authored orientation, rotate only the native barrel's
            // forward axis, and make pause/slow motion depend on replay time.
            double degrees = state.spin.PhaseAt(time) * gun.spinRate;
            state.target.localRotation = state.origin * Quaternion.AngleAxis((float)(degrees % 360), Vector3.forward);
        }
    }

    public void Execute(GunSpinStateEvent e)
    {
        if (!e.HasValidPayload() || !AcceptSample(e.unitId, e.Time) || !TryGetUnit(e.unitId, out _, out var unit)) return;
        Gun gun;
        if (e.legacyAddress)
        {
            if (!_replayGuns.TryGetValue(unit, out var guns)) { guns = unit.GetComponentsInChildren<Gun>(true); _replayGuns[unit] = guns; }
            gun = e.legacyGunIndex < guns.Length ? guns[e.legacyGunIndex] : null;
        }
        else
        {
            var address = new GunBulletEvent(); e.CopyTo(address); gun = ResolveReplayGun(unit, address);
        }
        // A checkpoint restores phase and residual spin, without emitting old
        // muzzle particles, projectile/tracer objects, sounds, recoil or damage.
        ReplaySpin(gun)?.spin.Restore(e.Time, e.phase, e.speed);
    }

    private Gun ResolveReplayGun(Unit unit, GunBulletEvent e)
    {
        if (!_resolvedReplayGuns.TryGetValue(unit, out var bindings)) _resolvedReplayGuns[unit] = bindings = new Dictionary<ReplayGunAddressKey, Gun>();
        var key = new ReplayGunAddressKey(e);
        if (bindings.TryGetValue(key, out var gun) && ReplayGunIdentity.Matches(unit, gun, e)) return gun;
        bindings.Remove(key);
        gun = ReplayGunIdentity.Resolve(unit, e);
        if (gun != null) bindings[key] = gun;
        return gun;
    }

    private void PlayReplayGunShot(Gun gun, double time, bool coalesceSameTime = true)
    {
        if (gun == null) return;
        ReplaySpin(gun)?.spin.RecordShot(time);
        if (!_replayGunAudio.TryGetValue(gun, out var audio)) _replayGunAudio[gun] = audio = new ReplayGunAudioPlayback(gun);
        audio.PlayShot(time, coalesceSameTime);
    }

    private void UpdateReplayGunAudio(double time)
    {
        UpdateReplayGunSpins(time);
        var player = ReplayManager.i?.Playback;
        bool paused = player == null || player.IsPaused || player.IsBuffering || player.IsSeeking || player.IsReloading;
        foreach (var pair in _replayGunAudio)
        {
            if (pair.Key != null) pair.Value.Update(time, paused);
        }
    }
    private void ForgetReplayGuns(Unit unit)
    {
        if (!ReferenceEquals(unit, null)) _resolvedReplayGuns.Remove(unit);
        foreach (var gun in _replaySpinningGuns.Keys.Where(g => g == null || g.attachedUnit == unit).ToArray()) _replaySpinningGuns.Remove(gun);
        foreach (var pair in _replayGunAudio.Where(p => p.Key == null || p.Key.attachedUnit == unit).ToArray())
        { pair.Value.Stop(); _replayGunAudio.Remove(pair.Key); }
    }
    private void ClearReplayGuns()
    { foreach (var audio in _replayGunAudio.Values) audio.Stop(); _replayGunAudio.Clear(); _resolvedReplayGuns.Clear(); _replaySpinningGuns.Clear(); }

    public void Execute(GunBulletEvent e)
    {
        PositionActorForEffect(e.unitId, e.Time);
        // Actions run before the frame's final turret interpolation. Put the
        // firing unit's barrels at the shot time before playing muzzle effects.
        MoveTurretsAt(e.Time,e.unitId);
        if (!TryGetUnit(e.unitId, out _, out var unit)) return;
        var gun = ResolveReplayGun(unit, e);
        if (gun == null) return;
        if (e.sound)
        {
            PlayReplayGunShot(gun, e.Time, false);
            if (gun.muzzleParticles != null) foreach (var particles in gun.muzzleParticles) if (particles != null) particles.Play();
        }
        if (!e.active || e.life <= 0) return;
        if (gun.bulletSim == null)
        {
            gun.bulletSim = BulletSim.Create(unit, gun, null);
            gun.bulletSim.gameObject.AddComponent<ReplayBulletLifetime>();
        }
        var sim = gun.bulletSim; sim.visualOnly = true; sim.tracerSize = e.tracerSize;
        BulletSim.TracerView tracer = null;
        if (e.tracer && !GameManager.IsHeadless)
        {
            tracer = BulletSim.GetTracer();
            tracer.Setup(e.position.ToLocalPosition(), e.velocity.sqrMagnitude > 0 ? Quaternion.LookRotation(e.velocity) : Quaternion.identity,
                new Vector3(e.tracerSize, e.tracerSize, e.velocity.magnitude * Time.fixedDeltaTime), e.color);
        }
        var bullet = new BulletSim.Bullet(e.velocity, tracer, e.life, false, e.explosive, null)
        { position = e.position, impacted = e.impacted, reliability = e.reliability };
        sim.bullets.Add(bullet);
    }
}
